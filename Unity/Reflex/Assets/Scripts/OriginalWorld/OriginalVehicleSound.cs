using System;
using System.Collections.Generic;
using Reflex.Port.Content;
using Reflex.Port.RxCore;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    // The vehicle sound layer of the open path (core/plan/QUEUE.md "w-audio"): every rider's engine from the core's telemetry
    // (rx_session_vehicle_audio). Every rider plays exactly one engine, by the choice of its bike's model (EngineSoundPacks.ChoiceFor
    // (VehicleModelId): the garage's ENGINE SOUND, an imported Alive bike's own set, the OPTIONS default; the local rider only, computer
    // riders keep the original unless PacksForComputerRiders): an MX Bikes engine pack plays through EnginePackPlayer, an MX vs ATV Alive
    // engine set through AliveEngineAudio (Alive's state machine on the set's FMOD events, core/plan/ALIVE_ENGINE_SOUNDS.md), every other
    // rider runs the original's EngineSoundEmitter (OriginalEngineAudio) on the game's own engine events (Silenced for the others). An Alive
    // set is added to OriginalAudio when the roster is known (PreloadAlive from OriginalAudio.UseRoster: the game's commands wait for it,
    // so the engine is there from the first frame of the start). Owned by OriginalWorldRuntime: Frame per collected core result, Tick per
    // render frame, Dispose with the world.
    public sealed class OriginalVehicleSound
    {
        readonly OriginalAudio audio;
        readonly OriginalWorldRuntime runtime;
        readonly OriginalEngineAudio engines;
        readonly AliveEngineAudio alive;
        readonly Func<RxVehicleAudioRider, AliveEngineSet> aliveOf;   // (made once: AliveEngineAudio.Frame takes it every frame, w-gcspikes)
        readonly OriginalGameSound game;   // w-sfx: drive-on / foley / collisions
        readonly OriginalLevelSound level; // w-sfx: the level's crowd emitters and ambience
        OriginalMusic music; int musicLookups;
        readonly RxVehicleAudioState last = new RxVehicleAudioState();
        readonly Dictionary<uint, EnginePackPlayer> packs = new Dictionary<uint, EnginePackPlayer>();   // by vehicle
        readonly List<uint> gone = new List<uint>();
        bool haveState;
        /// Whether computer riders also play the pack their bike's model has (off: the original's engines, as the brief asks by default).
        public static bool PacksForComputerRiders = false;
        /// Diagnostics: only the local rider's engine sounds (the computer riders' emitters are silent).
        public static bool SoloLocal = false;
        /// The local rider's engine is silent (the rider is in another vehicle: the drivable hovercraft, OriginalHovercraft).
        public static bool MuteLocal = false;
        /// The replay viewer plays its own engines (Reflex.Port.Replay.ReflexReplay): the live packs are silent meanwhile.
        public static bool ReplayMuted = false;
        /// SPLIT SCREEN (w-split): the two local riders' engines while each is heard at the own camera (OriginalAudio's second listener): two
        /// own-bike engines at full level sum about 3 dB over the one-rider mix, and the mix has no limiter.
        public static float SplitLocalTrim = 0.75f;
        /// RIDER CAP (w-rc-client): in a field past the original's 12, only this many computer riders nearest the listener run an engine (the
        /// original's vehicle sound manager had 12 emitters: the local rider + 11); the others are stopped until they come near again. A field of
        /// 12 or fewer is never culled (the original's mix). 0 or less: no limit.
        public static int EngineVoices = RxRiderCap.OriginalOpponents;
        /// A culled rider must come this much nearer (a fraction of its distance) than the farthest heard one to take its voice back: no
        /// flapping at the boundary.
        public const float EngineVoiceHysteresis = 0.15f;
        readonly HashSet<uint> culled = new HashSet<uint>();
        readonly float[] cullDistance = new float[RxRiderCap.Max];
        readonly uint[] cullVehicle = new uint[RxRiderCap.Max];
        /// The computer riders whose engines are culled now (diagnostics: the perf line, tests).
        public int CulledEngines => culled.Count;
        public OriginalEngineAudio Engines => engines;
        public AliveEngineAudio Alive => alive;
        public OriginalGameSound Game => game;
        public OriginalLevelSound Level => level;
        public RxVehicleAudioState Last => last;
        public EnginePackPlayer PackPlayerOfSlot(int slot)
        {
            for (int i = 0; i < last.riderCount; i++) { var r = last.riders[i]; if (r.slot == slot && packs.TryGetValue(r.vehicle, out var p)) return p; }   // (no iterator: every frame, w-gcspikes)
            return null;
        }

        public OriginalVehicleSound(OriginalAudio audio, OriginalWorldRuntime runtime)
        {
            this.audio = audio; this.runtime = runtime;
            engines = new OriginalEngineAudio(audio) { Silenced = r => PackOf(r) != null || PlaysAlive(r) || (SoloLocal && !Local(r)) || MutedLocal(r) || culled.Contains(r.vehicle) };
            // w-rng: a bike's rev end goes back to the core's start line (the core draws the next rev), between the simulation's frames
            Action<int> finished = slot => { var sim = runtime ? runtime.SimDriver : null; if (sim != null && sim.Open != null) sim.BetweenFrames(s => { try { RxVehicleAudioInput.Finished(s, slot); } catch (EntryPointNotFoundException) { } }); };
            engines.ReportFinished = finished;
            alive = new AliveEngineAudio(audio) { ReportFinished = finished };
            aliveOf = r => (SoloLocal && !Local(r)) || MutedLocal(r) || culled.Contains(r.vehicle) ? null : AliveOf(r);   // (SPLIT SCREEN: player 2's Alive engine plays on, as its original one does)
            if (runtime && runtime.Roster != null) PreloadAlive(audio, runtime.Roster);
            game = new OriginalGameSound(audio, runtime);
            game.FirstPersonView = () => runtime && runtime.FirstPersonView;
            game.FirstPersonView2 = () => runtime && runtime.FirstPersonView2;   // SPLIT SCREEN (w-split): the second rider's own view
            level = new OriginalLevelSound(audio);
        }

        /// The vehicle model of a rider's slot (the race roster), -1 unknown.
        int ModelOf(RxVehicleAudioRider r)
        {
            var roster = runtime ? runtime.Roster : null;
            if (roster == null) return -1;
            foreach (var e in roster.entries) if (e.slot.slot == r.slot) return e.vehicleModelId;
            return -1;
        }
        bool Local(RxVehicleAudioRider r)
        {
            // SPLIT SCREEN (w-split): both local riders carry the core's flag (pads 0 and 1); the roster's local entry is the viewed rider only
            if (RxSplitScreen.Active && (r.flags & RxVehicleAudioRider.LocalHuman) != 0) return true;
            var roster = runtime ? runtime.Roster : null;
            if (roster != null) foreach (var e in roster.entries) if (e.slot.slot == r.slot) return e.Local;
            return (r.flags & RxVehicleAudioRider.LocalHuman) != 0;
        }
        // MuteLocal is the first rider's (the hovercraft is driven by the first pad): the second local rider's engine plays on
        bool MutedLocal(RxVehicleAudioRider r) => MuteLocal && Local(r) && !(RxSplitScreen.Active && r.pad == 1);
        EnginePack PackOf(RxVehicleAudioRider r)
        {
            if (!Local(r) && !PacksForComputerRiders) return null;
            return EngineSoundPacks.PackFor(ModelOf(r));
        }
        AliveEngineSet AliveOf(RxVehicleAudioRider r)
        {
            if (!Local(r) && !PacksForComputerRiders) return null;
            return EngineSoundPacks.AliveSetFor(ModelOf(r));
        }
        // the rider's Alive emitter runs (a set whose state machine or sounds could not be read has none: the original engine plays instead)
        bool PlaysAlive(RxVehicleAudioRider r) => AliveOf(r) != null && alive.StateOf(r.slot).Length > 0;

        /// Adds the Alive engine sets the race's riders play (the local rider; every rider with PacksForComputerRiders) to the sound player
        /// before the start: OriginalAudio.AddSet's gate holds the game's commands until the set's waves are decoded. Once per set
        /// (AliveEngineAudio adds the same key when its emitter starts: a no-op then).
        public static void PreloadAlive(OriginalAudio audio, ReflexRoster roster)
        {
            if (!audio || roster == null) return;
            foreach (var e in roster.entries)
            {
                if (e.vehicleModelId < 0 || (!e.Local && !PacksForComputerRiders)) continue;
                AliveEngineSet set = null;
                try { set = EngineSoundPacks.AliveSetFor(e.vehicleModelId); }
                catch (Exception x) { Debug.LogWarning("Alive engine of vehicle model " + e.vehicleModelId + ": " + x.Message); }
                if (set != null) { var s = set; audio.AddSet(() => ReflexAudioSet.BuildAliveSet(s), s.id); }
            }
        }

        /// The riders' pelvis (bone 0, MXRider_Root, of the 69 bones the core exports) of a collected result: the foley emitter puts the rider's sounds on it
        /// (snapshot + 0x200, w-reverb; OriginalGameSound.SetPelvis). Called before Frame.
        public void Poses(OriginalSimulation.Results r)
        {
            if (r == null || game == null) return;
            if (r.riderBones > 0 && r.riderPose != null && r.riderPose.Length >= 16)
            {
                var va = r.vehicleAudio;
                if (va != null && va.valid) for (int i = 0; i < va.riderCount; i++) if ((va.riders[i].flags & RxVehicleAudioRider.LocalHuman) != 0) { game.SetPelvis(va.riders[i].slot, r.riderPose, 0); break; }
            }
            for (int i = 0; i < r.slotCount && i < r.slots.Length; i++)
                if (r.slotRiderBones[i] > 0 && r.slotRiderPose[i] != null && r.slotRiderPose[i].Length >= 16) game.SetPelvis(r.slots[i], r.slotRiderPose[i], 0);
        }

        /// A collected core result: the original emitters' game frame, and the pack players' telemetry; the other managers (w-sfx).
        public void Frame(RxVehicleAudioState state, RxSoundState sound, float dt)
        {
            if (state == null || !state.valid) return;
            if (sound != null && sound.valid) audio.SetLevelMix(sound.channelMix, sound.channelCount);
            last.CopyFrom(state); haveState = true;
            engines.LocalTrim = audio.SecondListener ? SplitLocalTrim : 1f;   // SPLIT SCREEN: two own engines
            CullEngines(state);
            alive.Frame(state, Mathf.Clamp(dt, 0f, 0.25f), aliveOf, engines.FieldTrim);   // (first: the original's Silenced asks whether an Alive emitter runs)
            engines.Frame(state, Mathf.Clamp(dt, 0f, 0.25f));
            if (sound != null)
            {
                // w-landclank: the landing clank is trimmed against the local rider's engine when it is not the original's (a pack, or an
                // Alive set: both play at the original engine's level, EnginePackPlayer's loudness match / the set's calibration trim)
                game.LocalHasPack = false;
                for (int i = 0; i < state.riderCount; i++) if (Local(state.riders[i]) && (PackOf(state.riders[i]) != null || PlaysAlive(state.riders[i]))) game.LocalHasPack = true;
                game.Frame(state, sound, Mathf.Clamp(dt, 0f, 0.25f)); level.Frame(sound);
                if (sound.musicValid) { if (!music && musicLookups++ % 120 == 0) music = UnityEngine.Object.FindAnyObjectByType<OriginalMusic>(); if (music) music.ObserveOpen(sound); }   // w-sfx: the core's MusicPlayer
            }
            gone.Clear();
            foreach (var kv in packs) gone.Add(kv.Key);
            for (int i = 0; i < state.riderCount; i++)
            {
                var r = state.riders[i];
                var p = PackOf(r);
                if (p == null) continue;
                gone.Remove(r.vehicle);
                if (!packs.TryGetValue(r.vehicle, out var player) || player.Pack != p)
                {
                    player?.Destroy();
                    packs[r.vehicle] = new EnginePackPlayer(p, audio.transform, audio.Reverb);
                }
            }
            foreach (var v in gone) { packs[v].Destroy(); packs.Remove(v); }
        }

        /// A render frame (after OriginalAudio.Tick's gains): the pack voices follow the newest telemetry.
        public void Tick(float dt)
        {
            if (!haveState || packs.Count == 0) return;
            float gain = ReplayMuted ? 0f : audio.ChannelGain(ReflexChannel.Engine);
            float localGain = audio.SecondListener ? gain * SplitLocalTrim : gain;   // SPLIT SCREEN: two own engines
            for (int i = 0; i < last.riderCount; i++)
            {
                var r = last.riders[i];
                if (!packs.TryGetValue(r.vehicle, out var player)) continue;
                bool local = Local(r);
                bool cut = !local && culled.Contains(r.vehicle);   // RIDER CAP: a culled computer rider's pack is silent too
                // w-aiengine: the computer riders' packs (PacksForComputerRiders) sit under the same field trim as their original engines
                // w-packrpm: the voices follow the audio rpm / load model (Reflex's quick shifts and wheel spin, smoothed); the limiter the true rpm
                player.AudioRpm.Step(r.rpm, r.idleRpm, r.maxRpm, r.throttle, r.gear, r.airborne, dt, out float audioRpm, out float audioThrottle);
                player.Update(r.rpm, r.idleRpm, r.maxRpm, r.throttle, r.airborne, r.engineSize, audio.Hear(new Vector3(r.px, r.py, r.pz), ref player.HeardBy2), MutedLocal(r) || cut ? 0f : local ? localGain : gain * engines.FieldTrim, dt,   // (SPLIT SCREEN: placed for the nearer listener)
                              audioRpm, audioThrottle);
            }
        }

        // RIDER CAP: the nearest EngineVoices computer riders keep their engines (squared distances to the listener, no allocation: the arrays
        // are sized for RxRiderCap.Max and sorted in place). A rider heard last frame counts EngineVoiceHysteresis nearer.
        void CullEngines(RxVehicleAudioState state)
        {
            if (state.riderCount <= RxRiderCap.Original || EngineVoices <= 0) { culled.Clear(); return; }
            var lis = audio.Listener; int n = 0;
            float keep = (1f - EngineVoiceHysteresis) * (1f - EngineVoiceHysteresis);
            for (int i = 0; i < state.riderCount && n < cullDistance.Length; i++)
            {
                var r = state.riders[i];
                if ((r.flags & RxVehicleAudioRider.Valid) == 0 || Local(r)) continue;
                var h = audio.Hear(new Vector3(r.px, r.py, r.pz));   // (SPLIT SCREEN: the nearer listener's distance)
                float dx = h.x - lis.x, dy = h.y - lis.y, dz = h.z - lis.z, d = dx * dx + dy * dy + dz * dz;
                cullDistance[n] = culled.Contains(r.vehicle) ? d : d * keep; cullVehicle[n] = r.vehicle; n++;
            }
            culled.Clear();
            if (n <= EngineVoices) return;
            Array.Sort(cullDistance, cullVehicle, 0, n);
            for (int k = EngineVoices; k < n; k++) culled.Add(cullVehicle[k]);
        }

        public void Dispose()
        {
            engines.Dispose();
            alive.Dispose();
            game.Dispose();
            level.Dispose();
            foreach (var p in packs.Values) p.Destroy();
            packs.Clear();
        }
    }
}
