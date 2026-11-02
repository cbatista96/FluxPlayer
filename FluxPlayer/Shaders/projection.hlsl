// Equirectangular 360/180 degree video -> rectilinear view. Compile: fxc /T ps_3_0 /E main /Fo projection.ps projection.hlsl
sampler2D input : register(s0);
float4 view : register(c0);   // x = yaw, y = pitch, z = vertical field of view (radians), w = aspect ratio of the view
float4 mode : register(c1);   // x = 1 for a 180 degree picture (front hemisphere), 0 for 360

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float tanHalf = tan(view.z * 0.5);
    float2 p = uv * 2.0 - 1.0;
    p.y = -p.y;
    float3 dir = normalize(float3(p.x * tanHalf * view.w, p.y * tanHalf, 1.0));
    float cp = cos(view.y), sp = sin(view.y);
    dir = float3(dir.x, dir.y * cp - dir.z * sp, dir.y * sp + dir.z * cp);
    float cy = cos(view.x), sy = sin(view.x);
    dir = float3(dir.x * cy + dir.z * sy, dir.y, -dir.x * sy + dir.z * cy);
    float lon = atan2(dir.x, dir.z);
    float lat = asin(clamp(dir.y, -1.0, 1.0));
    float u = mode.x > 0.5 ? lon / 3.14159265 + 0.5 : lon / 6.28318531 + 0.5;
    float v = 0.5 - lat / 3.14159265;
    if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0) return float4(0, 0, 0, 1);
    return tex2D(input, float2(u, v));
}
