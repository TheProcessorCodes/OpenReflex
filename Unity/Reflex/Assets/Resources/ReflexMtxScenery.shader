// Imported MTX Mototrax venues: Neversoft's multi-pass materials in one pass. The base texture times the prelit vertex colour
// (0x80 = 1.0), then up to three layers, each on its own UV set (pass k uses set k) or sphere-mapped (environment passes),
// with the engine's blend modes: 1/2 add, 3/4 subtract, 5/6 blend, 7/8 modulate, 9/10 brighten, 12/13 blend by the previous
// pass's alpha (inverse). Each pass is tinted by its pass colour (x2; 0.5 = none); an environment base pass is sphere-mapped. Source alpha = texture alpha x vertex alpha (unless the pass ignores vertex alpha); the even modes
// use the pass's fixed alpha. The alpha cutoff clips the base texture's alpha. Billboards turn about the vertical axis
// through their pivot (TEXCOORD4) by their offsets (TEXCOORD5: across, up).
// The pass colours are raw constants (Vector properties: a Color property is converted gamma -> linear in the linear project, which
// darkened every tint that is not white).
// _MtxRaw (Content/ExperimentalMtxLighting.cs): the textures are raw (not sRGB) like every Reflex world texture, the result goes to Reflex's
// HDR target through RfxOutput with Reflex's distance fog, and the venue's MTX `lighting` tint (world: colours toward vc_target by
// vc_percent, x lev / 128; sky: x sky / 128) multiplies the prelit colours. 0 (REFLEX_MTX_LIGHT=legacy): sRGB textures, Unity fog, no tint.
// _ReflexLit (the MX vs ATV Unleashed levels, ExperimentalMtxScenery.MakeMaterial): the texture is an albedo lit by Reflex's rig - the fill
// lights and the shadowed sun, in ENHANCED with the sky's ambient, the screen occlusion and the weather - and the vertex colour (255 = 1.0,
// layers as the base) is the venue's baked visibility on it (white outdoors, the stands' shade in a stadium; the scenery casts no dynamic
// shadow), not a second lighting. _Foliage (leaf cards, the distant tree cards): thin two-sided leaves - the sun reaches the far side as
// transmitted light and the ambient comes from the sky above, so a card facing down or away is not black and the canopy keeps its shape.
// Blended materials (the Transparent queue) take the volumetric haze and the weather's mist in front of them (ReflexVolumetricApply.hlsl).
Shader "Reflex/Experimental MTX Scenery"
{
    Properties
    {
        _TreeLod ("Vegetation LOD: 1 model, 2 distant billboard",Float)=0
        _ReflexLit ("Reflex light rig",Float)=0
        _Foliage ("Foliage",Float)=0
        _MainTex ("Base",2D)="white" {}
        _L1Tex ("Layer 1",2D)="black" {}
        _L2Tex ("Layer 2",2D)="black" {}
        _L3Tex ("Layer 3",2D)="black" {}
        _L1Mode ("Layer 1 blend",Float)=0
        _L2Mode ("Layer 2 blend",Float)=0
        _L3Mode ("Layer 3 blend",Float)=0
        _L1Params ("Layer 1 fixed alpha, env, ignore VA, UV set",Vector)=(1,0,0,1)
        _L2Params ("Layer 2 fixed alpha, env, ignore VA, UV set",Vector)=(1,0,0,2)
        _L3Params ("Layer 3 fixed alpha, env, ignore VA, UV set",Vector)=(1,0,0,3)
        _BaseColor ("Base pass colour (raw)",Vector)=(1,1,1,1)
        _BaseEnv ("Base env map: on, tiling",Vector)=(0,1,1,0)
        _L1Color ("Layer 1 colour (raw)",Vector)=(1,1,1,1)
        _L2Color ("Layer 2 colour (raw)",Vector)=(1,1,1,1)
        _L3Color ("Layer 3 colour (raw)",Vector)=(1,1,1,1)
        _L1Tile ("Layer 1 env tiling",Vector)=(1,1,0,0)
        _L2Tile ("Layer 2 env tiling",Vector)=(1,1,0,0)
        _L3Tile ("Layer 3 env tiling",Vector)=(1,1,0,0)
        _BaseBlend ("Base alpha blend",Float)=0
        _Cutoff ("Alpha cutoff",Float)=0
        _Sky ("Sky",Float)=0
        _FixedAlpha ("Fixed alpha",Float)=0
        _IgnoreVertexAlpha ("Ignore vertex alpha",Float)=0
        _ColorScale ("Vertex colour scale",Float)=2
        _Billboard ("Billboard",Float)=0
        _SrcBlend ("Source blend",Float)=1
        _DstBlend ("Destination blend",Float)=0
        _ZWrite ("Depth write",Float)=1
        _MtxRaw ("Raw Reflex pipeline",Float)=0
        _MtxTint ("MTX lighting tint (rgb), vc target (w)",Vector)=(1,1,1,0)
        _MtxVcPercent ("MTX lighting vc percent",Float)=0
        _MtxFog ("Reflex fog: 0 none, 1 blend, 2 dim only",Float)=0
        _MtxSkyGamma ("Sky dome raw value exponent",Float)=1
        _BaseUvU ("Base UV transform: u' = dot(xy, uv) + z",Vector)=(1,0,0,0)
        _BaseUvV ("Base UV transform: v' = dot(xy, uv) + z",Vector)=(0,1,0,0)
        _L1UvU ("Layer 1 UV transform: u'",Vector)=(1,0,0,0)
        _L1UvV ("Layer 1 UV transform: v'",Vector)=(0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Shaders/ReflexCommon.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_L1Tex); SAMPLER(sampler_L1Tex);
            TEXTURE2D(_L2Tex); SAMPLER(sampler_L2Tex);
            TEXTURE2D(_L3Tex); SAMPLER(sampler_L3Tex);
            CBUFFER_START(UnityPerMaterial)
            float4 _L1Params,_L2Params,_L3Params,_L1Tile,_L2Tile,_L3Tile,_BaseColor,_BaseEnv,_L1Color,_L2Color,_L3Color,_MtxTint;
            float4 _BaseUvU,_BaseUvV,_L1UvU,_L1UvV;   // the Unleashed UV animators (UnleashedSceneryFx): identity otherwise
            float _ReflexLit,_Foliage,_TreeLod;
            float _L1Mode,_L2Mode,_L3Mode,_BaseBlend,_Cutoff,_Sky,_FixedAlpha,_IgnoreVertexAlpha,_ColorScale,_Billboard,_ZWrite,_MtxRaw,_MtxVcPercent,_MtxFog,_MtxSkyGamma,_SrcBlend,_DstBlend;
            CBUFFER_END
            float4 _UnleashedTreeCamera,_UnleashedTreeRange;   // (UnleashedVegetation) range: x near, y far, z the frame's dither offset (moves only while the TAA resolves it)
            struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv0:TEXCOORD0;float2 uv1:TEXCOORD1;float2 uv2:TEXCOORD2;float2 uv3:TEXCOORD3;float3 origin:TEXCOORD4;float2 offset:TEXCOORD5;half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V {float4 p:SV_POSITION;float4 uv01:TEXCOORD0;float4 uv23:TEXCOORD1;float2 env:TEXCOORD2;half4 color:COLOR;half fog:TEXCOORD3;float3 nw:TEXCOORD4;half4 rfog:TEXCOORD5;float3 world:TEXCOORD6;float treeFade:TEXCOORD7;};
            V vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;float3 w=TransformObjectToWorld(i.p.xyz);
                if(_Billboard>.5)
                {
                    float3 pivot=TransformObjectToWorld(i.origin);float3 view=_WorldSpaceCameraPos-pivot;view.y=0;
                    float3 across=normalize(float3(-view.z,0,view.x)+float3(1e-5,0,0));
                    float k=length(float3(UNITY_MATRIX_M._m00,UNITY_MATRIX_M._m10,UNITY_MATRIX_M._m20));   // MAP SCALE: the venue's root scale
                    w=pivot+(across*i.offset.x+float3(0,i.offset.y,0))*k;
                }
                float3 treePivot=_Billboard>.5?TransformObjectToWorld(i.origin):TransformObjectToWorld(float3(0,0,0));
                o.treeFade=saturate((distance(treePivot.xz,_UnleashedTreeCamera.xz)-_UnleashedTreeRange.x)/max(_UnleashedTreeRange.y-_UnleashedTreeRange.x,1));
                o.world=w;o.p=TransformWorldToHClip(w);o.uv01=float4(i.uv0,i.uv1);o.uv23=float4(i.uv2,i.uv3);o.color=i.color;
                o.rfog=half4(0,0,0,1);
                if(_MtxRaw>.5)
                {
                    o.color.rgb=lerp(i.color.rgb,_MtxTint.w,_MtxVcPercent)*_MtxTint.rgb;
                    if(_MtxFog>.5)o.rfog=RfxFog(w,_WorldSpaceCameraPos);
                }
                o.nw=TransformObjectToWorldNormal(i.n);float3 vn=normalize(TransformWorldToViewDir(o.nw));o.env=vn.xy*.5+.5;
                o.fog=ComputeFogFactor(o.p.z);return o;
            }
            // the depth passes (URP's depth prepass and SSAO): what the forward pass writes to depth, cut-outs clipped
            // the 3D plant (_TreeLod 1) / distant card (2) cross-fade: complementary screen-door dither. The pattern moves with the frame
            // while the TAA runs (it then resolves to the coverage instead of standing on the screen as a fixed grid); the shadow pass
            // (shadow-map pixels, no TAA) switches the 3D plant's shadow at the middle of the fade instead.
            void TreeClip(V i,bool shadowPass)
            {
                if(_TreeLod<.5)return;
                float noise=shadowPass?.5:frac(52.9829189*frac(dot(floor(i.p.xy)+5.588238*_UnleashedTreeRange.z,float2(.06711056,.00583715))));
                if(_TreeLod<1.5)clip(1-i.treeFade-noise-.0001);
                else clip(i.treeFade-noise-.0001);
            }
            float2 UvXf(float2 uv,float4 u,float4 v){return float2(dot(u.xy,uv)+u.z,dot(v.xy,uv)+v.z);}
            void DepthClip(V i,bool shadowPass)
            {
                TreeClip(i,shadowPass);
                if(_Sky>.5||_ZWrite<.5)clip(-1);
                if(_Cutoff>0)clip(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,UvXf(i.uv01.xy,_BaseUvU,_BaseUvV)).a-_Cutoff);
            }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite [_ZWrite] Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _RFX_ENHANCED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/ReflexEnhanced.hlsl"
            #include "../Shaders/ReflexShadows.hlsl"
            #include "../Shaders/ReflexVolumetricApply.hlsl"
            float2 LayerUV(V i,float4 params,float4 tile)
            {
                if(params.y>.5)return i.env*tile.xy;
                return params.w<1.5?i.uv01.zw:params.w<2.5?i.uv23.xy:i.uv23.zw;
            }
            void Apply(inout half3 c,inout half prevA,half4 l,float mode,float4 params,half3 vc,half va)
            {
                if(mode<.5)return;
                half3 s=l.rgb*vc;bool even=fmod(mode,2)<.5&&mode<11.5;half a=even?params.x:l.a*(params.z>.5?1:va);
                if(mode<2.5)c+=s*a;
                else if(mode<4.5)c=max(c-s*a,0);
                else if(mode<6.5)c=lerp(c,s,saturate(a));
                else if(mode<8.5)c*=lerp(1,s,saturate(even?params.x:1));
                else if(mode<10.5)c+=c*s*a;
                else if(mode<12.5)c=lerp(c,s,saturate(prevA));
                else c=lerp(c,s,saturate(1-prevA));
                prevA=l.a*(params.z>.5?1:va);
            }
            half4 frag(V i, bool front:SV_IsFrontFace):SV_Target
            {
                TreeClip(i,false);
                half4 t=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,_BaseEnv.x>.5?i.env*_BaseEnv.yz:UvXf(i.uv01.xy,_BaseUvU,_BaseUvV));
                half3 vc=i.color.rgb*_ColorScale*_BaseColor.rgb;half va=saturate(i.color.a*2),va0=_IgnoreVertexAlpha>.5?1:va;
                half3 c=t.rgb*vc;if(_ReflexLit>.5)c=min(c,1);   // (an albedo: texture x the stored colour x 2 stops at white, as the original's frame buffer did)
                half alpha=_BaseBlend>.5?t.a*(_FixedAlpha>0?_FixedAlpha:va0):t.a;half prevA=t.a*va0;
                half3 lvc=i.color.rgb*(_ReflexLit>.5?_ColorScale:2);   // (lit: the layers on the base's scale)
                if(_L1Mode>.5)Apply(c,prevA,SAMPLE_TEXTURE2D(_L1Tex,sampler_L1Tex,UvXf(LayerUV(i,_L1Params,_L1Tile),_L1UvU,_L1UvV)),_L1Mode,_L1Params,lvc*_L1Color.rgb,va);
                if(_L2Mode>.5)Apply(c,prevA,SAMPLE_TEXTURE2D(_L2Tex,sampler_L2Tex,LayerUV(i,_L2Params,_L2Tile)),_L2Mode,_L2Params,lvc*_L2Color.rgb,va);
                if(_L3Mode>.5)Apply(c,prevA,SAMPLE_TEXTURE2D(_L3Tex,sampler_L3Tex,LayerUV(i,_L3Params,_L3Tile)),_L3Mode,_L3Params,lvc*_L3Color.rgb,va);
                if(_Cutoff>0)clip(alpha-_Cutoff);
                float3 N=normalize(i.nw)*(front?1:-1);
                bool solid=_ZWrite>.5&&_Sky<.5;   // opaque and cut-out materials (in the depth texture); not the blended ones
                if(_ReflexLit>.5)
                {
                    float3 L=_RfxSunDir.xyz;float ndl=dot(N,L);
                    float3 Na=N;float direct=saturate(ndl);
                    if(_Foliage>.5)
                    {
                        // a leaf card: lit from its front, or through it from behind (half); a share of the rounded canopy's facing.
                        // The ambient is the sky's, from above, whichever way the card faces.
                        Na=normalize(N+float3(0,1.2,0));
                        direct=lerp(max(ndl,-.5*ndl),saturate(dot(Na,L)),.4);
                    }
                    float3 ambient=RfxAmbient(Na);
                    float shadow=RfxMainLightShadow(TransformWorldToShadowCoord(i.world));
                    #if defined(_RFX_ENHANCED)
                    float2 occ=RfxEnhOcclusion(i.p);
                    if(!solid)occ=float2(1,1);   // (a blended surface is not in the occlusion buffer: what lies behind it is)
                    ambient=RfxEnhAmbient(ambient,Na,occ.x);shadow*=occ.y;
                    #endif
                    float3 sun=_RfxSunColor.rgb*shadow;
                    c*=ambient+sun*direct;
                    #if defined(_RFX_ENHANCED)
                    // WEATHER (ReflexWeather.hlsl; nothing without it): snow settles on the solid surfaces, rain wets them
                    if(RfxWeatherOn()&&solid)
                    {
                        if(_Foliage>.5)c=RfxWeatherFoliage(c,i.world,N.y,ambient+sun*direct,.8);
                        else c=RfxWeatherObject(c,float4(0,0,0,1),i.world,N,normalize(i.world-_WorldSpaceCameraPos),ambient,sun,_RfxWeatherFog.rgb,1,.6);
                    }
                    #endif
                }
                #if defined(_RFX_ENHANCED)
                // prelit venues (MTX, Untamed: their own baked light) follow the weather's light; snow on the solid surfaces' upward faces
                else if(_MtxRaw>.5&&_Sky<.5&&!(_DstBlend>.5&&_DstBlend<1.5))c=RfxWeatherPrelit(c,i.world,N,solid?1:0);
                #endif
                if(_MtxRaw>.5)
                {
                    if(_MtxSkyGamma!=1)c=pow(max(c,0),_MtxSkyGamma);   // sky domes (ExperimentalMtxLighting.SkyGamma)
                    c=_MtxFog>1.5?c*i.rfog.w:_MtxFog>.5?c*i.rfog.w+i.rfog.rgb:c;
                    // VOLUMETRIC LIGHT / the weather's mist: a blended material draws after their composites and takes the medium in front of it here
                    // (computed, then selected: the shader compiler does not survive the call inside this branch)
                    float3 hazed=RfxVolApply(c,alpha,i.world,i.p,_SrcBlend,_DstBlend);
                    c=(_ZWrite<.5&&_Sky<.5)?hazed:c;
                    return half4(RfxOutput(max(c,0)),alpha);
                }
                if(_Sky<.5)c=MixFog(c,i.fog);
                return half4(c,alpha);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            V shadowVert(A a)
            {
                V o=vert(a);
                o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.nw),_LightDirection));
                #if UNITY_REVERSED_Z
                o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
                #else
                o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
                #endif
                return o;
            }
            half4 shadowFrag(V i):SV_Target { DepthClip(i,true); return 0; }
            ENDHLSL
        }
        // MTX's ground and scenery in the camera's depth (and normals) textures: the MODERN look's haze, flare smoke and
        // soft particles stop at MTX's terrain like at the game's own
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment depthFrag
            half depthFrag(V i):SV_Target { DepthClip(i,false); return i.p.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment normalsFrag
            half4 normalsFrag(V i, bool front:SV_IsFrontFace):SV_Target { DepthClip(i,false); float3 n=normalize(i.nw)*(front?1:-1); return half4(n,0); }
            ENDHLSL
        }
    }
}
