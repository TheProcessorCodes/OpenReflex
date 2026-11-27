// MATERIALS: ENHANCED (OPTIONS > VIDEO, with LIGHTING: ENHANCED; not part of the original renderer): physically based bike / rider surfaces and
// mud / dirt build-up over Reflex/Vehicle's ported object programs (Shaders/ReflexVehicle.shader). The original program of the material still runs
// first and gives the base look (the game's albedo, decals, masks, fill lights, shadows); this file adds to its result, per material CLASS
// (Content/ReflexMaterialClasses.cs sets _RfxMat.x from the material's shader / material / texture names):
//   the original's environment reflection   scaled down (rfxReflScale, RfxReflection in ReflexObjectShading.hlsl): replaced by the GGX terms below
//   specular                                a GGX sun highlight and the environment's reflection (the level's reflection cube: in ENHANCED the HDRI
//                                           sky) through the split-sum approximation, a clear coat lobe over plastics / paint / helmets, F0 from
//                                           the metal class (chrome, anodised, brushed, painted frame)
//   fabric sheen, micro grain               a grazing-angle sheen for cloth, weave / pebble / rubber grain in the albedo (no new textures)
//   mud / dirt                              _RfxDirt (Content/ReflexDirt.cs): a mask from the vertex's rest-pose height (uv3: ground = 0), the
//                                           part's affinity (wheels > boots > frame > seat > jersey), fractal noise in rest space and the side
//                                           the rear wheel throws it to. The dirt has a thickness: a bevelled edge where it begins, clods and
//                                           grain on it (a height field in rest space, bumped), crevices between the clods darker, the surface
//                                           under it loses its gloss by the coverage (as the original's mud term did: spec x (1 - mud)); dry
//                                           dust where thin, caked mud where thick, darker and glossy when wet; the graphics show through the
//                                           thin dirt and wherever the dirt does not reach
//   the material's own maps                 the program's shading normal (rfxExpN: its normal map), the SFX / combo map's shine channel and the
//                                           diffuse map where the program's slots are known (_RfxMatMap, Content/ReflexMaterialClasses.cs), a
//                                           .rxmod material's normal map (through the surface's own UV frame: no vertex tangents needed)
// Everything is in the original's raw (display-referred) units like the programs' own output, lit by the same rig as the programs (RfxObjRig:
// the fill lights, the bounce and the sun's radiosity through the level's colour reference map, the volume shadow / MapShadow term, the
// shadow term the fragment gives the program) so the new layers sit in the level's light.
#ifndef REFLEX_VEHICLE_PBR_INCLUDED
#define REFLEX_VEHICLE_PBR_INCLUDED

float _RfxMatOn;       // global: MATERIALS: ENHANCED in effect (ReflexEnhancedMaterials; needs LIGHTING: ENHANCED)
float _RfxDirtOn;      // global: MUD BUILDUP in effect

// ---------------------------------------------------------------------------------------------------------------------------- classes
struct RfxSurf
{
    float metal, rough, cc, ccRough, f0, sheen, origRefl, gain, dirtAff, dirtH, grain, grainScale, chrome;
};

