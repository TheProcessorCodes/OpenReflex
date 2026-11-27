// WEATHER (OPTIONS > VIDEO > WEATHER, LIGHTING: ENHANCED only; not part of the original renderer): the shared inputs and the ground model of
// Scripts/Rendering/ReflexWeather.cs. Every function here is only reached behind a uniform branch on _RfxWeather.w (on), so with WEATHER: CLEAR
// (or LIGHTING: ORIGINAL) no pixel changes: the original programs' results are returned untouched.
//   _RfxWeather       x surface wetness 0..1 (darker, glossier), y snow cover 0..1, z overcast 0..1 (grey sky, flat light), w on (1)
//   _RfxWeather2      x puddle level 0..1 (how full the ruts are), y rain falling 0..1 (ripples), z snow falling 0..1, w time (s, wrapped)
//   _RfxWeatherSky    rgb: the overcast sky's zenith colour (raw values, from the level's own fog colour), w: cloud darkness
//   _RfxWeatherFog    rgb: the mist colour (raw), w: mist density (1/m)
//   _RfxWeatherWind   xz: the wind (m/s, world), y: unused, w: the cloud scroll (m)
//   _RfxWeatherDebug  x: 1 puddle mask, 2 snow cover / trail, 3 the raw deformation, 4 the tyre-track darkness (tests)
//   _RfxSnowMap       the snow trail map (Scripts/Rendering/ReflexSnowTrails.cs): r trench 0..1, g rim 0..1, a world-locked square
//   _RfxSnowMapRect   x0, z0 of the square (world), 1 / its size, w on (1);   _RfxSnowMapTexel  x: a texel (uv), y: a texel (ft)
#ifndef REFLEX_WEATHER_INCLUDED
#define REFLEX_WEATHER_INCLUDED

float4 _RfxWeather, _RfxWeather2, _RfxWeatherSky, _RfxWeatherFog, _RfxWeatherWind, _RfxWeatherDebug;
float4 _RfxSnowMapRect, _RfxSnowMapTexel;
TEXTURE2D(_RfxSnowMap);

bool RfxWeatherOn() { return _RfxWeather.w > 0.5; }

// ---------------------------------------------------------------------------------------------- noise (no textures: no sampler registers)
float RfxWHash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
float2 RfxWHash22(float2 p) { float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973)); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.xx + p3.yz) * p3.zy); }
float RfxWNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = RfxWHash12(i), b = RfxWHash12(i + float2(1, 0)), c = RfxWHash12(i + float2(0, 1)), d = RfxWHash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}
float RfxWFbm(float2 p, int octaves)
{
    float s = 0, a = 0.5, n = 0;
    [loop] for (int k = 0; k < octaves; k++) { s += a * RfxWNoise(p); n += a; p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6) + 17.3; a *= 0.5; }
    return s / n;
}

// ---------------------------------------------------------------------------------------------- the overcast sky
// The cloud deck seen in direction d (raw values): the level's fog colour as the light under the clouds (it was authored to sit against the
// horizon), darker towards the zenith, with slowly moving fbm cloud masses projected on a plane 1.5 km up. a: how much it covers the sky.
float3 RfxWeatherSkyColour(float3 d, float detail)
{
    float3 zenith = _RfxWeatherSky.rgb, horizon = _RfxWeatherFog.rgb;
    float up = saturate(d.y);
    float3 c = lerp(horizon, zenith, smoothstep(0.0, 0.55, up));
    [branch] if (detail > 0.5)   // (blurry reflections take the deck's gradient only)
    {
        float2 pl = d.xz / max(d.y + 0.08, 0.02) * 1.6 + _RfxWeatherWind.w * float2(0.0007, 0.0004);
        float n = RfxWFbm(pl, 4);
        float m = RfxWNoise(pl * 2.7 + 9.1);
        float dens = saturate((n - 0.32) * 1.8);
        // the undersides: darker masses (_RfxWeatherSky.w), lighter thinner gaps; less contrast towards the horizon (aerial perspective)
        float shade = lerp(1.18, 1.0 - _RfxWeatherSky.w, dens) * lerp(0.92, 1.08, m);
        c *= lerp(1.0, shade, detail * smoothstep(-0.02, 0.25, d.y));
    }
    return max(c, 0.0);
}


