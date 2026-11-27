#pragma once
#include "rx_game.h"
#include <stddef.h>
int rx_player_snapshot_export(void* output,uint32_t capacity,uint32_t* required,char* error,size_t error_size);
int rx_player_snapshot_import(const void* input,uint32_t bytes,char* error,size_t error_size);
/* SPLIT SCREEN (w-split): player database `from` (0..3) copied over database `to`. 0 ok, -1 refused (error text). */
/* SPLIT SCREEN (w-split): database `from` copied over database `to` (player 1's profile for player 2, 0 -> 1). The copy is the session's
 * only: rx_player_snapshot_export / _import stay database 0, so nothing player 2 does (laps, records, statistics) is ever saved. */
int rx_player_snapshot_clone(int from,int to,char* error,size_t error_size);
