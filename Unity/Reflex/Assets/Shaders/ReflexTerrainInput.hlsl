// Shared declarations for Reflex/Original HFTerrain (ReflexTerrain.shader).
// One UnityPerMaterial layout for every pass (SRP batcher compatible).
#ifndef REFLEX_TERRAIN_INPUT_INCLUDED
#define REFLEX_TERRAIN_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

CBUFFER_START(UnityPerMaterial)
    float _AliveTerrain;
    float4 _AliveVTRect0, _AliveVTRect1, _AliveVTRect2, _AliveVTRect3;   // AliveTerrainVT rings: x0, z0, size, valid
    float4 _AliveLook;         // ALIVE LOOK REFLEX (Content/AliveLook.cs): x detail layer picked from the top-down map, y chroma / lum adjustment of
                               // the top-down map, z detail colour matched to the top-down map (0..1), w the pick's sharpness
    float4 _AliveLayerMean0, _AliveLayerMean1, _AliveLayerMean2, _AliveLayerMean3;   // mean colour (a: alpha) of the four detail layers
    float4 _AliveLayerClass;          // each detail layer's kind: 0 dirt, 1 grass, 2 snow, 3 rock (by its texture name, AliveLook.cs)
    float4 _AliveLayerClassPresent;   // 1 where the donor has a layer of that kind (dirt, grass, snow, rock)
    float4 _AliveTopDownMean;  // the baked composite's mean colour (a: 1 once AliveTerrainVT has read it back)
    float4 _AliveLook2;        // x: the top-down map recoloured in the picked layers' hue (0..1), y: the specular mask, z: the layers' mean luminance
    float4 _AliveLook3;        // red clay: x strength (0 off), y..z the composite's redness over the picked layers' that ramps it in (AliveLook.cs)
    float4 _GridOrigin;        // x: world x of grid column 0, y: world z of grid row 0 (max z), z: 2047 * spacing, w: unused
    float4 _DetailTile;        // usr_detail_tile (x base, y red, z green, w blue)
    float4 _DirtTint;          // usr_dirt_tint_color
    float4 _BumpScalar;        // usr_bump_scalar (only used with the runtime rut render target; kept for completeness)
    float4 _SunDirection;      // gSunLight: unit vector towards the sun (original world == Unity world axes)
    float4 _SunColor;          // gSunLightColor
    float4 _DetailFade;        // x: start (384), y: range (128)  [vs_hft_lod0 0710..0730]
    float _UnleashedDetail;
    float4 _MtxDetail;         // experimental terrain only; zero for stock tracks
    float4 _UxOv;              // Untamed ground overlay: x on, y / z the weight scale of layer A / B
    float _MtxSourceColour;    // source colour map uses the venue palette
    float4 _DetailParams;      // x: detail texldb mip bias (-0.67), y: chroma scale (0.25)
    float4 _RTBumpNeutral;     // RT_Bump sample used when no rut target exists (x, y, z, w)
    float4 _DeformNeutral;     // x: DeformSampler value without ruts (128/255)
    float _UseDeformMap;       // 1: sample the live deformation surface (_DeformMap, original s10 DeformSampler)
    float _UseTrackMap;        // 1: multiply RT_Bump.xyz by the live tyre-track texture (_TrackMap)
    float4 _TrackMapRect;      // x: centre x, y: centre z, z: half size x, w: half size z (world units) of _TrackMap
    float4 _TrackMapTexel;     // x: 1 / width, y: 1 / height
    float4 _BumpFade;          // approximation of the vs o3.w cascade fade: x start, y end (horizontal distance), z |dy| start, w |dy| range
    float4 _FogColorStart;     // DistanceFog ColorStart (rgb)
    float4 _FogColorEnd;       // DistanceFog ColorEnd (rgb)
    float4 _FogParams;         // x DistanceStart, y DistanceEnd, z AmountStart, w AmountEnd
    float _FogEnabled;
    float _UseUnityShadows;
    float _UseShadowVis;
    float _GammaOutput;        // 1: treat the original result as display (gamma) values and convert for a linear project
    float4 _ColorRef;          // the colour reference (radiosity, s13) map's 1x1 texel (the _HFT_* variants, ReflexTerrain.shader)
    float4 _GridParams;        // x: N (height grid samples per side; 0: unknown), y: 1 to rebuild the normal from _HeightMap, z: g_NormalStrength.x (2 * spacing)
CBUFFER_END

// Terrain instancing data set per draw by the Terrain system (same names as URP TerrainLit).
CBUFFER_START(_Terrain)
#ifdef UNITY_INSTANCING_ENABLED
    float4 _TerrainHeightmapRecipSize;
#endif
    float4 _TerrainHeightmapScale;
CBUFFER_END

