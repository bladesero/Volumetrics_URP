using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Post-processing/Volumetric Fog")]
    public class Volumetric : VolumeComponent, IPostProcessComponent
    {
        public enum FogQuality { Low, Medium, High };

        [Tooltip("Fog Quality")]
        public FogQualityParameter fogQuality = new FogQualityParameter(FogQuality.Medium);
        [Tooltip("Fog Global Density")]
        public ClampedFloatParameter GlobalDensity = new ClampedFloatParameter(0.0f, 0.0f, 5.0f);
        [Tooltip("Fog Distance")]
        public ClampedFloatParameter Distance = new ClampedFloatParameter(50.0f, 20.0f, 100.0f);
        [Tooltip("Constant Fog Density")]
        public ClampedFloatParameter ConstantFogDensity = new ClampedFloatParameter(1.0f, 0.0f, 2.0f);
        [Tooltip("Height Fog Density")]
        public ClampedFloatParameter HeightFogDensity = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);
        [Tooltip("Height Fog Exponent")]
        public ClampedFloatParameter HeightFogExponent = new ClampedFloatParameter(0.125f, 0.0f, 1.0f);
        [Tooltip("Height Fog Height Offset")]
        public ClampedFloatParameter HeightFogOffset = new ClampedFloatParameter(0.0f, 0.0f, 100.0f);
        [Tooltip("Anisotropy Scatter")]
        public ClampedFloatParameter Anisotropy = new ClampedFloatParameter(0.5f, 0.0f, 0.98f);
        [Tooltip("Fog Ambient Color")]
        public ColorParameter AmbientColor = new ColorParameter(new Color(0.23f, 0.63f, 1.0f));

        Volumetric()
        {
            displayName = "Volumetric Fog";
        }

        public bool IsActive()
        {
            return GlobalDensity.value > 0.0f;
        }

        public bool IsTileCompatible()
        {
            return false;
        }

    }
}
