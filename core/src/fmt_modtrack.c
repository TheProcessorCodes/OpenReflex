#include "fmt_modtrack.h"
#include "fmt_vfs.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <zlib.h>
static float* heights;
static uint8_t* surface;
static int imported_surface;
static float pose[6];
static int terrain_spacing=2;
int fmt_modtrack_spacing(void) { return terrain_spacing; }
static struct { uint32_t driver; int lap,sector; } lap_gates[32];
static struct { uint32_t driver,node; float time; int attempts; } recoveries[32];
static uint8_t* race_stream; static uint32_t race_size; static float race_gate[10];
static int build_race(const uint8_t* data,uint32_t size,uint8_t** out,uint32_t* bytes,float gate[10]);
int fmt_modtrack_racing(void) { return race_stream != NULL; }
const float* fmt_modtrack_gate(void) { return race_gate; }
const uint8_t* fmt_modtrack_race_stream(uint32_t* size) { *size=race_size;return race_stream; }
static uint8_t* collision; static uint32_t collision_count, collision_world;
int fmt_modtrack_active(void) { return heights != NULL; }
/* The start gate kit: kit_pose = the drop bar's place and the riding direction in this level; kit_from = the donor frame (the
 * donor gate's axes at the donor drop bar), set when the scene is read. kit_state 0 none, 1 pose known, 2 donor frame known. */
