// TGToolKit — DefaultShader.hlsl (Enterprise CAD Revision)

cbuffer SceneConstants : register(b0)
{
    matrix WorldViewProj;
    float3 LightDir;
    float  HasTexture;
    float4 Ambient;
    float  IsGrid;       // Tells the shader we are drawing lines
    float3 _padding;     // HLSL buffers must be padded to 16-byte boundaries
};

Texture2D    DiffuseTexture : register(t0);
SamplerState LinearSampler  : register(s0);

struct VS_IN
{
    float3 Position : POSITION;
    float4 Normal   : NORMAL;
    float2 TexCoord : TEXCOORD0; // Explicitly indexed
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
    // --- Intercept Grid & Zone Rendering ---
    if (IsGrid > 1.5f)
    {
        // Renders the targeting zone as a bright fluorescent green
        return float4(0.0f, 1.0f, 0.0f, 1.0f);
    }
    else if (IsGrid > 0.5f)
    {
        // Renders the CAD grid as a sleek, low-opacity red/grey
        return float4(0.4f, 0.1f, 0.1f, 0.4f); 
    }

    // 1. Safeguard against Missing/Zeroed Normals (Prevents NaN crashes)
    float normalLength = length(input.norm);
    float3 n = normalLength > 0.1f ? (input.norm / normalLength) : float3(0.0f, 1.0f, 0.0f);

    // Key light (warm, from upper-front-right)
    float3 keyDir = normalize(float3(-0.6f, -1.0f, 0.5f));
    
    // 2. Use abs() for Two-Sided CAD Lighting (Lights the inside of vehicles/buildings)
    float key = abs(dot(n, -keyDir)) * 0.75f;
    
    // Fill light (cool, from left)
    float3 fillDir = normalize(float3(1.0f, -0.3f, -0.5f));
    float fill = abs(dot(n, -fillDir)) * 0.35f; // Slightly boosted for visibility
    
    // Lighting floor
    float light = key + fill + 0.15f;

    // 3. Premium "Clay" Fallback instead of flat grey
    float4 albedo = (HasTexture > 0.5f)
        ? DiffuseTexture.Sample(LinearSampler, input.tex)
        : float4(0.85f, 0.86f, 0.88f, 1.0f); // A slight blue/grey studio clay tint

    float3 lit = albedo.rgb * light + Ambient.rgb * 0.2f;
    
    // Clamp output to prevent blowing out whites
    return float4(saturate(lit), albedo.a);
}
