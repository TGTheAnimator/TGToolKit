// TGToolKit — DefaultShader.hlsl
// Minimal VS_IN matching the 3 semantics our InputLayout provides.

cbuffer SceneConstants : register(b0)
{
    matrix WorldViewProj;
    float3 LightDir;
    float  HasTexture;
    float4 Ambient;
};

Texture2D    DiffuseTexture : register(t0);
SamplerState LinearSampler  : register(s0);

struct VS_IN
{
    float3 Position : POSITION;
    float4 Normal   : NORMAL;     // float4 handles R8G8B8A8_SNorm and Float3
    float2 TexCoord : TEXCOORD;
};

struct PS_IN
{
    float4 pos  : SV_POSITION;
    float3 norm : NORMAL;
    float2 tex  : TEXCOORD0;
};

PS_IN VS(VS_IN input)
{
    PS_IN output;
    output.pos  = mul(float4(input.Position, 1.0f), WorldViewProj);
    output.norm = input.Normal.xyz;
    output.tex  = input.TexCoord;
    return output;
}

float4 PS(PS_IN input) : SV_Target
{
    float3 n = normalize(input.norm);

    // Key light (warm, from upper-front-right)
    float3 keyDir   = normalize(float3(-0.6f, -1.0f, 0.5f));
    float  key      = saturate(dot(n, -keyDir)) * 0.75f;

    // Fill light (cool, from left) — softens shadows
    float3 fillDir  = normalize(float3(1.0f, -0.3f, -0.5f));
    float  fill     = saturate(dot(n, -fillDir)) * 0.25f;

    float  light    = key + fill + 0.15f;  // 0.15 ambient floor

    float4 albedo = (HasTexture > 0.5f)
        ? DiffuseTexture.Sample(LinearSampler, input.tex)
        : float4(0.78f, 0.78f, 0.78f, 1.0f);

    // Apply lighting + global ambient tint
    float3 lit = albedo.rgb * light + Ambient.rgb * 0.2f;
    return float4(saturate(lit), albedo.a);
}
