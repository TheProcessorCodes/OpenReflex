// Part of the MODERN HUD kit (ReflexHudKit.cs).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Reflex.Port.Hud
{
    /// Keeps the HUD's root inside the safe area and within MaxAspect (centred on very wide screens), pulled in a further title-safe
    /// inset (ReflexHudKit.TitleSafe of the height) so that nothing sits on the very edge of a TV or a rounded display.
    public sealed class HudSafeRoot : MonoBehaviour
    {
        RectTransform _root; Vector2Int _laid = new Vector2Int(-1, -1); bool _split; int _half; float _mid = 0.5f;
        /// half (SPLIT SCREEN, w-split): 0 the first rider's view (the top half while the screen is cut; the whole screen otherwise), 1 the second rider's (the bottom half).
        public void Init(RectTransform root, int half = 0) { _root = root; _half = half; Layout(); }
        void LateUpdate() { if (_root && (_laid.x != OpenFrontEnd.OuLayout.ScreenWidth || _laid.y != OpenFrontEnd.OuLayout.ScreenHeight || _split != RxCore.RxSplitScreen.TwoViews || _split && _mid != RxCore.RxSplitScreen.MidY)) Layout(); }
        void Layout()
        {
            int w = OpenFrontEnd.OuLayout.ScreenWidth, h = OpenFrontEnd.OuLayout.ScreenHeight; if (w <= 0 || h <= 0 || !_root) return;   // (the real screen except in offscreen test renders)
            bool was = _split;
            _laid = new Vector2Int(w, h); _split = RxCore.RxSplitScreen.TwoViews; _mid = RxCore.RxSplitScreen.MidY;   // (one view again in Post / the viewer: the whole screen)
            Rect safe = OpenFrontEnd.OuLayout.SafeArea; if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, w, h);
            // SPLIT SCREEN (w-split): the first rider's HUD keeps to the top view, the second rider's to the bottom one; the line between
            // them is the cameras' (ReflexSplitScreenView: the middle of the launch viewport), and the HUD SCALE's split factor
            // (ReflexHudKit.ApplyScale) reaches the canvases that only scale when made (the toasts, the banner) as the cut comes and goes
            if (_split) { float mid = h * _mid; if (_half == 1) { if (safe.yMax > mid) safe.yMax = mid; } else if (safe.yMin < mid) { safe.height = safe.yMax - mid; safe.y = mid; } }
            if (_split != was) { var scaler = GetComponent<CanvasScaler>(); if (scaler) ReflexHudKit.ApplyScale(scaler); }
            float maxW = safe.height * ReflexHudKit.MaxAspect;
            if (safe.width > maxW) { safe.x += (safe.width - maxW) * 0.5f; safe.width = maxW; }
            float inset = safe.height * ReflexHudKit.TitleSafe;
            safe.xMin += inset; safe.xMax -= inset; safe.yMin += inset; safe.yMax -= inset;
            _root.anchorMin = new Vector2(safe.xMin / w, safe.yMin / h); _root.anchorMax = new Vector2(safe.xMax / w, safe.yMax / h);
            _root.offsetMin = _root.offsetMax = Vector2.zero;
        }
    }
}