// ---------------------------------------------------------------------------------------------- rain ripples on standing water
// Expanding rings from drops: one drop per cell (random centre and phase), the 3 x 3 neighbourhood so rings cross cell borders. Returns the
// slope (d/dx, d/dz) of the water surface.
float2 RfxWRippleLayer(float2 p, float t, float density)
{
    float2 g = 0;
    float2 i0 = floor(p);
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 c = i0 + float2(x, y);
        float2 h = RfxWHash22(c);
        if (h.y > density) continue;
        float ph = frac(t + h.x * 7.31);
        float2 centre = c + 0.5 + (RfxWHash22(c + 19.7) - 0.5) * 0.7;
        float2 dv = p - centre;
        float r = length(dv);
        float w = r - ph * 1.3;                         // the ring's front grows to 1.3 cells
        float env = (1.0 - ph) * (1.0 - ph) * exp(-w * w * 40.0);
        float wave = cos(w * 28.0) * env;
        g += dv / max(r, 1e-3) * wave;
    }
    return g;
}
float2 RfxWeatherRipples(float2 xz, float t, float rain)
{
    float d = saturate(rain);
    float2 g = RfxWRippleLayer(xz * 3.1, t * 1.15, 0.35 + 0.6 * d) * 0.55;
    g += RfxWRippleLayer(xz * 4.7 + 13.0, t * 1.4 + 0.37, 0.25 + 0.6 * d) * 0.4;
    return g * (0.25 + 0.75 * d);
}
// Wind wavelets on standing water (the slope of two crossing wave trains moving with the wind): a puddle is never a perfect mirror.
float2 RfxWeatherWavelets(float2 xz, float t)
{
    float2 w = _RfxWeatherWind.xz;
    float sp = length(w);
    float2 d = sp > 1e-3 ? w / sp : float2(1, 0);
    float2 d2 = float2(d.x * 0.82 - d.y * 0.57, d.x * 0.57 + d.y * 0.82);
    float a1 = dot(xz, d) * 9.0 - t * 5.5 + RfxWNoise(xz * 0.9) * 4.0;
    float a2 = dot(xz, d2) * 15.0 - t * 7.5 + RfxWNoise(xz * 1.7 + 4.1) * 3.0;
    return (d * cos(a1) * 0.6 + d2 * cos(a2) * 0.4) * saturate(0.25 + sp * 0.12);
}

// ---------------------------------------------------------------------------------------------- the snow trail map
// The trenches tyres left in the snow around P: trench 0..1 (packed), rim 0..1 (pushed-up snow beside it), grad: the slope of the snow
// surface they make (ft / ft, d/dx and d/dz), inside: 1 inside the map's square, 0 outside (the caller's coarse trail takes over).
// RFX_WX_SNOW_SAMPLER: a linear clamp sampler of the including shader (the terrain is at its sampler limit); without it: no map.
void RfxWeatherSnowTrail(float3 P, float cover, out float trench, out float rim, out float2 grad, out float inside)
{
    trench = 0.0; rim = 0.0; grad = 0; inside = 0.0;
#if defined(RFX_WX_SNOW_SAMPLER)
    [branch] if (_RfxSnowMapRect.w > 0.5)
    {
        float2 uv = (P.xz - _RfxSnowMapRect.xy) * _RfxSnowMapRect.z;
        float2 e = min(uv, 1.0 - uv);
        inside = saturate(min(e.x, e.y) * 14.0);
        [branch] if (inside > 0.0)
        {
            float t = _RfxSnowMapTexel.x * 1.5;
            float2 c = SAMPLE_TEXTURE2D_LOD(_RfxSnowMap, RFX_WX_SNOW_SAMPLER, uv, 0).rg;
            float2 xp = SAMPLE_TEXTURE2D_LOD(_RfxSnowMap, RFX_WX_SNOW_SAMPLER, uv + float2(t, 0), 0).rg;
            float2 xm = SAMPLE_TEXTURE2D_LOD(_RfxSnowMap, RFX_WX_SNOW_SAMPLER, uv - float2(t, 0), 0).rg;
            float2 zp = SAMPLE_TEXTURE2D_LOD(_RfxSnowMap, RFX_WX_SNOW_SAMPLER, uv + float2(0, t), 0).rg;
            float2 zm = SAMPLE_TEXTURE2D_LOD(_RfxSnowMap, RFX_WX_SNOW_SAMPLER, uv - float2(0, t), 0).rg;
            // the snow surface (ft): the trench down to the packed floor (0.4 ft of snow at full cover), the rim 0.14 ft above it
            float depth = 0.4 * saturate(cover * 1.2), rimH = 0.14 * saturate(cover * 1.2);
            float4 tr = saturate(float4(xp.x, xm.x, zp.x, zm.x));
            float4 rm = saturate(float4(xp.y, xm.y, zp.y, zm.y)) * (1.0 - saturate(tr * 1.6));
            float4 h = rm * rimH - tr * depth;
            grad = float2(h.x - h.y, h.z - h.w) / (2.0 * 1.5 * _RfxSnowMapTexel.y) * inside;
            trench = saturate(c.x) * inside;
            rim = saturate(c.y) * (1.0 - saturate(c.x * 1.6)) * inside;
        }
    }
#endif
}