static float kit_pose[6], kit_from[16]; static int kit_state;
int fmt_modtrack_kit(void) { return kit_state==2; }
int fmt_modtrack_kit_entity(const char* type,const char* name) {
    if(!kit_state||!type)return 0;
    if(!strcmp(type,"DropGateRSData")||!strcmp(type,"ThirtySecondBoardRSData"))return 1;
    return !strcmp(type,"CollisionRSData")&&name&&!strcmp(name,"TO_StartingGate");
}
void fmt_modtrack_kit_donor(const float* gate,const float* drop) {
    if(!kit_state)return;
    if(!gate||!drop){kit_state=0;return;}
    memcpy(kit_from,gate,sizeof kit_from);memcpy(kit_from+12,drop+12,3*sizeof(float));kit_state=2;
}
void fmt_modtrack_kit_matrix(float m[16]) {
    if(kit_state!=2)return;
    /* m' = m * inverse(from) * to, row vectors; both frames are rotations about y with a translation */
    const float* f=kit_from; float length=hypotf(kit_pose[3],kit_pose[5]), x=kit_pose[3]/length, z=kit_pose[5]/length;
    const float to[16]={z,0,-x,0, 0,1,0,0, x,0,z,0, kit_pose[0],kit_pose[1],kit_pose[2],1};
    float inv[16]={f[0],f[4],f[8],0, f[1],f[5],f[9],0, f[2],f[6],f[10],0, 0,0,0,1};
    for(int c=0;c<3;c++)inv[12+c]=-(f[12]*inv[c]+f[13]*inv[4+c]+f[14]*inv[8+c]);
    float a[16],b[16];
    for(int r=0;r<4;r++)for(int c=0;c<4;c++){a[4*r+c]=0;for(int k=0;k<4;k++)a[4*r+c]+=m[4*r+k]*inv[4*k+c];}
    for(int r=0;r<4;r++)for(int c=0;c<4;c++){b[4*r+c]=0;for(int k=0;k<4;k++)b[4*r+c]+=a[4*r+k]*to[4*k+c];}
    memcpy(m,b,sizeof b);
}
void fmt_modtrack_kit_point(float p[3]) {
    if(kit_state!=2||!heights)return;
    float m[16]={1,0,0,0, 0,1,0,0, 0,0,1,0, p[0],p[1],p[2],1};
    fmt_modtrack_kit_matrix(m);
    /* on the level's own ground (the heights are in level feet, the grid centred, rows from +z) */
    float half=1023.5f*(float)terrain_spacing, gx=fminf(2046.999f,fmaxf(0,(m[12]+half)/(float)terrain_spacing)), gz=fminf(2046.999f,fmaxf(0,(half-m[14])/(float)terrain_spacing));
    int ix=(int)gx,iz=(int)gz; float fx=gx-(float)ix,fz=gz-(float)iz; const float* h=heights+(size_t)iz*2048+ix;
    p[0]=m[12];p[2]=m[14];p[1]=(h[0]*(1-fx)+h[1]*fx)*(1-fz)+(h[2048]*(1-fx)+h[2049]*fx)*fz;
}
static float funnel_gate[9], funnel_move[2][4]; static float* funnel_points[2]; static uint32_t funnel_count[2]; static int funnel_on;
int fmt_modtrack_funnel(void) { return funnel_on; }
const float* fmt_modtrack_funnel_gate(void) { return funnel_gate; }
const float* fmt_modtrack_funnel_set(int set,uint32_t* vertices,float move[4]) {
    if(!funnel_on||set<0||set>1)return NULL;
    *vertices=funnel_count[set];memcpy(move,funnel_move[set],sizeof funnel_move[set]);return funnel_points[set];
}
static void funnel_clear(void) { for(int k=0;k<2;k++){free(funnel_points[k]);funnel_points[k]=NULL;funnel_count[k]=0;} funnel_on=0; }
/* RXFN v1 (tools/unleashed/funnel.py): every value bounded; 1 on success, 0 on a malformed file (nothing kept) */
static int funnel_read(const uint8_t* d,size_t size) {
    funnel_clear();
    if(size<44+2*20||memcmp(d,"RXFN",4)||fmt_u32(d+4)!=1)return 0;
    for(int i=0;i<9;i++){funnel_gate[i]=fmt_f32(d+8+4*i);if(!isfinite(funnel_gate[i])||fabsf(funnel_gate[i])>100000)return 0;}
    if(fabsf(hypotf(funnel_gate[3],funnel_gate[5])-1)>.01f||funnel_gate[6]<1||funnel_gate[6]>2000||funnel_gate[8]<=0||funnel_gate[8]>1000)return 0;
    size_t at=44;
    for(int k=0;k<2;k++) {
        if(at+20>size){funnel_clear();return 0;}
        for(int i=0;i<4;i++)funnel_move[k][i]=fmt_f32(d+at+4*i);
        uint32_t n=fmt_u32(d+at+16);at+=20;
        float len=sqrtf(funnel_move[k][0]*funnel_move[k][0]+funnel_move[k][1]*funnel_move[k][1]+funnel_move[k][2]*funnel_move[k][2]);
        if(!isfinite(len)||fabsf(len-1)>.01f||!(funnel_move[k][3]>0)||funnel_move[k][3]>200||n>60000||n%3||at+(size_t)n*12>size){funnel_clear();return 0;}
        if(n){funnel_points[k]=malloc((size_t)n*12);if(!funnel_points[k]){funnel_clear();return 0;}}
        for(uint32_t i=0;i<n*3;i++){float f=fmt_f32(d+at+4*i);if(!isfinite(f)||fabsf(f)>100000){funnel_clear();return 0;}funnel_points[k][i]=f;}
        funnel_count[k]=n;at+=(size_t)n*12;
    }
    if(at!=size){funnel_clear();return 0;}
    funnel_on=1;return 1;
}
void fmt_modtrack_clear(void) { funnel_clear(); kit_state=0; imported_surface=0; terrain_spacing=2; memset(recoveries,0,sizeof recoveries); memset(lap_gates,0,sizeof lap_gates); free(race_stream);race_stream=NULL;race_size=0; free(heights); heights = NULL; free(surface); surface=NULL; memset(pose,0,sizeof pose); free(collision); collision=NULL; collision_count=collision_world=0; }
float fmt_modtrack_height(uint32_t i) { return heights && i < 2048u*2048u ? heights[i] : 0; }
int fmt_modtrack_open(const char* folder) {
    char path[4096];
    if (!folder || snprintf(path,sizeof path,"%s",folder) >= (int)sizeof path) return -1;
    if(!strstr(path,".rxtrack")) return 0;
    gzFile f=gzopen(path,"rb"); if (!f) return -1;
    uint32_t package[3];
    if(gzread(f,package,12)!=12 || memcmp(package,"RXPK",4) || (package[1]<1 || package[1]>4) || package[2]>65536) { gzclose(f); return -1; }
    char metadata[65536]; if(gzread(f,metadata,package[2])!=(int)package[2]) { gzclose(f); return -1; }
    uint32_t header[4]; float p[6]; float* data=NULL; uint8_t* coll=NULL; uint8_t* surf=NULL; uint32_t chunks=0; int ok=0; uint8_t* race=NULL; uint32_t rsize=0; float gate[10];
    if (gzread(f,header,sizeof header)!=sizeof header || memcmp(header,"RXM1",4) || header[1]!=1 || header[2]!=2048 || (header[3]!=0x40000000u && header[3]!=0x40800000u)) goto done;
    if (gzread(f,p,sizeof p)!=sizeof p) goto done;
    for(int i=0;i<6;i++) if(!isfinite(p[i])) goto done;
    if(p[3]*p[3]+p[5]*p[5]<0.01f) goto done;
    data=malloc(2048u*2048u*sizeof(float)); if(!data) goto done;
    if(gzread(f,data,2048u*2048u*sizeof(float))!=2048u*2048u*sizeof(float)) goto done;
    if(package[1]>=2) {
        uint32_t section[2]; char scratch[8192];
        if(gzread(f,section,8)!=8 || memcmp(section,"SCNE",4) || section[1]<8 || section[1]>128u*1024u*1024u) goto done;
        uint32_t left=section[1];
        while(left) { unsigned count=left>sizeof scratch?sizeof scratch:left; if(gzread(f,scratch,count)!=(int)count) goto done; left-=count; }
        if(gzread(f,section,8)!=8 || memcmp(section,"COLL",4) || section[1]<12 || section[1]>32u*1024u*1024u) goto done;
        coll=malloc(section[1]);if(!coll || gzread(f,coll,section[1])!=(int)section[1]) goto done;
        uint32_t version;memcpy(&version,coll+4,4);memcpy(&chunks,coll+8,4);
        if(memcmp(coll,"RXCO",4)||(version!=1&&version!=2)||chunks>10000) goto done;
        size_t offset=12;
        for(uint32_t chunk=0;chunk<chunks;chunk++) {
            uint32_t count;if(offset+4>section[1])goto done;memcpy(&count,coll+offset,4);offset+=4;
            if(!count || count>48000 || count%3 || offset+(size_t)count*12>section[1])goto done;
            for(uint32_t k=0;k<count*3;k++){float value;memcpy(&value,coll+offset+4*k,4);if(!isfinite(value)||fabsf(value)>100000)goto done;}
            offset+=(size_t)count*12;
            if(version==2) {
                if(offset+(size_t)count*16>section[1])goto done;
                for(uint32_t k=0;k<count*4;k++){float v;memcpy(&v,coll+offset+4*k,4);if(!isfinite(v)||fabsf(v)>100000||(k%4==3&&v!=0&&v!=1))goto done;}
                offset+=(size_t)count*16;
            }
        }
        if(offset!=section[1])goto done;
    }
    if(package[1]>=3) {
        uint32_t section[2], h[3];
        if(gzread(f,section,8)!=8 || memcmp(section,"SURF",4) || section[1]!=12+2048u*2048u*4u) goto done;
        if(gzread(f,h,12)!=12 || memcmp(h,"RXSM",4) || h[1]!=1 || h[2]!=2048) goto done;
        surf=malloc(2048u*2048u*4u);if(!surf || gzread(f,surf,2048u*2048u*4u)!=2048u*2048u*4u)goto done;
        for(uint32_t i=0;i<2048u*2048u;i++)if((uint32_t)surf[4*i]+surf[4*i+1]+surf[4*i+2]>255)goto done;
    }
    if(package[1]>=4) {
        uint32_t section[2],h[3]; char scratch[8192];
        if(gzread(f,section,8)!=8 || memcmp(section,"COLR",4) || section[1]!=12+2048u*2048u*4u) goto done;
        if(gzread(f,h,12)!=12 || memcmp(h,"RXCM",4) || h[1]!=1 || h[2]!=2048) goto done;
        uint32_t left=2048u*2048u*4u;
        while(left) { unsigned count=left>sizeof scratch?sizeof scratch:left; if(gzread(f,scratch,count)!=(int)count) goto done; left-=count; }
    }
    int next=gzgetc(f);
    if(next!=-1) {
        uint32_t section[2]; ((uint8_t*)section)[0]=(uint8_t)next;
        if(gzread(f,(uint8_t*)section+1,7)!=7 || memcmp(section,"RACE",4) || section[1]<100 || section[1]>164000)goto done;
        uint8_t* payload=malloc(section[1]);
        if(!payload)goto done;
        int valid=gzread(f,payload,section[1])==(int)section[1] && build_race(payload,section[1],&race,&rsize,gate);
        free(payload);if(!valid || gzgetc(f)!=-1)goto done;
    }
    if(!gzeof(f))goto done;
    for(uint32_t i=0;i<2048u*2048u;i++) if(!isfinite(data[i]) || fabsf(data[i])>100000) goto done;
    fmt_modtrack_clear();terrain_spacing=header[3]==0x40800000u?4:2;race_stream=race;race=NULL;race_size=rsize;if(rsize)memcpy(race_gate,gate,sizeof gate); surface=surf;surf=NULL; heights=data; data=NULL; collision=coll;coll=NULL;collision_count=chunks; memcpy(pose,p,sizeof pose); ok=1;
 done: free(race); free(surf); free(coll); free(data); gzclose(f); return ok?1:-1;
}
/* Imported library terrain is resolved through the normal VFS (loose files or
 * RXAR). It shares the portable terrain implementation with experimental tracks,
 * but never mounts a mod or changes the session's content identity. */
