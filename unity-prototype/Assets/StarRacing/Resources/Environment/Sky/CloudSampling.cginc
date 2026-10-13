#ifndef STAR_RACING_CLOUD_SAMPLING
#define STAR_RACING_CLOUD_SAMPLING
sampler2D _CloudTex;
float _CloudY;
float3 CloudColor(float3 worldPoint, float3 eye) {
    float2 uv = worldPoint.xz / 950.0;
    float3 broad = tex2D(_CloudTex, uv).rgb;
    float3 detail = tex2D(_CloudTex, uv * 2.73 + float2(.37, .19)).rgb;
    // Broad cloud forms stay readable without competing with the nearby road texture.
    float3 cloud = lerp(broad, detail, .08);
    float luminance = dot(cloud, float3(.2126, .7152, .0722));
    cloud = lerp(float3(.78, .85, .93), lerp(luminance.xxx, cloud, .7), .78) * 1.08;
    float haze = smoothstep(800, 1700, length(worldPoint.xz - eye.xz));
    return lerp(cloud, float3(.70, .81, .91), haze);
}
#endif
