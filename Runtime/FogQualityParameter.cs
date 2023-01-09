using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    public class FogQualityParameter : VolumeParameter<Volumetric.FogQuality>
    {
        public FogQualityParameter(Volumetric.FogQuality value, bool overrideState = false) : base(value, overrideState) { }
    }
}