int fmt_imported_terrain_open(const fmt_vfs* vfs, const char* level) {
    char path[512]; fmt_buf terrain={0}, coll={0}, surf={0}, race={0}; uint8_t* built_race=NULL; uint32_t built_size=0; float gate[10]; int result=-1;
    if(!level || strncmp(level,"UNL_",4)) return 0;
    for(const char* p=level;*p;p++) if(!((*p>='A'&&*p<='Z')||(*p>='a'&&*p<='z')||(*p>='0'&&*p<='9')||*p=='_'))return -1;
    snprintf(path,sizeof path,"Assets/unleashed/%s/terrain.rxheight",level);
    if(fmt_vfs_size(vfs,path)!=40+2048u*2048u*4u || fmt_vfs_read_file(vfs,path,&terrain)<0)goto done;
    const uint8_t* t=terrain.data;float spacing=fmt_f32(t+12);
    if(memcmp(t,"RXM1",4)||fmt_u32(t+4)!=1||fmt_u32(t+8)!=2048||(spacing!=2&&spacing!=4&&spacing!=8&&spacing!=16))goto done;
    for(size_t i=16;i<terrain.size;i+=4)if(!isfinite(fmt_f32(t+i))||fabsf(fmt_f32(t+i))>100000)goto done;
    if(hypotf(fmt_f32(t+28),fmt_f32(t+36))<.01f)goto done;
    snprintf(path,sizeof path,"Assets/unleashed/%s/collision.rxcollision",level);
    int64_t size=fmt_vfs_size(vfs,path);
    if(size<12||size>32*1024*1024||fmt_vfs_read_file(vfs,path,&coll)<0)goto done;
    if(memcmp(coll.data,"RXCO",4)||fmt_u32(coll.data+4)!=1)goto done;
    uint32_t chunks=fmt_u32(coll.data+8);size_t offset=12;
    if(chunks>10000)goto done;
    for(uint32_t i=0;i<chunks;i++) {
        if(offset+4>coll.size)goto done;
        uint32_t count=fmt_u32(coll.data+offset);offset+=4;
        if(!count||count>48000||count%3||offset+(size_t)count*12>coll.size)goto done;
        for(uint32_t k=0;k<count*3;k++){float f=fmt_f32(coll.data+offset+4*k);if(!isfinite(f)||fabsf(f)>100000)goto done;}
        offset+=(size_t)count*12;
    }
    if(offset!=coll.size)goto done;
    snprintf(path,sizeof path,"Assets/unleashed/%s/surface.rxsurface",level);
    if(fmt_vfs_size(vfs,path)>0) {
        if(fmt_vfs_size(vfs,path)!=12+2048u*2048u*4u||fmt_vfs_read_file(vfs,path,&surf)<0)goto done;
        if(memcmp(surf.data,"RXSM",4)||fmt_u32(surf.data+4)!=1||fmt_u32(surf.data+8)!=2048)goto done;
        for(size_t i=12;i<surf.size;i+=4)if((unsigned)surf.data[i]+surf.data[i+1]+surf.data[i+2]>255)goto done;
        memmove(surf.data,surf.data+12,2048u*2048u*4u);
    }
    snprintf(path,sizeof path,"Assets/unleashed/%s/race.rxrace",level);
    if(fmt_vfs_size(vfs,path)>0) {
        if(fmt_vfs_size(vfs,path)>200000||fmt_vfs_read_file(vfs,path,&race)<0||!build_race(race.data,(uint32_t)race.size,&built_race,&built_size,gate))goto done;
    }
    /* Optional: where the level's own start gate stood (RXSG v1: position, riding direction). */
    float kit[6];int has_kit=0;
    snprintf(path,sizeof path,"Assets/unleashed/%s/startgate.rxgate",level);
    if(fmt_vfs_size(vfs,path)>0) {
        fmt_buf g={0};
        if(fmt_vfs_size(vfs,path)!=32||fmt_vfs_read_file(vfs,path,&g)<0)goto done;
        int ok=!memcmp(g.data,"RXSG",4)&&fmt_u32(g.data+4)==1;
        for(int i=0;i<6&&ok;i++){kit[i]=fmt_f32(g.data+8+4*i);ok=isfinite(kit[i])&&fabsf(kit[i])<100000;}
        ok=ok&&fabsf(hypotf(kit[3],kit[5])-1)<.01f&&fabsf(kit[4])<.01f;
        fmt_buf_free(&g);if(!ok)goto done;
        has_kit=1;
    }
    fmt_buf fn={0};
    snprintf(path,sizeof path,"Assets/unleashed/%s/funnel.rxfunnel",level);
    if(fmt_vfs_size(vfs,path)>0 && (fmt_vfs_size(vfs,path)>2000000||fmt_vfs_read_file(vfs,path,&fn)<0))goto done;
    float* data=malloc(2048u*2048u*4u);if(!data){fmt_buf_free(&fn);goto done;}
    memcpy(data,t+40,2048u*2048u*4u);
    fmt_modtrack_clear();
    if(fn.data){int ok=funnel_read(fn.data,fn.size);fmt_buf_free(&fn);if(!ok){free(data);goto done;}}
    if(has_kit){memcpy(kit_pose,kit,sizeof kit);kit_state=1;}imported_surface=1;surface=surf.data;surf.data=NULL;heights=data;terrain_spacing=(int)spacing;memcpy(pose,t+16,sizeof pose);
    race_stream=built_race;built_race=NULL;race_size=built_size;if(built_size)memcpy(race_gate,gate,sizeof gate);
    collision=coll.data;coll.data=NULL;collision_count=chunks;result=1;
 done:
    free(built_race);fmt_buf_free(&race);fmt_buf_free(&terrain);fmt_buf_free(&coll);fmt_buf_free(&surf);
    if(result<0)fmt_fail("invalid imported terrain for %s",level);
    return result;
}