// k: ReflexMaterialClasses.Kind
RfxSurf RfxSurfOf(int k)
{
    RfxSurf s;
    s.metal = 0; s.rough = 0.5; s.cc = 0; s.ccRough = 0.1; s.f0 = 0.04; s.sheen = 0; s.origRefl = 0; s.gain = 1; s.dirtAff = 0.8; s.dirtH = 1.0;
    s.grain = 0; s.grainScale = 60; s.chrome = 0;
    switch (k)
    {
        case 1:  s.rough = 0.42; s.cc = 1.0;  s.ccRough = 0.07; s.dirtAff = 0.85; s.dirtH = 1.0; s.grain = 0.01; break;                       // PAINT PLASTIC (graphic kit plastics, fenders)
        case 2:  s.rough = 0.62; s.cc = 0.2;  s.ccRough = 0.22; s.dirtAff = 0.95; s.dirtH = 1.1; s.grain = 0.03; s.grainScale = 110; break;    // MATTE PLASTIC (hand guards, nerf bars, neck brace)
        case 3:  s.metal = 0.62; s.rough = 0.46; s.cc = 0.5;  s.ccRough = 0.1; s.dirtAff = 1.05; s.dirtH = 1.0; s.grain = 0.015; break;       // FRAME (painted / powder-coated metal)
        case 4:  s.metal = 1.0;  s.rough = 0.07; s.dirtAff = 0.7; s.dirtH = 0.9; s.chrome = 1; break;                                        // CHROME
        case 5:  s.metal = 1.0;  s.rough = 0.2;  s.dirtAff = 0.9; s.dirtH = 0.9; break;                                                      // ANODISED
        case 6:  s.metal = 1.0;  s.rough = 0.42; s.dirtAff = 1.0; s.dirtH = 0.9; s.grain = 0.04; s.grainScale = 220; break;                   // BRUSHED METAL (engine, muffler)
        case 7:  s.rough = 0.88; s.f0 = 0.045; s.gain = 0.55; s.dirtAff = 1.45; s.dirtH = 2.0; s.grain = 0.05; s.grainScale = 160; break;      // RUBBER TYRE
        case 8:  s.rough = 0.64; s.cc = 0.08; s.ccRough = 0.3; s.dirtAff = 0.6; s.dirtH = 0.85; s.grain = 0.04; s.grainScale = 90; break;     // SEAT / GRIP
        case 9:  s.rough = 0.96; s.gain = 0.35; s.sheen = 1.0; s.dirtAff = 0.8; s.dirtH = 1.3; s.grain = 0.07; s.grainScale = 380; break;      // FABRIC (dirtH: the rest pose stands; 0.8 left the jersey's torso clean at any amount)
        case 10: s.rough = 0.5;  s.cc = 0.35; s.ccRough = 0.3; s.dirtAff = 1.25; s.dirtH = 0.5; s.grain = 0.04; s.grainScale = 150; break;    // LEATHER / BOOT
        case 11: s.rough = 0.4;  s.cc = 1.0;  s.ccRough = 0.035; s.dirtAff = 0.25; s.dirtH = 2.2; s.grain = 0.008; break;                     // HELMET SHELL (specks at the highest amounts only)
        case 12: s.rough = 0.04; s.f0 = 0.06;  s.gain = 1.6; s.dirtAff = 0.12; s.dirtH = 0.5; break;                                          // GOGGLE LENS
        case 13: s.rough = 0.55; s.cc = 0; s.f0 = 0.028; s.gain = 0.5; s.sheen = 0.2; s.dirtAff = 0.45; s.dirtH = 0.6; break;                // SKIN
        case 14: s.metal = 0.28; s.rough = 0.4;  s.cc = 1.0;  s.ccRough = 0.05; s.dirtAff = 0.9; s.dirtH = 1.4; s.grain = 0.01; break;       // BODY PAINT (trucks, buggies, UTVs)
        case 15: s.metal = 0.9;  s.rough = 0.3;  s.dirtAff = 1.2; s.dirtH = 1.2; break;                                                      // WHEEL RIM / HUB
        case 16: s.metal = 1.0;  s.rough = 0.26; s.dirtAff = 0.8; s.dirtH = 1.0; break;                                                      // SPRING / SHOCK
        default: break;
    }
    return s;
}

// the original program's own environment reflection kept for the class (0: all of it replaced by the terms below)
float RfxMatOrigReflection(int k) { return RfxSurfOf(k).origRefl; }
// the original program's own sun highlight kept for the class (rfxSunSpecKeep, ReflexObjectPrograms.hlsl): a clear-coat class takes its
// highlight from the coat's GGX lobe below, so the program's pow highlight on the same spot is dropped (one highlight, not two)
float RfxMatSunSpecKeep(int k) { return RfxSurfOf(k).cc >= 0.35 ? 0.0 : 1.0; }

// ---------------------------------------------------------------------------------------------------------------------------- noise
float RfxHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float RfxValueNoise(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(lerp(RfxHash(i), RfxHash(i + float3(1, 0, 0)), f.x), lerp(RfxHash(i + float3(0, 1, 0)), RfxHash(i + float3(1, 1, 0)), f.x), f.y);
    float b = lerp(lerp(RfxHash(i + float3(0, 0, 1)), RfxHash(i + float3(1, 0, 1)), f.x), lerp(RfxHash(i + float3(0, 1, 1)), RfxHash(i + float3(1, 1, 1)), f.x), f.y);
    return lerp(a, b, f.z);
}
// a fixed rotation (the value noise's grid shows as axis-aligned streaks when two octaves share its axes)
float3 RfxRot(float3 p) { return float3(0.36 * p.x + 0.48 * p.y - 0.8 * p.z, -0.8 * p.x + 0.6 * p.y, 0.48 * p.x + 0.64 * p.y + 0.6 * p.z); }
float RfxFbm2(float3 p) { return RfxValueNoise(p) * 0.6 + RfxValueNoise(RfxRot(p) * 2.17 + 31.7) * 0.4; }
float RfxFbm(float3 p)
{
    float a = 0.5, s = 0, w = 0;
    [unroll] for (int o = 0; o < 3; o++)
    {
        s += a * RfxValueNoise(p); w += a; p = p * 2.03 + 17.1; a *= 0.5;
    }
    return s / w;
}

