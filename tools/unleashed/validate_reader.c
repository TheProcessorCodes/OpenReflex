#include "fmt_modtrack.h"
#include <stdio.h>
#include <math.h>
int main(int argc,char**argv){if(argc!=2)return 2;if(fmt_modtrack_open(argv[1])!=1)return 3;if(fmt_modtrack_spacing()!=4)return 4;for(unsigned i=0;i<2048u*2048u;i++)if(!isfinite(fmt_modtrack_height(i)))return 5;printf("PASS: spacing=%d collision chunks=%u\n",fmt_modtrack_spacing(),fmt_modtrack_collision_count());fmt_modtrack_clear();if(fmt_modtrack_spacing()!=2||fmt_modtrack_active())return 6;return 0;}
