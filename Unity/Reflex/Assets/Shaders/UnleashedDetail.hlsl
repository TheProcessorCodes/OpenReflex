#ifndef REFLEX_UNLEASHED_DETAIL
#define REFLEX_UNLEASHED_DETAIL
// Unleashed terrain only: the tiled detail without its repeat. Each vertex of a triangle lattice (edge: 1.5 tiles) carries a random
// offset of the tile; a pixel blends the three copies of its triangle's vertices. The weights are sharpened (one copy alone over most
// of the triangle, a narrow blend along the seams) and the blend keeps the texture's contrast (variance-preserving, about the texture's
// mean: its last mip), so the detail is as sharp between the lattice points as at them - a plain bilinear blend of four copies was
// only sharp at the corners. The albedo and relief passes use identical coordinates and so stay aligned. bias: the mip bias of the
// plain tap this replaces (the gradients are scaled by 2^bias). Three taps and one of the last mip per layer.
float2 UnleashedOffset(float2 p)
{
    return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);
}
float4 UnleashedSampleBias(TEXTURE2D_PARAM(tex,samp),float2 uv,float bias)
{
    float k=exp2(bias);
    float2 dx=ddx(uv)*float2(1,-1)*k,dy=ddy(uv)*float2(1,-1)*k;
    // the simplex (triangle) lattice: skewed coordinates, the triangle's three vertices and barycentric weights
    float2 s=float2(uv.x-0.57735027*uv.y,1.15470054*uv.y)*(1.0/1.5);
    float2 cell=floor(s);float3 b=float3(frac(s),0);b.z=1.0-b.x-b.y;
    float3 w;float2 v0,v1,v2;
    if(b.z>0.0){w=float3(b.z,b.y,b.x);v0=cell;v1=cell+float2(0,1);v2=cell+float2(1,0);}
    else{w=float3(-b.z,1.0-b.y,1.0-b.x);v0=cell+float2(1,1);v1=cell+float2(1,0);v2=cell+float2(0,1);}
    w=w*w;w=w*w*w;w/=max(w.x+w.y+w.z,1e-6);   // w^6, normalised
    float2 a0=uv+UnleashedOffset(v0),a1=uv+UnleashedOffset(v1),a2=uv+UnleashedOffset(v2);
    float4 s0=SAMPLE_TEXTURE2D_GRAD(tex,samp,float2(a0.x,1-a0.y),dx,dy);
    float4 s1=SAMPLE_TEXTURE2D_GRAD(tex,samp,float2(a1.x,1-a1.y),dx,dy);
    float4 s2=SAMPLE_TEXTURE2D_GRAD(tex,samp,float2(a2.x,1-a2.y),dx,dy);
    float4 mean=SAMPLE_TEXTURE2D_LOD(tex,samp,float2(.5,.5),16);
    float4 c=s0*w.x+s1*w.y+s2*w.z;
    return saturate(mean+(c-mean)*rsqrt(dot(w,w)));
}
float4 UnleashedSample(TEXTURE2D_PARAM(tex,samp),float2 uv) { return UnleashedSampleBias(TEXTURE2D_ARGS(tex,samp),uv,0); }
#endif