// a screen-space bump (Mikkelsen's surface gradient): the normal tilted by the height field's screen derivatives (taken by the caller
// outside any divergent branch)
float3 RfxBumpFromHeight(float3 dpdx, float3 dpdy, float dhx, float dhy, float3 N, float k)
{
    float3 r1 = cross(dpdy, N), r2 = cross(N, dpdx);
    float det = dot(dpdx, r1);
    float3 grad = sign(det) * (dhx * r1 + dhy * r2);
    return normalize(abs(det) * N - k * grad);
}

// ---------------------------------------------------------------------------------------------------------------------------- BRDF
float3 RfxEnvBRDF(float3 F0, float rough, float NoV)
{
    // Karis' analytic split-sum (mobile): scale and bias of F0
    const float4 c0 = float4(-1, -0.0275, -0.572, 0.022), c1 = float4(1, 0.0425, 1.04, -0.04);
    float4 r = rough * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
    return F0 * AB.x + AB.y;
}
float RfxGGX(float NoH, float a2)
{
    float d = NoH * NoH * (a2 - 1.0) + 1.0;
    return a2 / (3.14159265 * d * d + 1e-6);
}
float RfxVisSmith(float NoV, float NoL, float a2)
{
    float gv = NoL * sqrt(NoV * NoV * (1.0 - a2) + a2), gl = NoV * sqrt(NoL * NoL * (1.0 - a2) + a2);
    return 0.5 / max(gv + gl, 1e-5);
}
float3 RfxFresnelSchlick(float3 F0, float VoH) { float f = pow(1.0 - saturate(VoH), 5.0); return F0 + (1.0 - F0) * f; }

// the environment in direction R at a perceptual roughness: the level's reflection cube (ENHANCED: the HDRI sky), raw HDR units.
// A level without a reflection map and without a sky (_RfxReflectionOn 0: indoor / night levels; the global then still holds the last
// level's cube, or none): rfxMatEnvFallback, the light of the level's rig around the surface (set by RfxVehicleMaterial), so metals do not
// reflect another level's sky or go black. The blur follows the cube's own mip count (the level maps differ; the sky cube has 7).
static float3 rfxMatEnvFallback = float3(0, 0, 0);
float3 RfxMatEnv(float3 R, float rough)
{
    if (_RfxReflectionOn < 0.5) return rfxMatEnvFallback;
    float maxMip = _RfxEnhParams2.z;
    if (_RfxEnhParams2.w < 0.5)
    {
        uint w, h, levels;
        _RfxReflectionCube.GetDimensions(0, w, h, levels);
        maxMip = max((float)levels - 1.0, 0.0);
    }
    return SAMPLE_TEXTURECUBE_LOD(_RfxReflectionCube, sampler_RfxReflectionCube, R, rough * (1.7 - 0.7 * rough) * maxMip).rgb;
}

// geometric specular anti-aliasing: the roughness widened by the normal's variance over the pixel (Kaplanyan / Tokuyoshi)
float RfxSpecAA(float3 N, float a2)
{
    float3 dx = ddx(N), dy = ddy(N);
    float variance = 0.25 * (dot(dx, dx) + dot(dy, dy));
    return saturate(a2 + min(2.0 * variance, 0.18));
}

float RfxLuma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

// ---------------------------------------------------------------------------------------------------------------------------- the light
// The light the original object programs give a surface at P with normal N (e.g. RfxPs0 071c..09e4): the four fill lights; the bounce from
// below through the level's colour reference (s13 at gColorRefProjection: sat(-N.y) lerp(ref.rgb, gBounceLightColor, ref.a)); the sun's
// visibility of the volume shadow map (s12 through RfxVolumeShadowTexel: the Alive MapShadow included) and its radiosity floor(ref.a) ? ref.rgb : 1.
// The radiosity is what darkens a vehicle in the unlit parts of a stadium (the colour reference maps of the supercross / night levels: the
// light pools under the rigs) - the new layers must take it, or they glow there (w-mudlight).
// amb: the ambient (before ENHANCED's sky tint), sunK: what multiplies _RfxSunColor before the dynamic shadow and N.L.
void RfxObjRig(float3 P, float3 N, out float3 amb, out float3 sunK)
{
    float3 n = normalize(N);
    float4 ref = RfxColorRefAt(P);
    amb = saturate(dot(_RfxLightDir1.xyz, n)) * _RfxLightColor1.rgb + saturate(dot(_RfxLightDir0.xyz, n)) * _RfxLightColor0.rgb
        + saturate(dot(_RfxLightDir2.xyz, n)) * _RfxLightColor2.rgb + saturate(dot(_RfxLightDir3.xyz, n)) * _RfxLightColor3.rgb
        + saturate(-n.y) * lerp(ref.rgb, _RfxBounceColor.rgb, ref.a);
    float2 vuv = float2(dot(float4(P, 1), RfxWorldProj0()), dot(float4(P, 1), RfxWorldProj1()));
    float vol = saturate(1.0 - 0.25 * (RfxVolumeShadowTexel(vuv).x * 2048.0 - P.y - 0.1));
    sunK = vol * lerp(float3(1, 1, 1), ref.rgb, floor(ref.a));
}