void fmt_modtrack_start(float out[16]) {
    float length=sqrtf(pose[3]*pose[3]+pose[5]*pose[5]), x=pose[3]/length,z=pose[5]/length;
    memset(out,0,16*sizeof(float)); out[0]=z;out[2]=-x;out[5]=1;out[8]=x;out[10]=z;out[15]=1;
    out[12]=pose[0];out[13]=pose[1];out[14]=pose[2];
}

int fmt_modtrack_collision_claim(uint32_t world) { if(!collision || collision_world==world)return 0;collision_world=world;return 1; }
uint32_t fmt_modtrack_collision_count(void) { return collision_count; }
const float* fmt_modtrack_collision_chunk(uint32_t index,uint32_t* vertices) {
    if(!collision||index>=collision_count)return NULL;
    size_t offset=12;uint32_t count=0;
    for(uint32_t k=0;k<=index;k++){memcpy(&count,collision+offset,4);offset+=4;if(k!=index)offset+=(size_t)count*(collision[4]==2?28:12);}
    *vertices=count;return (const float*)(collision+offset);
}

void fmt_modtrack_softness(uint32_t x,uint32_t z,float out[3]) {
    if(!surface || x>=2048 || z>=2048){out[0]=out[1]=out[2]=1;return;}
    const uint8_t* p=surface+4*(z*2048+x);float grass=p[0]/255.f,rock=p[2]/255.f;
    out[0]=.85f*(1-grass-rock)+(imported_surface && p[3]==255?.85f:.30f)*grass+.05f*rock;
    out[1]=.65f*(1-grass-rock)+(imported_surface && p[3]==255?.65f:.25f)*grass+.05f*rock;out[2]=1;
}

