// The FINISH banner (w-finishflag): when the local rider takes the checkered flag, a waving checkered flag and "FINISHED  3RD" play for a
// moment on the HUD, in the MODERN HUD kit's look (ReflexHudKit: the translucent band, Barlow), with either HUD STYLE. The original game has
// no finish element on its HUD (only MXUIFlagContainer's white FINAL LAP flag; its checkered flags are the track's flag holders in the
// world, core/game/world_flag_holders.c), so this is the client's own, like the camera toast.
//   when     once per race, at the local rider's own finish: the race state's lap count reached (reflex_world_race), or the local driver's
//            finished flag (a timed championship stage flags a lapped rider with fewer laps), after the race was seen running unfinished in
//            this world (so not when a world opens on a finished race, not in a replay, not in Free Ride / PRACTICE / Freestyle: no laps
//            to finish). A restart (the race back at Pre / Countdown) arms it again.
//   what     the flag (HudFinishFlag: a checker texture made in code on a mesh that waves), FINISHED, the place in gold / silver / bronze
//            (none when the rider raced alone), the race time, a moving checker strip; 3.4 s in all (in 0.45, out 0.45), top centre below
//            the HUD's top row, inside the HUD's safe root (at most 2.1 : 1 on an ultrawide screen), sized by HUD SCALE.
//   not      over the menus, the pause page, the results, the intro card, the replay viewer / PHOTO MODE, or with the HUD hidden (H, HUD
//            VISIBILITY off): any of them ends it. It takes no input and blocks none. REDUCE MOTION: no slide, no wave.
using Reflex.Port.Options;
using Reflex.Port.OriginalWorld;
using UnityEngine;
using UnityEngine.UI;

namespace Reflex.Port.Hud
{
    /// The checkered cloth: a texture made in code (8 x 5 squares, a soft edge) on a grid that waves from the pole (the left edge) outward.
    public sealed class HudFinishFlag : MaskableGraphic
    {
        public const int CellsX = 8, CellsY = 5;
        const int Cell = 32, Border = 2, Cols = 28, Rows = 8;
        /// The wave's clock (seconds) and its size (0 = flat: REDUCE MOTION).
        public float time, wave = 1f;
        static Texture2D s_tex;
        public override Texture mainTexture => Checker;
        static Texture2D Checker
        {
            get
            {
                if (s_tex) return s_tex;
                int w = CellsX * Cell + 2 * Border, h = CellsY * Cell + 2 * Border;
                s_tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "HudFinishFlag", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var px = new Color32[w * h];
                var white = new Color32(247, 247, 245, 255); var black = new Color32(18, 19, 22, 255);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int cx = Mathf.Clamp((x - Border) / Cell, 0, CellsX - 1), cy = Mathf.Clamp((y - Border) / Cell, 0, CellsY - 1);
                        var c = ((cx + cy) & 1) == 0 ? black : white;
                        if (x < Border || y < Border || x >= w - Border || y >= h - Border) c.a = 0;   // (the silhouette's soft edge)
                        px[y * w + x] = c;
                    }
                s_tex.SetPixels32(px); s_tex.Apply(false, true);
                return s_tex;
            }
        }