// ---------------------------------------------------------------------------------------------------------------------------- the material's maps
// _RfxMatMap (Content/ReflexMaterialClasses.cs; ReflexBikeModel.RxMod.cs for z): x the SFX / combo map's slot + 1 and y its shine channel
// (0 r, 2 b) where the program's slots are known, z the slot + 1 of a normal map only ENHANCED reads (a .rxmod material's, on a program
// without a normal map of its own), w the diffuse map's slot + 1; _RfxMat.z: the diffuse may stand for the albedo (metal F0).
// One of the material's textures by its slot, at the mesh's first UV set (what the programs sample their maps with), through the samplers
// the programs already use; gradients from the caller (sampled inside branches).
float4 RfxMatTex(int slot, float2 uv, float2 dx, float2 dy)
{
    switch (slot)
    {
        case 0: return SAMPLE_TEXTURE2D_GRAD(_Tex0, sampler_Tex0, uv, dx, dy);
        case 1: return SAMPLE_TEXTURE2D_GRAD(_Tex1, sampler_Tex1, uv, dx, dy);
        case 2: return SAMPLE_TEXTURE2D_GRAD(_Tex2, sampler_Tex2, uv, dx, dy);
        case 3: return SAMPLE_TEXTURE2D_GRAD(_Tex3, sampler_Tex3, uv, dx, dy);
        case 6: return SAMPLE_TEXTURE2D_GRAD(_Tex6, sampler_Tex1, uv, dx, dy);   // (the ENHANCED-only normal map: no sampler of its own)
        default: return float4(0, 0, 0, 1);
    }
}

// a tangent-space normal (signed x / y, as the tyre program's map) on the surface's own UV frame, from the screen derivatives of the
// position and the UVs: T the direction of growing u square to N, the programs' bitangent T x N (ReflexObjectPrograms.hlsl RfxVs0)
float3 RfxMatDetailNormal(float3 N, float3 dpdx, float3 dpdy, float2 duvx, float2 duvy, float2 nxy)
{
    float det = duvx.x * duvy.y - duvy.x * duvx.y;
    float3 T = (dpdx * duvy.y - dpdy * duvx.y) * (det < 0 ? -1.0 : 1.0);
    T -= N * dot(N, T);
    float l = dot(T, T);
    if (l < 1e-20) return N;
    T *= rsqrt(l);
    float3 B = cross(T, N);
    return normalize(nxy.x * T + nxy.y * B + sqrt(saturate(1.0 - dot(nxy, nxy))) * N);
}