const float* fmt_modtrack_collision_anchors(uint32_t index) {
    uint32_t count;const float* points=fmt_modtrack_collision_chunk(index,&count);
    return points && collision[4]==2 ? points+count*3 : NULL;
}
void fmt_modtrack_scale_point(const float* point,const float* anchor,float scale,float out[3]) {
    for(int k=0;k<3;k++)out[k]=anchor && anchor[3]==1 ? point[k]+anchor[k]*(scale-1) : point[k]*scale;
}

/* Translate the bounded, portable RXRA route to an in-memory stream for the
 * original Reflex parser. This is not stored in or resolved from a database. */
typedef struct { uint8_t* data; uint32_t pos; } race_writer;
static void rw_u(race_writer* w,uint32_t v) { v=__builtin_bswap32(v);memcpy(w->data+w->pos,&v,4);w->pos+=4; }
static void rw_f(race_writer* w,float f) { uint32_t v;memcpy(&v,&f,4);rw_u(w,v); }
static void rw_tag(race_writer* w,uint32_t t,uint32_t n) { rw_u(w,t);rw_u(w,n); }
static void rw_word(race_writer* w,uint32_t t,uint32_t v) { rw_tag(w,t,4);rw_u(w,v); }
static void rw_float(race_writer* w,uint32_t t,float v) { rw_tag(w,t,4);rw_f(w,v); }
static void rw_vec(race_writer* w,uint32_t t,float x,float y,float z) { rw_tag(w,t,12);rw_f(w,x);rw_f(w,y);rw_f(w,z); }
static void rw_grid(race_writer* w,uint32_t t,uint32_t v) { rw_tag(w,t,136);rw_u(w,8);rw_u(w,4);for(int i=0;i<32;i++)rw_u(w,v); }
static void rw_marker(race_writer* w,uint32_t t,const float* p,const float* d,float width,uint32_t sec,uint32_t node) {
    rw_tag(w,t,48);
    rw_f(w,p[0]);rw_f(w,5001);rw_f(w,p[2]);rw_u(w,0);
    rw_f(w,d[0]);rw_f(w,0);rw_f(w,d[2]);rw_u(w,0);
    rw_f(w,width);rw_u(w,0x01000000); /* enabled byte is not byte-swapped by the native reader */
    rw_u(w,sec);rw_u(w,node);
}
static float route_length(const float* p,uint32_t n) {
    float total=0;for(uint32_t i=1;i<n;i++)total+=hypotf(p[5*i]-p[5*i-5],p[5*i+2]-p[5*i-3]);return total;
}
static int build_race(const uint8_t* data,uint32_t size,uint8_t** out,uint32_t* bytes,float gate[10]) {
    if(size<100||memcmp(data,"RXRA",4))return 0;
    uint32_t h[4];memcpy(h,data+4,16);
    const uint32_t n=h[2],m=h[3];
    if(h[0]!=1||h[1]>1||n<10||m<2||n>4096||m>4096||size!=100+20*(n+m))return 0;
    const float* g=(const float*)(data+20);const float* lap=(const float*)(data+100);const float* lead=lap+5*n;
    for(uint32_t i=0;i<20+5*(n+m);i++)if(!isfinite(g[i])||fabsf(g[i])>100000)return 0;
    for(int i=0;i<2;i++)if(g[10*i+9]<10||g[10*i+9]>300||fabsf(hypotf(g[10*i+3],g[10*i+5])-1)>.01f)return 0;
    for(uint32_t i=0;i<n+m;i++)if(lap[5*i+3]<6||lap[5*i+3]>300||lap[5*i+4]<1||lap[5*i+4]>200)return 0;
    if(hypotf(lap[0]-lap[5*(n-1)],lap[2]-lap[5*(n-1)+2])>.1f || hypotf(lap[0]-lead[5*(m-1)],lap[2]-lead[5*(m-1)+2])>.1f)return 0;
    const float lengths[2]={route_length(lap,n),route_length(lead,m)};
    if(lengths[0]<100||lengths[1]<1)return 0;
    race_writer w={malloc((n+m)*1800u+8192u),0};if(!w.data)return 0;
    rw_u(&w,n+m);rw_u(&w,2);rw_u(&w,4);rw_u(&w,4);
    rw_word(&w,0x1f,h[1]);
    float extent=2047;
    for(uint32_t i=0;i<n+m;i++)extent=fmaxf(extent,fmaxf(fabsf(lap[5*i]),fabsf(lap[5*i+2]))+100);
    rw_vec(&w,1,extent,5001,extent);rw_vec(&w,2,-extent,-1000,-extent);rw_vec(&w,3,2*extent,6001,2*extent);

    rw_word(&w,7,1); /* Start chute arms the lap clock; it is not a completed lap. */
    rw_float(&w,4,lengths[1]);rw_float(&w,5,lengths[0]);
    rw_marker(&w,9,lap,g+13,lap[3],0,n-1); /* finish */
    rw_marker(&w,8,lead,g+3,lead[3],1,0); /* start */
    for(uint32_t s=0;s<2;s++) {
        const float* p=s?lead:lap;uint32_t count=s?m:n;
        rw_tag(&w,11,0);rw_float(&w,13,lengths[s]);rw_word(&w,17,s?4:0);
        rw_grid(&w,0x13,195); /* enabled class/lane choices, as native authored sections */
        rw_word(&w,12,1);rw_tag(&w,14,8);rw_u(&w,0);rw_u(&w,0);
        rw_word(&w,15,s?0:2);
        if(!s)for(uint32_t j=0;j<2;j++){rw_tag(&w,16,8);rw_u(&w,j);rw_u(&w,j);}
        float remaining=lengths[s];
        /* Each node's forward direction, segment length and edge points first: the edges are corrected before they are written. */
        float* e=malloc((size_t)count*7*sizeof(float));if(!e){free(w.data);return 0;}
        for(uint32_t i=0;i<count;i++) {
            const float* q=p+5*i; const float* next=i+1<count?q+5:q;
            float dx=next[0]-q[0],dz=next[2]-q[2],len=hypotf(dx,dz);
            if(i+1==count){dx=lap[5]-lap[0];dz=lap[7]-lap[2];}
            float norm=hypotf(dx,dz);if(norm<.001f){free(e);free(w.data);return 0;}dx/=norm;dz/=norm;
            const float* prev=i?q-5:(s?q:lap+5*(n-2));
            if(i+1==count)prev=lap+5*(n-2);
            float px=q[0]-prev[0],pz=q[2]-prev[2],pn=hypotf(px,pz);
            float ex=dx+(pn>.001f?px/pn:dx),ez=dz+(pn>.001f?pz/pn:dz),en=hypotf(ex,ez);
            if(en<.001f){ex=dx;ez=dz;}else{ex/=en;ez/=en;}
            float* o=e+7*i;
            /* Reflex's left/right edge winding: left = (-forward.z, forward.x). */
            o[0]=q[0]-ez*q[3]*.5f;o[1]=q[2]+ex*q[3]*.5f;o[2]=q[0]+ez*q[3]*.5f;o[3]=q[2]-ex*q[3]*.5f;o[4]=dx;o[5]=dz;o[6]=len;
        }
        /* A corner tighter than half the track's width folds its inner edge back on itself: the segment then contains no position at
         * all (race_track.c rx_track_segment_contains: every side must agree), and a rider on the racing line reads OFF TRACK through
         * the corner. The inner edge stays at the apex instead while it would run backwards: an edge of two identical points does not
         * limit a segment (the native tracks' forks and joins), so the corner becomes a fan of segments around the apex. */
        for(uint32_t i=0;i+1<count;i++) {
            float* o=e+7*i; float* nx=o+7;
            for(int k=0;k<4;k+=2) if((nx[k]-o[k])*o[4]+(nx[k+1]-o[k+1])*o[5]<.05f){nx[k]=o[k];nx[k+1]=o[k+1];}
        }
        for(uint32_t i=0;i<count;i++) {
            const float* q=p+5*i; const float* o=e+7*i;
            rw_tag(&w,20,0);rw_vec(&w,21,q[0],q[1],q[2]);
            rw_vec(&w,22,o[0],q[1],o[1]);
            rw_vec(&w,23,o[2],q[1],o[3]);
            rw_vec(&w,26,o[4],0,o[5]);rw_float(&w,24,o[6]);rw_float(&w,25,fmaxf(0,remaining));
            rw_grid(&w,0x1c,264);/* The line speed (node +0xe8): -1 is "none", the AI then rides at what its own cornering allows. 45 is the converters'
             * placeholder, not an authored speed: as a limit it held every rider to 45 units/s on the straights too (laps a third slower). */
            float speed=q[4]==45.f?-1.f:fminf(90,q[4]);uint32_t bits;memcpy(&bits,&speed,4);rw_grid(&w,0x1d,bits);
            remaining-=o[6];
        }
        free(e);
    }
    memcpy(gate,g,40);*out=w.data;*bytes=w.pos;return 1;
}

