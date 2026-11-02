sampler2D input : register(s0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    return float4(1.0 - c.rgb, c.a);
}
