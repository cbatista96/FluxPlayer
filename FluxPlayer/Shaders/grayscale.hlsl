sampler2D input : register(s0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float g = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
    return float4(g, g, g, c.a);
}