int fmt_modtrack_lap_gate(uint32_t driver,int laps,float fraction) {
    if(laps<0) {
        for(int i=0;i<32;i++)if(lap_gates[i].driver==driver)lap_gates[i].driver=0;
        return 1; /* the first finish crossing only arms the clock */
    }
    for(int i=0;i<32;i++)if(lap_gates[i].driver==driver || !lap_gates[i].driver) {
        if(lap_gates[i].driver!=driver || lap_gates[i].lap!=laps){lap_gates[i].driver=driver;lap_gates[i].lap=laps;lap_gates[i].sector=0;}
        int sector=lap_gates[i].sector;
        if(sector<3 && fraction>=(sector+1)*.25f && fraction<(sector+1)*.25f+.15f)lap_gates[i].sector++;
        return lap_gates[i].sector==3;
    }
    return 0;
}

int fmt_modtrack_recovery_retry(uint32_t driver,uint32_t node,float time) {
    for(int i=0;i<32;i++)if(recoveries[i].driver==driver || !recoveries[i].driver) {
        /* Adjacent nodes can alternate while a bike is trapped on a jump lip.
         * A sustained interval without another crash, rather than a node change,
         * ends this recovery sequence. */
        if(recoveries[i].driver!=driver || time<recoveries[i].time || time-recoveries[i].time>8.f)recoveries[i].attempts=0;
        recoveries[i].driver=driver;recoveries[i].node=node;recoveries[i].time=time;
        return ++recoveries[i].attempts>=3;
    }
    return 0;
}

