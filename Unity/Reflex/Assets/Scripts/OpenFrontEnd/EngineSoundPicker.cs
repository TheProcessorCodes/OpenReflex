using System;
using System.Collections.Generic;
using System.IO;
using Reflex.Port.Content;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    // The garage's per-bike ENGINE SOUND choice (w-audio): "ORIGINAL" (the game's own engine events), any MX Bikes engine pack found
    // (Content/EngineSoundPacks.cs: the shipped ones and the Mods folders) or any imported MX vs ATV Alive engine set ("ALIVE 450F":
    // AliveEngineSets). Persisted in the client save per bike mod while one is active on the model ("EngineSound.Mod.<modId>"), else per
    // vehicle model ("EngineSound.Model.<VehicleModelId>"); the value is the choice id ("" original, "alive:<PROJECT>" an Alive set). An
    // imported Alive bike without its own choice plays its own Alive set ("BIKE'S OWN · ALIVE 450F", the first entry of its spinner, which
    // removes the stored choice); any other model follows the OPTIONS > AUDIO > ENGINE SOUNDS default. The race plays the choice for the
    // local rider's bike (OriginalVehicleSound).
    //
    // A self-contained component so any garage can host it:
    //   EngineSoundPicker.Row(parent, vehicleModelId, label, name)   one controller-navigable spinner row (OuUi.Spinner) for one bike
    //   EngineSoundPicker.Section(parent, bikes)                     a titled section with one row per (model id, title)
    //   EngineSoundPicker.OptionsRows(panel)                          the OPTIONS > AUDIO > ENGINE SOUNDS rows (ReflexOptionsHooks.AudioExtras)
    //   EngineSoundPicker.Changed                                     raised after a choice is stored (model id, pack id)
    public static class EngineSoundPicker
    {
        public static event Action<int, string> Changed;

        static string Label(string id) => EngineSoundPacks.Title(id);
        /// The spinner entry "the bike's own Alive set" (not a choice id: stepping onto it clears the stored choice).
        const string BikesOwn = "(bike's own)";

        static void Step(List<string> choices, string current, int d, Action<string> set)
        {
            int i = choices.IndexOf(current); if (i < 0) i = 0;
            set(choices[((i + d) % choices.Count + choices.Count) % choices.Count]);
        }

        /// One spinner row for a bike (vehicle model id).
        public static VisualElement Row(VisualElement parent, int vehicleModelId, string label = "ENGINE SOUND", string name = null)
        {
            var row = OuUi.Spinner(label, () =>
            {
                string id = EngineSoundPacks.ChoiceFor(vehicleModelId);
                if (EngineSoundPacks.IsBikesOwn(vehicleModelId)) return "BIKE'S OWN · " + Label(id);
                return Label(id) + (EngineSoundPacks.HasOwnChoice(vehicleModelId) ? "" : " · DEFAULT");
            }, d =>
            {
                var choices = EngineSoundPacks.Choices();
                bool own = EngineSoundPacks.OwnAliveSet(vehicleModelId) != null;
                if (own) choices.Insert(0, BikesOwn);
                string current = own && !EngineSoundPacks.HasOwnChoice(vehicleModelId) ? BikesOwn : EngineSoundPacks.ChoiceFor(vehicleModelId);
                Step(choices, current, d, id =>
                {
                    if (id == BikesOwn) EngineSoundPacks.ClearChoice(vehicleModelId); else EngineSoundPacks.SetChoice(vehicleModelId, id);
                    Changed?.Invoke(vehicleModelId, EngineSoundPacks.ChoiceFor(vehicleModelId));
                });
            }, parent);
            row.name = name ?? "engine-sound-" + vehicleModelId;
            return row;
        }

        /// "ENGINE SOUND" and one row per bike.
        public static void Section(VisualElement parent, IEnumerable<(int model, string title)> bikes)
        {
            OuTheme.Section(parent, "ENGINE SOUND");
            var seen = new HashSet<int>();
            foreach (var (model, title) in bikes) if (model >= 0 && seen.Add(model)) Row(parent, model, title);
            int packs = EngineSoundPacks.All.Count, sets = AliveEngineSets.All.Count;
            string alive = sets == 0 ? "" : " " + sets + " MX vs ATV Alive engine" + (sets == 1 ? "" : "s") + " imported.";
            OuUi.Text(packs == 0 && sets == 0 ? "No engine sound packs found. Drop MX Bikes engine packs into Mods/EngineSounds."
                : "ORIGINAL is the game's own engine. " + packs + " MX Bikes engine pack" + (packs == 1 ? "" : "s") + " installed (more go in Mods/EngineSounds)." + alive, "mxa-note", parent);
        }

        // ---- OPTIONS > AUDIO > ENGINE SOUNDS (the options page's hook; its owner leaves it null) ----
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallOptionsHook() { if (ReflexOptionsHooks.AudioExtras == null) ReflexOptionsHooks.AudioExtras = OptionsRows; }

        public static void OptionsRows(VisualElement panel)
        {
            OptUi.Spinner("DEFAULT ENGINE SOUND", () => Label(EngineSoundPacks.DefaultChoice), d =>
                Step(EngineSoundPacks.Choices(), EngineSoundPacks.DefaultChoice, d, id => EngineSoundPacks.DefaultChoice = id), panel).name = "engine-sound-default";
            OptUi.Text("The engine sound every bike plays unless the garage gives it its own (GARAGE > VEHICLE > ENGINE SOUND; an imported MX vs ATV Alive bike plays its own Alive engine). Ours.", "mxa-note", panel);
            OptUi.Button("RESCAN ENGINE PACKS", "mxa-main-button", () => { EngineSoundPacks.Refresh(); AliveEngineSets.Refresh(); _ = EngineSoundPacks.All; _ = AliveEngineSets.All; }, panel).name = "engine-sound-rescan";
            string user = EngineSoundPacks.UserFolder;
            // (the options pages' link buttons, each with its own help under it: OPEN MUSIC FOLDER's look)
            OptUi.Button("RESCAN ENGINE PACKS", "mxa-link", () => { EngineSoundPacks.Refresh(); _ = EngineSoundPacks.All; }, panel).name = "engine-sound-rescan";
            OptUi.Text("Looks again for engine packs you added while the game was running. Ours.", "mxa-note", panel);
            OptUi.Button("OPEN ENGINE PACKS FOLDER", "mxa-link", () =>
            {
                try { Directory.CreateDirectory(user); Application.OpenURL(new Uri(user).AbsoluteUri); } catch (Exception e) { Debug.LogWarning("[EngineSounds] " + e.Message); }
            }, panel).name = "engine-sound-folder";
            OptUi.Text("Opens the folder for MX Bikes engine packs: a folder with engine.scl, sfx.cfg and the WAVs each. A Mods/EngineSounds folder next to the game works too. Ours.", "mxa-note", panel);
        }
    }
}
