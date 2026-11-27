// WEATHER, SNOW (LIGHTING: ENHANCED only; not part of the original renderer): the snow trail map of Scripts/Rendering/ReflexSnowTrails.cs, a
// world-locked square around the camera that remembers where tyres went through the snow.
//   r  trench 0..1: how far the tyre pressed the snow down (1 = packed to the ground)
//   g  rim 0..1: the snow the tyre pushed aside, raised along both sides of the trench
// Pass 0 "Stamp": the new pieces of the core's tyre-track ribbons as capsules (one per ribbon triangle: its centre line, the tyre's half
//   width), written with BlendOp Max so crossing tracks keep the deepest trench. Across the capsule (distances in half widths): the packed
//   floor to 0.7, the wall to 1.3, the rim from 1.05 to 2.5 (highest at 1.6).
// Pass 1 "Fill": the snow that fell since the last fill is taken off both channels (BlendOp RevSub), so tracks fill and rims settle.
// Pass 2 "Scroll": the map copied to its new place when the square is moved after the camera (texels outside the old square: untouched snow).
Shader "Hidden/Reflex/SnowTrail"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Stamp"
            BlendOp Max
            Blend One One
            ColorMask RG
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _SnowRect;   // x0, z0 of the square (world), 1 / its size, unused
            struct Attributes { float3 position : POSITION; float4 uv : TEXCOORD0; };   // position.xy: world x, z; uv: along, across (half widths), length (half widths), strength
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float2 n = (v.position.xy - _SnowRect.xy) * _SnowRect.z * 2.0 - 1.0;
                o.positionCS = mul(UNITY_MATRIX_VP, float4(n, 0.5, 1));
                o.uv = v.uv;
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv.xy;
                float d = length(p - float2(clamp(p.x, 0.0, i.uv.z), 0.0));
                float k = i.uv.w;
                float trench = k * (1.0 - smoothstep(0.7, 1.3, d));
                float rim = k * smoothstep(1.05, 1.6, d) * (1.0 - smoothstep(1.6, 2.5, d));
                return float4(trench, rim, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Fill"
            BlendOp RevSub
            Blend One One
            ColorMask RG
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _SnowFill;   // x: off the trench, y: off the rim
            float4 Vert(uint id : SV_VertexID) : SV_POSITION { return float4(float2((id << 1) & 2, id & 2) * 2.0 - 1.0, 0.5, 1.0); }
            float4 Frag() : SV_Target { return float4(_SnowFill.xy, 0, 0); }
            ENDHLSL
        }

        Pass
        {
            Name "Scroll"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_SnowPrev); SAMPLER(sampler_SnowPrev);
            float4 _SnowShift;   // xy: added to a new texel's uv to find it in the old map
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                float2 q = float2((id << 1) & 2, id & 2);
                o.positionCS = float4(q * 2.0 - 1.0, 0.5, 1.0);
                o.uv = q;
            #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1.0 - o.uv.y;
            #endif
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv + _SnowShift.xy;
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_SnowPrev, sampler_SnowPrev, uv, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
