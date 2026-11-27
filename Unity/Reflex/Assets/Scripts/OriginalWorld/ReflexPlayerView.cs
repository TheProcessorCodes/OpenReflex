// SPLIT SCREEN (port addition, w-split): one local rider's own view of the world. The camera's pose for a presented frame was
// OriginalWorldRuntime.MoveCamera's (Dynamic FPV: OriginalOnboardCamera; the game's camera, a custom preset's rig over it: ReflexCameraRig;
// the Unity follow camera); it is here so both riders' cameras run the same code, each on its own state: its own rig, its own onboard
// camera (the second rider's is bound to its rider model among OriginalOpponents') and its own follow camera. The first rider's selection
// stays the runtime's (cameraMode, DrivingCameraPreset, ReflexCameraPresets' current entry: saved, and read by many); the second rider's is
// this object's (cameraMode, drivingCamera, custom: never saved, started on the first rider's entry, cycled by the second pad's Camera
// input through the same ON / OFF list). The option values (CHASE CAMERA FOV, the ReflexFpv.* keys, the camera list) are shared.
// One view shown although two riders are in the world (the results, a replay, the viewer): the second view rests (Rest: it lets its
// onboard camera go) and picks its selection up again when the screen is cut in two.
using Reflex.Port.Content;
using Reflex.Port.Options;
using Reflex.Port.RxCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reflex.Port.OriginalWorld
{
    public sealed class ReflexPlayerView
    {
        /// One presented frame of one rider, as the runtime has it (the first rider: its own fields; the second: Results.view2).
        public struct Frame
        {
            /// 0 the game's camera, 1 Dynamic FPV, 2 the follow camera; the core's DrivingCamera preset (-1: not known, the saved one stands in).
            public int cameraMode, drivingPreset;
            /// The custom preset over the game's camera (null: the game's view as it is).
            public CameraPreset preset;
            public ReflexCamera gameCamera; public bool haveGameCamera;
            public ReflexBikeState state; public RxOnboardState onboardState;
            public ReflexVehicleInfo vehicle; public bool hasVehicle;
            /// The rider model is drawn; a custom preset shows (OriginalWorldRuntime.CustomCameraView); the options' camera editor previews.
            public bool riderShown, customView, paused, previewing;
            public OriginalTerrain terrain;
            public double renderClock;
            /// ownPad: the look-back input is this pad's alone (the second rider: no keyboard); else the first rider's keyboard and pad.
            public Gamepad pad; public bool ownPad;
        }

        public Camera cam;
        public OriginalOnboardCamera onboard;
        public readonly ReflexCameraRig rig;
        /// The first rider's view teaches WATCH GHOST's chase camera (OriginalWorldRuntime.LearnChase); null: never.
        public System.Action<Vector3, Vector3, float> learnChase;
        /// What placed the camera last: 0 nothing / the game's camera as it is, 1 Dynamic FPV, 2 a custom preset's rig, 3 the follow camera.
        public int Pose { get; private set; }
        Vector3 cameraTarget, cameraForward = Vector3.forward, cameraVelocity;

        public ReflexPlayerView(ReflexCameraRig rig) { this.rig = rig; }

        static Vector3 P(float[] a, int i) => new Vector3(a[i * 4], a[i * 4 + 1], a[i * 4 + 2]);
        static Quaternion Q(float[] a, int i) => new Quaternion(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]);

        /// The camera for this frame (OriginalWorldRuntime.MoveCamera, after its own overrides: the viewer, a replay, the hovercraft).
        public void Move(in Frame f, bool snap)
        {
            var state = f.state; var onboardState = f.onboardState; var vehicleInfo = f.vehicle; bool hasVehicleInfo = f.hasVehicle;
            Pose = 0;
            if (f.cameraMode == 1 && onboard)
            {
                var kb = Keyboard.current;
                Vector3 front = P(state.renderPosition, 2), rear = P(state.renderPosition, 3);
                if (hasVehicleInfo && vehicleInfo.FourWheels) { front = vehicleInfo.Front; rear = vehicleInfo.Rear; }   // an ATV / a machine: its axles (ATV bodies 2..5, machine bodies 1..4 are its wheels)
                var velocity = new Vector3(state.velocity[0], state.velocity[1], state.velocity[2]);
                bool validOnboardState = onboardState.valid != 0;
                onboard.SetContext(hasVehicleInfo && vehicleInfo.Atv, hasVehicleInfo && vehicleInfo.Machine,
                    f.riderShown && (!validOnboardState || onboardState.rider_active != 0),
                    validOnboardState && onboardState.wrecked != 0, validOnboardState && onboardState.trick_active != 0,
                    OriginalInput.RightStickRides, validOnboardState && onboardState.front_grounded != 0,
                    validOnboardState && onboardState.rear_grounded != 0, validOnboardState && onboardState.contacts_valid != 0,
                    f.renderClock);
                bool lookBack = f.ownPad ? OriginalInput.PadLookBackHeld(f.pad) : kb != null && kb.leftAltKey.isPressed || OriginalInput.LookBackHeld;
                // the game's rider-lean inputs: [7] fore / aft (+ forward), [6] side (+ right)
                if (onboard.Apply(P(state.renderPosition, 0), Q(state.renderOrientation, 0), front, rear, velocity, state.speed,
                                  state.inputs[7], state.inputs[6], f.terrain ? f.terrain.UnityTerrain : null, lookBack)) { Pose = 1; return; }
                // off the bike (a wreck): the game's camera, as MX Bikes goes external when crashed
            }
            if ((f.cameraMode == 0 || f.cameraMode == 1) && f.haveGameCamera && ApplyGameCamera(f)) return;
            Vector3 pos = P(state.renderPosition, 0);
            Vector3 forward = Q(state.renderOrientation, 0) * Vector3.forward; forward.y = 0;
            if (forward.sqrMagnitude < 0.001f) forward = cameraForward; else forward.Normalize();
            Vector3 target = pos + Vector3.up * 1.1f;
            // The game teleports the bike back to the track after a wreck: cut instead of swooping.
            if ((target - cameraTarget).sqrMagnitude > 30f * 30f) snap = true;
            cameraTarget = target;
            // Heading follows the bike smoothly (a tumbling bike would otherwise spin the camera).
            float dt = Time.unscaledDeltaTime;
            cameraForward = snap ? forward : Vector3.Slerp(cameraForward, forward, 1f - Mathf.Exp(-4f * dt)).normalized;
            Vector3 desired = target - cameraForward * 16f + Vector3.up * 7f + Vector3.Cross(Vector3.up, cameraForward) * 3f;
            if (snap) cameraVelocity = Vector3.zero;
            cam.transform.position = snap ? desired : Vector3.SmoothDamp(cam.transform.position, desired, ref cameraVelocity, 0.13f, 400, dt);
            cam.transform.LookAt(target + cameraForward);
            Pose = 3;
        }

        /// SPLIT SCREEN: the screen was cut in two (or put back) after this frame's Move: the field of view again for the camera's new aspect
        /// (Dynamic FPV and a custom preset set in degrees: OriginalOnboardCamera.ViewFov; the others do not depend on it).
        public void Refov()
        {
            if (!cam) return;
            if (Pose == 1 && onboard) cam.fieldOfView = OriginalOnboardCamera.ViewFov(onboard.fov, cam.aspect);
            else if (Pose == 2 && rig.Refov(cam.aspect, out float fov)) cam.fieldOfView = fov;
        }

        /// The game's camera as it is, with no rider state to go with it (the second view before its first bike frame).
        public void Raw(in ReflexCamera c)
        {
            Pose = 0;
            var forward = new Vector3(c.fx, c.fy, c.fz); var up = new Vector3(c.ux, c.uy, c.uz); var pos = new Vector3(c.px, c.py, c.pz);
            if (forward.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f || !(c.fov > 0.05f && c.fov < 3f) || float.IsNaN(pos.x)) return;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, up));
            cam.fieldOfView = ReflexClientLive.ChaseFov(c.fov) * Mathf.Rad2Deg;
            if (c.nearPlane > 0 && c.farPlane > c.nearPlane) { cam.nearClipPlane = Mathf.Min(c.nearPlane, OriginalWorldRuntime.ThirdPersonNear); cam.farClipPlane = c.farPlane; }
        }

        /// Another camera than the rider's own view has the screen: the Dynamic FPV camera lets go (the whole rider is drawn again).
        public void LeaveOnboard() { if (onboard && onboard.Active) onboard.Deactivate(); }

        // The original render camera: rows right/up/forward in native axes (= Unity axes), vertical FOV in radians. A custom camera preset
        // (OPTIONS > CAMERA & HUD; ReflexCameraPresets) moves it over the driving camera (ReflexCameraRig).
        bool ApplyGameCamera(in Frame f)
        {
            var c = f.gameCamera;
            var forward = new Vector3(c.fx, c.fy, c.fz); var up = new Vector3(c.ux, c.uy, c.uz); var pos = new Vector3(c.px, c.py, c.pz);
            if (forward.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f || !(c.fov > 0.05f && c.fov < 3f) || float.IsNaN(pos.x)) return false;
            if (f.cameraMode == 0 && learnChase != null) learnChase(pos, forward, c.fov);
            var preset = f.preset;
            if (preset != null && rig.Apply(preset, RigFrame(f, pos, forward, up, c.fov), out var rigPos, out var rigRot, out float rigFov))
            {
                cam.transform.SetPositionAndRotation(rigPos, rigRot);
                cam.fieldOfView = rigFov;
                Pose = 2;
            }
            else
            {
                if (preset == null) rig.Reset();
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, up));
                cam.fieldOfView = ReflexClientLive.ChaseFov(c.fov) * Mathf.Rad2Deg;   // CHASE CAMERA FOV (Options; 100 %: the game's)
            }
            if (c.nearPlane > 0 && c.farPlane > c.nearPlane) { cam.nearClipPlane = DrivingNear(f, c.nearPlane); cam.farClipPlane = c.farPlane; }
            return true;
        }

        /// OriginalWorldRuntime.ThirdPersonNear's rule: the game's first-person camera (preset 3) keeps its own near plane (0.5: the rider's
        /// head, which nothing else hides, stays out of the view), every other view clips at 0.25 ft.
        static float DrivingNear(in Frame f, float gameNear) => f.cameraMode == 0 && f.drivingPreset == 3 ? gameNear : Mathf.Min(gameNear, OriginalWorldRuntime.ThirdPersonNear);

        ReflexCameraRig.Frame RigFrame(in Frame f, Vector3 pos, Vector3 forward, Vector3 up, float fov)
        {
            var state = f.state; var onboardState = f.onboardState;
            bool known = onboardState.valid != 0 && onboardState.contacts_valid != 0;
            float real = Time.unscaledDeltaTime;
            return new ReflexCameraRig.Frame
            {
                gamePos = pos, gameForward = forward, gameUp = up, gameFov = fov, aspect = cam.aspect,
                bike = P(state.renderPosition, 0), bikeRot = Q(state.renderOrientation, 0),
                velocity = state.velocity != null ? new Vector3(state.velocity[0], state.velocity[1], state.velocity[2]) : Vector3.zero, speed = state.speed,
                riding = f.customView, contactsKnown = known,
                holdKey = ReflexCameraRig.HoldKey(f.hasVehicle ? f.vehicle.type : 0, f.drivingPreset >= 0 ? f.drivingPreset : ReflexSettings.DrivingCamera), grounded = known && (onboardState.front_grounded != 0 || onboardState.rear_grounded != 0),
                wrecked = onboardState.valid != 0 && onboardState.wrecked != 0, terrain = f.terrain,
                dt = f.paused ? 0f : real, preview = f.previewing ? real : 0f,
            };
        }

        // ------------------------------------------------------------------------------------------ the second rider's selection
        // (the first rider's is the runtime's and ReflexCameraPresets'; nothing below is saved or touches the first rider's settings)
        /// 0 the game's camera, 1 Dynamic FPV, 2 the follow camera.
        public int cameraMode;
        /// The DrivingCamera preset asked of the core for this rider (0 VISUAL, 1 GAME PLAY, 2 USER, 3 1ST PERSON; -1: none yet).
        public int drivingCamera = -1;
        /// The selected custom preset (null: a built-in entry); shown while its BASE is the driving preset.
        public CameraPreset custom;
        bool started, issue, resting = true; float issueAfter;

        /// The selection was made for this world (Begin).
        public bool Started => started;
        /// A new world: the selection is made again from the first rider's (Begin), the rig starts again.
        public void NewWorld() { started = false; resting = true; rig.Reset(); Pose = 0; }
        /// The first rider's entry as this rider's start; the core takes the preset once this rider's race runs (Reconcile).
        public void Begin(int mode, int preset, CameraPreset customPreset)
        {
            cameraMode = Mathf.Clamp(mode, 0, 2); drivingCamera = Mathf.Clamp(preset, 0, 3); custom = customPreset;
            started = true; issue = true; issueAfter = 0f;
            if (onboard) onboard.NewSession();
        }
        int Dc => drivingCamera >= 0 ? drivingCamera : ReflexSettings.DrivingCamera;
        /// The custom preset the game camera shows for this rider (ReflexCameraPresets.ActiveFor's rule, without the editor's preview).
        public CameraPreset ActivePreset(int dc) => cameraMode == 0 && custom != null && custom.baseView == dc ? custom : null;
        /// ReflexCameraPresets.ViewKey / DisplayName for this rider's selection (its HUD's camera toast).
        public int ViewKey { get { int dc = Mathf.Clamp(Dc, 0, 3); return cameraMode * 100000 + (cameraMode != 1 && custom != null && custom.baseView == dc ? 1000 + custom.id : cameraMode == 1 ? 4 : dc); } }
        public string DisplayName => (ReflexCameraPresets.Find(ReflexCameraPresets.CurrentIdOf(cameraMode, Mathf.Clamp(Dc, 0, 3), custom))?.name ?? "CAMERA") + (cameraMode == 2 ? "  (FOLLOW)" : "");

        /// OriginalWorldRuntime.SetCameraMode without the save.
        public void SetCameraMode(int mode)
        {
            cameraMode = Mathf.Clamp(mode, 0, 2);
            if (cameraMode != 1 && onboard) onboard.Deactivate();
            if (cameraMode == 1 && onboard) onboard.Resnap();
            if (cameraMode == 2 && cam) { cam.nearClipPlane = 0.15f; cam.farClipPlane = 14000; cam.fieldOfView = 47; resting = true; }   // (the next Move snaps)
        }

        /// The Camera input of this rider's pad: the next ON entry of the camera list (ReflexCameraPresets.Cycle's, on this rider's state).
        public void Cycle(int direction, OpenReflexSimulation open)
        {
            var e = ReflexCameraPresets.Find(ReflexCameraPresets.NextId(ReflexCameraPresets.CurrentIdOf(cameraMode, Mathf.Clamp(Dc, 0, 3), custom), direction));
            if (e == null) return;
            custom = e.preset;
            int index = e.Custom ? e.preset.baseView : e.builtin;
            if (index == 4) { SetCameraMode(1); return; }
            SetCameraMode(0);
            drivingCamera = Mathf.Clamp(index, 0, 3);
            Issue(open);
        }

        void Issue(OpenReflexSimulation open)
        {
            if (open == null) { issue = true; return; }
            open.SetDrivingCamera(drivingCamera, 1); issue = false; issueAfter = Time.unscaledTime + 0.5f;
        }

        /// The core's preset follows the selection: rx_player_set_camera refuses before the rider's activity runs (the gate, the intro), so
        /// the preset is issued once it does (driving) and again, at most twice a second, while the core's own (readback, View2.drivingCamera;
        /// -1: not reported) is another.
        public void Reconcile(OpenReflexSimulation open, int readback, bool driving)
        {
            if (!started || !driving || open == null || drivingCamera < 0) return;
            if (issue || readback >= 0 && readback != drivingCamera && Time.unscaledTime >= issueAfter) Issue(open);
        }

        /// One view is shown: this one does nothing until the screen is cut in two again.
        public void Rest() { LeaveOnboard(); resting = true; }
        /// True once after a rest (the first Move cuts: the follow camera snaps, the rig starts again, the onboard camera reads its settings).
        public bool Resume()
        {
            if (!resting) return false;
            resting = false; rig.Reset();
            if (onboard) onboard.Load();
            return true;
        }
    }
}
