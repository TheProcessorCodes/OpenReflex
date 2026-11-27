/* alive_class_probe.c -- ALIVE CLASSES (core/plan/ALIVE_BIKES.md): the local bike with and without another game's parameter file
 * (rx_session_vehicle_physics), ridden full throttle on the reference National, its physics class, its bodies, its model pose and its speed.
 * Static link with the product objects (core/tools/alive_class_probe.sh builds it):
 * usage: alive_class_probe <game files> <frames>
 * env: ALIVE_EQUIP=<owner>,<id> (the local's vehicle, as bike_parity_probe's PROBE_EQUIP: 4,140 an AI rider's Blaze 125S, 4,64 a Blaze 450F),
 *      ALIVE_PHYS=<VehicleModelId>,<class>,<bxml file> (rx_session_vehicle_physics before the load), ALIVE_NODES=<node file> (its
 *      rx_session_vehicle_physics_nodes), ALIVE_EVENT as PROBE_EVENT,
 *      ALIVE_BRAKE_AT=<racing frame> (throttle off and the brake from then).
 * Output: "class", then "F <racing frame> speed <ft/s> rpm <rpm> body k (x y z) ... node <name> (x y z) ..." every 30 racing frames, then
 * "MAX speed". Development tool only. */
#include "rx_api.h"
#include "rx_mem.h"
#include "build_activity.h"
#include "build_vehicle_select.h"
#include "race_activation.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char** argv) {
    if (argc < 3) { fprintf(stderr, "usage: %s <game files> <frames>\n", argv[0]); return 2; }
    setvbuf(stdout, NULL, _IONBF, 0);
    const int frames = atoi(argv[2]);
    rx_session* s = rx_session_create(argv[1]);
    if (!s) { fprintf(stderr, "create: %s\n", rx_session_last_error(NULL)); return 1; }
    rx_event_spec e; rx_session_event_default(&e);
    e.riders = 1;
    if (getenv("ALIVE_EVENT")) {
        char buf[256]; snprintf(buf, sizeof buf, "%s", getenv("ALIVE_EVENT"));
        char* f[7] = {0}; int k = 0; for (char* t = strtok(buf, ","); t && k < 7; t = strtok(NULL, ",")) f[k++] = t;
        if (k != 7) return 2;
        snprintf(e.event_type, sizeof e.event_type, "%s", f[0]); e.event_type_id = atoi(f[1]);
        snprintf(e.location, sizeof e.location, "%s", f[2]); e.location_id = atoi(f[3]);
        e.vehicle_class = atoi(f[4]); e.riders = atoi(f[5]); e.ai_difficulty = atoi(f[6]);
    }
    if (getenv("ALIVE_PHYS")) {
        int model = -1, cls = -1; char path[1024] = {0};
        if (sscanf(getenv("ALIVE_PHYS"), "%d,%d,%1023s", &model, &cls, path) != 3) return 2;
        FILE* f = fopen(path, "rb"); if (!f) return 2;
        static uint8_t b[1 << 20]; size_t n = fread(b, 1, sizeof b, f); fclose(f);
        char rep[256];
        int r = rx_session_vehicle_physics(s, model, cls, b, (uint32_t)n, rep, sizeof rep);
        printf("vehicle physics: %d (%s)%s%s\n", r, rep, r < 0 ? ": " : "", r < 0 ? rx_session_last_error(s) : "");
        if (r < 0) return 1;
        if (getenv("ALIVE_NODES")) {   /* the bike's own hierarchy ("<node> <16 floats>" lines) */
            FILE* nf = fopen(getenv("ALIVE_NODES"), "rb"); if (!nf) return 2;
            static char text[65536]; size_t tn = fread(text, 1, sizeof text - 1, nf); fclose(nf); text[tn] = 0;
            printf("vehicle physics nodes: %d\n", rx_session_vehicle_physics_nodes(s, model, text));
        }
    }
    if (getenv("ALIVE_EQUIP")) {
        int owner = -1, id = -1; if (sscanf(getenv("ALIVE_EQUIP"), "%d,%d", &owner, &id) != 2) return 2;
        rx_ensure_local_players(0, 0);
        const rx_addr sp = rx_sp();
        rx_set_sp(sp - 8); const uint32_t row = U32(rx_player_table_row(0), 0); rx_set_sp(sp);
        rx_set_sp(sp - 12); rx_table_equip_vehicle((uint32_t)owner, row, (uint32_t)id); rx_set_sp(sp);
    }
    if (rx_session_event_load(s, &e)) { fprintf(stderr, "load: %s\n", rx_session_last_error(s)); return 1; }
    static const char* names[18] = {"OBJ_Frame", "OBJ_UPR_Forks", "OBJ_Throttle", "OBJ_LWR_Forks", "OBJ_FRT_Tire", "OBJ_FRT_Fender", "OBJ_ClutchLVR",
                                    "OBJ_BrakeLVR", "NDE_RHT_Hand", "NDE_LFT_Hand", "NDE_FRT_FenderVIB", "OBJ_Swingarm", "OBJ_BCK_Tire", "OBJ_BCK_Fender",
                                    "NDE_RiderLocator", "NDE_RHT_Foot", "NDE_LFT_Foot", "NDE_BCK_FenderVIB"};
    const int brake_at = getenv("ALIVE_BRAKE_AT") ? atoi(getenv("ALIVE_BRAKE_AT")) : -1;
    int racing = 0; float peak = 0;
    for (int i = 0; i < frames; i++) {
        uint8_t keys[256] = {0};
        if (racing) { if (brake_at >= 0 && racing >= brake_at) keys[0x1f] = 0x80; else keys[0x11] = 0x80; }
        rx_pad pad[4] = {{0}}; uint32_t mask = 0;
        if (getenv("ALIVE_PAD") && racing) {   /* ALIVE_PAD=<period frames>: pad 0, full trigger, both sticks' Y a sine of that period */
            int per = atoi(getenv("ALIVE_PAD")); if (per < 2) per = 60;
            short y = (short)(32000 * __builtin_sinf(6.2831853f * (float)racing / (float)per));
            pad[0].right_trigger = 255; pad[0].ly = y; pad[0].ry = y; mask = 1; keys[0x11] = 0;
        }
        if (rx_session_set_input(s, keys, mask ? pad : NULL, mask) || rx_session_advance_dt(s, getenv("ALIVE_DT") ? (float)atof(getenv("ALIVE_DT")) : 1.f / 60)) { fprintf(stderr, "advance: %s\n", rx_session_last_error(s)); return 1; }
        if (rx_session_get_state(s) != RX_SESSION_RACING) continue;
        if (!racing) printf("class %d\n", (int)rx_session_local_phys_class(s));
        racing++;
        rx_vehicle_info v; if (rx_session_vehicle_info(s, -1, &v)) continue;
        if (getenv("ALIVE_TRACE")) {   /* every frame: the wheels' bodies in the frame body's space (y up, z forward) */
            rx_bike_state b; if (!rx_session_bike(s, &b)) {
                const float* q = b.orientation[0];
                printf("T %d", racing);
                for (int w = 2; w < 4; w++) {
                    const int rnd = getenv("ALIVE_RENDER") != NULL;   /* the render bodies (what the client draws by) */
                    const float (*bp)[4] = rnd ? b.render_position : b.position;
                    if (rnd) q = b.render_orientation[0];
                    float d[3] = {bp[w][0] - bp[0][0], bp[w][1] - bp[0][1], bp[w][2] - bp[0][2]};
                    /* rotate d by the inverse of q (x, y, z, w) */
                    float x = -q[0], y = -q[1], z = -q[2], qw = q[3];
                    float tx = 2 * (y * d[2] - z * d[1]), ty = 2 * (z * d[0] - x * d[2]), tz = 2 * (x * d[1] - y * d[0]);
                    float r0 = d[0] + qw * tx + (y * tz - z * ty), r1 = d[1] + qw * ty + (z * tx - x * tz), r2 = d[2] + qw * tz + (x * ty - y * tx);
                    printf(" %s %.3f %.3f", w == 2 ? "front" : "rear", r1, r2);
                    (void)r0;
                }
                printf(" speed %.1f\n", v.speed);
                if (getenv("ALIVE_TRACE")[0] == '3') {   /* ALIVE_TRACE=3: the bike's 18 node worlds, then the rider's 69 bones (16 floats each) */
                    float pose[64 * 16], rp[80 * 16]; int n = rx_session_bike_pose(s, pose, 64), nr = rx_session_rider_pose(s, rp, 80);
                    printf("Q %d %d %d", racing, n, nr);
                    for (int k = 0; k < n; k++) for (int e = 0; e < 16; e++) printf(" %.4f", pose[16 * k + e]);
                    for (int k = 0; k < nr; k++) for (int e = 0; e < 16; e++) printf(" %.4f", rp[16 * k + e]);
                    printf(" %.4f %.4f %.4f %.4f %.4f %.4f", b.render_position[3][0], b.render_position[3][1], b.render_position[3][2], b.position[3][0], b.position[3][1], b.position[3][2]);
                    printf("\n");
                }
                if (getenv("ALIVE_TRACE")[0] == '2') {   /* ALIVE_TRACE=2: the posed frame, upper / lower forks and front tyre (16 floats each) */
                    float pose[64 * 16]; int n = rx_session_bike_pose(s, pose, 64);
                    printf("P %d", racing);
                    for (int k = 0; k < 5 && k < n; k++) { if (k == 2) continue; for (int e = 0; e < 16; e++) printf(" %.5f", pose[16 * k + e]); }
                    for (int w = 2; w < 3; w++) printf(" %.4f %.4f %.4f", b.position[w][0], b.position[w][1], b.position[w][2]);
                    printf("\n");
                }
            }
        }
        if (v.speed > peak) peak = v.speed;
        if (racing % 30 == 1) {
            float pose[64 * 16]; int n = rx_session_bike_pose(s, pose, 64);
            rx_bike_state b; if (rx_session_bike(s, &b)) continue;
            printf("F %d speed %.2f rpm %.0f gear %d", racing, v.speed, v.rpm, v.gear);
            static const char* bn[4] = {"frame", "rider", "front", "rear"};
            for (int k = 0; k < 4; k++) printf(" %s (%.3f %.3f %.3f)", bn[k], b.position[k][0], b.position[k][1], b.position[k][2]);
            for (int k = 0; k < n && k < 18; k++) {
                if (k != 0 && k != 1 && k != 3 && k != 4 && k != 11 && k != 12) continue;
                const float* m = pose + 16 * k;   /* row-major D3D world: the translation in elements 12..14 */
                printf(" %s (%.3f %.3f %.3f)", names[k], m[12], m[13], m[14]);
            }
            printf("\n");
        }
    }
    printf("MAX speed %.2f racing frames %d\n", peak, racing);
    rx_session_destroy(s);
    return 0;
}
