sampler2D input : register(s0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    return float4(dot(c.rgb, float3(0.393, 0.769, 0.189)), dot(c.rgb, float3(0.349, 0.686, 0.168)), dot(c.rgb, float3(0.272, 0.534, 0.131)), c.a);
}
