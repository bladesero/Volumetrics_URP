#ifndef EXPONENT_HEIGHT_FOG_PASS_INCLUDED
#define EXPONENT_HEIGHT_FOG_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ACES.hlsl"

static const float FLT_EPSILON2 = 0.01f;

// UE 4.22 HeightFogCommon.ush
// Calculate the line integral of the ray from the camera to the receiver position through the fog density function
// The exponential fog density function is d = GlobalDensity * exp(-HeightFalloff * y)
float CalculateLineIntegralShared(float FogHeightFalloff, float RayDirectionY, float RayOriginTerms)
{
    float Falloff = max(-127.0f, FogHeightFalloff * RayDirectionY);    // if it's lower than -127.0, then exp2() goes crazy in OpenGL's GLSL.
    float LineIntegral = (1.0f - exp2(-Falloff)) / Falloff;
    float LineIntegralTaylor = log(2.0) - (0.5 * pow(log(2.0),2)) * Falloff;        // Taylor expansion around 0
        
    return RayOriginTerms * (abs(Falloff) > FLT_EPSILON2 ? LineIntegral : LineIntegralTaylor);
}

half4 GetExponentialHeightFog(
        float3 WorldPositionRelativeToCamera, 
        half4 ExponentialFogParameters, half4 ExponentialFogParameters2,
        half4 ExponentialFogColorParameter
        ) // camera to vertex
{
    const half MinFogOpacity = ExponentialFogColorParameter.w;
        
    float3 CameraToReceiver = WorldPositionRelativeToCamera;
    float CameraToReceiverLengthSqr = dot(CameraToReceiver, CameraToReceiver);
    float CameraToReceiverLengthInv = rsqrt(CameraToReceiverLengthSqr);
    float CameraToReceiverLength = CameraToReceiverLengthSqr * CameraToReceiverLengthInv;
    half3 CameraToReceiverNormalized = CameraToReceiver * CameraToReceiverLengthInv;
        
    // FogDensity * exp2(-FogHeightFalloff * (CameraWorldPosition.y - FogHeight))
    half RayOriginTerms = ExponentialFogParameters.x;
    float RayLength = CameraToReceiverLength;
    float RayDirectionY = CameraToReceiver.y;
        
    // Factor in StartDistance
    half ExcludeDistance = ExponentialFogParameters.w;

    UNITY_BRANCH
    if (ExcludeDistance > 0)
    {
        float ExcludeIntersectionTime = ExcludeDistance * CameraToReceiverLengthInv;
        float CameraToExclusionIntersectionY = ExcludeIntersectionTime * CameraToReceiver.y;
        float ExclusionIntersectionY = _WorldSpaceCameraPos.y + CameraToExclusionIntersectionY;
        float ExclusionIntersectionToReceiverY = CameraToReceiver.y - CameraToExclusionIntersectionY;
        
        // Calculate fog off of the ray starting from the exclusion distance, instead of starting from the camera
        RayLength = (1.0f - ExcludeIntersectionTime) * CameraToReceiverLength;
        RayDirectionY = ExclusionIntersectionToReceiverY;
        // ExponentialFogParameters.y : height falloff
        // ExponentialFogParameters2.y : fog height
        // height falloff * height
        float Exponent = max(-127.0f, ExponentialFogParameters.y * (ExclusionIntersectionY - ExponentialFogParameters2.y));
        // ExponentialFogParameters2.x : fog density
        RayOriginTerms = ExponentialFogParameters2.x * exp2(-Exponent);
    }
        
    // Calculate the "shared" line integral (this term is also used for the directional light inscattering) by adding the two line integrals together (from two different height falloffs and densities)
    // ExponentialFogParameters.y : fog height falloff
    float ExponentialHeightLineIntegralShared = 
        CalculateLineIntegralShared(ExponentialFogParameters.y, RayDirectionY, RayOriginTerms);
    // fog amount
    float ExponentialHeightLineIntegral = ExponentialHeightLineIntegralShared * RayLength;
        
    half3 InscatteringColor = ExponentialFogColorParameter.xyz;
        
    // Calculate the amount of light that made it through the fog using the transmission equation
    // 
    half ExpFogFactor = max(saturate(exp2(-ExponentialHeightLineIntegral)), MinFogOpacity);
        
    // ExponentialFogParameters2.w : FogCutoffDistance
    if (ExponentialFogParameters2.w > 0 && CameraToReceiverLength > ExponentialFogParameters2.w)
    {
        ExpFogFactor = 1;
    }
        
    half3 FogColor = (InscatteringColor) * (1 - ExpFogFactor);
    return half4(FogColor, ExpFogFactor);
}


struct FogData
{
    float3 inscatteringColor;
    float  maxOpacity;
    float  density;
    float  height;
    float  cutoffDistance;
    float  startDistance;
    float  heightFalloff;
};

half4 ApplyExponentialHeightFog(float2 uv, float depth,FogData fogdata)
{
    if (UNITY_REVERSED_Z)
        depth = 1 - depth;

    float4 ndcPos = float4(float3(uv, depth) * 2 - 1, 1);
    float4 camPos = mul(unity_CameraInvProjection, ndcPos);
    camPos /= camPos.w;
    camPos.z *= -1;
    float3 worldPos = mul(unity_CameraToWorld, camPos).xyz;
    float3 worldCameraRay = worldPos - _WorldSpaceCameraPos;

    half4 ExponentialFogColorParameter;
    ExponentialFogColorParameter = float4(
            fogdata.inscatteringColor,
            1.0 - fogdata.maxOpacity
            );

    float4 ExponentialFogParameters;
    ExponentialFogParameters = float4(
            fogdata.density * exp2(-fogdata.heightFalloff * (_WorldSpaceCameraPos.y - fogdata.height)),
            fogdata.heightFalloff,
            0,
            fogdata.startDistance
            );

    half4 ExponentialFogParameters2;
    ExponentialFogParameters2 = float4(
            fogdata.density,
            fogdata.height,
            0,
            fogdata.cutoffDistance
            );

    float4 fogColor = GetExponentialHeightFog(
            worldCameraRay, 
            ExponentialFogParameters, ExponentialFogParameters2,
            ExponentialFogColorParameter);

    return fogColor;
}

#endif