// ---------------------------------------------------------------------------------------------------------------------------- the layers
// c: the original program's result (fog included). P / N / rp: world position, shading normal (the program's: normal-mapped where it is),
// rest-pose position in metres. occ: (ao, contact shadow) of ReflexOcclusionPass, shadow: Unity's main light shadow at the pixel (1 without),
// fog: RfxFog(P) (rgb, 1 - amount). mask: the SFX map's shine 0..1 (-1: none), albedo: the surface's own colour, the program's albedo or the diffuse map (a < 0: none: estimated
// from c), specKeep: what the mud over the surface leaves of its gloss (1 - the coverage).
float3 RfxMatLayers(float3 c, int k, float3 P, float3 N, float3 rp, float2 occ, float shadow, float4 fog, bool front, float mask, float4 albedo, float specKeep, inout float alpha)
{
    RfxSurf s = RfxSurfOf(k);
    float3 V = normalize(_WorldSpaceCameraPos - P);
    float NoVraw = dot(N, V);
    if (NoVraw < 0) { N = -N; NoVraw = -NoVraw; }
    float NoV = max(NoVraw, 1e-3);
    float3 L = normalize(_RfxSunDir.xyz);
    float NoL = saturate(dot(N, L));
    float3 amb, sunK;
    RfxObjRig(P, N, amb, sunK);
    float aoAmb = lerp(1.0, occ.x, _RfxEnhParams.y);
    #if defined(_RFX_ENHANCED)
    amb = RfxEnhAmbient(amb, N, occ.x);
    #endif
    float3 sunC = _RfxSunColor.rgb * sunK * (shadow * occ.y);
    // the SFX map's shine: glossier and stronger where the map says so, duller where it does not (the class's constants at its middle)
    float rough = saturate(s.rough + _RfxMat.y + (mask >= 0 ? lerp(0.12, -0.06, mask) : 0.0));
    float gain = s.gain * (mask >= 0 ? lerp(0.5, 1.2, mask) : 1.0) * specKeep;

    // ---- micro grain in the albedo (weave / pebble / rubber), faded where it would shimmer
    float grainFade = 0;
    if (s.grain > 0)
    {
        float3 q = rp * s.grainScale;
        grainFade = saturate(1.0 - length(fwidth(q)) * 0.6);
        float g;
        if (k == 9) g = (sin(q.x) * sin(q.y) + sin(q.y) * sin(q.z) + sin(q.z) * sin(q.x)) * 0.33;   // weave
        else g = RfxValueNoise(q) * 2.0 - 1.0;
        c *= 1.0 + s.grain * g * grainFade;
    }

    // ---- albedo for F0: the hue and a share of the luminance of the material's diffuse map where it is known (the surface's own colour:
    // the same in sun and shade, near and far), else of the colour the program gave (lit and fogged: an estimate)
    float3 src = albedo.a >= 0 ? albedo.rgb : c;
    float lum = RfxLuma(src);
    float3 tint = saturate((src + 0.03) / (lum + 0.03));
    float share = 0.55 + 0.45 * saturate(lum * 1.4);
    // a surface whose own colour is known and dark (a black rim, a black A-arm or frame, a carbon pipe: painted, anodised or no metal at all)
    // is not bare metal: its metalness falls with the albedo's luminance, so it keeps its own dark diffuse and reflects as a dark coated
    // surface (F0 0.04) instead of as alloy; the share of the luminance follows it down. Chrome stays chrome (its maps are painted
    // reflections); the lit estimate keeps the floor (a bright metal in the shade is dark there too)
    float metal = s.metal;
    if (albedo.a >= 0 && s.chrome < 0.5)
    {
        float bare = smoothstep(0.05, 0.22, lum);
        metal *= bare;
        share = lerp(lum, share, bare);
    }
    float3 albedoF = saturate(tint * share);

    // ---- specular: base layer
    float3 F0d = s.f0.xxx;
    float3 F0 = lerp(F0d, s.chrome > 0.5 ? lerp(float3(0.93, 0.93, 0.93), tint, 0.2) : albedoF * 0.95 + 0.03, metal);
    float a = rough * rough;
    float a2 = RfxSpecAA(N, a * a);
    float3 H = normalize(L + V);
    float NoH = saturate(dot(N, H)), VoH = saturate(dot(V, H));
    float3 R = reflect(-V, N);
    float specOcc = saturate(pow(NoV + occ.x, exp2(-16.0 * rough - 1.0)) - 1.0 + occ.x);
    float3 envBase = RfxMatEnv(R, rough) * specOcc;
    float3 specBase = envBase * RfxEnvBRDF(F0, rough, NoV);
    float3 sunSpecBase = RfxFresnelSchlick(F0, VoH) * RfxGGX(NoH, a2) * RfxVisSmith(NoV, NoL, a2) * NoL * 3.14159265 * sunC;

    // ---- clear coat over it
    float3 specCC = 0, sunSpecCC = 0; float ccAtten = 1;
    if (s.cc > 0)
    {
        float cr = s.ccRough, ca = cr * cr, ca2 = RfxSpecAA(N, ca * ca);
        float Fcc = 0.04 + 0.96 * pow(1.0 - NoV, 5.0);
        float3 envCC = RfxMatEnv(R, cr) * specOcc;
        specCC = envCC * RfxEnvBRDF(float3(0.04, 0.04, 0.04), cr, NoV) * s.cc * 2.0;   // (art direction: the sky in a clear coat reads stronger than physical)
        sunSpecCC = (0.04 + 0.96 * pow(1.0 - VoH, 5.0)) * RfxGGX(NoH, ca2) * RfxVisSmith(NoV, NoL, ca2) * NoL * 3.14159265 * sunC * s.cc * 1.5;
        ccAtten = 1.0 - Fcc * s.cc;
    }

    // ---- the sum: the original's diffuse (metals keep part of it: their textures paint the metal), the new specular over it. The sun's glint
    // of a clear-coat class is the coat's alone: the program's own pow highlight is off for it (RfxMatSunSpecKeep)
    float diffuseKeep = 1.0 - 0.62 * metal;
    float3 spec = (specBase * ccAtten + specCC + (sunSpecBase * ccAtten + sunSpecCC)) * gain;
    c = (c - fog.rgb) * diffuseKeep + fog.rgb + spec * fog.w;

    // ---- sheen
    if (s.sheen > 0)
    {
        float f = pow(1.0 - NoV, 2.5);
        float3 litv = amb * aoAmb + sunC * NoL;
        c += (c * 0.9 + 0.05 * litv) * f * s.sheen * specKeep * fog.w;
    }
    // ---- lens: the reflection shows through the (blended) tint
    if (k == 12) alpha = saturate(alpha + RfxLuma(spec) * 0.8);
    return c;
}