// ---------------------------------------------------------------------------------------------- the ground (terrain)
struct RfxWeatherGround
{
    float3 N;          // the shading normal after the weather (snow smooths the bumps, trench walls and rims shape it)
    float wet;         // surface wetness (darkening, gloss)
    float film;        // how much of a water film lies on the wet soil 0..1 (flat ground, beside the puddles: the glossiest)
    float puddle;      // standing water 0..1
    float depth;       // the water's depth 0 (the shore) .. 1
    float edge;        // the soaked band round a puddle (darkest, wettest soil)
    float snow;        // snow over the ground 0..1 (ridden or not)
    float thin;        // the cover's thin margin 0..1 (the ground shows through)
    float trail;       // the coarse trail 0..1 (outside the snow trail map: the ruts and tyre tracks through the snow)
    float mudTrail;    // the coarse trail's share that shows churned mud (deep ruts) instead of packed snow
    float packed;      // packed snow 0..1 (the floor of a tyre trench)
    float rim;         // pushed-up loose snow beside a trench 0..1
    float sparkle;     // snow glints (a multiple of the sun)
    float tone;        // the snow's brightness over the ground (drifts, wind-blown patches)
};

// Glints of the snow's ice crystals: every cell holds one facet with a random tilt; it flashes when it mirrors the sun into the eye, so
// the glints come and go as the camera moves and are most where the light grazes the surface. Two sizes; faded where a cell is smaller
// than a pixel (no shimmer).
float RfxWSparkle(float3 P, float3 N, float3 V, float dist)
{
    float3 H = normalize(_RfxSunDir.xyz - V);
    float s = 0.0;
    float scale = 26.0;
    [unroll] for (int k = 0; k < 2; k++)
    {
        float2 p = P.xz * scale;
        float2 cell = floor(p);
        float2 h = RfxWHash22(cell + 7.3 * k);
        float2 h2 = RfxWHash22(cell + 31.7 + 3.1 * k);
        float3 fn = normalize(N + float3(h.x - 0.5, 0.0, h.y - 0.5) * 1.9);
        float2 f = frac(p) - 0.5 - (h2 - 0.5) * 0.5;
        float spot = saturate(1.0 - dot(f, f) * 22.0);
        float fade = saturate(1.6 - dist * scale * 0.0022);       // (a cell under about a pixel: gone)
        s += pow(saturate(dot(fn, H)), 380.0) * spot * fade;
        scale = 61.0;
    }
    return s;
}

