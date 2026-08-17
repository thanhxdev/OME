Texture2D bgTexture : register(t0);
Texture2D layerTexture : register(t1);
SamplerState smp : register(s0);

cbuffer MixerConfig : register(b0) {
    float layerOpacity;
    float3 padding;
};

struct PS_INPUT {
    float4 Pos : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

float4 main(PS_INPUT input) : SV_TARGET {
    float4 bg = bgTexture.Sample(smp, input.TexCoord);
    float4 layer = layerTexture.Sample(smp, input.TexCoord);
    
    // Apply user opacity to the layer's alpha
    float effectiveAlpha = layer.a * layerOpacity;
    
    // Alpha blending: (Layer * LayerAlpha) + Background * (1 - LayerAlpha)
    float4 output;
    output.rgb = (layer.rgb * effectiveAlpha) + (bg.rgb * (1.0f - effectiveAlpha));
    output.a = max(bg.a, effectiveAlpha);
    
    return output;
}