// ----------------------------------------------------------------------------------------------------------------------------- mud / dirt
// The dirt as a layer with a thickness, in rest space (metres): where it is (the coverage), how thick (0 thin dust .. 1 caked) and its
// surface (a height, 1 = about a centimetre: the bevel where the layer begins, clods with creases between them, a pebbly
// top, thrown specks past the edge, grain). (The shapes were tuned on a plane outside the game before the in-game shots.)
struct RfxDirtField { float m, edge, core, height, relief, tone; };

RfxDirtField RfxDirtFieldAt(float3 rp, float3 N, float amount, float aff, float H, float seed, float fadeClod, float fadeFine, float fadeGrain)
{
    RfxDirtField o; o.m = 0; o.edge = 0; o.core = 0; o.height = 0; o.relief = 0; o.tone = 0.5;
    float h = rp.y;
    float cover = amount * aff * saturate(1.15 - h / H);
    float back = saturate(dot(N, -_RfxDirtF.xyz) * 0.5 + 0.5);               // the rear wheel's roost lands behind and below
    cover *= lerp(0.82, 1.18, back);
    if (cover < 0.035) return o;                                             // (above the dirt's reach: no noise at all)
    float w = smoothstep(0.035, 0.15, cover);                                // (the noise comes in with the cover: no line at the dirt's reach)
    float n = RfxFbm(rp * 5.0 + seed);                                       // blotches (20 cm)
    // clods (2 - 3 cm): a warped two-octave noise, its high parts plateaus with a bevel (caked lumps, creases between them)
    float cn = lerp(0.5, RfxFbm2(RfxRot(rp) * 42.0 + seed * 1.7 + (n - 0.5) * 5.0), fadeClod);
    float clod = smoothstep(0.40, 0.66, cn) * (0.7 + 0.3 * smoothstep(0.62, 0.85, cn));
    float speck = 0, peb = 0.5, grain = 0.5;
    [branch] if (fadeFine > 0.03)
    {
        // thrown specks and drips out past the caked edge: the peaks of a noise stretched down the vehicle, warped (no two alike)
        float sn = RfxFbm2(RfxRot(rp * float3(1.0, 0.5, 1.0)) * 64.0 + seed * 3.1 + (cn - 0.5) * 2.0);
        speck = smoothstep(0.67, 0.82, sn) * fadeFine;
        peb = RfxValueNoise(RfxRot(rp) * 110.0 + seed * 2.3);                // the caked mud's pebbly top (1 cm)
        peb = lerp(0.5, 1.0 - abs(2.0 * peb - 1.0), fadeFine);
        grain = lerp(0.5, RfxValueNoise(rp * 210.0 + seed * 5.3), fadeGrain);   // grain (5 mm)
    }
    float d0 = cover * 1.6 - 0.4 + w * ((n - 0.5) * 0.9 + (cn - 0.5) * 0.5);
    float d = d0 + w * speck * 0.55;
    o.edge = smoothstep(0.0, 0.14, d);                                       // the layer begins
    o.core = smoothstep(0.1, 0.6, d0);                                       // caked
    float bevel = smoothstep(0.0, 0.14, d0);                                 // the caked layer's own edge (a speck is a low dome: no step around it)
    o.m = o.edge * lerp(0.8, 0.96, o.core);
    o.relief = saturate(0.25 + clod * 0.6 * o.core + speck * (1.0 - o.core) * 0.5 + (peb - 0.5) * 0.3);   // 0 in the creases .. 1 on the clods
    o.height = bevel * (0.3 + 0.2 * o.core) + o.core * (clod * 0.34 + (peb - 0.5) * 0.10) + o.edge * ((grain - 0.5) * 0.06 + speck * (1.0 - bevel) * 0.1);
    o.tone = lerp(cn, n, 0.5);
    return o;
}

