using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Reflex.Port.Content;
using Reflex.Port.RxCore;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    // MX vs ATV Alive's engine sound (core/plan/ALIVE_ENGINE_SOUNDS.md section 5), beside Reflex's OriginalEngineAudio: one emitter per rider
    // who plays an imported Alive engine set (AliveEngineSets), fed every game frame with the core's RxVehicleAudioRider.
    //
    // Alive's engine is not rpm-driven. Each state of AudioEngineConfig_<size> (35: Idle, AccelPwr, SustainHigh, Bogging, PanicStartInAir ...)
    // names one event of the set's FMOD project (group STATES, one sound definition of 1-12 recordings), and what plays next is decided by
    // data: the EngineActions* action lists (tests of the vehicle audio data, gotostate, run / halt another list) and the sync points recorded
    // in the waves (FSB4 SYNC: Run_<list>, Goto_<state>, Halt_<list>), which OriginalAudio raises as HostSync while a recording plays. E.g.
    // 450F_Accel_Track04 runs OnThrottleCheckPwr at 0 ms and goes to SustainHigh at 2066 ms; the sustain recordings end with a Goto of their
    // own state ~100 ms before their end, so the next variant starts while the last fades (sub 1, the event's fade-out). Every emitter
    // therefore plays on two wrappers in turn: the new state's event on one while the old one fades on the other.
    //
    // AliveEngineMachine is the interpreter (the program from the set's XML, the running lists, the data members); AliveEngineAudio maps it
    // onto OriginalAudio's host commands and adds what Reflex's emitter does around its own state machine: the start line (cues and the
    // core's StartLine / Rev serial, each rev one REV event), the wreck, the drowned engine (Choke) and the underwater occlusion (0.6), AIFX
    // (0 for the local human, else the core's audio slot), the event volume (sub 2: the set's calibration trim x the AI field trim for the
    // computer riders), the 3D position every frame (sub 5) and Reflex's engine range per event (sub 6: 1 / 300 ft; the Alive events'
    // 40 / 10000 is Alive's own world scale). The set's XML is read by AliveEngineMachine.Program (the only reader of AudioEngineConfig.xml
    // and EngineActions.xml), compiled into indexed arrays so that a frame's evaluation allocates nothing.
    //
    // Additions (Alive's runtime is not decompiled; each is outside what the action lists decide):
    //  - the config's StartLine state names the REV event (every Alive set); Reflex's core enters StartLine between revs, so the start line
    //    plays the start state's event (the idle loop) and each rev is one REV event (the Rev state).
    //  - a recording that ends without a Goto marker or a state change (no list moved on) returns to the start state, so an engine never
    //    stays silent; the Wreck and Choke states (whose events end the engine) and the start line are excluded.
    //  - the start line without the core's export (an older core): StartLine holds the idle until the line ends (no own revs).
    public sealed class AliveEngineAudio
    {
        /// Logs the action lists' debugmessage / startfoley / stopfoley actions (editor and development builds).
        public static bool LogMessages;
        /// Engine events' 3D range as Reflex's engine events (all 1 .. 300 game feet; OriginalEngineAudio.FieldRolloffMin / Max), posted with every event.
        const float RangeMin = 1f, RangeMax = 300f;
        /// Reflex's underwater occlusion (MXRegistry UnderwaterOcclusionFactor default).
        const float UnderwaterOcclusion = 0.6f;
        /// How long a stopped event's wrapper keeps getting its 3D position (a key-off one-shot plays out; the longest recording is ~8 s).
        const float TailSeconds = 8f;

        // ------------------------------------------------------------------------------------------------ a set's program
        sealed class SetInfo
        {
            public AliveEngineSet set; public AliveEngineMachine.Program program; public bool failed, eventsChecked;
            public bool[] exists;
            public int startLine = -1, rev = -1, wreck = -1, choke = -1;
            public int panicStart = -1, panicLoop = -1, limiterStart = -1, limiterLoop = -1;   // PanicStartInAir / PanicLoopInAir, their *2nd
            public string startLinePath = "";
            public string PathOf(int s) => s == startLine ? startLinePath : program.PathOf(s);
            public bool Known(int s) { string p = PathOf(s); return p.Length > 0 && (!eventsChecked || s >= exists.Length || exists[s]); }
        }

        sealed class Emitter
        {
            public uint vehicle; public int slot; public SetInfo info; public AliveEngineMachine machine;
            public readonly RxVehicleAudioRider rider = new RxVehicleAudioRider();
            public AliveEngineMachine.Telemetry tele;
            public uint wrapperA, wrapperB, wrapper, serial; public string path = ""; public bool playing, finished;
            public uint other; public float otherTail;                   // the wrapper of the event stopped last (fading / playing out)
            public float trimA = -1f, trimB = -1f;                       // the event volume last posted per wrapper (-1: none since the event started)
            public float aifx = -1f; public Vector3 position;
            public bool startLine, coreStartLine, finishReported; public uint coreSerial;
            public bool seen;
        }

        readonly OriginalAudio audio;
        readonly Dictionary<uint, Emitter> emitters = new Dictionary<uint, Emitter>();
        readonly Dictionary<uint, Emitter> byWrapper = new Dictionary<uint, Emitter>();
        readonly Dictionary<string, SetInfo> sets = new Dictionary<string, SetInfo>(StringComparer.Ordinal);
        readonly List<uint> gone = new List<uint>();
        readonly Stack<uint> freePairs = new Stack<uint>();             // wrapper pairs of removed emitters (wrapperA), reused by Register
        uint nextWrapper, nextSerial, lastCue; bool cueInit;
        float fieldTrim = 1f;
        /// A bike's rev one-shot played to its end on an emitter the core's start line drives: the sound slot, for
        /// rx_session_vehicle_audio_finished (set by the owner, as OriginalEngineAudio.ReportFinished).
        public Action<int> ReportFinished;
        public int EmitterCount => emitters.Count;

        public AliveEngineAudio(OriginalAudio audio)
        {
            this.audio = audio;
            nextWrapper = OriginalAudio.HostWrapperBase + 0x800;
            if (audio) { audio.HostFinished += OnFinished; audio.HostSync += OnSync; }
        }

        public void Dispose()
        {
            if (audio)
            {
                audio.HostFinished -= OnFinished; audio.HostSync -= OnSync;
                foreach (var e in emitters.Values) { audio.Host(12, e.wrapperA, 0, "", null, 0f, Vector3.zero, false); audio.Host(12, e.wrapperB, 0, "", null, 0f, Vector3.zero, false); }
            }
            emitters.Clear(); byWrapper.Clear();
        }

        /// The state of a rider's emitter (diagnostics): its name, "" without an emitter.
        public string StateOf(int slot) { foreach (var e in emitters.Values) if (e.slot == slot) return e.machine.StateName; return ""; }
        /// The event a rider's emitter plays (diagnostics), "" none.
        public string EventOf(int slot) { foreach (var e in emitters.Values) if (e.slot == slot) return e.playing ? e.path : ""; return ""; }

        void OnFinished(uint wrapper, uint serial)
        {
            if (byWrapper.TryGetValue(wrapper, out var e))
            {
                if (e.wrapper == wrapper && e.serial == serial) { e.finished = true; e.playing = false; }
                else if (e.other == wrapper) e.otherTail = 0f;
            }
        }

        void OnSync(uint wrapper, uint serial, string name)
        {
            // (also after the event's EVENT_FINISHED: OriginalAudio raises a one-shot's last markers after its end in the same tick)
            if (byWrapper.TryGetValue(wrapper, out var e) && e.wrapper == wrapper && e.serial == serial) e.machine.Marker(name);
        }

        /// One game frame (a collected core result). `setOf`: the rider's Alive set, null when another emitter plays the rider (its Alive
        /// emitter, if any, is removed). `fieldTrim`: OriginalEngineAudio.FieldTrim, the computer riders' event volume.
        public void Frame(RxVehicleAudioState state, float dt, Func<RxVehicleAudioRider, AliveEngineSet> setOf, float fieldTrim)
        {
            if (!audio || state == null) return;
            this.fieldTrim = fieldTrim;
            ApplyCues(state);
            foreach (var e in emitters.Values) e.seen = false;
            for (int i = 0; i < state.riderCount; i++)
            {
                var r = state.riders[i];
                if ((r.flags & RxVehicleAudioRider.Valid) == 0) continue;
                var set = setOf != null ? setOf(r) : null;
                if (set == null) continue;
                var info = InfoOf(set);
                if (info == null) continue;
                if (emitters.TryGetValue(r.vehicle, out var e) && e.info != info) { Remove(e); e = null; }
                if (e == null) e = Register(r, info);
                e.seen = true;
                e.rider.CopyFrom(r);
                if (r.audioSlot >= 0) e.aifx = r.audioSlot;
                e.position = new Vector3(r.px, r.py, r.pz);
                CheckEvents(info);
                FollowCore(e, r);
                Update(e, dt);
            }
            gone.Clear();
            foreach (var kv in emitters) if (!kv.Value.seen) gone.Add(kv.Key);
            foreach (var k in gone) Remove(emitters[k]);
        }

        // ------------------------------------------------------------------------------------------------ the sets
        SetInfo InfoOf(AliveEngineSet set)
        {
            if (string.IsNullOrEmpty(set.id)) return null;
            if (sets.TryGetValue(set.id, out var info)) return info.failed || !info.eventsChecked && Unloadable(info) ? null : info;
            info = new SetInfo { set = set };
            sets[set.id] = info;
            try
            {
                string config = File.ReadAllText(set.ConfigPath), actions = File.ReadAllText(set.ActionsPath);
                info.program = AliveEngineMachine.Program.Parse(config, actions, set.project);
            }
            catch (Exception x) { info.failed = true; Debug.LogWarning("Alive engine " + set.id + ": the state machine was not read: " + x.Message); return null; }
            var p = info.program;
            info.startLine = p.StateIndex("StartLine"); info.rev = p.StateIndex("Rev"); info.wreck = p.StateIndex("Wreck"); info.choke = p.StateIndex("Choke");
            info.panicStart = p.StateIndex("PanicStartInAir"); info.panicLoop = p.StateIndex("PanicLoopInAir");
            info.limiterStart = p.StateIndex("PanicStartInAir2nd"); info.limiterLoop = p.StateIndex("PanicLoopInAir2nd");
            info.startLinePath = info.startLine < 0 ? "" : p.PathOf(info.startLine);
            if (info.rev >= 0 && info.startLinePath == p.PathOf(info.rev)) info.startLinePath = p.PathOf(p.StartState);   // each rev one REV event
            info.exists = new bool[p.StateCount];
            audio.AddSet(() => ReflexAudioSet.BuildAliveSet(set), set.id);
            return Unloadable(info) ? null : info;
        }

        // the set's library was not built (an .fsb / .fev the player cannot read): no emitter (removed), the original engine plays
        bool Unloadable(SetInfo info)
        {
            if (!audio.SetFailed(info.set.id)) return false;
            info.failed = true; Debug.LogWarning("Alive engine " + info.set.id + ": its sounds were not loaded; the original engine plays");
            return true;
        }

        // once the set is merged: which state events exist (Choke, Flooding, EnterAir, BarBangBog1, BarBangBog2 name events no Alive project has)
        void CheckEvents(SetInfo info)
        {
            if (info.eventsChecked || !audio.SetLoaded(info.set.id)) return;
            for (int s = 0; s < info.exists.Length; s++) info.exists[s] = audio.HasEvent(info.PathOf(s));
            info.eventsChecked = true;
        }

        Emitter Register(RxVehicleAudioRider r, SetInfo info)
        {
            var e = new Emitter { vehicle = r.vehicle, slot = r.slot, info = info, machine = new AliveEngineMachine(info.program) };
            if (freePairs.Count > 0) e.wrapperA = freePairs.Pop(); else { e.wrapperA = nextWrapper; nextWrapper += 2; }
            e.wrapperB = e.wrapperA + 1;
            e.wrapper = e.wrapperB;                                    // the first play goes to A
            if (r.audioSlot >= 0) e.aifx = r.audioSlot;
            e.rider.CopyFrom(r);
            e.position = new Vector3(r.px, r.py, r.pz);
            emitters[r.vehicle] = e; byWrapper[e.wrapperA] = e; byWrapper[e.wrapperB] = e;
            var em = e;
            e.machine.Entered = s => Enter(em, s);
            e.machine.Redirect = s => InAir(em, s);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            e.machine.Message = m => { if (LogMessages) Debug.Log("[Alive engine " + em.slot + "] " + em.machine.StateName + ": " + m); };
#endif
            e.machine.GotoState(info.program.StartState);
            return e;
        }

        void Remove(Emitter e)
        {
            if (e.playing) audio.Host(12, e.wrapper, e.serial, "", null, 0f, Vector3.zero, false);
            if (e.otherTail > 0f) audio.Host(12, e.other, 0, "", null, 0f, Vector3.zero, false);
            e.playing = false; e.otherTail = 0f;
            emitters.Remove(e.vehicle); byWrapper.Remove(e.wrapperA); byWrapper.Remove(e.wrapperB);
            freePairs.Push(e.wrapperA);                                // (a culled rider who returns, a set change: no new instances)
        }

        bool LocalHuman(Emitter e) => (e.rider.flags & RxVehicleAudioRider.LocalHuman) != 0;

        /// The throttle from which a flight plays the rev limiter (InAir below).
        public const float LimiterThrottle = 0.5f;
        // Off the ground with the throttle held, the lists enter PanicStartInAir and loop PanicLoopInAir: recordings of throttle blips
        // that die down ~10 dB within a second, which in Reflex (long flights, the gas held through them) sounds like the engine fading
        // out. The flat rev-limiter recordings are the *2nd states (Alive plays them once the throttle comes back on in the air), so
        // with the throttle held the flight plays those instead; their own lists (Airborne2ThrottleCheck, OffThrottlePanic) carry on.
        int InAir(Emitter e, int s)
        {
            var i = e.info;
            if (e.tele.throttle < LimiterThrottle) return s;
            if (s == i.panicStart && i.Known(i.limiterStart)) return i.limiterStart;
            if (s == i.panicLoop && i.Known(i.limiterLoop)) return i.limiterLoop;
            return s;
        }

        // ------------------------------------------------------------------------------------------------ the wrappers (FMOD commands)
        // a state entered (also the same state again): the old event stops with its fade-out, the new one plays on the other wrapper
        void Enter(Emitter e, int s)
        {
            if (e.playing)
            {
                audio.Host(1, e.wrapper, e.serial, e.path, null, 0f, Vector3.zero, false);
                e.other = e.wrapper; e.otherTail = TailSeconds;
            }
            e.playing = false; e.finished = false; e.path = "";
            e.serial = ++nextSerial;                                   // the old event's late markers / end no longer match (unique across
                                                                       // emitters: a reused wrapper pair never matches its last owner's)
            if (!e.info.Known(s)) return;                              // a state with no event: stop only
            e.wrapper = e.wrapper == e.wrapperA ? e.wrapperB : e.wrapperA;
            if (e.other == e.wrapper) e.otherTail = 0f;
            e.path = e.info.PathOf(s);
            e.playing = true;
            audio.Host(0, e.wrapper, e.serial, e.path, null, 0f, e.position, true);
            // the range and the volume per event, not per wrapper: the replay maps each recorded event to an instance of its own
            audio.Host(6, e.wrapper, e.serial, e.path, null, RangeMin, new Vector3(RangeMax, 0f, 0f), false);
            if (e.wrapper == e.wrapperA) e.trimA = -1f; else e.trimB = -1f;
            audio.Host(4, e.wrapper, e.serial, e.path, "AIFX", LocalHuman(e) ? 0f : e.aifx, Vector3.zero, false);
            audio.Host(4, e.wrapper, e.serial, e.path, "LOAD", e.rider.load, Vector3.zero, false);
            PostVolume(e);
        }

        // the event volume (sub 2, the command the game's SetVolume uses): the set's calibration trim (set.json "trim", section 7), times
        // the AI field trim for the computer riders as OriginalEngineAudio posts it; kept by the wrapper's instance, so posted when an event
        // starts (Enter) and on a change
        void PostVolume(Emitter e)
        {
            if (!e.playing) return;
            float v = (LocalHuman(e) ? 1f : fieldTrim) * e.info.set.trim;
            float posted = e.wrapper == e.wrapperA ? e.trimA : e.trimB;
            if (Mathf.Abs(posted - v) < 0.004f) return;
            if (e.wrapper == e.wrapperA) e.trimA = v; else e.trimB = v;
            audio.Host(2, e.wrapper, e.serial, e.path, null, v, Vector3.zero, false);
        }

        // ------------------------------------------------------------------------------------------------ the start line (as OriginalEngineAudio)
        void ApplyCues(RxVehicleAudioState st)
        {
            if (!cueInit) { cueInit = true; lastCue = 0; }
            if (st.cueSerial < lastCue) lastCue = 0;    // a new session
            if (st.cueSerial == lastCue) return;
            for (uint n = lastCue + 1; n <= st.cueSerial; n++)
            {
                RxVehicleAudioCue cue = null;
                foreach (var c in st.cues) if (c.serial == n) { cue = c; break; }
                if (cue == null) continue;
                switch (cue.kind)
                {
                    case RxVehicleAudioState.CueStartLineEnable:
                        foreach (var e in emitters.Values) { e.startLine = true; if (!e.coreStartLine && e.info.startLine >= 0) e.machine.GotoState(e.info.startLine); }
                        break;
                    case RxVehicleAudioState.CueStartLineDisable:
                        foreach (var e in emitters.Values) e.startLine = false;
                        break;
                    case RxVehicleAudioState.CuePreExited:
                        foreach (var e in emitters.Values) { e.startLine = false; e.machine.GotoState(e.info.program.StartState); }
                        break;
                }
            }
            lastCue = st.cueSerial;
        }

        // the core's start line (w-rng): a new serial enters StartLine, enters Rev or replays the rev (the REV event again)
        void FollowCore(Emitter e, RxVehicleAudioRider r)
        {
            e.coreStartLine = (r.flags & RxVehicleAudioRider.StartLine) != 0;
            if (!e.coreStartLine || r.startLineSerial == e.coreSerial) return;
            e.coreSerial = r.startLineSerial;
            if (r.startLineState == 4 && e.info.startLine >= 0) { e.startLine = true; e.machine.GotoState(e.info.startLine); }
            else if (r.startLineState == 2 && e.info.rev >= 0) { e.finishReported = false; e.machine.GotoState(e.info.rev); }
        }

        // ------------------------------------------------------------------------------------------------ per frame
        void Update(Emitter e, float dt)
        {
            var r = e.rider; var info = e.info; var m = e.machine;
            int s = m.State;
            bool line = s == info.startLine || s == info.rev;
            if (line) { if (!e.startLine) m.GotoState(info.program.StartState); }
            else if (r.wrecked) { if (s != info.wreck && info.wreck >= 0) m.GotoState(info.wreck); }
            else if (s == info.wreck) m.GotoState(info.program.StartState);
            else if (r.underwater && r.drowned) { if (s != info.choke && info.choke >= 0) m.GotoState(info.choke); }
            else if (s == info.choke) m.GotoState(info.program.StartState);
            s = m.State;
            m.ListsEnabled = s != info.startLine && s != info.rev && s != info.wreck && s != info.choke;
            // the data members (Alive's vehicle audio data; feet and seconds as the core's)
            ref var t = ref e.tele;
            t.throttle = r.throttle; t.speed = r.speed; t.verticalSpeed = r.vy; t.load = r.load; t.control2 = r.control2;
            t.clutch = r.clutch; t.airborne = r.airborne; t.flooded = r.drowned; t.localPlayer = LocalHuman(e);
            // the rear wheels: a bike's contact group 0 (sim + 0x8894; group 1 is the front), an ATV's rear left / right
            t.rearAirborne = r.vehicleClass == RxVehicleAudioRider.ClassMx ? r.wheelAir[0] : r.wheelAir[0] && r.wheelAir[1];
            m.Step(in t, dt);
            if (e.finished)
            {
                e.finished = false;
                s = m.State;
                if (s == info.rev)
                {
                    if (e.coreStartLine) { if (!e.finishReported) { e.finishReported = true; if (r.audioSlot >= 0) ReportFinished?.Invoke(r.audioSlot); } }
                    else m.GotoState(e.startLine ? info.startLine : info.program.StartState);
                }
                else if (s != info.startLine && s != info.wreck && s != info.choke) m.GotoState(info.program.StartState);   // ran out with no state change
            }
            if (e.playing) audio.Host(5, e.wrapper, e.serial, e.path, null, 0f, e.position, true);
            if (e.otherTail > 0f) { e.otherTail -= dt; audio.Host(5, e.other, 0, "", null, 0f, e.position, true); }
            float occ = r.underwater ? UnderwaterOcclusion : 0f;
            if (e.playing) audio.Host(10, e.wrapper, e.serial, e.path, null, occ, Vector3.zero, false);
            PostVolume(e);
        }
    }

    // The Alive engine's state machine: the program read from a set's AudioEngineConfig.xml and EngineActions.xml (the bxml as XML), the
    // per-emitter state, the running action lists and the data members they test.
    //  - states: the config's children (notused skipped; a state an action or marker names that the config lacks has no event).
    //    GotoState: the running lists are cleared, statetime restarts, Entered is called (also for the state it is in: the event again).
    //  - lists: Run (a marker or runactionlist) starts a list; already running: restarted. repeateveryframe="T" lists are evaluated every
    //    Step until halted (haltactionlist, Halt_ marker) or the state changes; "F" lists run once, at once. runactionlist evaluates the list
    //    at once (and keeps a T list running); a gotostate ends every list's evaluation for the frame.
    //  - actions: test (every test passes: testspassedactions, else testsfailedactions; depth-first in document order), gotostate,
    //    runactionlist, haltactionlist; startfoley / stopfoley / debugmessage go to Message (nothing plays).
    //  - tests: vehicleaudiodata (datamember comparisontype lt|le|eq|ge|gt|ne comparisonvalue), throttlerangetime (the throttle has stayed
    //    in [rangelow, rangehigh] for `seconds`), speedchangetime (speed now minus speed `seconds` ago, compared with comparisonvalue).
    //  - members: throttle, speed (ft/s), statetime (s), clutch, reverseon (control2 > 0.1 and speed < 1), reartiresairborne, engineload,
    //    numwhoops (consecutive airborne hops shorter than 0.4 s; reset by a longer flight or 1.5 s on the ground), verticalspeed (ft/s),
    //    justlanded (the first Step after airborne), flooded, localplayer. Booleans are 0 / 1.
    public sealed class AliveEngineMachine
    {
        public const int TestData = 0, TestThrottleRange = 1, TestSpeedChange = 2;
        public const int ActTest = 0, ActGoto = 1, ActRun = 2, ActHalt = 3, ActMessage = 4;
        public enum Member { Throttle, Speed, StateTime, Clutch, ReverseOn, RearTiresAirborne, EngineLoad, NumWhoops, VerticalSpeed, JustLanded, Flooded, LocalPlayer }
        public enum Cmp { Lt, Le, Eq, Ge, Gt, Ne }
        public const float WhoopMaxAir = 0.4f, WhoopGround = 1.5f, HistorySeconds = 2f;

        public sealed class TestDef { public int type; public Member member; public Cmp cmp; public float value, low, high, seconds; }
        public sealed class ActionDef { public int type; public int target = -1; public string text = ""; public TestDef[] tests; public ActionDef[] passed, failed; }
        public sealed class ListDef { public string name; public bool repeat; public ActionDef[] actions; }

        /// The telemetry of one frame.
        public struct Telemetry { public float throttle, speed, verticalSpeed, load, control2; public bool clutch, airborne, rearAirborne, flooded, localPlayer; }

        public sealed class Program
        {
            public string project = "", sound = "";
            readonly List<string> states = new List<string>(), paths = new List<string>();
            readonly Dictionary<string, int> stateByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, int> listByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            /// marker name -> (kind ActGoto / ActRun / ActHalt, index)
            readonly Dictionary<string, (int kind, int index)> markers = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
            public ListDef[] lists = new ListDef[0];
            public int StartState;
            /// The states the config names are 0 .. ConfigStateCount-1, in file order; a start state or gotostate the config does not name follows.
            public int ConfigStateCount { get; private set; }
            /// What the parser met and did not know (action / test types, members, comparisons): empty for every Alive set.
            public readonly List<string> unknown = new List<string>();
            public int StateCount => states.Count;
            public string StateName(int s) => s >= 0 && s < states.Count ? states[s] : "";
            /// The state's event path ("alive_<project>/<group>/<event>", lower case), "" none.
            public string PathOf(int s) => s >= 0 && s < paths.Count ? paths[s] : "";
            public int StateIndex(string name) => name != null && stateByName.TryGetValue(name, out int i) ? i : -1;
            public int ListIndex(string name) => name != null && listByName.TryGetValue(name, out int i) ? i : -1;
            public bool Marker(string name, out int kind, out int index)
            {
                if (name != null && markers.TryGetValue(name, out var m)) { kind = m.kind; index = m.index; return true; }
                kind = index = -1; return false;
            }

            int State(string name)
            {
                if (string.IsNullOrEmpty(name)) return -1;
                if (stateByName.TryGetValue(name, out int i)) return i;
                states.Add(name); paths.Add(""); return stateByName[name] = states.Count - 1;
            }

            /// `project`: the set's project (the event prefix "alive_<project>"); empty: the config's sound=.
            public static Program Parse(string configXml, string actionsXml, string project)
            {
                var p = new Program();
                var config = new XmlDocument(); config.LoadXml(configXml);
                var root = config.DocumentElement;
                if (!Same(root.LocalName, "audio_engine")) throw new FormatException("the config's root is <" + root.LocalName + ">, not <audio_engine>");
                p.sound = Attr(root, "sound");
                p.project = string.IsNullOrEmpty(project) ? p.sound : project;
                string prefix = "alive_" + p.project.ToLowerInvariant() + "/";
                foreach (XmlNode n in root.ChildNodes)
                {
                    if (!(n is XmlElement c)) continue;
                    int s = p.State(c.LocalName);
                    string notused = Attr(c, "notused"), ev = c.InnerText.Trim();
                    if (notused.StartsWith("T", StringComparison.OrdinalIgnoreCase) || notused == "1" || ev.Length == 0) continue;
                    p.paths[s] = prefix + ev.ToLowerInvariant();
                }
                p.ConfigStateCount = p.states.Count;
                string start = Attr(root, "startEngineState");
                p.StartState = p.State(start.Length > 0 ? start : "Idle");
                var actions = new XmlDocument(); actions.LoadXml(actionsXml);
                var lr = actions.DocumentElement;
                if (!Same(lr.LocalName, "engineactionlists")) throw new FormatException("the actions' root is <" + lr.LocalName + ">, not <engineactionlists>");
                var lists = new List<(XmlElement e, ListDef d)>();
                foreach (XmlNode n in lr.ChildNodes)
                {
                    if (!(n is XmlElement c)) continue;
                    if (!Same(c.LocalName, "engineactionlist")) { p.unknown.Add("element " + c.LocalName); continue; }
                    string name = Attr(c, "name");
                    if (name.Length == 0 || p.listByName.ContainsKey(name)) { p.unknown.Add("list name '" + name + "'"); continue; }
                    var d = new ListDef { name = name, repeat = Attr(c, "repeateveryframe").StartsWith("T", StringComparison.OrdinalIgnoreCase) };
                    p.listByName[name] = lists.Count; lists.Add((c, d));
                }
                foreach (var (e, d) in lists) d.actions = p.Actions(e);
                p.lists = new ListDef[lists.Count];
                for (int i = 0; i < lists.Count; i++) p.lists[i] = lists[i].d;
                for (int i = 0; i < p.lists.Length; i++) { p.markers["Run_" + p.lists[i].name] = (ActRun, i); p.markers["Halt_" + p.lists[i].name] = (ActHalt, i); }
                for (int s = 0; s < p.states.Count; s++) p.markers["Goto_" + p.states[s]] = (ActGoto, s);
                return p;
            }

            // the <engineaction> children of an element (a list, testspassedactions, testsfailedactions)
            ActionDef[] Actions(XmlElement parent)
            {
                var l = new List<ActionDef>();
                foreach (XmlNode n in parent.ChildNodes)
                {
                    if (!(n is XmlElement c)) continue;
                    if (!Same(c.LocalName, "engineaction")) { unknown.Add("element " + c.LocalName); continue; }
                    string type = Attr(c, "actiontype").ToLowerInvariant();
                    var a = new ActionDef();
                    switch (type)
                    {
                        case "test":
                            a.type = ActTest;
                            var tests = new List<TestDef>(); a.passed = a.failed = new ActionDef[0];
                            foreach (XmlNode k in c.ChildNodes)
                            {
                                if (!(k is XmlElement ke)) continue;
                                if (Same(ke.LocalName, "tests")) { foreach (XmlNode t in ke.ChildNodes) if (t is XmlElement te) { var d = Test(te); if (d != null) tests.Add(d); } }
                                else if (Same(ke.LocalName, "testspassedactions")) a.passed = Actions(ke);
                                else if (Same(ke.LocalName, "testsfailedactions")) a.failed = Actions(ke);
                                else unknown.Add("element " + ke.LocalName);
                            }
                            a.tests = tests.ToArray();
                            break;
                        case "gotostate": a.type = ActGoto; a.target = State(Attr(c, "statename")); if (a.target < 0) unknown.Add("gotostate without a state"); break;
                        case "runactionlist": case "haltactionlist":
                            a.type = type == "runactionlist" ? ActRun : ActHalt;
                            string ln = Attr(c, "actionlistname");
                            a.target = ListIndex(ln);
                            if (a.target < 0) { unknown.Add(type + " of an unknown list '" + ln + "'"); a.type = ActMessage; a.text = type + " " + ln + " (no such list)"; }
                            break;
                        case "startfoley": case "stopfoley": a.type = ActMessage; a.text = type + " " + Attr(c, "foleyname"); break;
                        case "debugmessage": a.type = ActMessage; a.text = Attr(c, "message"); break;
                        default: a.type = ActMessage; a.text = "actiontype " + type; unknown.Add("actiontype " + type); break;
                    }
                    l.Add(a);
                }
                return l.ToArray();
            }

            TestDef Test(XmlElement t)
            {
                if (!Same(t.LocalName, "test")) { unknown.Add("element " + t.LocalName); return null; }
                var d = new TestDef();
                switch (Attr(t, "testtype").ToLowerInvariant())
                {
                    case "vehicleaudiodata":
                    {
                        d.type = TestData;
                        string m = Attr(t, "datamember").ToLowerInvariant();
                        switch (m)
                        {
                            case "throttle": d.member = Member.Throttle; break;
                            case "speed": d.member = Member.Speed; break;
                            case "statetime": d.member = Member.StateTime; break;
                            case "clutch": d.member = Member.Clutch; break;
                            case "reverseon": d.member = Member.ReverseOn; break;
                            case "reartiresairborne": d.member = Member.RearTiresAirborne; break;
                            case "engineload": d.member = Member.EngineLoad; break;
                            case "numwhoops": d.member = Member.NumWhoops; break;
                            case "verticalspeed": d.member = Member.VerticalSpeed; break;
                            case "justlanded": d.member = Member.JustLanded; break;
                            case "flooded": d.member = Member.Flooded; break;
                            case "localplayer": d.member = Member.LocalPlayer; break;
                            default: unknown.Add("datamember " + m); return null;
                        }
                        if (!Compare(t, d)) return null;
                        break;
                    }
                    case "throttlerangetime": d.type = TestThrottleRange; d.low = F(t, "rangelow"); d.high = F(t, "rangehigh"); d.seconds = F(t, "seconds"); break;
                    case "speedchangetime": d.type = TestSpeedChange; d.seconds = F(t, "seconds"); if (!Compare(t, d)) return null; break;
                    default: unknown.Add("testtype " + Attr(t, "testtype")); return null;
                }
                return d;
            }

            bool Compare(XmlElement t, TestDef d)
            {
                switch (Attr(t, "comparisontype").ToLowerInvariant())
                {
                    case "lt": d.cmp = Cmp.Lt; break; case "le": d.cmp = Cmp.Le; break; case "eq": d.cmp = Cmp.Eq; break;
                    case "ge": d.cmp = Cmp.Ge; break; case "gt": d.cmp = Cmp.Gt; break; case "ne": d.cmp = Cmp.Ne; break;
                    default: unknown.Add("comparisontype " + Attr(t, "comparisontype")); return false;
                }
                d.value = F(t, "comparisonvalue");
                return true;
            }

            float F(XmlElement e, string name)
            {
                string s = Attr(e, name).Trim().TrimEnd('f', 'F');
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
                unknown.Add(name + " '" + s + "'"); return 0f;
            }
            static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            static string Attr(XmlElement e, string name)
            {
                foreach (XmlAttribute a in e.Attributes) if (Same(a.LocalName, name)) return a.Value ?? "";
                return "";
            }
        }

        public readonly Program program;
        public int State { get; private set; } = -1;
        public string StateName => program.StateName(State);
        public float StateTime { get; private set; }
        /// Called on every state entry (also the same state again), after the lists were cleared.
        public Action<int> Entered;
        /// The action lists' messages (debugmessage, startfoley / stopfoley): nothing plays them.
        public Action<string> Message;
        /// False while the host owns the state (the start line, the wreck, the drowned engine): no list is evaluated and markers are ignored.
        public bool ListsEnabled = true;
        /// The host's say on every state entry (a list's gotostate, a Goto_ marker, the host's own): the state actually entered.
        public Func<int, int> Redirect;

        readonly int[] running, snapshot; int runCount; readonly bool[] isRunning;
        bool abort; int depth;
        Telemetry t;
        // the 2 s history of the throttle and the speed (throttlerangetime, speedchangetime)
        const int Ring = 512;
        readonly float[] hTime = new float[Ring], hThrottle = new float[Ring], hSpeed = new float[Ring];
        int hHead = -1, hCount; float clock;
        // numwhoops / justlanded
        float airTime, groundTime; int whoops; bool wasAirborne, justLanded;

        public AliveEngineMachine(Program program)
        {
            this.program = program;
            running = new int[program.lists.Length]; snapshot = new int[program.lists.Length]; isRunning = new bool[program.lists.Length];
        }

        public int NumWhoops => whoops;
        public bool IsRunning(string list) { int i = program.ListIndex(list); return i >= 0 && isRunning[i]; }
        public int RunningCount => runCount;

        /// Enters a state (also the one it is in): the running lists are cleared, statetime restarts, Entered.
        public void GotoState(int s)
        {
            if (Redirect != null) s = Redirect(s);
            if (s < 0 || s >= program.StateCount) return;
            for (int i = 0; i < runCount; i++) isRunning[running[i]] = false;
            runCount = 0;
            State = s; StateTime = 0f; abort = true;
            Entered?.Invoke(s);
        }

        /// A sync point of the playing recording: Run_<list>, Halt_<list>, Goto_<state> (any case); other names are ignored.
        public void Marker(string name)
        {
            if (!ListsEnabled || !program.Marker(name, out int kind, out int index)) return;
            abort = false;
            switch (kind)
            {
                case ActGoto: GotoState(index); break;
                case ActHalt: Halt(index); break;
                case ActRun: if (program.lists[index].repeat) Start(index); else { depth = 0; Exec(program.lists[index].actions); } break;
            }
        }

        /// One game frame: the data members, statetime, then every running list in the order they were started.
        public void Step(in Telemetry tele, float dt)
        {
            if (dt < 0f) dt = 0f;
            t = tele; clock += dt;
            hHead = (hHead + 1) % Ring; if (hCount < Ring) hCount++;
            hTime[hHead] = clock; hThrottle[hHead] = t.throttle; hSpeed[hHead] = t.speed;
            justLanded = wasAirborne && !t.airborne;
            if (t.airborne)
            {
                if (!wasAirborne) airTime = 0f;
                airTime += dt;
            }
            else
            {
                if (justLanded) { if (airTime < WhoopMaxAir) whoops++; else whoops = 0; groundTime = 0f; }
                else { groundTime += dt; if (groundTime > WhoopGround) whoops = 0; }
            }
            wasAirborne = t.airborne;
            StateTime += dt;
            if (!ListsEnabled || runCount == 0) return;
            abort = false;
            int n = runCount;
            Array.Copy(running, snapshot, n);
            for (int i = 0; i < n; i++)
            {
                int l = snapshot[i];
                if (!isRunning[l]) continue;                    // halted earlier this frame
                if (!program.lists[l].repeat) Halt(l);
                depth = 0;
                Exec(program.lists[l].actions);
                if (abort) return;                              // a gotostate: no further actions this frame
            }
        }

        void Start(int l)
        {
            if (isRunning[l]) Halt(l);
            running[runCount++] = l; isRunning[l] = true;
        }
        void Halt(int l)
        {
            if (!isRunning[l]) return;
            isRunning[l] = false;
            int j = 0;
            for (int i = 0; i < runCount; i++) if (running[i] != l) running[j++] = running[i];
            runCount = j;
        }

        void Exec(ActionDef[] actions)
        {
            if (actions == null) return;
            if (++depth > 16) { depth--; return; }                  // a list that runs itself
            for (int i = 0; i < actions.Length && !abort; i++)
            {
                var a = actions[i];
                switch (a.type)
                {
                    case ActTest: Exec(Pass(a.tests) ? a.passed : a.failed); break;
                    case ActGoto: GotoState(a.target); break;
                    case ActHalt: Halt(a.target); break;
                    case ActRun: { var l = program.lists[a.target]; if (l.repeat) Start(a.target); Exec(l.actions); break; }
                    case ActMessage: Message?.Invoke(a.text); break;
                }
            }
            depth--;
        }

        bool Pass(TestDef[] tests)
        {
            for (int i = 0; i < tests.Length; i++) if (!Pass(tests[i])) return false;
            return true;
        }

        bool Pass(TestDef d)
        {
            switch (d.type)
            {
                case TestData: return Compare(Value(d.member), d.cmp, d.value);
                case TestThrottleRange: return ThrottleHeld(d.low, d.high, d.seconds);
                case TestSpeedChange: return Compare(SpeedChange(d.seconds), d.cmp, d.value);
            }
            return false;
        }

        static bool Compare(float a, Cmp c, float b)
        {
            switch (c)
            {
                case Cmp.Lt: return a < b; case Cmp.Le: return a <= b; case Cmp.Eq: return a == b;
                case Cmp.Ge: return a >= b; case Cmp.Gt: return a > b; default: return a != b;
            }
        }

        public float Value(Member m)
        {
            switch (m)
            {
                case Member.Throttle: return t.throttle;
                case Member.Speed: return t.speed;
                case Member.StateTime: return StateTime;
                case Member.Clutch: return t.clutch ? 1f : 0f;
                case Member.ReverseOn: return t.control2 > 0.1f && t.speed < 1f ? 1f : 0f;
                case Member.RearTiresAirborne: return t.rearAirborne ? 1f : 0f;
                case Member.EngineLoad: return t.load;
                case Member.NumWhoops: return whoops;
                case Member.VerticalSpeed: return t.verticalSpeed;
                case Member.JustLanded: return justLanded ? 1f : 0f;
                case Member.Flooded: return t.flooded ? 1f : 0f;
                case Member.LocalPlayer: return t.localPlayer ? 1f : 0f;
            }
            return 0f;
        }

        // the throttle has been in [lo, hi] since at least `seconds` ago (the current frame included)
        bool ThrottleHeld(float lo, float hi, float seconds)
        {
            if (hCount == 0) return false;
            float since = clock;
            for (int k = 0, i = hHead; k < hCount; k++, i = (i + Ring - 1) % Ring)
            {
                float v = hThrottle[i];
                if (v < lo || v > hi) break;
                since = hTime[i];
            }
            return hCount > 0 && hThrottle[hHead] >= lo && hThrottle[hHead] <= hi && clock - since >= seconds - 1e-4f;
        }

        // the speed now minus the speed `seconds` ago (the oldest of the history when it is shorter)
        float SpeedChange(float seconds)
        {
            if (hCount == 0) return 0f;
            float target = clock - seconds, then = hSpeed[hHead];
            for (int k = 0, i = hHead; k < hCount; k++, i = (i + Ring - 1) % Ring)
            {
                then = hSpeed[i];
                if (hTime[i] <= target + 1e-5f) break;
            }
            return hSpeed[hHead] - then;
        }
    }
}
