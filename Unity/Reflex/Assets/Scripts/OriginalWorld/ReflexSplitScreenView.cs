// SPLIT SCREEN (port addition, w-split; the original's console split screen, RxCore/RxSplitScreen.cs): the second local rider's view.
// The screen is cut in two across: the first rider keeps the world camera (chaseCamera, the top half), the second rider gets a camera of its
// own (the bottom half) placed from the core's second player camera (rx_session_set_view_player 1 -> Results.camera2) and drawn through its
// own ReflexRenderPipeline (the original's post chain, exposure and TAA per view). The first rider's HUD keeps to the top half (HudSafeRoot);
// the second rider has the same HUD in the bottom half (ReflexModernHud's second model + view, from OriginalWorldRuntime.SplitHud; the line
// between the halves is this component's: RxSplitScreen.MidY). The second rider's bike is drawn as the other riders
// are (the core reports it as a rider that is not the viewed local one: OriginalOpponents).
// The level's view-dependent parts are drawn for both cameras (ReflexWorldViews: terrain detail, tessellation, forest, grass and water per
// camera; LODs, culling and pose rates by the nearer of the two), and the second pipeline mirrors the first's settings.
// The world's sounds are heard by both riders: this view's camera pose is the audio's second listener (TryListener -> OriginalAudio.Hear).
// The second rider has every camera the first has (ReflexPlayerView: the game's four views, Dynamic FPV on an OriginalOnboardCamera of
// this camera's own, the custom presets), cycled by the second pad's Camera input: OriginalWorldRuntime.MoveSecondView places this
// camera each frame, after Apply cut the screen.
// Limits (core/plan/QUEUE.md w-split): the water's wake and the snow trails are simulated around the first rider only.
using Reflex.Port.RxCore;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    public sealed class ReflexSplitScreenView : MonoBehaviour
    {
        Camera main, second; Rendering.ReflexRenderPipeline mainPipeline; OriginalOnboardCamera onboard;
        ReflexCamera cam2; bool haveCam2;
        bool hidden, keepMainRect, cut;   // cut: this component took the first rider's camera off the whole screen (its to put back)

        /// The world camera of the first rider (OriginalWorldRuntime.chaseCamera).
        public void Bind(Camera chase) { if (main == chase) return; RestoreMain(); main = chase; mainPipeline = main ? main.GetComponent<Rendering.ReflexRenderPipeline>() : null; }

        /// The second rider's camera (null until two views were first shown) and its Dynamic FPV camera: placed by the runtime's second view.
        public Camera Second => second;
        public OriginalOnboardCamera Onboard => onboard;

        /// The presented frame's second rider (OriginalWorldRuntime.ApplyView).
        public void Present(OriginalSimulation.Results v)
        {
            if (v.hasCamera2) { cam2 = v.camera2; haveCam2 = true; }
        }

        /// A full-screen view has the screen (a replay, the results, the pause page's map ...): one view until it lets go.
        public bool Hidden { get => hidden; set => hidden = value; }
        /// The replay renderer holds the first rider's camera rect (OwnReplayRenderer.OwnsViewport: it throws when the rect is replaced):
        /// not written here until it lets go.
        public bool KeepMainRect { get => keepMainRect; set => keepMainRect = value; }

        Rect Base => mainPipeline && mainPipeline.viewport.width > 0 && mainPipeline.viewport.height > 0 && Content.ReflexLaunchSettings.Applied ? mainPipeline.viewport : new Rect(0, 0, 1, 1);

        /// Once per Unity frame, from OriginalWorldRuntime.LateUpdate after the camera move (UpdateSplitViews: no LateUpdate of its own, the
        /// order of two LateUpdates on one object is not defined).
        public void Apply()
        {
            if (!RxSplitScreen.Active || !main) { Off(); return; }   // no two-rider world: the second view goes
            if (hidden) { Collapse(); return; }
            if (!second) Make();
            second.gameObject.SetActive(haveCam2);
            ReflexWorldViews.Set(second, haveCam2);   // (the view-dependent world parts draw for this camera too while it renders)
            var b = Base;
            main.rect = new Rect(b.x, b.y + b.height * 0.5f, b.width, b.height * 0.5f); cut = true;
            second.rect = new Rect(b.x, b.y, b.width, b.height * 0.5f);
            RxSplitScreen.MidY = b.y + b.height * 0.5f;   // (the HUDs' halves meet where the cameras do: HudSafeRoot)
        }

        /// The second rider's ears for this frame's audio (OriginalAudio.SetSecondListener): the presented frame's camera, not the Camera's
        /// transform (placed in LateUpdate, after the audio's Tick). False while one view is shown or the camera is not a valid pose.
        public bool TryListener(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (!RxSplitScreen.TwoViews || hidden || !haveCam2 || !main) return false;
            var c = cam2;
            var forward = new Vector3(c.fx, c.fy, c.fz); var up = new Vector3(c.ux, c.uy, c.uz); pos = new Vector3(c.px, c.py, c.pz);
            if (forward.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f || float.IsNaN(pos.x + pos.y + pos.z) || float.IsInfinity(pos.x + pos.y + pos.z)) return false;
            rot = Quaternion.LookRotation(forward, up);
            return true;
        }

        void Make()
        {
            var go = new GameObject("Split screen: player 2") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(main.transform.parent, false);
            go.SetActive(false);
            second = go.AddComponent<Camera>();
            second.CopyFrom(main);
            second.depth = main.depth + 1;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(second);
            var mainData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main);
            if (data && mainData) { data.renderPostProcessing = mainData.renderPostProcessing; data.renderShadows = mainData.renderShadows; data.antialiasing = mainData.antialiasing; }
            // the original's post chain for this view too, with the first view's settings (the level's HDR settings, LIGHTING ENHANCED's chain,
            // the frame source: ReflexRenderPipeline.mirror; set while the object is off, before the pipeline's OnEnable)
            if (mainPipeline) go.AddComponent<Rendering.ReflexRenderPipeline>().mirror = mainPipeline;
            // the second rider's Dynamic FPV camera (the first's sits on the world camera): its own springs, the second pad's free look, no mouse
            onboard = go.AddComponent<OriginalOnboardCamera>(); onboard.ownPad = true;
        }

        // One view although two riders are in the world: the second camera is kept, switched off (its per-view state survives a
        // pause-viewer toggle), and the first rider's camera has the whole screen (the second rider's HUD goes with RxSplitScreen.TwoViews).
        void Collapse()
        {
            if (second && second.gameObject.activeSelf) second.gameObject.SetActive(false);
            ReflexWorldViews.Set(second, false);
            RestoreMain();
        }

        // the first rider's camera back to the whole screen (the launch viewport), whenever this component cut it: also when the second
        // camera is already gone, and after the replay renderer gave back a rect it had captured cut (OwnReplayRenderer.Dispose's priorRect)
        void RestoreMain()
        {
            if (keepMainRect) { cut = true; return; }   // (the replay's full-screen rect stands; put back here once it lets go)
            if (cut && main) main.rect = Base;
            cut = false;
        }

        void Off()
        {
            ReflexWorldViews.Clear(second);   // (before the camera goes: the per-camera state kept for it is released)
            if (second) Destroy(second.gameObject);
            second = null; onboard = null; haveCam2 = false;
            RestoreMain();
        }

        void OnDestroy() { Off(); }
    }
}
