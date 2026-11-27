#pragma once
#include <stdint.h>
/* Experimental gzip RXPK package with an RXM1 terrain: 2048 square world-space heights in feet. */
int fmt_modtrack_open(const char* folder);
void fmt_modtrack_clear(void);
int fmt_modtrack_active(void);
int fmt_modtrack_spacing(void);
float fmt_modtrack_height(uint32_t i);
void fmt_modtrack_start(float out[16]);

int fmt_modtrack_collision_claim(uint32_t world);
uint32_t fmt_modtrack_collision_count(void);
const float* fmt_modtrack_collision_chunk(uint32_t index, uint32_t* vertices);

void fmt_modtrack_softness(uint32_t x,uint32_t z,float out[3]);

const float* fmt_modtrack_collision_anchors(uint32_t index);
void fmt_modtrack_scale_point(const float* point,const float* anchor,float scale,float out[3]);

/* Optional RXRA racing extension: source routes, never native database assets. */
int fmt_modtrack_racing(void);
int fmt_modtrack_recovery_retry(uint32_t driver,uint32_t node,float time);
const float* fmt_modtrack_gate(void);
const uint8_t* fmt_modtrack_race_stream(uint32_t* size);
/* Ordered course sectors for imported lap validation; state resets with the mod session. */
int fmt_modtrack_lap_gate(uint32_t driver,int laps,float fraction);
/* Riding layer nearest the authored route, including elevated mesh surfaces. */
float fmt_modtrack_ride_height(float x,float z,float source_y,float terrain_y,float scale);

/* The Reflex start gate kit of an imported level (optional startgate.rxgate: where the level's own gate stood): the donor scene's
 * TO_StartingGate, DropGate and start board (the "30 seconds" girl) are kept and moved there as one rigid group. */
int fmt_modtrack_kit(void);
int fmt_modtrack_kit_entity(const char* type,const char* name);
void fmt_modtrack_kit_donor(const float* gate,const float* drop);   /* the donor's matrices; either NULL: no kit */
void fmt_modtrack_kit_matrix(float m[16]);                          /* a donor matrix of the kit -> its place in this level */
void fmt_modtrack_kit_point(float p[3]);                            /* a donor point of the kit -> this level, on its ground */

/* The funnel blockers of an imported race level (optional funnel.rxfunnel, tools/unleashed/funnel.py): two bale sets (0 down:
 * standing at the start, 1 up: `distance` under the ground at the start) that swap when a racer first passes the gate. */
int fmt_modtrack_funnel(void);
const float* fmt_modtrack_funnel_gate(void);                                 /* position 3, look 3, width, height, rate (ft/s) */
const float* fmt_modtrack_funnel_set(int set,uint32_t* vertices,float move[4]);   /* triangles at the .scn pose; move = direction 3, distance */

/* Built-in imported assets, read through the regular library overlay. */
struct fmt_vfs;
int fmt_imported_terrain_open(const struct fmt_vfs* vfs, const char* level);