// P: world position, Ngeo: the heightfield normal (no bump), Nb: the bump-mapped normal, dm: deformation - neutral (ruts < 0, berms > 0),
// conc: the deformation's concavity (neighbours' mean - centre: > 0 at a rut's bottom), basin: the height field's concavity (ft),
// track: the tyre-track darkness at the pixel (0 none .. 1), trackWide: the same over a few decimetres, trackGrad: its gradient (per ft),
// deformGrad: the deformation's gradient (per ft), V: camera -> point, dist: to the camera
RfxWeatherGround RfxWeatherGroundPrepare(float3 P, float3 Ngeo, float3 Nb, float dm, float conc, float basin, float track, float trackWide,
                                         float2 trackGrad, float2 deformGrad, float3 V, float dist)
{
    RfxWeatherGround g = (RfxWeatherGround)0;
    g.N = Nb;
    float near = 1.0 - saturate((dist - 120.0) / 120.0);   // the small-scale terms fade out with distance
    // (the terrain's normal sees the heights three times steeper than the geometry, as the original's: the true slope for the water)
    float trueY = normalize(float3(Ngeo.x / 3.0, Ngeo.y, Ngeo.z / 3.0)).y;
    float flat = smoothstep(0.90, 0.985, trueY);
    float n1 = RfxWNoise(P.xz * 0.35), n2 = RfxWNoise(P.xz * 1.7 + 5.3);

    // ---- rain: wet soil everywhere, water collecting where the ground is lowest: the ruts' bottoms, the tyre grooves, hollows
    float wet = _RfxWeather.x;
    g.wet = wet;
    g.film = flat * 0.5;
    float level = _RfxWeather2.x;
    [branch] if (level > 0.001)
    {
        float rut = saturate(-dm * 14.0);                       // depth below the neutral surface
        float bottom = saturate(conc * 60.0);                   // the trough itself
        float groove = track * 0.55 + trackWide * 0.35;
        float hollow = saturate(basin * 3.0);
        // the shoreline: where the ground's own shape says nothing a slow noise decides (broad pools, not speckle), a finer one frays it
        float shore = RfxWFbm(P.xz * 0.13 + 3.7, 3);
        float s = max(max(rut * 0.55 + bottom * 0.75, groove), hollow) + (shore - 0.5) * 0.62 + (n1 - 0.5) * 0.16 + (n2 - 0.5) * 0.05;
        float th = 1.02 - level * 0.72;                          // a higher level reaches shallower ground
        float d = s - th;                                        // > 0: under water (the water is level, so its edge is sharp)
        float p = smoothstep(0.0, 0.035, d) * flat;
        g.puddle = p * near;
        g.depth = saturate(d / 0.28);
        g.edge = saturate(1.0 + d / 0.16) * (1.0 - p) * flat;    // the soaked band outside the water, fading over a hand's width
        g.film = max(g.film, g.edge);
    }

    // ---- snow: on the flatter ground, deeper in the hollows and thin on the crests, broken up at its edge by noise; ridden through where
    // tyres went (the snow trail map near the camera, the tyre-track map and the ruts beyond it)
    float cover = _RfxWeather.y;
    [branch] if (cover > 0.001)
    {
        float slope = smoothstep(0.42, 0.78, Ngeo.y + (n1 - 0.5) * 0.25);
        float drift = saturate(basin * 1.5) * 0.25 - saturate(-basin * 1.5) * 0.3;     // hollows hold it, the wind strips the crests
        float a = cover * 1.35 - (1.0 - slope) + drift * (1.0 - cover * 0.6) - (n2 - 0.5) * 0.25 * (1.0 - cover);
        float amount = saturate(a);
        g.thin = amount * (1.0 - saturate(a * 2.2 - 0.4));       // the margin of a patch: a dusting the ground shows through
        float trench, rim, inside; float2 tg;
        RfxWeatherSnowTrail(P, cover, trench, rim, tg, inside);
        // the coarse trail (outside the map; the ruts everywhere: the game's own deformation is not in the map)
        float rutTrail = saturate(abs(dm) * 18.0 - 0.25) * near;
        float tyre = saturate(track * 1.6 + trackWide * 1.2) * near * (1.0 - inside);
        g.trail = smoothstep(0.22, 0.62, max(rutTrail, tyre) + (n2 - 0.5) * 0.12);
        g.mudTrail = saturate(rutTrail * 1.5 - 0.25) * saturate(1.15 - cover * 0.25);
        g.snow = amount;
        g.packed = trench;
        g.rim = rim;
        // the surface: snow fills the small bumps (towards the heightfield's normal), rolls gently in drifts, and falls away into the
        // trenches and the coarse trail's tracks
        float2 dg = float2(RfxWNoise(P.xz * 0.55 + float2(0.6, 0.0)) - RfxWNoise(P.xz * 0.55 - float2(0.6, 0.0)),
                           RfxWNoise(P.xz * 0.55 + float2(0.0, 0.6)) - RfxWNoise(P.xz * 0.55 - float2(0.0, 0.6))) * 0.22;
        float2 fine = (float2(RfxWNoise(P.xz * 9.0 + 2.0), RfxWNoise(P.xz * 9.0 + 11.0)) - 0.5) * (0.10 + 0.35 * rim) * saturate(1.0 - dist / 60.0);
        float depth = 0.12 * amount;
        float2 hg = -(trackGrad * 1.6 * (1.0 - inside) + deformGrad * 18.0) * depth * near * 6.0;
        float3 Ns = normalize(Ngeo + float3(-(hg.x + tg.x * amount + dg.x + fine.x), 0.0, -(hg.y + tg.y * amount + dg.y + fine.y)));
        g.N = normalize(lerp(Nb, Ns, saturate(amount * 1.5)));
        [branch] if (dist < 90.0) g.sparkle = RfxWSparkle(P, g.N, V, dist) * amount * (1.0 - 0.8 * trench);
        g.tone = lerp(0.92, 1.04, RfxWNoise(P.xz * 0.06)) * lerp(0.97, 1.02, n2);
    }
    return g;
}