#ifdef UNITY_INSTANCING_ENABLED
TYPED_TEXTURE2D(float4, _TerrainHeightmapTexture);
TEXTURE2D(_TerrainNormalmapTexture);
SAMPLER(sampler_TerrainNormalmapTexture);
#endif

// The level's height map (HFTerrainRSData HeightMapAsset, R32F, D3D row order: row 0 = the grid's top row, -z): the pixel program's
// HeightMapSampler s9 (ReflexTerrainMaterial.cs). Read with Load and filtered here (bilinear, clamp: the sampler's state).
TEXTURE2D_FLOAT(_HeightMap);

// Bilinear sample of the height map at a D3D texture coordinate (texel centres at (i + 0.5) / N), clamp addressing.
float RfxHeightAt(float2 uvD)
{
    float n = _GridParams.x;
    float2 t = uvD * n - 0.5;
    float2 f = floor(t), w = t - f;
    int2 a = clamp(int2(f), 0, int(n) - 1), b = clamp(int2(f) + 1, 0, int(n) - 1);
    float h00 = LOAD_TEXTURE2D(_HeightMap, int2(a.x, a.y)).r, h10 = LOAD_TEXTURE2D(_HeightMap, int2(b.x, a.y)).r;
    float h01 = LOAD_TEXTURE2D(_HeightMap, int2(a.x, b.y)).r, h11 = LOAD_TEXTURE2D(_HeightMap, int2(b.x, b.y)).r;
    return lerp(lerp(h00, h10, w.x), lerp(h01, h11, w.x), w.y);
}

TEXTURE2D(_TrackMap);
SAMPLER(sampler_TrackMap);
TEXTURE2D(_DeformMap);
SAMPLER(sampler_DeformMap);

// RT_Bump of the frame (Rendering/ReflexRTBump.cs): A8R8G8B8 768 x 1536, three slices of the camera's ground footprint.
TEXTURE2D(_RfxRTBump);
SAMPLER(sampler_RfxRTBump);           // bilinear, clamp (the target's own state, as 007ccdb8)
float4 _RfxRTBumpParams;              // x: 1 when RT_Bump is valid for this camera, y: 1 when it holds the tyre-track pass
float4 _RfxRTBumpU, _RfxRTBumpW;      // u = U.p / W.p over p = (x, z, 1)        (g_TexProjMatrix rows 0 / 3)
float4 _RfxRTBumpV0, _RfxRTBumpV1, _RfxRTBumpV2;   // v_k = V_k.p / W.p (rows 1 of the three slab matrices)
float4 _RfxRTBumpStep;                // g_UVOneStep: (+x tap, +z tap) offsets in RT uv
TEXTURE2D(_RfxRTBumpNear);           // the world-locked near detail target (Rendering/ReflexRTBump.cs): RT_Bump's draws top-down over a snapped square;
                                      // read with sampler_RfxRTBump (clamp; an own sampler took _HFT_SX_FY + shadows past Metal's 16)
float4 _RfxRTBumpNearRect;            // x, y: the square's world origin (x, z), z: 1 / span, w: the edge band (m) faded to RT_Bump
float _RfxRTBumpNearOn;

// (u, V) of a world position as vs_hft_lod0 (0740..0854): V = sum_k min(sat(v_k / 3), 1/3)
float2 RfxRTBumpUV(float3 positionWS)
{
    float3 p = float3(positionWS.x, positionWS.z, 1.0);
    float rw = rcp(dot(_RfxRTBumpW.xyz, p));
    float u = dot(_RfxRTBumpU.xyz, p) * rw;
    float v0 = dot(_RfxRTBumpV0.xyz, p) * rw, v1 = dot(_RfxRTBumpV1.xyz, p) * rw, v2 = dot(_RfxRTBumpV2.xyz, p) * rw;
    float V = min(saturate(v1 * 0.33333), 0.33333) + min(saturate(v2 * 0.33333), 0.33333) + min(saturate(v0 * 0.33333), 0.3333);
    return float2(u, V);
}

// the rut / specular fade o3.w (088c..08f8): (1 - 1.25 max(V^2 - 0.2, 0)) (1 - sat((cam.y - y - 128) / 128))
float RfxRTBumpFade(float V, float positionY)
{
    return (1.0 - 1.25 * max(V * V - 0.2, 0.0)) * (1.0 - saturate((_WorldSpaceCameraPos.y - positionY - 128.0) * 0.0078125));
}

