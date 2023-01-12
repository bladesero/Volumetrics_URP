//Source:Unity Volumetric lighting

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public class VolumetricFogPass : ScriptableRenderPass
    {
        const string PassName = "VolumetricFog";
        const string CombineShaderName = "Hidden/Volumetrics/ApplyToOpaque";
        private static readonly int s_VolumeInject = Shader.PropertyToID("_VolumeInject");
        private static readonly int s_VolumeScatter = Shader.PropertyToID("_VolumeScatter");

        //Public Params
        public struct FogSettings
        {
            public float m_GlobalIntensityMult;
            public float m_GlobalDensityMult;
            public float m_ConstantFog;
            public float m_HeightFogAmount;
            public float m_HeightFogExponent;
            public float m_HeightFogOffset;
            public float m_Anisotropy;
            public float NearClip;
            public float FarClip;
            public Color AmbientColor;
        };
        //public FogSettings fogSettings = new FogSettings();

        private Texture2D m_Noise;
        

        static readonly Vector2[] frustumUVs =
        new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        static float[] frustumRays = new float[16];
        float[] m_fogParams;

        private ComputeShader m_InjectLightingAndDensity;
        private ComputeShader m_Scatter;
        private Material Combine;
        private Volumetric volumetric;

        RenderTargetIdentifier VolumeInjectID = new RenderTargetIdentifier(s_VolumeInject);
        RenderTargetIdentifier VolumeScatterID = new RenderTargetIdentifier(s_VolumeScatter);
        Vector3Int m_InjectNumThreads = new Vector3Int(16, 2, 16);
        Vector3Int m_ScatterNumThreads = new Vector3Int(32, 2, 1);
        Vector3Int m_VolumeResolution = new Vector3Int(160, 90, 128);
        public int ZSlice=128;

        private ScriptableRenderer m_renderer;
        RenderTargetIdentifier source;
        RenderTargetHandle m_TemporaryColorTexture;

        #region Light Data&Function
        struct PointLightParams
        {
            public Vector3 pos;
            public float range;
            public Vector3 color;
            float padding;
        }

        PointLightParams[] m_PointLightParams;
        ComputeBuffer m_PointLightParamsCB;

        struct SpotLightParams
        {
            public Vector3 pos;
            public Vector3 spotDir;
            public float range;
            public Vector3 color;
            public Vector2 angle;
        }

        SpotLightParams[] m_SpotLightParams;
        ComputeBuffer m_SpotLightParamsCB;

        ComputeBuffer m_DummyCB;

        void SetUpPointLightBuffers(int kernel,CommandBuffer cmd)
        {
            int count = m_PointLightParamsCB == null ? 0 : m_PointLightParamsCB.count;
            m_InjectLightingAndDensity.SetFloat("_PointLightsCount", count);
            if (count == 0)
            {
                // Can't not set the buffer
                m_InjectLightingAndDensity.SetBuffer(kernel, "_PointLights", m_DummyCB);
                return;
            }

            if (m_PointLightParams == null || m_PointLightParams.Length != count)
                m_PointLightParams = new PointLightParams[count];

            HashSet<FogLight> fogLights = LightManagerFogLights.Get();

            int j = 0;
            for (var x = fogLights.GetEnumerator(); x.MoveNext();)
            {
                var fl = x.Current;
                if (fl == null || fl.type != FogLight.Type.Point || !fl.isOn)
                    continue;

                Light light = fl.light;
                m_PointLightParams[j].pos = light.transform.position;
                float range = light.range * fl.m_RangeMult;
                m_PointLightParams[j].range = 1.0f / (range * range);
                m_PointLightParams[j].color = new Vector3(light.color.r, light.color.g, light.color.b) * light.intensity * fl.m_IntensityMult;
                j++;
            }

            // TODO: try a constant buffer with setfloats instead for perf
            cmd.SetComputeBufferData(m_PointLightParamsCB, m_PointLightParams);
            cmd.SetComputeBufferParam(m_InjectLightingAndDensity, kernel, "_PointLights", m_PointLightParamsCB);
        }

        void SetUpSpotLightBuffers(int kernel,CommandBuffer cmd)
        {
            int count = m_SpotLightParamsCB == null ? 0 : m_SpotLightParamsCB.count;
            m_InjectLightingAndDensity.SetFloat("_SpotLightsCount", count);
            if (count == 0)
            {
                // Can't not set the buffer
                m_InjectLightingAndDensity.SetBuffer(kernel, "_SpotLights", m_DummyCB);
                return;
            }

            if (m_SpotLightParams == null || m_SpotLightParams.Length != count)
                m_SpotLightParams = new SpotLightParams[count];

            HashSet<FogLight> fogLights = LightManagerFogLights.Get();

            int j = 0;
            for (var x = fogLights.GetEnumerator(); x.MoveNext();)
            {
                var fl = x.Current;
                if (fl == null || fl.type != FogLight.Type.Spot || !fl.isOn)
                    continue;

                Light light = fl.light;
                m_SpotLightParams[j].pos = light.transform.position;
                m_SpotLightParams[j].spotDir = light.transform.forward;
                float range = light.range * fl.m_RangeMult;
                m_SpotLightParams[j].range = 1.0f / (range * range);
                m_SpotLightParams[j].color = new Vector3(light.color.r, light.color.g, light.color.b) * light.intensity * fl.m_IntensityMult;
                float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
                float outerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.spotAngle);
                float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
                m_SpotLightParams[j].angle = new Vector2(angleRangeInv, -outerCos * angleRangeInv);
                j++;
            }

            // TODO: try a constant buffer with setfloats instead for perf
            //m_SpotLightParamsCB.SetData(m_SpotLightParams);
            cmd.SetComputeBufferData(m_SpotLightParamsCB, m_SpotLightParams);
            cmd.SetComputeBufferParam(m_InjectLightingAndDensity, kernel, "_SpotLights", m_SpotLightParamsCB);
        }

        void CreateBuffer(ref ComputeBuffer buffer, int count, int stride)
        {
            if (buffer != null && buffer.count == count)
                return;

            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }

            if (count <= 0)
                return;

            buffer = new ComputeBuffer(count, stride);
        }
        void ReleaseComputeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
                buffer.Release();
            buffer = null;
        }
        #endregion

        #region Init Function
        public VolumetricFogPass()
        {
            if (Combine == null)
                Combine = CoreUtils.CreateEngineMaterial(Shader.Find(CombineShaderName));
            //m_Noise = Resources.Load<Texture2D>("noise");
            m_InjectLightingAndDensity = Resources.Load<ComputeShader>("InjectLightingAndDensity");
            m_Scatter = Resources.Load<ComputeShader>("Scatter");
            m_TemporaryColorTexture.Init("_TempColor");
        }

        public bool Setup(ScriptableRenderer render)
        {
            //this.fogSettings = settings;
            this.m_renderer = render;
            return true;
        }

        #endregion

        #region Cleanup Function

        void Cleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(s_VolumeInject);
            cmd.ReleaseTemporaryRT(s_VolumeScatter);
        }

        public void ReleaseComputeBuffer()
        {
            ReleaseComputeBuffer(ref m_PointLightParamsCB);
            ReleaseComputeBuffer(ref m_SpotLightParamsCB);
            ReleaseComputeBuffer(ref m_DummyCB);
        }

        #endregion

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var camera = data.cameraData.camera;
            var stack = VolumeManager.instance.stack;
            volumetric = stack.GetComponent<Volumetric>();


            if (camera != null && camera.cameraType != CameraType.Preview && volumetric != null && volumetric.IsActive())
            {
                if (!CheckSupport())
                {
                    Debug.LogError(GetUnsupportedErrorMessage());
                    return;
                }

                var cmd = CommandBufferPool.Get(PassName);

                //Caculate volume texture
                Scatter(cmd, camera);
                //Draw to Screen
                RenderTextureDescriptor opaqueDesc = data.cameraData.cameraTargetDescriptor;


                //Combine.SetTexture("_MainTex");
                CombinewithSecondHeightFog(cmd, data, camera, opaqueDesc);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            
        }

        #region Camera Function
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var stack = VolumeManager.instance.stack;
            volumetric = stack.GetComponent<Volumetric>();
            ZSlice = VolumetricQuality(volumetric.fogQuality.value);
            InitVolume(s_VolumeInject, cmd);
            InitVolume(s_VolumeScatter, cmd);
            cmd.GetTemporaryRT(m_TemporaryColorTexture.id, renderingData.cameraData.cameraTargetDescriptor);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            Cleanup(cmd);
            cmd.ReleaseTemporaryRT(m_TemporaryColorTexture.id);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            this.source = m_renderer.cameraColorTarget;    
        }

        #endregion

        #region Check Compute Shader Support
        public static bool CheckSupport()
        {
            return SystemInfo.supportsComputeShaders;
        }

        public static string GetUnsupportedErrorMessage()
        {
            return "Volumetric Fog requires compute shaders and this platform doesn't support them. Disabling. \nDetected device type: " +
                SystemInfo.graphicsDeviceType + ", version: " + SystemInfo.graphicsDeviceVersion;
        }
        #endregion

        #region Do Volume Render
        void InitVolume(int volume,CommandBuffer cmd)
        {
            RenderTextureDescriptor volumeDescriptor = new RenderTextureDescriptor();
            volumeDescriptor.width = m_VolumeResolution.x;
            volumeDescriptor.height = m_VolumeResolution.y;
            volumeDescriptor.volumeDepth = ZSlice;
            volumeDescriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            volumeDescriptor.enableRandomWrite = true;
            volumeDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            volumeDescriptor.msaaSamples = 1;

            cmd.GetTemporaryRT(volume, volumeDescriptor, FilterMode.Bilinear);
        }

        void SetFrustumRays(Camera cam)
        {
            float far = volumetric.Distance.value;
            Vector3 cameraPos = cam.transform.position;
            Vector2[] uvs = frustumUVs;

            for (int i = 0; i < 4; i++)
            {
                Vector3 ray = cam.ViewportToWorldPoint(new Vector3(uvs[i].x, uvs[i].y, far)) - cameraPos;
                frustumRays[i * 4 + 0] = ray.x;
                frustumRays[i * 4 + 1] = ray.y;
                frustumRays[i * 4 + 2] = ray.z;
                frustumRays[i * 4 + 3] = 0;
            }

            m_InjectLightingAndDensity.SetVector("_CameraPos", cameraPos);
            m_InjectLightingAndDensity.SetFloats("_FrustumRays", frustumRays);
        }

        void SetUpForScatter(int kernel,Camera cam,CommandBuffer cmd)
        {
            //fogSettings.m_GlobalDensityMult = Mathf.Max(fogSettings.m_GlobalDensityMult, 0);
            //fogSettings.m_ConstantFog = Mathf.Max(fogSettings.m_ConstantFog, 0);
            //fogSettings.m_HeightFogAmount = Mathf.Max(fogSettings.m_HeightFogAmount, 0);

            SetFrustumRays(cam);

            //Create Compute Buffer
            int pointLightCount = 0, spotLightCount = 0;
            HashSet<FogLight> fogLights = LightManagerFogLights.Get();
            for (var x = fogLights.GetEnumerator(); x.MoveNext();)
            {
                var fl = x.Current;
                if (fl == null)
                    continue;

                bool isOn = fl.isOn;

                switch (fl.type)
                {
                    case FogLight.Type.Point: if (isOn) pointLightCount++; break;
                    case FogLight.Type.Spot: if (isOn) spotLightCount++; break;
                }
            }
            CreateBuffer(ref m_PointLightParamsCB, pointLightCount, Marshal.SizeOf(typeof(PointLightParams)));
            CreateBuffer(ref m_SpotLightParamsCB, spotLightCount, Marshal.SizeOf(typeof(SpotLightParams)));
            CreateBuffer(ref m_DummyCB, 1, 4);
            //Set light
            SetUpPointLightBuffers(kernel,cmd);
            SetUpSpotLightBuffers(kernel,cmd);

            // Compensate for more light and density being injected in per world space meter when near and far are closer.
            // TODO: Not quite correct yet.
            float depthCompensation = (volumetric.Distance.value - 0.3f/*fixed NearClip*/) * 0.01f;
            m_InjectLightingAndDensity.SetFloat("_Density", volumetric.GlobalDensity.value * 0.128f * depthCompensation);
            m_InjectLightingAndDensity.SetFloat("_Intensity", 1.0f);
            m_InjectLightingAndDensity.SetFloat("_Anisotropy", volumetric.Anisotropy.value);
            cmd.SetComputeTextureParam(m_InjectLightingAndDensity, kernel, "_VolumeInject", VolumeInjectID);
            //m_InjectLightingAndDensity.SetTexture(kernel, "_Noise", m_Noise);
            if (m_fogParams == null || m_fogParams.Length != 4)
                m_fogParams = new float[4];
            m_fogParams[0] = volumetric.ConstantFogDensity.value;
            m_fogParams[1] = volumetric.HeightFogExponent.value;
            m_fogParams[2] = volumetric.HeightFogOffset.value;
            m_fogParams[3] = volumetric.HeightFogDensity.value;

            //m_InjectLightingAndDensity.SetFloat("_Time", Time.time);
            m_InjectLightingAndDensity.SetFloats("_FogParams", m_fogParams);
            m_InjectLightingAndDensity.SetFloat("_NearOverFarClip", cam.nearClipPlane / cam.farClipPlane);
            m_InjectLightingAndDensity.SetVector("_AmbientLight", volumetric.AmbientColor.value);
            m_InjectLightingAndDensity.SetFloat("_SunFogDensity", volumetric.SunDensity.value);
            m_InjectLightingAndDensity.SetFloat("_MieG", volumetric.MieG.value);
            m_InjectLightingAndDensity.SetFloat("_ZSlice", (float)ZSlice);
        }

        void Scatter(CommandBuffer cmd, Camera cam)
        {
            int kernel = 0;

            SetUpForScatter(0, cam, cmd);
            //Inject
            cmd.DispatchCompute(m_InjectLightingAndDensity, kernel, m_VolumeResolution.x / m_InjectNumThreads.x, m_VolumeResolution.y / m_InjectNumThreads.y, ZSlice / m_InjectNumThreads.z);

            //Scatter
            cmd.SetComputeTextureParam(m_Scatter, 0, "_VolumeInject", VolumeInjectID);
            cmd.SetComputeTextureParam(m_Scatter, 0, "_VolumeScatter", VolumeScatterID);
            cmd.SetComputeFloatParam(m_Scatter, "_ZSlice", (float)ZSlice);
            cmd.DispatchCompute(m_Scatter,0, m_VolumeResolution.x / m_ScatterNumThreads.x, m_VolumeResolution.y / m_ScatterNumThreads.y, 1);
        }

        void CombinewithSecondHeightFog(CommandBuffer cmd, RenderingData data,Camera camera, RenderTextureDescriptor descriptor)
        {
            //Set params
            cmd.SetGlobalTexture("_VolumeScatter", VolumeScatterID);
            cmd.SetGlobalTexture("_VolumeSourceTex", m_TemporaryColorTexture.Identifier());
            Combine.SetVector("_Screen_TexelSize", new Vector4(1.0f / descriptor.width, 1.0f / descriptor.height, descriptor.width, descriptor.height));
            Combine.SetVector("_VolumeScatter_TexelSize", new Vector4(1.0f / m_VolumeResolution.x, 1.0f / m_VolumeResolution.y, 1.0f / ZSlice, 0));
            Combine.SetFloat("_CameraFarOverMaxFar", camera.farClipPlane / volumetric.Distance.value);
            Combine.SetFloat("_NearOverFarClip", 0.3f/*fixed NearClip*/ / volumetric.Distance.value);

            //SecondFog param
            Combine.SetColor("_InscatteringColor", volumetric.AmbientColor.value);
            Combine.SetFloat("_MaxOpacity", 1.0f);
            Combine.SetFloat("_Density", volumetric.SecondHeightFogDensity.value);
            Combine.SetFloat("_Height", volumetric.HeightFogOffset.value);
            Combine.SetFloat("_StartDistance", volumetric.Distance.value);
            Combine.SetFloat("_HeightFalloff", volumetric.HeightFogExponent.value);
            //Blit
            cmd.Blit(data.cameraData.renderer.cameraColorTarget, m_TemporaryColorTexture.Identifier());
            cmd.Blit(m_TemporaryColorTexture.Identifier(), source, Combine, 0);
        }

        int VolumetricQuality(Volumetric.FogQuality fogQuality)
        {
            switch(fogQuality)
            {
                case Volumetric.FogQuality.High: return 256;
                case Volumetric.FogQuality.Medium: return 128;
                case Volumetric.FogQuality.Low: return 64;
                default: return 128;
            }
        }
        #endregion

    }

    public class VolumetricFeature : ScriptableRendererFeature
    {
        VolumetricFogPass pass;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        //[Header("Clip Size")]
        //public float nearClip = 0.3f;
        //public float farClip = 20f;

        //[Header("Fog Settings")]
        //public float GlobalIntensityMult = 1.0f;
        //public float GlobalDensityMult = 1.0f;
        //public float ConstantFog = 1;
        //public float HeightFogAmount = 1;
        //public float HeightFogExponent = 0.125f;
        //public float HeightFogOffset = 0;
        //[Range(0,1)]
        //public float Anisotropy = 0.5f;
        //public Color AmbientColor = Color.gray;

        //VolumetricFogPass.FogSettings fogSettings = new VolumetricFogPass.FogSettings();
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if(pass!=null)
            {
                //fogSettings.m_GlobalIntensityMult = GlobalIntensityMult;
                //fogSettings.m_GlobalDensityMult = GlobalDensityMult;
                //fogSettings.m_ConstantFog = ConstantFog;
                //fogSettings.m_HeightFogAmount = HeightFogAmount;
                //fogSettings.m_HeightFogExponent = HeightFogExponent;
                //fogSettings.m_HeightFogOffset = HeightFogOffset;
                //fogSettings.m_Anisotropy = Anisotropy;
                //fogSettings.NearClip = nearClip;
                //fogSettings.FarClip = farClip;
                //fogSettings.AmbientColor = AmbientColor;

                pass.Setup(renderer);
                pass.ConfigureInput(ScriptableRenderPassInput.Color);
                renderer.EnqueuePass(pass);
            }
            
        }

        public override void Create()
        {
            if(pass==null)
            {
                pass = new VolumetricFogPass();
            }
            pass.renderPassEvent = renderPassEvent;
            
        }

        

        private void OnDisable()
        {
            pass.ReleaseComputeBuffer();
        }

        protected override void Dispose(bool disposing)
        {
            pass.ReleaseComputeBuffer();
        }
    }
}
