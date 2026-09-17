// HLSL Color Grading & CCU Pixel Shader
// Executes broadcast CCU mathematics:
// Color_out = Gain * (Color_in + Lift * (1 - Color_in))^(1 / Gamma)
// With Saturation, Contrast, Brightness, and optional 3D LUT sampling.

cbuffer ColorGradingConstants : register(b0)
{
    float3 Lift;       // Shadows adjustment ([-1.0, 1.0], default 0.0)
    float  Brightness; // Brightness offset ([-1.0, 1.0], default 0.0)
    
    float3 Gamma;      // Midtones adjustment ([0.2, 5.0], default 1.0)
    float  Contrast;   // Contrast multiplier ([0.0, 3.0], default 1.0)
    
    float3 Gain;       // Highlights adjustment ([0.0, 5.0], default 1.0)
    float  Saturation; // Saturation multiplier ([0.0, 3.0], default 1.0)
    
    int    LutEnabled; // 1 if 3D LUT is active, 0 if disabled
    int    LutSize;    // e.g. 17, 33, 64
    float2 Padding;
};

Texture2D InputTexture : register(t0);
SamplerState LinearSampler : register(s0);

Texture3D Lut3DTexture : register(t1);
SamplerState LutSampler : register(s1);

struct PS_INPUT
{
    float4 Pos : SV_POSITION;
    float2 Tex : TEXCOORD0;
};

float3 ApplyCCU(float3 color)
{
    // Clamp input to [0, 1] range for stability
    color = saturate(color);
    
    // 1. Lift / Gamma / Gain math:
    // Lift affects blacks/shadows, Gain scales highlights, Gamma bends midtones
    float3 lifted = color + Lift * (1.0f - color);
    lifted = max(lifted, 0.0f);
    
    float3 powered = pow(lifted, 1.0f / max(Gamma, 0.001f));
    float3 graded = Gain * powered;
    
    // 2. Brightness & Contrast
    graded = (graded - 0.5f) * Contrast + 0.5f + Brightness;
    
    // 3. Saturation (Rec.709 Luma coefficients: 0.2126 R + 0.7152 G + 0.0722 B)
    float luma = dot(graded, float3(0.2126f, 0.7152f, 0.0722f));
    graded = lerp(float3(luma, luma, luma), graded, Saturation);
    
    return saturate(graded);
}

float4 main(PS_INPUT input) : SV_TARGET
{
    float4 srcColor = InputTexture.Sample(LinearSampler, input.Tex);
    float3 ccuColor = ApplyCCU(srcColor.rgb);
    
    if (LutEnabled != 0)
    {
        // 3D LUT cube coordinate calculation:
        // Texture3D texcoords range from (0.5/size) to (1.0 - 0.5/size)
        float scale = (float(LutSize) - 1.0f) / float(LutSize);
        float offset = 0.5f / float(LutSize);
        float3 lutCoord = ccuColor * scale + offset;
        
        float4 lutSample = Lut3DTexture.Sample(LutSampler, lutCoord);
        return float4(lutSample.rgb, srcColor.a);
    }
    
    return float4(ccuColor, srcColor.a);
}
