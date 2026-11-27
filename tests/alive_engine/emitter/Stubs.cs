// tests/alive_engine/emitter: what AliveEngineAudio.cs needs from Unity's side, outside Unity.
//  - OriginalAudio: a fake event player with OriginalAudio's host surface (Host, HostFinished, HostSync, AddSet, SetLoaded, SetFailed, HasEvent). It
//    plays the set's real events: each play takes the next recording of the event's sound definition (round robin), advances it by the
//    frame time at 44.1 kHz from the tick after the play (as OriginalAudio's voice starts in the Tick after the command), raises the
//    recording's sync points it passed (a loop wraps) and EVENT_FINISHED when a one-shot ends, after the tick's voice loop.
//  - AliveEngineSet / ReflexAudioSet: the members AliveEngineAudio uses.
using System;
using System.Collections.Generic;
using Reflex.Port.Content;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed class AliveEngineSet
    {
        public string id, project, name, folder; public int[] aliveIds = new int[0]; public float trim = 1f;
        public bool unreadable;   // (test: its library fails to build, as an .fsb / .fev the player cannot read)
        public string ConfigPath => System.IO.Path.Combine(folder, "AudioEngineConfig.xml");
        public string ActionsPath => System.IO.Path.Combine(folder, "EngineActions.xml");
    }
    public sealed class ReflexAudioSet { public static ReflexAudioSet BuildAliveSet(AliveEngineSet s) => s.unreadable ? throw new System.IO.InvalidDataException("unreadable") : new ReflexAudioSet(); }
}

namespace Reflex.Port.OriginalWorld
{
    // UnityEngine.Debug calls into the engine (not loaded here): AliveEngineAudio's logs go to the console (this class is found before the
    // using directive's).
    public static class Debug
    {
        public static readonly System.Collections.Generic.List<string> warnings = new System.Collections.Generic.List<string>();
        public static void Log(object m) => System.Console.WriteLine("    log " + m);
        public static void LogWarning(object m) { warnings.Add(m?.ToString() ?? ""); System.Console.WriteLine("    warning " + m); }
    }

    public sealed class OriginalAudio
    {
        public const uint HostWrapperBase = 0xF0000000u, ReplayWrapperBase = 0xFE000000u;
        public const int Rate = 44100;
        public event Action<uint, uint> HostFinished;
        public event Action<uint, uint, string> HostSync;
        public Vector3 Listener;
        public static implicit operator bool(OriginalAudio a) => a != null;

        public sealed class Recording { public string name; public int length; public bool loop; public List<(int sample, string name)> syncs = new List<(int, string)>(); }
        public sealed class Event { public string path; public List<Recording> recordings = new List<Recording>(); public int next; }
        public readonly Dictionary<string, Event> events = new Dictionary<string, Event>(StringComparer.OrdinalIgnoreCase);

        sealed class Voice { public uint wrapper, serial; public Event ev; public Recording rec; public double pos; public bool fresh; }
        readonly Dictionary<uint, Voice> voices = new Dictionary<uint, Voice>();
        readonly HashSet<string> sets = new HashSet<string>(StringComparer.Ordinal);
        readonly List<(uint, uint, string)> syncOut = new List<(uint, uint, string)>();
        readonly List<(uint, uint)> finishOut = new List<(uint, uint)>();
        readonly List<Voice> scratch = new List<Voice>();

        public int AddSetCalls, Commands, UnknownPlays, Starts, RangePosts, VolumePosts;
        public readonly Dictionary<uint, float> aifx = new Dictionary<uint, float>(), volume = new Dictionary<uint, float>(), occlusion = new Dictionary<uint, float>();
        public readonly Dictionary<uint, (float min, float max)> range = new Dictionary<uint, (float, float)>();
        public readonly List<string> plays = new List<string>();   // "wrapper serial path recording"

        public void Clear() { voices.Clear(); plays.Clear(); }
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);
        public void AddSet(Func<ReflexAudioSet> build, string key) { AddSetCalls++; if (!sets.Add(key)) return; try { build(); } catch (Exception) { failed.Add(key); } }
        public bool SetLoaded(string key) => key != null && sets.Contains(key) && !failed.Contains(key);
        public bool SetFailed(string key) => key != null && failed.Contains(key);
        public bool HasEvent(string path) => !string.IsNullOrEmpty(path) && events.ContainsKey(path);
        public string RecordingOf(uint wrapper) => voices.TryGetValue(wrapper, out var v) ? v.rec.name : "";

        public void Host(int sub, uint wrapper, uint serial, string path, string param, float value, Vector3 position, bool hasPosition)
        {
            Commands++;
            switch (sub)
            {
                case 0:
                    Starts++;
                    if (!events.TryGetValue(path ?? "", out var ev) || ev.recordings.Count == 0) { UnknownPlays++; voices.Remove(wrapper); HostFinished?.Invoke(wrapper, serial); return; }
                    var rec = ev.recordings[ev.next++ % ev.recordings.Count];
                    voices[wrapper] = new Voice { wrapper = wrapper, serial = serial, ev = ev, rec = rec, fresh = true };
                    plays.Add(wrapper.ToString("x") + " " + serial + " " + path + " " + rec.name);
                    break;
                case 1: case 12: voices.Remove(wrapper); break;
                case 2: VolumePosts++; volume[wrapper] = value; break;
                case 4: if (string.Equals(param, "AIFX", StringComparison.OrdinalIgnoreCase)) aifx[wrapper] = value; break;
                case 6: RangePosts++; range[wrapper] = (value, position.x); break;
                case 10: occlusion[wrapper] = value; break;
            }
        }

        public void Tick(float dt)
        {
            scratch.Clear(); scratch.AddRange(voices.Values);
            foreach (var v in scratch)
            {
                if (v.fresh) { v.fresh = false; continue; }
                int len = v.rec.length;
                double from = v.pos, to = from + dt * (double)Rate;
                if (!v.rec.loop && to >= len)
                {
                    foreach (var s in v.rec.syncs) if (s.sample >= from) syncOut.Add((v.wrapper, v.serial, s.name));
                    finishOut.Add((v.wrapper, v.serial)); voices.Remove(v.wrapper); continue;
                }
                if (to < len) { foreach (var s in v.rec.syncs) if (s.sample >= from && s.sample < to) syncOut.Add((v.wrapper, v.serial, s.name)); v.pos = to; }
                else
                {
                    to -= len;
                    foreach (var s in v.rec.syncs) if (s.sample >= from) syncOut.Add((v.wrapper, v.serial, s.name));
                    foreach (var s in v.rec.syncs) if (s.sample < to) syncOut.Add((v.wrapper, v.serial, s.name));
                    v.pos = to;
                }
            }
            foreach (var (w, s) in finishOut) HostFinished?.Invoke(w, s);
            foreach (var (w, s, n) in syncOut) HostSync?.Invoke(w, s, n);
            finishOut.Clear(); syncOut.Clear();
        }
    }
}
