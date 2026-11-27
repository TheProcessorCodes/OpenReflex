// Which source models a clean riding / racing venue leaves out: port of tools/unleashed/scene_rules.py (the rules of Open Unleashed's
// scene_api.c).
using System;
using System.Collections.Generic;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnHiddenModels
    {
        /// The hidden .scn sections.
        public readonly HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
        /// Lower-cased model names whose collision goes with them.
        public readonly HashSet<string> collisionNames = new HashSet<string>(StringComparer.Ordinal);
        /// reason -> count, in the order the reasons first occur.
        public readonly JObj reasons = new JObj();
    }

    public static class UnSceneRules
    {
        /// enabled(value, default).
        public static bool Enabled(string value, bool fallback)
        {
            if (value == null) return fallback;
            string v = UnIni.Lower(UnIni.Strip(value));
            return !(v == "0" || v == "false" || v == "f" || v == "no");
        }

        static IEnumerable<UnIniSection> All(UnIni scene)
        {
            yield return scene.Defaults;
            foreach (var s in scene.Sections) yield return s;
        }

        /// hidden_models(scene, race_gate): race_gate = the level starts from Reflex's own gate, so the source's gate and its collision
        /// are left out.
        public static UnHiddenModels HiddenModels(UnIni scene, bool raceGate = false)
        {
            var r = new UnHiddenModels();
            bool? crowd = null;
            foreach (var obj in All(scene))
            {
                string section = obj.Name;
                if (!UnIni.Lower(section).StartsWith("model", StringComparison.Ordinal) || !obj.Has("slt")) continue;
                string name = obj["slt"] ?? throw new UnValueError("Unleashed scene: model without a name in [" + section + "]");
                string low = UnIni.Lower(name), kind = UnIni.Lower(obj.Get("type", "") ?? throw new UnValueError("Unleashed scene: empty Type in [" + section + "]"));
                string reason = null;
                string activity = UnIni.Lower(obj.Get("activity", "") ?? throw new UnValueError("Unleashed scene: empty Activity in [" + section + "]"));
                bool raceActivity = (activity.Contains("national") || activity.Contains("supercross") || activity.Contains("supermoto") || activity.Contains("short track")) && !activity.Contains("waypoint");
                if (raceGate && Array.IndexOf(UnStartGate.SourceModels, low) >= 0) reason = "source start gate (the Reflex gate stands there)";
                else if (kind == "startzone" || kind == "waypoint" || (kind == "activityspecific" && !raceActivity)) reason = "activity/challenge object";
                else if (kind == "activityspecific" && Enabled(obj.Get("invisible"), false)) reason = "invisible race object";
                else if (Enabled(obj.Get("collisiononly"), false)) reason = "collision-only model";
                else if (!Enabled(obj.Get("startenabled"), true)) reason = "disabled source model";
                else if (low.Contains("cameraflash")) reason = "inactive camera flash";
                else if (low.Contains("seats"))
                {
                    if (crowd == null)
                    {
                        crowd = false;
                        foreach (var o in All(scene)) if (UnIni.Lower(o.Get("slt", "") ?? "").Contains("crowd")) { crowd = true; break; }
                    }
                    if (crowd.Value) reason = "empty-seat crowd alternative";
                }
                if (reason == null) continue;
                r.hidden.Add(section);
                r.reasons.Set(reason, (r.reasons.Get(reason) is long c ? c : 0L) + 1);
                if ((kind == "startzone" || kind == "waypoint" || kind == "activityspecific") && Enabled(obj.Get("hascol"), true)) r.collisionNames.Add(low);
                if (reason.StartsWith("source start gate", StringComparison.Ordinal)) r.collisionNames.Add(low);
            }
            return r;
        }
    }
}