float fmt_modtrack_ride_height(float x,float z,float source_y,float terrain_y,float scale) {
    float best=terrain_y,distance=fabsf(terrain_y-source_y);
    for(uint32_t chunk=0;chunk<collision_count;chunk++) {
        uint32_t count;const float* v=fmt_modtrack_collision_chunk(chunk,&count);
        const float* anchors=fmt_modtrack_collision_anchors(chunk);
        for(uint32_t i=0;i<count;i+=3) {
            float p[3][3];
            for(int j=0;j<3;j++)fmt_modtrack_scale_point(v+3*(i+j),anchors?anchors+4*(i+j):NULL,scale,p[j]);
            if(x<fminf(p[0][0],fminf(p[1][0],p[2][0]))||x>fmaxf(p[0][0],fmaxf(p[1][0],p[2][0]))||z<fminf(p[0][2],fminf(p[1][2],p[2][2]))||z>fmaxf(p[0][2],fmaxf(p[1][2],p[2][2])))continue;
            float ax=p[1][0]-p[0][0],az=p[1][2]-p[0][2],bx=p[2][0]-p[0][0],bz=p[2][2]-p[0][2],den=ax*bz-az*bx;
            if(fabsf(den)<.0001f)continue;
            float u=((x-p[0][0])*bz-(z-p[0][2])*bx)/den,w=(ax*(z-p[0][2])-az*(x-p[0][0]))/den;
            if(u<0||w<0||u+w>1)continue;
            float y=p[0][1]+u*(p[1][1]-p[0][1])+w*(p[2][1]-p[0][1]),d=fabsf(y-source_y);
            if(d<distance){best=y;distance=d;}
        }
    }
    return best;
}
