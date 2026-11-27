// SPLIT SCREEN (port addition, w-split): the cameras the world is drawn for. One in every world but a two-rider one: the game camera each
// view-dependent part already knows (OriginalWorldRuntime.chaseCamera, "primary" below). ReflexSplitScreenView registers the second rider's
// camera here while it renders, and the parts that follow the camera ask this instead of comparing with their one camera:
//   per camera (state set in beginCameraRendering, each view its own): RT_Bump, the terrain clipmap, the forest and grass, the water's
//     grid, the weather passes, Alive's terrain rings                                          -> Is(primary, cam), Index(cam)
//   once per frame for both (renderer state on the CPU: LOD meshes, culling, pose rates): the scenery LODs, the other riders, the
//     ambient characters, the podium                                                           -> the nearer of the two viewers: SqrDistance
// Without a second view Is(primary, c) == (c == primary), Index is 0 and SqrDistance is the primary's: a one-rider world is as before.
// Main thread only.
using System;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    public static class ReflexWorldViews
    {
        static Camera second; static bool rendering;

        /// The second rider's camera while it renders this frame (null: one view, also while two riders share one: a replay, the results).
        public static Camera Second => rendering && second ? second : null;
        /// Two views are drawn.
        public static bool Two => rendering && second;
        /// The second camera is going away (destroyed, not merely switched off): per-camera state kept for it is released.
        public static event Action<Camera> SecondGone;

        /// ReflexSplitScreenView, once per frame: its camera and whether it renders (false while collapsed to one view).
        public static void Set(Camera camera, bool renders) { second = camera; rendering = renders && camera; }
        /// ReflexSplitScreenView: the camera is destroyed (or the view is torn down).
        public static void Clear(Camera camera)
        {
            if (!ReferenceEquals(second, camera) && second) return;   // (another view's: not ours to clear)
            var gone = second; second = null; rendering = false;
            if (!ReferenceEquals(gone, null)) SecondGone?.Invoke(gone);
        }

        /// c is a camera the world is drawn for: the primary, or the second view while it renders.
        public static bool Is(Camera primary, Camera c) => c && (c == primary || (rendering && c == second));
        /// 0 the primary (and any other camera), 1 the second view.
        public static int Index(Camera c) => rendering && c && c == second ? 1 : 0;
        /// The squared distance of p from the nearer viewer (the primary alone without a second view; 0 without any camera).
        public static float SqrDistance(Camera primary, Vector3 p)
        {
            bool two = rendering && second;
            if (!primary) return two ? (p - second.transform.position).sqrMagnitude : 0f;
            float d = (p - primary.transform.position).sqrMagnitude;
            if (!two) return d;
            float d2 = (p - second.transform.position).sqrMagnitude;
            return d2 < d ? d2 : d;
        }
        /// The second viewer's position (false: one view).
        public static bool SecondEye(out Vector3 eye)
        {
            if (rendering && second) { eye = second.transform.position; return true; }
            eye = default; return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { second = null; rendering = false; SecondGone = null; }
    }
}
