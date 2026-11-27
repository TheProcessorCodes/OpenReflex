// The northern lights of the imported MX vs ATV Unleashed level "Northern Lights" (Nat16), made new for OPEN REFLEX in place of the
// original's camera-following dome (two scrolling 256 px textures): procedural curtains, no textures.
// A dome around the camera at the far plane (drawn after the sky and the opaque world, depth-tested, additive). The view ray is cut
// by a stack of altitude layers; on each layer a warped ridge field gives the thin folded ribbons of a curtain seen from below, fine
// rays run along the ribbons, and the colour climbs from oxygen green at the foot through teal to a violet fringe. Slow drift and a
// slower swell of the whole display; a dither on the first layer hides the layer steps. Values are raw HDR (RfxOutput).
Shader "Reflex/Aurora"
{
    Properties
    {
        _Intensity ("Brightness (raw)", Float) = 0.34
        _Shape ("Ribbon width, curtain lean, fold, ray contrast", Vector) = (0.9, 0.75, 1.15, 1)
        _Detail ("Ray frequency, fold frequency, ribbon spacing, fine fold", Vector) = (36, 0.36, 3.0, 0.7)
        _Speed ("Drift speed", Float) = 1
        _ArcDir ("Arc: the heading it lies across (xz), width (w)", Vector) = (0.2, 0, 1, 0.55)
        _Low ("Foot colour", Color) = (0.10, 1.00, 0.42, 1)
        _Mid ("Middle colour", Color) = (0.05, 0.75, 0.80, 1)
        _High ("Fringe colour", Color) = (0.62, 0.22, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-490" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest LEqual Blend One One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Shaders/ReflexCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _ArcDir, _Low, _Mid, _High, _Shape, _Detail;
            float _Intensity, _Speed;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 p : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i)
            {
                V o; o.dir = i.p.xyz;
                // around the camera, at the far plane: behind everything that wrote depth
                o.p = TransformWorldToHClip(_WorldSpaceCameraPos + i.p.xyz * 100);
                #if UNITY_REVERSED_Z
                o.p.z = 1e-6 * o.p.w;
                #else
                o.p.z = (1 - 1e-6) * o.p.w;
                #endif
                return o;
            }
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            float Fbm(float2 p) { return Noise(p) * .55 + Noise(p * 2.03 + 7.1) * .3 + Noise(p * 4.01 + 3.7) * .15; }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                if (d.y < .015) return 0;
                float t = _Time.y * _Speed;
                float2 arc = normalize(_ArcDir.xz + 1e-4);
                float dither = Hash(i.p.xy + frac(t) * 13.7);
                const int Steps = 18;
                float3 sum = 0;
                for (int k = 0; k < Steps; k++)
                {
                    float a = (k + dither) / Steps;                    // 0 the foot of the curtain, 1 its top
                    float h = 1.0 + a * _Shape.y;                       // the layer's height above the eye (the curtain's lean when seen from the side)
                    float2 P = d.xz * (h / (d.y + .07));               // where the ray meets it (flattened toward the horizon)
                    // the display lies in an arc across the sky: `along` runs down the arc, `across` over it
                    float along = dot(P, arc), across = dot(P, float2(-arc.y, arc.x));
                    float2 q = float2(along, across);
                    // the folds: the ribbon's centre line wanders across the arc, slowly
                    float2 warp = float2(Fbm(q * _Detail.y + float2(t * .013, 3.1)), Fbm(q * _Detail.y * .8 + float2(7.7, -t * .011))) - .5;
                    // (a finer fold on top: the small S-bends that run along a curtain)
                    float fine = (Noise(float2(along * 1.9 + t * .06, across * .4)) - .5) * _Detail.w;
                    float c = across + (warp.x * 5.5 + sin(along * .21 + t * .05 + warp.y * 4) * 1.4 + fine) * _Shape.z;
                    // three ribbons side by side, each a thin ridge; they breathe along their length
                    float ridge = 0;
                    for (int r = 0; r < 3; r++)
                    {
                        float off = (r - 1) * _Detail.z + (Noise(float2(along * .07 + r * 9.1, t * .02)) - .5) * 2.2;
                        float w = (.07 + .26 * Noise(float2(along * .13 + r * 4.3, t * .03 + 1.7))) * _Shape.x * (1 + .5 * a);
                        float x = (c - off) / w;
                        float life = smoothstep(.25, .75, Noise(float2(along * .045 + r * 2.9, t * .017 + r)));
                        ridge += exp(-x * x) * life;
                    }
                    // rays: fine streaks along the ribbon, flickering upward
                    float rn = Noise(float2(along * _Detail.x + warp.x * 9, t * .3)) * .65 + Noise(float2(along * _Detail.x * 2.7 + 5.3, t * .5)) * .35;
                    float rays = lerp(1, .12 + 2.9 * rn * rn, saturate(_Shape.w));   // the same streak at every height: it stands up the curtain
                    // the curtain is brightest just above its foot and thins out to the fringe
                    float profile = smoothstep(0, .12, a) * exp(-a * 2.6) * 2.4 + .22 * exp(-a * .9);
                    float3 col = a < .45 ? lerp(_Low.rgb, _Mid.rgb, a / .45) : lerp(_Mid.rgb, _High.rgb, saturate((a - .45) / .4));
                    sum += col * ridge * rays * profile;
                }
                sum /= Steps;
                // the arc's width, the horizon's haze, a slow swell of the whole display
                float2 flat = d.xz / (d.y + .07);
                float band = exp(-pow(dot(flat, float2(-arc.y, arc.x)) / (9 * _ArcDir.w + 1e-3), 2));
                float swell = .65 + .35 * sin(t * .07) * sin(t * .031 + 1.3);
                float horizon = smoothstep(.015, .16, d.y);
                float3 c3 = sum * band * swell * horizon * _Intensity * 6;
                c3 = c3 / (1 + dot(c3, float3(.3, .5, .2)) * .6);   // the brightest folds roll off instead of burning to one flat colour
                return half4(RfxOutput(max(c3, 0)), 0);
            }
            ENDHLSL
        }
    }
}