// vertex displacement y += fade * RT.z (vertex fetch of s1 at mip 0, 0870..08e4)
// OPEN REFLEX (not the original's mip 0 point fetch): RT_Bump is re-fitted to the camera every frame, so its texel grid slides over the
// ground, and its detail (bump grain, rut decals) is far finer than the terrain's vertex spacing: a mip 0 fetch gave each vertex a new
// random height every frame - the ground just ahead of the rider boiled while riding (a tearing look, at any frame rate). The fetch now
// reads the mip level whose texel covers two height-map cells around the vertex (RT_Bump carries a mip chain, generated per frame), so
// a sub-texel slide barely changes the height, and the fine and the coarse terrain LODs displace the same smooth relief. The pixel
// program's taps still read mip 0, as the original.
// fade * RT.z at a world position, RT_Bump read at the mip level whose texel spans `cell` world units (0 when RT_Bump is off)
float RfxRTBumpHeight(float3 positionWS, float cell)
{
    if (_RfxRTBumpParams.x < 0.5) return 0.0;
    float2 ruv = RfxRTBumpUV(positionWS);
    const float eps = 0.05;
    float2 ux = RfxRTBumpUV(positionWS + float3(eps, 0, 0)) - ruv, uz = RfxRTBumpUV(positionWS + float3(0, 0, eps)) - ruv;
    float texels = max(length(ux * float2(768.0, 1536.0)), length(uz * float2(768.0, 1536.0))) * (cell / eps);
    float level = clamp(log2(max(texels, 1.0)), 0.0, 8.0);
    float4 rt = SAMPLE_TEXTURE2D_LOD(_RfxRTBump, sampler_RfxRTBump, ruv, level);
    return RfxRTBumpFade(ruv.y, positionWS.y) * rt.z;
}

float3 RfxRTBumpDisplace(float3 positionWS)
{
    positionWS.y += RfxRTBumpHeight(positionWS, max(2.0 * _TerrainHeightmapScale.x, 0.5));   // two height-map cells
    return positionWS;
}

// ---- TERRAIN TESSELLATION (OPEN REFLEX option, on by default; Rendering/ReflexTerrainClipmap.cs) ----
// The original draws its terrain as a camera-centred geometry clipmap (the water's mesh class, FUN_007e4280 from 007c49c8: a 129 x 129
// grid of 0.25-unit cells, each further level twice as coarse; vs_hft_lod0 places a vertex at v0 * gMeshResolution + gVertexOffset,
// clamped to the terrain) and fetches the height map, the deformation surface and RT_Bump.z at every vertex. Unity's terrain has one
// vertex per height-map sample (1 or 2 units), where RT_Bump's relief cannot be displaced. With the option on, the same nested grid is
// drawn around the camera out to the level whose cell is one height-map sample; Unity's terrain sinks under it and goes on beyond.
float4 _RfxClipOrigin;     // x, y: world (x, z) of the mesh's centre, z: the finest cell (world units), w: 1 while the clipmap is drawn
float4 _RfxClipRect;       // x, y: world (x, z) half size of the clipmap square, z: the inner margin where Unity's terrain is sunk whole, w: sink depth
float4 _RfxClipGrid;       // x, y: world (x, z) of height-map texel (0, 0), z: 1 / spacing, w: the height map's size (texels per side)
float4 _RfxClipHeight;     // x: world y of height 0, y: world units per unpacked height, z: the coarsest level's cell / the finest, w: unused
float4 _RfxClipBounds;     // the terrain's world extents: min x, min z, max x, max z
TEXTURE2D(_RfxClipHeightTex);   // Unity's live height map (the collision heights with the ruts, OriginalTerrain)

float RfxClipTexel(int2 t)
{
    int n = int(_RfxClipGrid.w) - 1;
    return UnpackHeightmap(LOAD_TEXTURE2D(_RfxClipHeightTex, clamp(t, 0, n)));
}

// how far Unity's own terrain vertex at a world position is lowered: a hair under the whole clipmap square (so the clipmap always wins
// the depth test where both draw the same ground), and out of sight further in
float RfxClipSink(float2 xz)
{
    if (_RfxClipOrigin.w < 0.5) return 0.0;
    float2 d = _RfxClipRect.xy - abs(xz - _RfxClipOrigin.xy);
    float e = min(d.x, d.y);
    return e <= 0.001 ? 0.0 : (e > _RfxClipRect.z ? _RfxClipRect.w : 0.03);
}

