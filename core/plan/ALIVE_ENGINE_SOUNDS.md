# MX vs ATV Alive engine sounds

Status: **implemented, not yet heard in Unity** (branch `alive-bike-sounds`, from `main`, 2026-11-14). A player request: ride with MX vs ATV
Alive's own engine sounds. Verified outside Unity (see Tests): the import of all 7 sets, their libraries as the game builds them, the emitter
on the converted sets; the Unity assemblies compile. Not run yet: `OriginalAudio.HostSync` / `AddSet` in the player, the routing, the menus,
a listening test. The calibration trims (section 7) are measured offline against Reflex's engines, not by ear.
The user's choices (2026-11-14): **a faithful Alive player** (Alive's own state machine, its own recordings, not an MX Bikes pack conversion), and
**imported Alive bikes use their own Alive engine by default, and every bike can pick an Alive engine in GARAGE > ENGINE SOUND**.

## What Alive ships (research, 2026-11-14)

Alive's engine sound is not rpm-driven. Each engine is an FMOD Designer project (FEV1 **v0x00400000**, Reflex's is v0x003a0000) of 25 events
(group `STATES`), one layer and one sound instance each, over one FSB4 bank of **mono 44.1 kHz XMA** recordings (75-104 per bank: 1-6 s
throttle runs with the gear changes recorded in them, a few loops). Which recording plays is decided by a **state machine** that is data:

* `AudioEngineConfig_<size>.bxml`: `<audio_engine state_group="Bike" startEngineState="Idle" sound="<project>">` and one child per state
  naming its event (`notused` states skipped). 35 states, 32 with an event in the config; in every set five name events that do not exist:
  Choke (STATES/CHOKE), Flooding (STATES/FLOODING), EnterAir (STATES/PANIC_START), BarBangBog1 (STATES/BOGGING2), BarBangBog2
  (STATES/THROTTLE_LOW) (the state then has no sound: the old event is stopped and nothing plays until the next state). Every config's
  StartLine names the REV event.
* `EngineActions*.bxml` (`EngineActions`, `EngineActions_2Stroke`, `EngineActions_50`, `EngineActionsTurbo`): `<engineactionlists>` of named
  `<engineactionlist name=".." repeateveryframe="T|F">` trees of `<engineaction actiontype="test|gotostate|haltactionlist|runactionlist|
  startfoley|stopfoley|debugmessage">`; a test holds `<tests>` (all must pass) and `<testspassedactions>` / `<testsfailedactions>`.
  Test types: `vehicleaudiodata datamember=.. comparisontype=lt|le|eq|ge|gt|ne comparisonvalue=..`, `throttlerangetime rangelow rangehigh
  seconds`, `speedchangetime seconds comparisontype comparisonvalue`.
* **Sync points** inside each FSB sample (`SYNC` after the XMA seek table in the sample header: u32 count, count x {u32 sample, char[256]
  name}): `Run_<list>` starts an action list, `Goto_<State>` switches state, `Halt_<list>` / other names as found. The recordings' markers
  drive the machine: e.g. 450F_Accel_Track04 runs OnThrottleCheckPwr at 0 ms and goes to SustainHigh at 2066 ms.

Seven distinct sets: disc `MX_450_TRACK` (450Fs and 450 ATVs), `MX_250F` (250Fs and 250 ATVs), `MX_125R` (125s, KTM SX150; 2-stroke
actions), `MX_400_TRACK` (MXFR650: reached through `PresetVehicleParts`, its `VehicleModelParts` row is `Default=0`), `ATV_1000cc`
(ATV 1000 Dune, turbo actions); LIVE `MX_50cc` (Stewart Compound, `_50` actions), `MX_250R` (Sliderock MX250 FMX). The brand vehicle packs
ship no engine audio. Every set lives in the EngineAudio part's **material** package (`<db>_<size>_Default`): config, actions,
`<project>.sound` (64-byte name, u32 bank count, 64-byte bank names, u32 FEV size, FEV), `<bank>.soundbnk` (64-byte name, u32 size, FSB4).
Full research notes: the research reports of 2026-11-14 (not committed; the facts above are what the code relies on).