// The ground's colour after the weather. albedo: the original's albedo (raw display values), light: what it is multiplied by (sun + ambient,
// raw), spec: the original's specular term, sunLight: the sun's share of light (shadowed, with the surface's own facing), sunRaw: the
// shadowed sun without the facing, V: camera -> point, skyRough: the sky a rough wet surface reflects (raw), skySharp: the sky the water
// mirrors along reflect(V, waterN), waterN: the water's normal (ripples).
float3 RfxWeatherGroundShade(RfxWeatherGround g, float3 albedo, float3 light, float3 spec, float3 sunLight, float3 sunRaw, float3 V,
                             float3 skyRough, float3 skySharp, float3 waterN)
{
    float3 colour = albedo * light + spec;
    float3 L = _RfxSunDir.xyz;
    float3 amb = max(light - sunLight, 0.0);
    const float3 lumW = float3(0.299, 0.587, 0.114);
    // wet soil: darker (water fills the pores) and more saturated; a film of water over it that mirrors the sky at grazing angles and
    // carries the sun's highlight (broken up by the ground's own bumps)
    float wet = saturate(g.wet * (1.0 - g.snow));
    [branch] if (wet > 0.001)
    {
        float lum = dot(albedo, lumW);
        float3 wa = max(lerp(lum.xxx, albedo, 1.4), 0.0) * lerp(1.0, 0.55, wet);
        wa *= lerp(1.0, 0.70, g.edge * wet);
        float NoV = saturate(dot(-V, g.N));
        float fr = 0.025 + 0.975 * pow(1.0 - NoV, 5.0);
        float gloss = wet * lerp(0.45, 1.0, saturate(g.film));
        float rough = lerp(0.45, 0.2, gloss), a2 = rough * rough * rough * rough;
        float3 H = normalize(L - V);
        float NoH = saturate(dot(g.N, H)), NoL = saturate(dot(g.N, L));
        float dd = NoH * NoH * (a2 - 1.0) + 1.0;
        float lobe = min(a2 / (3.14159265 * dd * dd) * 0.03, 2.5);
        colour = wa * light + spec * lerp(1.0, 0.5, wet) + skyRough * (fr * gloss * 0.6) + sunRaw * (lobe * NoL * gloss);
    }
    // standing water: clear at the shore (the soaked soil under it), silty with depth; a mirror by Fresnel's law of the sky above and, at
    // the flattest angles, of the land beyond it; the drops' ripples and the wind's wavelets in its normal; the sun's glint
    [branch] if (g.puddle > 0.001)
    {
        float cosV = saturate(dot(-V, waterN));
        float fr = 0.03 + 0.97 * pow(1.0 - cosV, 4.0);
        float lum = dot(albedo, lumW);
        float3 silt = lerp(albedo, lum * float3(1.08, 0.96, 0.80), 0.35);
        float3 under = lerp(albedo * 0.55, silt * 0.34, g.depth) * light;
        float3 Rp = reflect(V, waterN);
        float3 land = albedo * light * 0.5 + _RfxWeatherFog.rgb * 0.10;
        float3 mirror = lerp(land, skySharp, smoothstep(0.015, 0.16, Rp.y));
        float3 water = lerp(under, mirror, fr);
        float3 H = normalize(L - V);
        float glint = pow(saturate(dot(waterN, H)), 900.0) * 40.0;
        water += sunRaw * saturate(dot(waterN, L)) * glint * fr;
        colour = lerp(colour, water, g.puddle);
    }
    // snow: light goes into it and comes back out everywhere (a soft terminator, bright even facing away from the sun), blue where only
    // the sky lights it, glints of its crystals; packed grey-blue and icy where tyres pressed it, loose and bright on the rims they
    // pushed up, the ground through its thin margins; mud where the ruts went deep
    [branch] if (g.snow > 0.001)
    {
        float NoL = dot(g.N, L);
        float wrap = saturate((NoL + 0.45) / 1.45);
        float3 sunD = sunRaw * wrap;
        const float3 shade = float3(0.80, 0.93, 1.20);
        float3 skyL = amb * shade * 1.05;
        float3 snowA = float3(0.80, 0.83, 0.88) * g.tone;
        // loose snow: the pushed-up rims are fresher and brighter; packed snow: denser, greyer, bluer, a little of the ground in it
        snowA = lerp(snowA, float3(0.90, 0.92, 0.95), g.rim * 0.6);
        float3 packedA = lerp(float3(0.64, 0.69, 0.78), albedo, 0.08);
        float3 mudA = albedo * float3(0.78, 0.70, 0.62);
        float3 a = lerp(snowA, packedA, g.packed * 0.85);
        a = lerp(a, mudA, smoothstep(0.92, 1.0, g.packed) * 0.30);
        // the trench: its floor lies in its own walls' shade (less sky), the walls take the light by the surface normal
        float cavity = 1.0 - 0.24 * g.packed + 0.10 * g.rim;
        float3 snowC = a * (sunD * float3(1.03, 1.0, 0.96) * lerp(1.0, 0.8, g.packed) + skyL * cavity);
        // the light that travelled through the snow: a faint blue glow where the sun only grazes or has just left the surface
        snowC += a * sunRaw * float3(0.70, 0.86, 1.0) * (0.12 * saturate(1.0 - abs(NoL) * 2.2) * (1.0 - g.packed));
        float NoV = saturate(dot(-V, g.N));
        float fr = pow(1.0 - NoV, 4.0);
        snowC += skyRough * fr * lerp(0.08, 0.30, g.packed);                       // packed snow is icy: it takes a sheen
        snowC += sunRaw * (g.sparkle * 9.0);
        // the coarse trail (outside the map): packed snow, churned mud where the ruts went deep
        float3 trailA = lerp(packedA, mudA, smoothstep(0.35, 0.9, g.mudTrail));
        float3 trailC = trailA * (sunD + skyL * 0.85);
        float cov = saturate(_RfxWeather.y * 1.2);
        snowC = lerp(snowC, trailC, saturate(g.trail * cov));
        // the thin margin lets the ground through
        colour = lerp(colour, snowC, g.snow * (1.0 - 0.55 * g.thin));
    }
    return colour;
}