// A clipmap vertex: positionOS.xz = the vertex in finest-cell units from the centre, uv.x = its level's cell in finest cells (2^L).
// (The world normal it returns must not go through the object's matrices.)
// Returns the world position; uv becomes the terrain uv Unity's instanced patches carry, normal the height map's.
float3 RfxClipVertex(float4 positionOS, inout float3 normal, inout float2 uv)
{
    float levelCells = uv.x;
    float2 xz = _RfxClipOrigin.xy + positionOS.xz * _RfxClipOrigin.z;
    xz = clamp(xz, _RfxClipBounds.xy, _RfxClipBounds.zw);                 // as vs_hft_lod0 04c0 / 04d0: the grid ends at the terrain's edge
    float2 t = (xz - _RfxClipGrid.xy) * _RfxClipGrid.z;                   // height-map sample coordinates
    float2 f = floor(t), w = t - f; int2 a = int2(f);
    float h00 = RfxClipTexel(a), h10 = RfxClipTexel(a + int2(1, 0)), h01 = RfxClipTexel(a + int2(0, 1)), h11 = RfxClipTexel(a + int2(1, 1));
    float y = _RfxClipHeight.x + lerp(lerp(h00, h10, w.x), lerp(h01, h11, w.x), w.y) * _RfxClipHeight.y;
    int2 c = int2(round(t));
    float sx = (RfxClipTexel(c - int2(1, 0)) - RfxClipTexel(c + int2(1, 0))) * _RfxClipHeight.y;
    float sz = (RfxClipTexel(c - int2(0, 1)) - RfxClipTexel(c + int2(0, 1))) * _RfxClipHeight.y;
    normal = normalize(float3(sx, 2.0 / _RfxClipGrid.z, sz));
    uv = t / (_RfxClipGrid.w - 1.0);
    float3 p = float3(xz.x, y, xz.y);

    // RT_Bump.z: what Unity's vertices get (smoothed over two height-map cells) at the square's rim, so the two meshes meet; inside, the
    // relief at this level's own vertex spacing - from the world-locked near target at mip 0 where it reaches (the original's fetch: a
    // ground point keeps its height from frame to frame), from the slices at the level's mip beyond it
    float spacing = 1.0 / _RfxClipGrid.z;
    float coarse = RfxRTBumpHeight(p, max(2.0 * spacing, 0.5));
    float rim = (_RfxClipRect.x - max(abs(xz.x - _RfxClipOrigin.x), abs(xz.y - _RfxClipOrigin.y)) - _RfxClipRect.z) / max(_RfxClipRect.z, 1e-3);
    float wFine = saturate(rim);
    if (wFine > 0.0 && _RfxRTBumpParams.x > 0.5)
    {
        float fine = RfxRTBumpHeight(p, _RfxClipOrigin.z * levelCells);
        if (_RfxRTBumpNearOn > 0.5)
        {
            float2 nuv = (xz - _RfxRTBumpNearRect.xy) * _RfxRTBumpNearRect.z;
            float edge = min(min(nuv.x, nuv.y), min(1.0 - nuv.x, 1.0 - nuv.y)) / (_RfxRTBumpNearRect.z * _RfxRTBumpNearRect.w);
            float wn = saturate(edge);
            if (wn > 0.0)
            {
                float nz = SAMPLE_TEXTURE2D_LOD(_RfxRTBumpNear, sampler_RfxRTBump, nuv, 0).z;
                fine = lerp(fine, RfxRTBumpFade(RfxRTBumpUV(p).y, y) * nz, wn);
            }
        }
        coarse = lerp(coarse, fine, wFine);
    }
    p.y += coarse;
    return p;
}

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
UNITY_INSTANCING_BUFFER_END(Terrain)

void ReflexTerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 patchVertex = positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));
    positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    positionOS.y = height * _TerrainHeightmapScale.y;
    normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;
    uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif
}

// The world position of a terrain vertex in every pass: Unity's instanced patch (sunk under the clipmap while TERRAIN TESSELLATION draws
// it), a clipmap vertex, or a plain mesh.
float3 ReflexTerrainVertexWS(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
    // the clipmap's mesh (y = -(10000 + its level's cell), in either instancing variant): only in the game camera's view (every vertex on
    // one point elsewhere: nothing is drawn)
    float3 p = float3(0, 0, 0);
    bool clipmap = positionOS.y < -5000.0;
    if (clipmap)
    {
        float2 cuv = float2(-positionOS.y - 10000.0, 0.0);
        float3 cn = float3(0, 1, 0);
        if (_RfxClipOrigin.w > 0.5) p = RfxClipVertex(positionOS, cn, cuv);
        normal = cn; uv = cuv;
    }
    else
    {
#ifdef UNITY_INSTANCING_ENABLED
        ReflexTerrainInstancing(positionOS, normal, uv);
        p = TransformObjectToWorld(positionOS.xyz);
        float sink = RfxClipSink(p.xz);
        p = RfxRTBumpDisplace(p);
        p.y -= sink;
#else
        p = RfxRTBumpDisplace(TransformObjectToWorld(positionOS.xyz));
#endif
    }
    return p;
}

// Per-pixel heightmap normal (the original ps_hft_lod0 rebuilds the normal per pixel from heightmap
// central differences, 0788..0924). Falls back to the interpolated vertex normal without instancing.
float3 ReflexTerrainNormalWS(float2 terrainUV, float3 vertexNormalWS)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 sampleCoords = (terrainUV / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
    float3 n = SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1;
    return normalize(TransformObjectToWorldNormal(normalize(n)));
#else
    return normalize(vertexNormalWS);
#endif
}

#endif