// f: the field at the pixel, Nd: the dirt's bumped normal, nVar: its variance over the pixel (all from RfxVehicleMaterial: derivatives are
// taken outside the branches)
float3 RfxDirtLayer(float3 c, RfxDirtField f, float3 P, float3 N, float3 Nd, float nVar, float2 occ, float shadow, float4 fog)
{
    float amount = _RfxDirt.x, wet = _RfxDirt.y;
    float3 V = normalize(_WorldSpaceCameraPos - P);
    float NoV = max(dot(Nd, V), 1e-3);
    float3 L = normalize(_RfxSunDir.xyz);
    float NoL = saturate(dot(Nd, L));
    // (the rig's fill lights and the sky take the relief at half strength: at full strength every clod turned a face to another light and
    // the caked mud read as crumpled foil in the shade)
    float3 Na = normalize(lerp(N, Nd, 0.5));
    float3 amb, sunK;
    RfxObjRig(P, Na, amb, sunK);
    #if defined(_RFX_ENHANCED)
    amb = RfxEnhAmbient(amb, Na, occ.x);
    #endif
    float aoAmb = lerp(1.0, occ.x, _RfxEnhParams.y);
    float3 sunC = _RfxSunColor.rgb * sunK * (shadow * occ.y);
    // the dirt itself: the level's mud colour; dust where thin, darker caked mud where thick, lighter on the clods' drying tops, darker in
    // the crevices between them; wet mud darker
    float3 mud = saturate(_RfxDirtColor.rgb * 1.9 + 0.07);
    float3 dust = saturate(_RfxDirtColor.rgb * 2.3 + 0.2);
    float thick = saturate(f.core * 0.85 + amount * 0.35);
    float3 alb = lerp(dust, mud, thick);
    alb *= lerp(0.86, 1.12, f.relief) * (0.9 + 0.2 * f.tone);
    alb *= 1.0 - 0.36 * wet;
    // the crevices between the clods take less of the sky and (less so) of the sun; the slopes that face the eye read lighter than those that
    // turn away (the relief shows in flat light and in the shade too, where the rig's light alone would leave it flat)
    float cav = lerp(1.0, lerp(0.66, 1.0, smoothstep(0.2, 0.7, f.relief)), f.core);
    float facing = saturate(0.5 + (dot(Nd, V) - dot(N, V)) * 1.6);
    float3 lit = (amb * aoAmb * cav + sunC * NoL * lerp(cav, 1.0, 0.55)) * lerp(0.9, 1.1, facing);
    float3 dirtC = alb * lit;
    // a share of the original's colour shows through the thin dirt (the graphics never disappear under dust)
    dirtC = lerp(dirtC, c * alb * 1.2, 0.14 * (1.0 - f.core));
    // gloss: dry dirt is rough with a faint sheen on the clods, wet mud shines (the film sits on the clods: glints on every lump)
    float rough = saturate(lerp(0.72, 0.27, wet) + (0.5 - f.relief) * lerp(0.16, 0.08, wet));
    float a = rough * rough, a2 = saturate(a * a + min(2.0 * nVar, 0.18));
    float3 H = normalize(L + V);
    float NoH = saturate(dot(Nd, H)), VoH = saturate(dot(V, H));
    float3 F0 = lerp(0.035, 0.05, wet).xxx;
    float3 sp = RfxFresnelSchlick(F0, VoH) * RfxGGX(NoH, a2) * RfxVisSmith(NoV, NoL, a2) * NoL * 3.14159265 * sunC;
    sp += RfxMatEnv(reflect(-V, Nd), rough) * RfxEnvBRDF(F0, rough, NoV) * lerp(0.4, 0.75, wet) * saturate(occ.x + 0.2) * cav;
    dirtC += sp;
    float3 dirtFinal = dirtC * fog.w + fog.rgb;
    return lerp(c, dirtFinal, f.m);
}