// ---------------------------------------------------------------------------------------------- objects (scenery, trees, vehicles, riders)
// Rain and snow on a lit object. colour: its lit colour (before fog; fog: the fog still to come over it, rgb added and w multiplied,
// (0, 0, 0, 1) when the caller applies fog afterwards), N: the world normal, V: camera -> point, amb: the ambient light on it (raw),
// sunRaw: the shadowed sun without the facing, skyRough: the sky it reflects, settle: how well snow settles on this kind of thing 0..1
// (static scenery 1, foliage less, vehicles that move a dusting), film: how much of a water film it carries when wet 0..1.
float3 RfxWeatherObject(float3 colour, float4 fog, float3 P, float3 N, float3 V, float3 amb, float3 sunRaw, float3 skyRough, float settle, float film)
{
    float wet = _RfxWeather.x, cover = _RfxWeather.y;
    float up = N.y;
    if (cover > 0.001 && settle > 0.0)
    {
        float n = RfxWNoise(P.xz * 1.3 + P.y * 0.7), n2 = RfxWNoise(P.xz * 6.1 + P.y * 2.3 + 3.7);
        float m = smoothstep(0.30, 0.78, up + (n - 0.5) * 0.4);
        m = saturate(m * cover * 1.5 - (n2 - 0.5) * 0.35 * (1.0 - cover * 0.7)) * settle;
        float NoL = dot(N, _RfxSunDir.xyz);
        float wrap = saturate((NoL + 0.45) / 1.45);
        float3 snowC = float3(0.87, 0.90, 0.95) * lerp(0.94, 1.03, n2) * (sunRaw * wrap + amb * float3(0.80, 0.93, 1.20) * 1.12);
        colour = lerp(colour, snowC * fog.w + fog.rgb, m);
        wet *= 1.0 - m;
    }
    if (wet > 0.001)
    {
        // wet: a little darker, a film of water that mirrors the sky at grazing angles (most on the faces the rain lands on)
        float upw = saturate(up * 0.6 + 0.6);
        float fr = 0.04 + 0.96 * pow(1.0 - saturate(dot(-V, N)), 5.0);
        colour = (colour - fog.rgb) * lerp(1.0, 0.86, wet * upw) + fog.rgb;
        colour += skyRough * (fr * wet * film * upw * 0.55) * fog.w;
    }
    return colour;
}