        public void Tick(float t, float waveSize) { time = t; wave = waveSize; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0 || r.height <= 0) return;
            var vert = UIVertex.simpleVert;
            float amp = r.height * 0.085f * wave;
            for (int j = 0; j <= Rows; j++)
                for (int i = 0; i <= Cols; i++)
                {
                    float u = i / (float)Cols, v = j / (float)Rows;
                    float phase = u * 8.2f - time * 6.4f, grow = 0.18f + 0.82f * u;
                    float dy = (Mathf.Sin(phase) + 0.35f * Mathf.Sin(phase * 0.53f + v * 1.7f + 1.1f)) * amp * grow;
                    float dx = Mathf.Cos(phase) * amp * 0.30f * grow - amp * 0.6f * u;   // (the cloth is a little shorter than flat: it folds)
                    float shade = wave > 0f ? Mathf.Clamp01(0.80f + 0.26f * Mathf.Cos(phase + 0.5f) * grow) : 1f;
                    vert.position = new Vector3(r.xMin + u * r.width + dx, r.yMin + v * r.height + dy);
                    vert.uv0 = new Vector2(u, v);
                    var c = color; c.r *= shade; c.g *= shade; c.b *= shade;
                    vert.color = c;
                    vh.AddVert(vert);
                }
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    int a = j * (Cols + 1) + i, b = a + 1, d = a + Cols + 1, e = d + 1;
                    vh.AddTriangle(a, d, e); vh.AddTriangle(a, e, b);
                }
        }
    }

    /// Two rows of small light squares that move along a band and fade out toward its ends.
    public sealed class HudCheckerStrip : MaskableGraphic
    {
        public float offset, fade = 200f;
        public void Tick(float o) { offset = o; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float w = r.width, h = r.height;
            if (w <= 0 || h <= 0) return;
            float s = h * 0.5f, o = Mathf.Repeat(offset, 2f * s);
            var vert = UIVertex.simpleVert; vert.uv0 = new Vector2(0.5f, 0.5f);
            int n = Mathf.CeilToInt(w / s) + 3;
            for (int row = 0; row < 2; row++)
                for (int i = -2; i < n; i++)
                {
                    if (((i + row) & 1) != 0) continue;
                    float x0 = Mathf.Max(0f, i * s + o), x1 = Mathf.Min(w, (i + 1) * s + o);
                    if (x1 <= x0) continue;
                    float y0 = r.yMin + row * s, y1 = y0 + s;
                    int at = vh.currentVertCount;
                    vert.color = Faded(x0, w); vert.position = new Vector3(r.xMin + x0, y0); vh.AddVert(vert);
                    vert.position = new Vector3(r.xMin + x0, y1); vh.AddVert(vert);
                    vert.color = Faded(x1, w); vert.position = new Vector3(r.xMin + x1, y1); vh.AddVert(vert);
                    vert.position = new Vector3(r.xMin + x1, y0); vh.AddVert(vert);
                    vh.AddTriangle(at, at + 1, at + 2); vh.AddTriangle(at, at + 2, at + 3);
                }
        }
        Color32 Faded(float x, float w)
        {
            var c = color; c.a *= fade <= 0f ? 1f : Mathf.Clamp01(Mathf.Min(x, w - x) / fade);
            return c;
        }
    }

    /// The banner itself (its own canvas): Play, then Tick every frame. The HUD driver below and the showcase (synthetic finishes) use it.
    public sealed class ReflexFinishBannerView
    {
        public const float Duration = 3.4f, InTime = 0.45f, OutTime = 0.45f, CancelTime = 0.15f;
        const float BandW = 900f, BandH = 132f, FlagW = 168f, FlagH = 104f, Gap = 30f;

        readonly GameObject go; readonly Canvas canvas; readonly CanvasScaler scaler; readonly CanvasGroup group;
        readonly RectTransform band, content, flagRoot, textRoot; readonly HudFinishFlag flag; readonly HudCheckerStrip strip;
        readonly Text label, title, place; readonly HudRect divider, pole;
        float t = -1f, cancel = -1f, clock;

        /// Playing (on screen), how far in (seconds; -1 not playing), and what it says (tests).
        public bool Playing => t >= 0f;
        public float Time01 => t < 0f ? -1f : t / Duration;
        public string Title => title.text; public string Place => place.gameObject.activeSelf ? place.text : ""; public string Label => label.text;
        public Canvas Canvas => canvas;

        public ReflexFinishBannerView(Transform parent, int sortingOrder = 13, int half = 0)
        {
            go = new GameObject("Finish banner");
            if (parent) go.transform.SetParent(parent, false);
            var root = ReflexHudKit.CreateCanvas(go, sortingOrder, out canvas, half);   // (over the HUD 11 and its toast 12, under the pause page 50; half: SPLIT SCREEN, 1 the bottom view)
            scaler = go.GetComponent<CanvasScaler>();
            band = ReflexHudKit.Rect(root, "Band", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(BandW, BandH));
            group = band.gameObject.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var back = ReflexHudKit.Panel(band, "Back", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 190f, 190f, 0f);
            ReflexHudKit.Stretch(back.rectTransform);
            back.top = new Color(0.13f, 0.14f, 0.16f, 0.86f); back.bottom = new Color(0.05f, 0.055f, 0.065f, 0.90f);
            var line = ReflexHudKit.Panel(band, "Top line", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(BandW, 2f), 190f, 190f, 0f);
            line.panel = false; line.top = line.bottom = new Color(0.97f, 0.97f, 0.96f, 0.85f);
            strip = ReflexHudKit.Graphic<HudCheckerStrip>(ReflexHudKit.Rect(band, "Checker strip", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(BandW, 14f)));
            strip.raycastTarget = false; strip.color = new Color(0.97f, 0.97f, 0.96f, 0.92f); strip.fade = 190f;

            content = ReflexHudKit.Rect(band, "Content", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), new Vector2(BandW, BandH));
            // the flag on its pole, leaning back a little
            flagRoot = ReflexHudKit.Rect(content, "Flag", new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(FlagW, FlagH));
            flagRoot.localRotation = Quaternion.Euler(0f, 0f, 7f);
            pole = ReflexHudKit.Panel(flagRoot, "Pole", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-2f, -14f), new Vector2(6f, FlagH + 34f), 0f, 0f, 3f);
            pole.panel = false; pole.top = new Color(0.93f, 0.94f, 0.95f, 1f); pole.bottom = new Color(0.60f, 0.62f, 0.66f, 1f);
            flag = ReflexHudKit.Graphic<HudFinishFlag>(ReflexHudKit.Rect(flagRoot, "Cloth", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 4f), new Vector2(FlagW, FlagH)));
            flag.raycastTarget = false;

            textRoot = ReflexHudKit.Rect(content, "Text", new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(600f, BandH));
            label = ReflexHudKit.Label(textRoot, "Label", ReflexHudKit.Cond, ReflexHudKit.TypeBody, new Color(0.97f, 0.97f, 0.96f, 0.80f), TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 41f), new Vector2(600f, 30f));
            title = ReflexHudKit.Shadowed(ReflexHudKit.Label(textRoot, "Title", ReflexHudKit.NumHeavy, 80, ReflexHudKit.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -12f), new Vector2(420f, 84f)));
            divider = ReflexHudKit.Panel(textRoot, "Divider", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(3f, 54f), 0f, 0f, 0f);
            divider.panel = false; divider.top = divider.bottom = ReflexHudKit.Faint;
            place = ReflexHudKit.Shadowed(ReflexHudKit.Label(textRoot, "Place", ReflexHudKit.NumHeavy, 80, ReflexHudKit.Gold, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -12f), new Vector2(220f, 84f)));
            canvas.enabled = false; band.gameObject.SetActive(false);
        }

        public static Color PlaceColor(int place) => place == 1 ? ReflexHudKit.Gold : place == 2 ? ReflexHudKit.Silver : place == 3 ? ReflexHudKit.Bronze : ReflexHudKit.White;

        /// Starts the banner: place 0 / riders below 2 = no place shown; who = a watched rider's name (a spectator), null the local rider.
        public void Play(int placeNumber, int riders, float raceTime, string who = null)
        {
            ReflexHudKit.ApplyScale(scaler);
            bool hasPlace = placeNumber > 0 && riders > 1;
            ReflexHudKit.SetText(title, "FINISHED");
            ReflexHudKit.Show(place, hasPlace); ReflexHudKit.Show(divider, hasPlace);
            if (hasPlace) { ReflexHudKit.SetText(place, placeNumber + ReflexHudKit.Ordinal(placeNumber)); place.color = PlaceColor(placeNumber); }
            string head = string.IsNullOrEmpty(who) ? (hasPlace && placeNumber == 1 ? "RACE WINNER" : "CHECKERED FLAG") : who;
            bool timed = raceTime > 0f && raceTime < 3.0e38f;
            ReflexHudKit.SetText(label, head + (timed ? "   ·   " + ReflexHudKit.Clock(raceTime) : ""));
            // the flag and the words as one group, centred on the band
            float tw = title.preferredWidth, pw = hasPlace ? place.preferredWidth : 0f, x = tw;
            if (hasPlace)
            {
                divider.rectTransform.anchoredPosition = new Vector2(x + 26f, -12f);
                place.rectTransform.anchoredPosition = new Vector2(x + 52f, -12f);
                x += 52f + pw;
            }
            float textW = Mathf.Max(x, label.preferredWidth), total = FlagW + Gap + textW;
            flagRoot.anchoredPosition = new Vector2(-total * 0.5f, 0f);
            textRoot.anchoredPosition = new Vector2(-total * 0.5f + FlagW + Gap, 0f);
            t = 0f; cancel = -1f; clock = 0f;
            canvas.enabled = true; band.gameObject.SetActive(true);
            Apply(false);
        }

        /// One frame: dt seconds on; blocked (a menu, the results, the HUD hidden) ends it with a quick fade.
        public void Tick(float dt, bool blocked, bool reduceMotion)
        {
            if (t < 0f) return;
            t += dt; clock += dt;
            if (blocked && cancel < 0f) cancel = 0f;
            if (cancel >= 0f) cancel += dt;
            if (t >= Duration || cancel >= CancelTime) { Stop(); return; }
            Apply(reduceMotion);
        }

        /// A given moment of the animation (the showcase's stills).
        public void Seek(float seconds, bool reduceMotion = false) { if (t < 0f) return; t = Mathf.Clamp(seconds, 0f, Duration - 0.001f); clock = t; cancel = -1f; Apply(reduceMotion); }

        public void Stop() { t = -1f; cancel = -1f; if (canvas) canvas.enabled = false; if (band) band.gameObject.SetActive(false); }
        public void Destroy() { if (go) Object.Destroy(go); }

        void Apply(bool reduceMotion)
        {
            float enter = HudEase.OutCubic(t / InTime), exit = HudEase.InCubic((t - (Duration - OutTime)) / OutTime);
            float a = Mathf.Clamp01(t / (InTime * 0.6f)) * (1f - exit);
            if (cancel >= 0f) a *= 1f - Mathf.Clamp01(cancel / CancelTime);
            group.alpha = a;
            float move = reduceMotion ? 0f : 1f;
            band.localScale = new Vector3(1f + (enter - 1f) * 0.22f * move, 1f, 1f);
            band.anchoredPosition = new Vector2(0f, -250f + exit * 18f * move);
            content.localScale = new Vector3(1f / band.localScale.x, 1f, 1f);   // (the band opens sideways; what is on it keeps its shape)
            float slide = (1f - enter) * move;
            flagRoot.localRotation = Quaternion.Euler(0f, 0f, 7f + slide * 22f);
            flagRoot.localScale = Vector3.one * (1f - 0.25f * slide);
            title.rectTransform.anchoredPosition = new Vector2(slide * 46f, -12f);
            // the place lands a beat after the word
            if (place.gameObject.activeSelf)
            {
                float p = Mathf.Clamp01((t - 0.22f) / 0.38f), pop = reduceMotion ? 1f : HudEase.OutBack(p);
                place.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, pop);
                var pc = place.color; pc.a = Mathf.Clamp01(p * 2.5f); place.color = pc;
            }
            flag.Tick(clock, reduceMotion ? 0f : 1f);
            strip.Tick(reduceMotion ? 0f : clock * 46f);
        }
    }

    /// The HUD driver on the world runtime: watches the race for the local rider's finish and plays the banner once.
    [DisallowMultipleComponent, DefaultExecutionOrder(905)]
    public sealed class ReflexFinishBanner : MonoBehaviour
    {
        OriginalWorldRuntime rt; ReflexModernHud modern;
        ReflexFinishBannerView view;
        bool armed;
        ReflexFinishBannerView view2; bool armed2;   // SPLIT SCREEN (w-split): the second rider's banner, in the bottom view

        /// Banners played in this world, and the banner (tests).
        public int Played { get; private set; }
        public ReflexFinishBannerView View => view;
        public bool Showing => view != null && view.Playing;

        void Awake() { rt = GetComponent<OriginalWorldRuntime>(); }


        void Play(int place, int riders, float time, string who)
        {
            view ??= new ReflexFinishBannerView(transform);
            view.Play(place, riders, time, who);
            Played++;
            Debug.Log($"[finish banner] {(who ?? "the local rider")}: place {place} of {riders}, {time:0.000} s");
        }

        bool Blocked()
            => !rt || !rt.Running || ReflexOptions.IsOpen || rt.OwnMenuActive || rt.pauseMenu && rt.pauseMenu.IsOpen || OriginalPostRace.Active || rt.IntroCardShown
               || ReflexSettings.HudHidden || !ReflexSettings.HudVisible || Reflex.Port.Replay.ReflexReplay.ViewerOpen || rt.OriginalReplayShowing;

        void LateUpdate()
        {
            if (!rt) return;
            Detect();
            if (view != null && view.Playing) view.Tick(Time.unscaledDeltaTime, Blocked(), ReflexSettings.ReduceMotion);
            if (RxCore.RxSplitScreen.Active || armed2 || view2 != null && view2.Playing) Detect2();
        }

        // The local rider's finish, from the race state the HUDs read (the header's "when").
        void Detect()
        {
            var race = rt.Race;
            if (!rt.Running || race.valid == 0 || race.num_laps <= 0 || race.num_laps >= 0x61) { armed = false; return; }   // (0x61: an endless lap race, PRACTICE)
            if (race.state <= 1) { armed = false; return; }
            if (!modern) modern = GetComponent<ReflexModernHud>();
            var mode = modern ? modern.Model.mode : HudMode.Race;
            if (mode == HudMode.Tutorial) { armed = false; return; }
            bool finished = race.laps_completed >= race.num_laps; float time = race.race_time;
            // the driver's own flag: a rider flagged with fewer laps (a timed stage, a lapped rider); not in the knock-out formats, where a rider leaves the race otherwise
            bool flagCounts = mode == HudMode.Race || mode == HudMode.Waypoint;
            for (int i = 0; i < rt.DriverCount && i < rt.Drivers.Length; i++)
            {
                var d = rt.Drivers[i]; if (d.local == 0) continue;
                if (d.finished != 0 && d.race_time > 0f) { if (flagCounts) finished = true; if (finished) time = d.race_time; }
                break;
            }
            if (!finished) { if (race.state == 2) armed = true; return; }
            if (!armed) return;
            armed = false;
            if (Blocked()) return;
            Play(Mathf.Max(1, race.place), Mathf.Max(1, race.riders), time, null);
        }

        // SPLIT SCREEN (w-split): the second rider's finish, by the same rules from its own source (OriginalWorldRuntime.SplitHud: its race
        // state, the standings row of its slot), played in the bottom view; only while two views are shown (it goes at once when they
        // collapse: its safe root is the whole screen then). Not counted in Played (the first rider's).
        void Detect2()
        {
            if (view2 != null && view2.Playing)
            {
                if (!RxCore.RxSplitScreen.TwoViews) view2.Stop();
                else view2.Tick(Time.unscaledDeltaTime, Blocked(), ReflexSettings.ReduceMotion);
            }
            var src = RxCore.RxSplitScreen.Active ? rt.SplitHud : null;
            if (src == null || !src.Valid) { armed2 = false; return; }
            var race = src.Race;
            if (!rt.Running || race.valid == 0 || race.num_laps <= 0 || race.num_laps >= 0x61 || race.state <= 1) { armed2 = false; return; }
            if (!modern) modern = GetComponent<ReflexModernHud>();
            var mode = modern ? modern.Model2.mode : HudMode.Race;
            if (mode == HudMode.Tutorial) { armed2 = false; return; }
            bool finished = race.laps_completed >= race.num_laps; float time = race.race_time;
            bool flagCounts = mode == HudMode.Race || mode == HudMode.Waypoint;
            int slot = src.LocalSlot;
            for (int i = 0; i < rt.DriverCount && i < rt.Drivers.Length; i++)
            {
                var d = rt.Drivers[i]; if (d.slot != slot) continue;
                if (d.finished != 0 && d.race_time > 0f) { if (flagCounts) finished = true; if (finished) time = d.race_time; }
                break;
            }
            if (!finished) { if (race.state == 2) armed2 = true; return; }
            if (!armed2) return;
            armed2 = false;
            if (Blocked() || !RxCore.RxSplitScreen.TwoViews) return;
            view2 ??= new ReflexFinishBannerView(transform, 13, 1);
            view2.Play(Mathf.Max(1, race.place), Mathf.Max(1, race.riders), time, null);
            Debug.Log($"[finish banner] the second local rider: place {race.place} of {race.riders}, {time:0.000} s");
        }

        void OnDisable() { view?.Stop(); armed = false; view2?.Stop(); armed2 = false; }
        void OnDestroy() { view?.Destroy(); view2?.Destroy(); }
    }
}
