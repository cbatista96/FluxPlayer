sampler2D input : register(s0);
float4 texel : register(c0);   // x = 1 / width, y = 1 / height
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float4 n = tex2D(input, uv + float2(0, -texel.y)) + tex2D(input, uv + float2(0, texel.y)) + tex2D(input, uv + float2(-texel.x, 0)) + tex2D(input, uv + float2(texel.x, 0));
    return float4(saturate(c.rgb * 1.8 - n.rgb * 0.2), c.a);
}