// Foliage (trees, their distant cards, grass): the snow caught on it and the rain's darkening. colour: its lit colour before fog, albedo
// lit by `lit` (the light on a white surface there, raw), up: how far the surface faces up (-1..1), hold: how much snow it holds 0..1.
// Nothing without weather (_RfxWeather.w = 0).
float3 RfxWeatherFoliage(float3 colour, float3 P, float up, float3 lit, float hold)
{
    [branch] if (_RfxWeather.w > 0.5)
    {
        float cover = _RfxWeather.y;
        [branch] if (cover > 0.001)
        {
            float n = RfxWNoise(P.xz * 0.9 + P.y * 1.7);
            float m = saturate((up * 0.5 + 0.5) * 1.35 - 0.4 + (n - 0.5) * 0.5) * saturate(cover * 1.3) * hold;
            colour = lerp(colour, float3(0.87, 0.90, 0.95) * lit, m);
        }
        colour *= lerp(1.0, 0.84, _RfxWeather.x * (1.0 - cover));
    }
    return colour;
}

// ---------------------------------------------------------------------------------------------- unlit / prelit surfaces
// What has no light of its own to dim (grass, particles, the crowd, banners, prelit scenery and backdrops) takes the clouds' share here:
//   _RfxWeatherLight    rgb: what is left of its clear-day brightness (the level's ambient + dimmed sun over ambient + sun, with the grey
//                       sky's tint; ReflexWeather.ApplyGlobals), w: the sun's share alone (1 - 0.72 overcast)
//   _RfxWeatherSnowSun  rgb: the dimmed sun on upward-facing snow (the snow's wrap included)
//   _RfxWeatherSnowAmb  rgb: the sky's light on upward-facing snow
// All (1, 1) / unused without weather (_RfxWeather.w = 0): the functions return their input.
float4 _RfxWeatherLight, _RfxWeatherSnowSun, _RfxWeatherSnowAmb;

float3 RfxWeatherLightScale() { return _RfxWeather.w > 0.5 ? _RfxWeatherLight.rgb : float3(1, 1, 1); }
float RfxWeatherSunScale() { return _RfxWeather.w > 0.5 ? _RfxWeatherLight.w : 1.0; }

// The snow foliage holds (RfxWeatherFoliage's mask): up -1..1, hold 0..1. 0 without snow cover.
float RfxWeatherFoliageSnow(float3 P, float up, float hold)
{
    float cover = _RfxWeather.w > 0.5 ? _RfxWeather.y : 0.0;
    float n = RfxWNoise(P.xz * 0.9 + P.y * 1.7);
    return saturate((up * 0.5 + 0.5) * 1.35 - 0.4 + (n - 0.5) * 0.5) * saturate(cover * 1.3) * hold;
}

// A surface with baked or no lighting (prelit scenery, backdrops, banners) under the weather: its brightness follows the clouds, snow
// settles on its upward faces (lit by the level's light on snow: no shadow term, these surfaces have none), rain darkens them. colour:
// before fog, N: the unit world normal (0: no snow), settle 0..1. No branch (the Alive program variants cannot take one here): every
// term is exactly the input without weather.
float3 RfxWeatherPrelit(float3 colour, float3 P, float3 N, float settle)
{
    float on = _RfxWeather.w > 0.5 ? 1.0 : 0.0;
    float wet = _RfxWeather.x * on, cover = _RfxWeather.y * on;
    float3 c = colour * lerp(float3(1, 1, 1), _RfxWeatherLight.rgb, on);
    float n = RfxWNoise(P.xz * 0.21 + P.y * 0.13);   // (broad: these are often far backdrops)
    float m = smoothstep(0.30, 0.78, N.y + (n - 0.5) * 0.4) * saturate(cover * 1.5) * settle;
    float3 snowC = float3(0.87, 0.90, 0.95) * (_RfxWeatherSnowSun.rgb + _RfxWeatherSnowAmb.rgb);
    c = lerp(c, snowC, m);
    return c * lerp(1.0, 0.86, wet * (1.0 - m) * saturate(N.y * 0.6 + 0.6));
}

#endif