// every ENHANCED layer over the program's result; call only when _RfxMatOn > 0.5 and the material has a class.
// N: the vertex normal (world), Ns: the program's shading normal (its normal map; = N without one), uv: the mesh's first UV set.
// nOut: the normal of what is seen at the pixel (the material's, the dirt's where it lies), facing the eye: the rain film and the menu light.
float4 RfxVehicleMaterial(float4 c, int k, float3 P, float3 N, float3 Ns, float3 restPos, float2 uv, float2 occ, float shadow, bool front, out float3 nOut)
{
    float unit = _RfxDirt.z > 0 ? _RfxDirt.z : 1.0;                         // model units per metre
    float3 rp = restPos / unit;
    float4 fog = RfxFog(P, _WorldSpaceCameraPos);
    float alpha = c.a;
    N = normalize(N); Ns = normalize(Ns);
    float3 V = normalize(_WorldSpaceCameraPos - P);
    float3 dpdx = ddx(P), dpdy = ddy(P); float2 duvx = ddx(uv), duvy = ddy(uv);
    RfxSurf s = RfxSurfOf(k);
    {   // (a level without a reflection map: RfxMatEnv's stand-in, the rig's light on a surface facing the reflection)
        float3 ambF, sunKF;
        RfxObjRig(P, reflect(-V, Ns), ambF, sunKF);
        rfxMatEnvFallback = ambF * 0.9 + _RfxSunColor.rgb * sunKF * 0.12;
    }

    // ---- the material's own maps
    float mask = -1; float4 albedo = float4(0, 0, 0, -1);
    int sfxSlot = (int)(_RfxMatMap.x + 0.5) - 1, nrmSlot = (int)(_RfxMatMap.z + 0.5) - 1, difSlot = (int)(_RfxMatMap.w + 0.5) - 1;
    [branch] if (sfxSlot >= 0)
    {
        float4 sfx = RfxMatTex(sfxSlot, uv, duvx, duvy);
        mask = saturate(_RfxMatMap.y > 1.0 ? sfx.b : sfx.r);
    }
    // (a metal's own colour: the program's albedo where a material constant colours it - rfxExpAlb: a wheel's / hub's / A-arm's / frame's /
    // spring's colour option is such a constant, not a texture - else its diffuse map)
    [branch] if (s.metal > 0)
    {
        if (rfxExpAlbOn > 0.5) albedo = float4(saturate(rfxExpAlb), 1);
        else if (difSlot >= 0 && _RfxMat.z > 0.5) albedo = float4(RfxMatTex(difSlot, uv, duvx, duvy).rgb, 1);
    }
    [branch] if (nrmSlot >= 0)
    {
        float3 nf = dot(Ns, V) < 0 ? -Ns : Ns;
        Ns = RfxMatDetailNormal(nf, dpdx, dpdy, duvx, duvy, RfxMatTex(nrmSlot, uv, duvx, duvy).xy);
    }

    // ---- the dirt's field and its surface (before the material: the dirt takes the gloss of what it covers)
    RfxDirtField f; f.m = 0; f.edge = 0; f.core = 0; f.height = 0; f.relief = 0; f.tone = 0.5;
    float3 Ng = dot(N, V) < 0 ? -N : N;
    bool dirty = _RfxDirtOn > 0.5 && _RfxDirt.x >= 0.004 && s.dirtAff > 0.001;
    float px = length(fwidth(rp));   // (derivatives: outside the branches)
    [branch] if (dirty)
    {
        // (each scale of the relief fades out where a pixel no longer resolves it: nothing to shimmer under TAA or far away)
        f = RfxDirtFieldAt(rp, N, _RfxDirt.x, s.dirtAff, s.dirtH, _RfxDirt.w, saturate(1.6 - px * 42.0 * 1.1), saturate(1.3 - px * 64.0 * 0.9), saturate(1.2 - px * 210.0 * 0.7));
    }
    float dhx = ddx(f.height), dhy = ddy(f.height);
    float3 Nd = Ng;
    [branch] if (f.m > 0.002)
    {
        // the height as a bump: a centimetre of relief in world units, drawn stronger than life so it reads at riding distance; the slope capped
        float kb = 0.01 * unit * 1.25;
        float3 r1 = cross(dpdy, Ng), r2 = cross(Ng, dpdx);
        float det = dot(dpdx, r1);
        float3 grad = (det < 0 ? -1.0 : 1.0) * (dhx * r1 + dhy * r2) * kb / max(abs(det), 1e-12);
        float gl = length(grad);
        grad *= min(gl, 1.0) / max(gl, 1e-6);
        Nd = normalize(Ng - grad);
    }

    float3 dndx = ddx(Nd), dndy = ddy(Nd);
    float nVar = 0.25 * (dot(dndx, dndx) + dot(dndy, dndy));               // (the dirt's specular anti-aliasing, as RfxSpecAA)

    c.rgb = RfxMatLayers(c.rgb, k, P, Ns, rp, occ, shadow, fog, front, mask, albedo, 1.0 - f.m, alpha);
    nOut = dot(Ns, V) < 0 ? -Ns : Ns;
    [branch] if (f.m > 0.002)
    {
        c.rgb = RfxDirtLayer(c.rgb, f, P, Ng, Nd, nVar, occ, shadow, fog);
        nOut = normalize(lerp(nOut, Nd, f.m));
    }
    c.a = alpha;
    return c;
}

#endif
