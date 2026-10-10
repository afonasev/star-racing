#ifndef STAR_RACING_CLOUD_SAMPLING
#define STAR_RACING_CLOUD_SAMPLING
sampler2D _CloudTex;
float _CloudY;
float3 CloudColor(float3 worldPoint, float3 eye) {
    float2 uv = worldPoint.xz / 950.0;
    float3 broad = tex2D(_CloudTex, uv).rgb;
    float3 detail = tex2D(_CloudTex, uv * 2.73 + float2(.37, .19)).rgb;
    float3 cloud = lerp(broad, detail, .18) * 1.18;
    float haze = smoothstep(800, 1700, length(worldPoint.xz - eye.xz));
    return lerp(cloud, float3(.70, .81, .91), haze);
}
#endif
