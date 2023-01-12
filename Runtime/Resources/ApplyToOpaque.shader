Shader "Hidden/Volumetrics/ApplyToOpaque" {
SubShader {
	//Tags { "RenderType" = "transparent" "RenderPipeline" = "UniversalPipeline"}
    Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
	LOD 100

Pass {
	Name "FogCombine"
	ZTest Always Cull Off ZWrite Off
    Blend Off

HLSLPROGRAM
	#pragma target 3.0
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
	#include"Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
	#include "VolumetricFog.hlsl"
	#include "ExponentHeightFog.hlsl"
	#pragma vertex FullscreenVert
	#pragma fragment frag

	TEXTURE2D_X(_VolumeSourceTex);
    SAMPLER(sampler_VolumeSourceTex);

	float3 _InscatteringColor;
    float  _MaxOpacity;
    float  _Density;
    float  _Height;
    float  _CutoffDistance;
    float  _StartDistance;
    float  _HeightFalloff;

	//struct Attributes
 //   {
 //       float4 positionHCS   : POSITION;
 //       float2 uv           : TEXCOORD0;
 //       UNITY_VERTEX_INPUT_INSTANCE_ID
 //   };

 //   struct Varyings
 //   {
 //       float4  positionCS  : SV_POSITION;
 //       float2  uv          : TEXCOORD0;
 //       UNITY_VERTEX_OUTPUT_STEREO
 //   };

 //   Varyings VertDefault(Attributes input)
 //   {
 //       Varyings output;
 //       UNITY_SETUP_INSTANCE_ID(input);
 //       UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

 //       // Note: The pass is setup with a mesh already in CS
 //       // Therefore, we can just output vertex position
 //       output.positionCS = float4(input.positionHCS.xyz, 1.0);

 //       #if UNITY_UV_STARTS_AT_TOP
 //       output.positionCS.y *= -1;
 //       #endif

 //       output.uv = input.uv;

 //       // Add a small epsilon to avoid artifacts when reconstructing the normals
 //       output.uv += 1.0e-6;

 //       return output;
 //   }

	half4 frag (Varyings input) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
		float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

		float depth = SampleSceneDepth(uv);

		half linear01Depth = Linear01Depth(depth,_ZBufferParams);
		half4 fog = Fog(linear01Depth, uv);

		FogData fogData=(FogData)0;

		//Init fogdata
		fogData.inscatteringColor=_InscatteringColor;
		fogData.maxOpacity=_MaxOpacity;
		fogData.density=_Density;
		fogData.height=_Height;
		fogData.cutoffDistance=_CutoffDistance;
		fogData.startDistance=_StartDistance;
		fogData.heightFalloff=_HeightFalloff;

		half4 farHeightFog=ApplyExponentialHeightFog(uv,depth,fogData);
		

        half4 col = SAMPLE_TEXTURE2D_X(_VolumeSourceTex, sampler_VolumeSourceTex, uv)* fog.a + fog;
		col=col* farHeightFog.a+ farHeightFog;

        //Stop Nan,but how to keep color,maybe rgb to hsv
        col.rgb=min(col.rgb,float3(50,50,50));
        return col;
	}

ENDHLSL
}
}
}
