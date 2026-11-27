/* world_dropgate.h -- DropGate's activity handlers (world_dropgate.c). */
#pragma once
#include "rx_game.h"
void rx_drop_gate_activity_init(rx_addr gate, rx_addr activity);                       /* 006bdb50 (thiscall; RET 4) */
void rx_event_unregister_countdown_step(rx_addr channel, rx_addr entity, uint32_t cb, uint32_t arg);   /* 006c3d90 (EAX = the channel; stack entity, callback, arg; RET 0xc) */
void rx_drop_gate_activity_uninit(rx_addr gate, rx_addr activity);                     /* 006bdb90 (thiscall; RET 4) */
void rx_drop_gate_countdown_step(rx_addr gate, rx_addr activity, uint32_t step);       /* 006bed90 (thiscall; RET 8) */
/* 006bdbe0's start rule: the gate starts closed (up, collision on) for a gated race; open (down, collision off) for a free ride (kind 8),
 * mode 0 / 6 (the Overtake career races), a session result (+0xfe8), and PRACTICE (a port addition, practice.h: free to go at once) */
int  rx_gate_starts_closed(rx_addr activity);
void rx_drop_gate_reset(rx_addr gate, rx_addr activity);                               /* 006bdbe0 (EAX = the activity, ECX = the gate) */
void rx_event_register_countdown_step(rx_addr channel, rx_addr entity, uint32_t cb, uint32_t arg);   /* 006c3cd0 (build_scene_props.c; EAX = the channel; stack entity, callback, argument; RET 0xc) */