## Architecture

The faithful path re-uses Open Reflex's port of the original's FMOD player (`OriginalAudio`: events, layers, envelopes, AIFX, 3D, the
replay's command ring) and adds an Alive emitter beside Reflex's (`OriginalEngineAudio`):

```
import (once, in GAME LIBRARY > MX vs ATV ALIVE)                        race
  disc/LIVE: config bxml, actions bxml, .sound, .soundbnk                 AliveEngineSets: the installed sets
  XMA samples -> RIFF/XMA2 -> client ffmpeg -> PCM16                      ReflexAudioSet.BuildAliveSet -> OriginalAudio.AddSet
  -> <install>/AliveEngineSounds/<PROJECT>/                              AliveEngineAudio: per rider, Alive's state machine,
       <PROJECT>.fev   (the FEV as shipped)                                 OriginalAudio.Host commands, HostSync markers
       <PROJECT>.fsb   (FSB4, PCM16 LE mono, SYNC kept)                   OriginalVehicleSound: which rider plays what
       AudioEngineConfig.xml, EngineActions.xml (the bxml as XML)
       set.json        (what it is, which Alive vehicles use it)
     <install>/AliveEngineSounds/<PROJECT>.import.json (files + sha256)
```

### 1. Formats (`Content/ReflexFmod.cs`)

* `ParseFev` reads v0x00400000 as well as v0x003a0000 (little-endian both). v0x40 differences: after the version, u32 a, u32 b, then
  **u32 n + n x (u32 id, u32 count)**; the project name; wave banks are **u32 flags, u32 maxStreams, u8[8] bank hash, string name**;
  sound-def blocks are **70 bytes** (v0x3a's 15 dwords + 10 bytes); the tail after the sound defs is u32 0 (reverbs) and a `comp` chunk
  (`u32 size 'comp' { u32 size 'sett' f32 f32 }`): read the reverbs as v0x3a does and ignore the rest. Events, groups, categories,
  instances, envelopes and sound defs are the v0x3a layout. The research parser `fev40.py` (scratch) parsed all 9 Alive projects to the end.
* `ParseFsb` keeps reading Reflex's banks unchanged and additionally reads, when a sample header is longer than the fixed part and contains
  a `SYNC` block, its sync points into `Sample.syncs` (`List<(int sample, string name)>`, empty otherwise). The XMA seek block (+0x5C: u32
  size, u32 1, u32 n, (n-1) x u32) precedes `SYNC` in Alive's headers; search for `SYNC` inside the header's extra bytes.

### 2. Shared Alive audio helpers (`Content/AliveImport/AliveXma.cs`, new)

No dependency on the Untamed code (the `untamed-soundtracks` branch's `AliveMusic.cs` can switch to these after both merge):

```csharp
public sealed class FsbSample { public int index; public string name; public long samples; public int offset, length; public long loopStart, loopEnd;
    public uint mode; public int freq, channels; public byte[] header; public List<(long sample, string name)> syncs; public bool Xma => (mode & 0x01000000) != 0; }
public static class AliveFsb   { public static List<FsbSample> Read(byte[] bank); }           // FSB4 found in the first 0x400 bytes; every sample
public static class AliveXma   { public static byte[] Wave(byte[] bank, int offset, int length, long samples, int channels, int rate); }   // RIFF/XMA2
public static class AliveFfmpeg
{
    public static (int code, string output, string errors) Run(string ffmpeg, IList<string> args, CancellationToken cancel, int timeoutMs);
    public static bool HasDecoder(string ffmpeg, string name);                                  // "xma2"
    public static short[] XmaToPcm(string ffmpeg, byte[] riffXma, long samples, string scratchDir, CancellationToken cancel);   // s16le mono, exactly `samples` long
}
```

The RIFF/XMA2 wrapper: format 0x166, channels, rate, avgBytes rate*2*ch, block align 2048, 16 bits, cbSize 34, NumStreams 1, channel mask
(4 mono, 3 stereo), **SamplesEncoded = PlayLength = the FSB sample count** (a wrapper without it adds a gap of padding at every loop),
BytesPerBlock 0x8000, PlayBegin 0, loop 0, EncoderVersion 4, BlockCount. ffmpeg: `-nostdin -hide_banner -v error -y -i x.xma.wav -f wav
-acodec pcm_s16le -ac 1 -bitexact out.pcm.wav`, the PCM read from its `data` chunk (not `-f s16le`: the raw muxer prints "non monotonically
increasing dts" for some recordings of MX_450_TRACK, MX_400_TRACK, MX_250R and ATV_1000cc, with the same samples); fail on a non-zero exit
or any stderr line; the PCM is cut or zero-padded to `samples` (the decode was measured exact for all 638 samples). One process per sample
(~100 per set, ~3-5 s per set); 30 s timeout per sample.

### 3. The importer (`Content/AliveImport/AliveEngineSounds.cs`, new)

* `AliveEngineSoundSource { string project, db, pkg, actions; List<int> aliveIds; List<string> vehicles; string source; bool installed; string problem; }`
  found in `AliveImporter.Scan` after the bikes: for every Alive vehicle (bikes and ATVs alike, including those `AliveBikes.Find` lists as
  not importable) its `EngineAudio` part (VehicleModelParts `Default == 1`, else the model's preset parts, `PresetVehicleParts` via
  `PresetVehicle.VehicleModelId`), `materials` (db, pkg) -> read the package, its config's `sound=` names the project. One source per
  project (the first package that holds it; ATV packages are byte-identical copies). Stored in a new `AliveSources.engineSounds` list.
* `Convert(src, sources, folder, ffmpeg, cancel)`: the FEV out of the `.sound` wrapper as is; the FSB rebuilt as FSB4 with every sample PCM16
  LE mono 44100 (`mode` = M_16BITS | M_MONO | the loop bits of the original, loop points kept; Alive's 0x80000000 flag dropped), each
  sample header = the fixed 80-byte part + the original `SYNC` block (no XMA seek block; +72, where Alive repeats the header size, rewritten
  to the new size); the 48-byte bank header copied (bank hash kept), header mode 0; the two bxml files as indented XML text (AliveBxml's
  element tree; typed values without their `_type:` prefix, no `_valuetype`); `set.json` (`{"format":1, "project", "name" (450F, 250F, 125,
  650, 50cc, 250R, ATV 1000), "actions" (the actions file's stem), "vehicles":[{alive_id, name}], "source", "trim"}`; `"trim"` from the
  importer's table `AliveEngineSounds.Trims` (section 7), left out for a project the table does not list). The output
  is deterministic (same disc -> same bytes). Signature: `Convert(src, sources, folder, ffmpeg, cancel, Action<int, int> step = null)`;
  ffmpeg works in `<folder>-xma`, deleted afterwards.
* `Import(sets, sources, content, ffmpeg, Action<AliveProgress> progress, cancel)`: the AliveBikes install rules (stage
  `<content>/AliveEngineSounds/.ae-<8hex>`, a folder without its `<PROJECT>.import.json` is never replaced, manifest with every file's
  sha256, links refused). Progress: `level` = the project, `stage` "Decoding" / "Installing", `done` / `total` = recordings.
* The source also carries `name, config, bank, samples, seconds, Bytes, Importable, Label`; `installed` is set by the UI (and by `Import`).
  `Find` reads the `MXTables_*` tables itself (AliveBikes' reader is private).
* UI (`OpenFrontEnd/OpenShell.AliveEngineSounds.cs`, new partial; one-line calls from `OpenShell.AliveImport.cs`): an
  "ALIVE ENGINE SOUNDS" section after ALIVE BIKES, one row per set (`_aliveChosen` key `"sound:" + project`, ticked by default when not
  installed), blocked rows when ffmpeg (`FfmpegBinkSource.Locate()`) is missing or lacks `xma2`; progress, results rows, `AliveEngineSets.Refresh()`.

### 4. The installed sets at run time (`Content/AliveEngineSets.cs`, new)

* `AliveEngineSet { string id /* "alive:<PROJECT>" */, project, name, folder, actions, source; int[] aliveIds; string[] vehicles; float trim;
  string Title /* "ALIVE 450F" */ }` (+ `FevPath, FsbPath, ConfigPath, ActionsPath`); listed only when set.json and the four files are there.
* `AliveEngineSets.All` (cached, `Refresh()`, `Version`), `Find(id)`, `ForAliveVehicle(int aliveId)`, `Folder` (=
  `<ReflexInstall.Dir>/AliveEngineSounds`), `Scan(folder, problems)`, `Read(dir)`. The two XML files have one reader, the emitter's
  `AliveEngineMachine.Program.Parse` (section 5), which compiles them into indexed arrays.
* `ReflexAudioSet.BuildAliveSet(AliveEngineSet s)` (in `ReflexAudio.cs`): the library of one set from its folder: the FEV (project renamed
  `ALIVE_<PROJECT>` so it can never meet a Reflex project), the FSB via `ParseFsb`, events through the existing `BuildEvent`, wave keys
  `alive/<PROJECT>/<stem>.wav` (codec pcm, never probed: `CodecOf` reads some PCM as MPEG), plus the sync points per wave key (`syncs`).
  The FSB cuts sample names to 29 characters: samples sharing a stem get `<stem>~<index>.wav` (MX_50cc 7 and 8).
* `OriginalAudio.AddSet(Func<ReflexAudioSet> build, string key)`: merges a library built off the main thread like `AddProjects` (the gate,
  the clip loading), and keeps the wave keys' sync points. `OriginalAudio.HostSync` (`event Action<uint wrapper, uint serial, string name>`):
  for a host wrapper's playing voices whose clip has sync points, raised (main thread, inside `Tick`) for every marker the voice's
  `timeSamples` passed since the last tick (a voice that started this tick: from 0; a looping clip that wrapped: the markers to the end, then
  from the start; a voice that ended: the markers to its end, raised after its EVENT_FINISHED in the same tick). Paused voices are skipped;
  the markers are queued and raised after the instance loop. `SetLoaded(key)` and `HasEvent(path)` serve the emitter. Nothing changes for
  instances without sync points.

### 5. The Alive emitter (`OriginalWorld/AliveEngineAudio.cs`, new)

One emitter per rider who plays an Alive set (two wrappers per rider from `OriginalAudio.HostWrapperBase + 0x800`, used in turn so the old
event's fade-out is heard; the serial goes up on every state entry), fed every game frame with the core's `RxVehicleAudioRider` like
`OriginalEngineAudio` (`Frame(state, dt, Func<RxVehicleAudioRider, AliveEngineSet> setOf, fieldTrim)`, `ReportFinished` as Reflex's):

* **States**: the config's state -> event path (`alive_<project>/states/<event>` lower case). `startEngineState` first. Entering a state:
  stop the old event with its fade-out (sub 1, not 12: the sustain clips hand over ~100 ms before their end), play the new one (sub 0),
  `AIFX` = 0 for the local human else the core's audio slot (as Reflex), `LOAD` = the rider's load; a state with no event: stop only.
  `statetime` restarts. Re-entering the same state (a `Goto_<own state>` marker) plays the event again (the next random variant).
* **Action lists**: `Run_<list>` starts a list (each list at most once at a time; already running: restarted), `repeateveryframe="T"`
  lists are evaluated every game frame until halted (`haltactionlist`, `Halt_<list>` marker) or the state changes (a state change clears
  every running list); `F` lists run once. Evaluation is depth-first in document order; a `gotostate` switches state and ends the frame's
  evaluation of every list (no further actions). `runactionlist` / `haltactionlist` act at once. `startfoley` / `stopfoley` /
  `debugmessage`: ignored (logged in debug builds).
* **Markers**: `HostSync` for the emitter's wrapper and serial: `Run_X`, `Goto_X`, `Halt_X` as above, case-insensitive; unknown names ignored.
* **Data members** (from the telemetry; Alive's units are feet and seconds like Reflex's core): `throttle`, `speed` (ft/s), `statetime`
  (s in the state), `clutch` (0/1), `reverseon` (control2 > 0.1 and speed < 1: 0/1), `reartiresairborne` (a bike's `wheelAir[0]`,
  contact group 0 = sim + 0x8894, the rear (`bike_sim.c`, `sound_state.c`); ATV: `wheelAir[0] && wheelAir[1]`, rear left / right),
  `engineload` (`load`), `numwhoops` (consecutive short airborne hops < 0.4 s within 1.5 s of each other, reset on a 1.5 s ground stretch),
  `verticalspeed` (vy, ft/s), `justlanded` (1 for the first frame after airborne), `flooded` (`drowned`), `localplayer` (local human).
  `throttlerangetime lo hi s`: the throttle has stayed within [lo, hi] for at least s. `speedchangetime s cmp v`: speed now minus speed s
  seconds ago (a 2 s history ring) compared with v.
* **Reflex's own events around it**: wrecked -> `Wreck`; underwater occlusion as Reflex (0.6); the start line (`RxVehicleAudioRider
  .StartLine` / cues) -> `StartLine` and `Rev` states with the core's rev serial (each rev one REV event; StartLine plays the start state's
  event, the idle, because every config's StartLine names REV), back to the start state when the start line ends; an older core without the
  start-line export: StartLine holds the idle (no own revs). The event volume (sub 2) is the set's trim, x the field trim
  (`OriginalEngineAudio.FieldTrim`) for non-local riders.
* **The rev limiter in the air** (an addition): off the ground with the throttle held, Alive's lists enter `PanicStartInAir` and loop
  `PanicLoopInAir`, recordings of throttle blips that drop ~10 dB within a second (measured on every set); Alive plays the flat limiter
  recordings (`PanicStartInAir2nd` -> `PanicLoopInAir2nd`, a 0.2-1 s loop at full level) only when the throttle comes back on in the air.
  In Reflex (long flights, whoops, the gas held through them) the blips sound like the engine fading out, so every state entry passes
  `AliveEngineMachine.Redirect`: with throttle >= 0.5 (`AliveEngineAudio.LimiterThrottle`) `PanicStartInAir` / `PanicLoopInAir` become their
  `*2nd` states (when the set has their events). Their own lists carry on: `Airborne2ThrottleCheck` back to the throttle states on landing,
  `OffThrottlePanic` / the start's `OffThrottle*` lists to a decel when the throttle is released in the air; below 0.5 Alive's blips play.
* A recording that ends with no marker and no state change returns to the start state (not in Wreck, Choke or the start line). A one-shot
  stopped with sub 1 and no fade-out plays to its end (OriginalAudio), so a decel recording can overlap the next state.
* 3D range: the Alive events' 40 / 10000 is Alive's own world scale; posted with every event as Reflex's engine range (sub 6: 1 / 300 ft),
  as is the event volume (sub 2), so the replay's instance of each recorded event has both. Two wrappers per emitter; a removed emitter's
  pair (a culled rider, a set change) is reused by the next one, serials are unique across emitters.

### 6. Who plays what (`OriginalVehicleSound`, `EngineSoundPacks`, `EngineSoundPicker`)

* A choice is a string: `""` Original, `"mod:..."` / `"builtin:..."` an MX Bikes pack, **`"alive:<PROJECT>"`** an Alive set.
  `EngineSoundPacks.Choices()` lists the Alive sets after the packs (titles "ALIVE 450F" ...); `PackFor` stays MX Bikes only; new
  `EngineSoundPacks.AliveSetFor(int vehicleModelId)` resolves the Alive choice.
* **Default for Alive bikes**: resolved in this order: (1) while a bike mod is active on the model (`EngineSoundPacks.ActiveMod`: the TEST
  RIDE, else `BikeMods.ChoiceFor`), its own stored choice `EngineSound.Mod.<modId>`; (2) an imported Alive bike (`AliveBikes/alive-<id>-...`):
  its own set (`AliveEngineSets.ForAliveVehicle(id)`); (3) the model's `EngineSound.Model.<id>`; (4) `EngineSound.Default`, else Original.
  So a model-wide choice made before an Alive bike was imported does not override the bike's own set. `SetChoice` stores per mod while a
  mod is active, else per model; `ClearChoice` removes it. Cached per model (save keys, `BikeMods.Version`, `AliveEngineSets.Version`, the
  TEST RIDE, `Refresh`). The picker shows "BIKE'S OWN · ALIVE 450F" for that default; its spinner's first entry "BIKE'S OWN" clears the
  choice.
* `OriginalVehicleSound`: per rider: an MX Bikes pack -> `EnginePackPlayer`; an Alive set -> `AliveEngineAudio` (the set is added to
  `OriginalAudio` once per race, before the start); else Reflex's `OriginalEngineAudio`. `Silenced` keeps each rider in exactly one.
  Local rider only, like packs (`PacksForComputerRiders` covers both). The set is added by `OriginalVehicleSound.PreloadAlive` from
  `OriginalAudio.UseRoster` (key `set.id`); a set whose XML cannot be read, or whose library fails to build (`OriginalAudio.SetFailed`),
  leaves that rider on the original engine.
* Replays: the emitter's commands go through `OriginalAudio.Host` (recorded by `ReflexReplay.Audio`, wrappers below `ReplayWrapperBase`) into
  the same OriginalAudio, whose library still holds the set; `HostSync` is not raised for replay wrappers (the recorded commands already
  hold every state change).

### 7. Level

The Alive events are mixed by their own levels (event volume 1, Reflex's engine events 0.797) and six of the seven sets are louder than the
Reflex engine of their class (the 50cc is quieter), so each set has a calibration trim: `set.json` `"trim"`, written by the importer from its table `AliveEngineSounds.Trims` (keyed by
project), read by `AliveEngineSets.Read` (a positive number, else 1: a set installed before the calibration plays at 1 until it is imported
again) and posted by `AliveEngineAudio.PostVolume` as the event volume (sub 2) of every event: the local rider's at the trim, a computer
rider's at the trim x the field trim. The trim puts the set at the A-weighted level of the Reflex engine of its class.

Measured 2026-11-14 (`tests/alive_engine/level`: `Dump.cs` builds both libraries through the game's own code, `ReflexAudioSet.BuildProjects`
on the install's VehicleEngineAudio projects and `AliveEngineSets.Scan` / `BuildAliveSet` on the importer's output, and writes every wave;
`level.py` measures):

* What a state sounds like to the local rider: the state's event (Reflex: the emitter's `AudioEngineStates` table; Alive: the config as
  `AliveEngineMachine.Program` reads it) at AIFX 0, with `OriginalAudio.Tick`'s voice gain: event volume x (1 - U(0, volumeRand)) x the
  instance gain x the range fades x the layer's volume envelopes x sound-def volume x U(volumeRandMin, 1), as mean squares; the pitch
  (octaves, pitch envelopes) shifts the spectrum and the low-pass envelopes apply as a one pole. At AIFX 0 the envelopes of both games give
  volume 1 (0.998 in a few), pitch 1 and a low-pass at 20 kHz (Reflex) or 22 kHz (Alive). The Engine channel gain is the same for both
  and left out.
* A state's level: the sound definition's waves by pick weight, each counted for its length (a random one-shot is heard for its length),
  each wave's A-weighted mean square from its power spectrum (the method of `tests/engine_audio/offline/loudness.py`; Reflex's MPEG waves
  decoded by ffmpeg).
* The ride: SustainPower 30 %, SustainHigh 30 % (full throttle), SustainMed, SustainLow, SustainHalf 8.3 % each (part throttle), Idle 7.5 %,
  DecelHigh 7.5 %; the same state names on both sides. A state whose event plays no wave is left out and the others' shares grow: the
  2-stroke Alive sets' HALF_SUSTAINED has no entry and their actions never enter SustainHalf (no `gotostate` or `Goto_` marker names it).
* trim = Reflex level / Alive level, rounded to 0.01, clamped to 0.25 .. 4.

| Alive set | A rms | Reflex class | A rms | Alive vs Reflex | trim | after the trim |
|---|---|---|---|---|---|---|
| MX_450_TRACK (450F) | 0.2338 | MX_450F_01 | 0.2043 | +1.17 dB | 0.87 | -0.04 dB |
| MX_250F (250F) | 0.2946 | MX_250F_01 | 0.1339 | +6.85 dB | 0.45 | -0.09 dB |
| MX_125R (125) | 0.2916 | MX_125R_01 | 0.2087 | +2.91 dB | 0.72 | +0.05 dB |
| MX_250R (250R) | 0.2637 | MX_250R_TEST_05 | 0.1883 | +2.93 dB | 0.71 | -0.05 dB |
| MX_400_TRACK (650) | 0.2118 | MX_450F_01 | 0.2043 | +0.31 dB | 0.96 | -0.04 dB |
| ATV_1000cc (ATV 1000) | 0.2596 | MX_450F_01 | 0.2043 | +2.08 dB | 0.79 | +0.04 dB |
| MX_50cc (50cc) | 0.1538 | MX_125R_01 | 0.2087 | -2.65 dB | 1.36 | +0.02 dB |

(rms of full scale 1, the Engine channel at 1.) The Reflex levels agree with the original emitter's on a recorded race
(`tests/engine_audio/offline/loudness.py` `target_for`: 450F 0.2136, 250F 0.1389, 250 two-stroke 0.1772; here 0.2043, 0.1339, 0.1883), so
the static ride stands in for the race within ~0.5 dB. Other rides move the trims little: full throttle only (Power and High 50/50) changes
them by -0.08 .. -0.75 dB, a softer ride (40 % full, 40 % part, 20 % idle / decel) by +0.34 .. +0.74 dB. The levels after the trims were
measured again on a fresh import that carries them (+-0.1 dB). Not measured: the start line (REV), the air / clutch / landing states, a
listening test.

## Tests

* `tests/alive_engine/` (new): the importer outside Unity (Program.cs + build/run scripts like tests/alive_bikes): find the sets, convert
  all 7, check every FEV parses (v0x40), every rebuilt FSB parses with `ReflexFmod.ParseFsb`, sample counts / sync counts equal the
  disc's, PCM non-silent, deterministic bytes (two runs), the manifest. Disc + Stewart Compound + Sliderock (2026-11-14): 7 sets, 2914
  sync points, ~27 s per conversion, ALIVE ENGINE PASS.
* `tests/alive_engine/emitter/`: the emitter's state machine (AliveEngineAudio.cs with a fake OriginalAudio) on synthetic telemetry traces
  (idle -> full throttle -> shift markers -> off throttle -> air -> landing -> clutch -> whoops -> wreck -> drowned -> start line -> computer
  rider) against expected state sequences, from the extracted bxml or (`--installed <content>`) from the importer's output: ALL PASSED on
  all 7 sets both ways, with the same state sequences.
* The libraries as the game builds them (2026-11-14, a scratch harness on the compiled Assembly-CSharp: `AliveEngineSets.Scan` ->
  `BuildAliveSet` on the importer's output): every wave entry of every set resolves, every sample of every FSB is played by its own key,
  every state event exists but the five above, every `Run_` / `Halt_` marker and `runactionlist` / `haltactionlist` names a list, every
  `Goto_` marker and `gotostate` names a config state.
* `tests/alive_engine/level/` (section 7): `build.ps1 <scratch> -Asm <the compiled assemblies>`, then `AliveLevel.dll --game <Reflex game
  files> --content <folder holding AliveEngineSounds> --out <dir> [--rxcore <rxcore.dll>]` (the core library for a packed game library) and
  `python -I level.py <dir> [--ffmpeg F] [--json F]`: the levels, the trims, and how far each installed set's trim leaves it from its class.
  The importer check verifies set.json's `"trim"` against the table; the emitter test (`--installed`) reads set.json's trim and checks the
  local rider's event volume is it (1 for a set without one).
* Compile checks: the Unity assemblies (Windows, editor csc).
