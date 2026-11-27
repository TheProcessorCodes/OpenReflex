// B.R.A.A.P. -- Build Riders: AI, Appearance & Plates (after Open Unleashed's MxaBraap, 2026-11-16): the player's own race AI. A roster is a
// list of AI riders, each with a name, a jersey name, a number, a gear look and a bike graphics kit, and whether he lines up at all.
//   DEFAULT          the game's own computer riders (the AIRider table), read-only: races are exactly the original's while it is in use
//   season rosters   built-in, read-only (ReflexBraap.Seasons.cs): the real fields of a season's series, in standings order
//   the player's     copies of those, or rosters made from nothing, edited on the B.R.A.A.P. screen (OpenFrontEnd/OpenShell.Braap.cs)
// The roster IN USE renames and redresses the computer riders of offline races (ReflexRoster.Braap.cs: the core still runs its own AIRider
// rows on their own bikes: only what Unity draws and names changes, as a gear / kit pack only changes the drawing). Never the career (its
// standings name the game's riders) and never an online session.
// A rider's look is a list of picks, one per gear type: a stock RiderGearId, or the token of a gear pack piece (KitPacks "g:<hash>:<row>") or of
// a "gear" .rxmod piece (RxGear "<mod>/<piece>"); the tokens stay valid from run to run and from player to player. No picks = AUTO: the gear
// set of one of the game's riders, picked by the rider's name (the same every race). A pick this game does not have (a friend's pack) leaves
// AUTO's piece of that type. The bike graphics are kit pack tokens ("k:..."): the ones that fit the bike the core gave the rider are drawn.
// Storage: the client save ("Braap.Rosters", JSON), so save backups cover it and sandboxed test runs never touch the player's.
// Sharing: a share code on the clipboard (RXBRAAP1:<base64 of the gzipped JSON>) or a .braap file (pretty JSON) in <save>/Braap. Open
// Unleashed's codes (BRAAP1:) and .braap files read too (names, numbers and who lines up; its gear and bikes are another game's: AUTO),
// and Open Unleashed reads these the same way.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Reflex.Port.Options;
using UnityEngine;

namespace Reflex.Port.Content
{
    [Serializable]
    public class BraapPick
    {
        public int type;            // RiderGearTypeId
        public int id = -1;         // a stock RiderGearId (-1 with a token)
        public string token = "";   // a gear pack piece / "gear" .rxmod piece
        public BraapPick Clone() => (BraapPick)MemberwiseClone();
    }

    [Serializable]
    public class BraapRider
    {
        public string name = "";
        public string plate = "";                        // the jersey / plate name ("" = the name's family name)
        public int number = 100;
        public bool enabled = true;                      // lines up in the field
        public int skill;                                // 0: the event's AI OPPONENTS setting; 1 Rookie, 2 Amateur, 3 Pro, 4 All Time (ReflexBraap.SkillNames)
        public string team = "";                         // a note the cards show (team, brand)
        public string lookName = "";                     // what the look was picked from (the cards show it)
        public List<BraapPick> look = new List<BraapPick>();   // empty = AUTO
        public string kitName = "";
        public List<string> kits = new List<string>();   // bike graphics: kit pack tokens ("k:..."); empty = the bike as the game dressed it

        public BraapRider Clone()
        {
            var c = (BraapRider)MemberwiseClone();
            c.look = (look ?? new List<BraapPick>()).Select(p => p.Clone()).ToList();
            c.kits = new List<string>(kits ?? new List<string>());
            return c;
        }
    }

    [Serializable]
    public class BraapRoster
    {
        public string id = "";
        public string name = "";
        public string author = "";
        public string note = "";
        public int draw;                                 // 0: a random draw from the riders in the field; 1: from the top of the list
        public List<BraapRider> riders = new List<BraapRider>();

        public bool IsDefault => id == ReflexBraap.DefaultId;
        public bool IsSeason => id != null && id.StartsWith(ReflexBraap.SeasonPrefix, StringComparison.Ordinal);
        public bool BuiltIn => IsDefault || IsSeason;
        public int EnabledCount => riders.Count(r => r.enabled);
        public BraapRoster Clone()
        {
            var c = (BraapRoster)MemberwiseClone();
            c.riders = riders.Select(r => r.Clone()).ToList();
            return c;
        }
    }

    public static partial class ReflexBraap
    {
        public const string DefaultId = "default", SeasonPrefix = "season:";
        public const string ShareTag = "RXBRAAP1:", AnyShareTag = "BRAAP1:";   // (Open Unleashed's tag is the end of this game's: one search finds both)
        public const int MaxNameLength = 24, MaxPlateLength = ReflexPlateRaster.MaxName, MaxRiders = 400, MaxRosters = 64, MaxPicks = 12, MaxKits = 96;
        public const string SaveKey = "Braap.Rosters";

        [Serializable] class Store { public int version = 1; public string active = ""; public List<BraapRoster> rosters = new List<BraapRoster>(); }

        static Store s_store;
        static BraapRoster s_default;
        /// Bumped on every change (screens follow).
        public static int Revision { get; private set; }

        static Store Data
        {
            get
            {
                if (s_store != null) return s_store;
                string json = ReflexSave.GetString(SaveKey, "");
                try { s_store = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Store>(json); }
                catch (Exception e) { Debug.LogWarning("[Braap] the saved rosters do not read (" + e.Message + "): starting empty"); }
                s_store ??= new Store();
                s_store.rosters ??= new List<BraapRoster>();
                s_store.rosters.RemoveAll(r => r == null);
                foreach (var r in s_store.rosters) Sanitize(r);
                s_store.rosters.RemoveAll(r => string.IsNullOrEmpty(r.id) || r.BuiltIn);
                return s_store;
            }
        }

        static void Write()
        {
            Revision++;
            ReflexSave.SetString(SaveKey, JsonUtility.ToJson(Data));
            ReflexSave.Save();
        }

        /// Forgets what was read (tests: the save was replaced).
        public static void Reload() { s_store = null; s_default = null; Revision++; }

        /// The player's rosters, in the order they were made.
        public static List<BraapRoster> Rosters => Data.rosters;

        /// Every roster: DEFAULT, the season rosters, then the player's.
        public static List<BraapRoster> All()
        {
            var l = new List<BraapRoster> { Default };
            l.AddRange(Seasons);
            l.AddRange(Data.rosters);
            return l;
        }

        // ------------------------------------------------------------------------------------------------ the game's tables

        static List<Dictionary<string, object>> Rows(string table)
        {
            try { return ReflexEvents.Tables.TryGetValue(table, out var r) ? r : new List<Dictionary<string, object>>(); }
            catch (Exception) { return new List<Dictionary<string, object>>(); }   // (no install yet)
        }
        static Dictionary<string, object> Row(string table, string column, int value) => Rows(table).FirstOrDefault(r => Rfx.Int(r, column, int.MinValue) == value);

        /// The gear type of an id: a stock piece's, a gear pack piece's, a "gear" .rxmod piece's; -1 unknown.
        public static int GearTypeOf(int id)
        {
            if (id < 0) return -1;
            var kit = KitPacks.Item(id); if (kit != null) return kit.bike ? -1 : kit.type;
            var rx = RxGear.Piece(id); if (rx != null) return rx.type;
            return Rfx.Int(Row("RiderGear", "RiderGearId", id), "RiderGearTypeId", -1);
        }

        /// A piece as a pick (a pack / .rxmod piece by its token: its id changes from run to run).
        public static BraapPick PickOf(int id)
        {
            int type = GearTypeOf(id); if (type < 0) return null;
            var kit = KitPacks.Item(id); if (kit != null) return new BraapPick { type = type, token = kit.Token };
            var rx = RxGear.Piece(id); if (rx != null) return new BraapPick { type = type, token = rx.Token };
            return new BraapPick { type = type, id = id };
        }

        /// The id a pick stands for now; -1: this game does not have it (a pack that is not installed, a piece of another game version).
        public static int Resolve(BraapPick p)
        {
            if (p == null) return -1;
            if (string.IsNullOrEmpty(p.token)) return p.id >= 0 && GearTypeOf(p.id) == p.type && !KitPacks.IsKitId(p.id) && !RxGear.IsId(p.id) ? p.id : -1;
            var kit = KitPacks.FromToken(p.token); if (kit != null && !kit.bike) return kit.id;
            var rx = RxGear.FromToken(p.token); return rx != null ? rx.id : -1;
        }

        /// The RiderGear ids of a gear set (RiderGearSetGear).
        public static List<int> SetGear(int gearSet) =>
            gearSet < 0 ? new List<int>() : Rows("RiderGearSetGear").Where(r => Rfx.Int(r, "RiderGearSetId", -1) == gearSet).Select(r => Rfx.Int(r, "RiderGearId", -1)).Where(x => x >= 0).ToList();

        /// The game's riders and the gear set each wears (AIRider), by display name.
        public static List<(int rider, string name, int gearSet)> StockSets()
        {
            var seen = new HashSet<int>();
            return Rows("AIRider").Select(r => (rider: Rfx.Int(r, "AIRiderId", -1), name: Rfx.Str(r, "DisplayName") ?? Rfx.Str(r, "Name") ?? "", gearSet: Rfx.Int(r, "DefaultRiderGearSetId", -1)))
                .Where(x => x.gearSet >= 0 && SetGear(x.gearSet).Count > 0 && seen.Add(x.gearSet)).ToList();
        }

        /// A look of ids as picks (one per gear type).
        public static List<BraapPick> PicksOf(IEnumerable<int> ids)
        {
            var l = new List<BraapPick>();
            foreach (int id in ids) { var p = PickOf(id); if (p != null && !l.Any(x => x.type == p.type)) l.Add(p); }
            return l;
        }

        static int NameHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? "") h = h * 31 + char.ToUpperInvariant(c);
                return h & 0x7fffffff;
            }
        }

        /// AUTO's gear for a rider: the gear set of one of the game's riders, the same for the same name.
        public static List<int> AutoGear(string name)
        {
            var sets = StockSets();
            return sets.Count == 0 ? new List<int>() : SetGear(sets[NameHash(name) % sets.Count].gearSet);
        }

        /// What a rider wears: AUTO's set with his picks in place of the pieces of their types (own: the gear the core gave the AI rider, the
        /// base when the game's tables give no AUTO set). A real chest protector / neck brace takes the other one off, as the garage does.
        public static int[] GearIds(BraapRider r, int[] own)
        {
            var ids = AutoGear(r.name);
            if (ids.Count == 0) ids = (own ?? new int[0]).ToList();
            foreach (var p in r.look ?? new List<BraapPick>())
            {
                int id = Resolve(p); if (id < 0) continue;
                int k = ids.FindIndex(x => GearTypeOf(x) == p.type);
                if (k >= 0) ids[k] = id; else ids.Add(id);
            }
            return ids.ToArray();
        }

        // ------------------------------------------------------------------------------------------------ the rosters

        /// The game's own riders as a roster (read-only: edit a copy): every AIRider row with his name, number and gear set.
        public static BraapRoster Default
        {
            get
            {
                if (s_default != null && s_default.riders.Count > 0) return s_default;
                var d = new BraapRoster { id = DefaultId, name = "DEFAULT", author = "MX vs ATV Reflex", note = "The game's own computer riders." };
                foreach (var r in Rows("AIRider"))
                {
                    string display = Rfx.Str(r, "DisplayName") ?? Rfx.Str(r, "Name") ?? "Rider";
                    int.TryParse(Rfx.Str(r, "Number") ?? "", out int number);
                    var b = new BraapRider { name = CleanName(display), plate = CleanPlate(Rfx.Str(r, "Name") ?? ""), number = Mathf.Clamp(number, 0, 999), lookName = display.ToUpperInvariant() };
                    b.look = PicksOf(SetGear(Rfx.Int(r, "DefaultRiderGearSetId", -1)));
                    d.riders.Add(b);
                }
                return s_default = d;
            }
        }

        public static BraapRoster Find(string id)
        {
            if (string.IsNullOrEmpty(id) || id == DefaultId) return Default;
            if (id.StartsWith(SeasonPrefix, StringComparison.Ordinal)) return Seasons.FirstOrDefault(r => r.id == id);
            return Data.rosters.FirstOrDefault(r => r.id == id);
        }

        /// The roster in use for races (DEFAULT = the game's own riders, untouched).
        public static string ActiveId
        {
            get
            {
                string test = Environment.GetEnvironmentVariable("REFLEX_BRAAP_USE");   // (tests: a roster in use without a save, e.g. "season:sx450-2026")
                if (!string.IsNullOrEmpty(test) && Find(test) != null) return test;
                string a = Data.active ?? ""; return a.Length > 0 && Find(a) != null ? a : DefaultId;
            }
            set
            {
                string v = value == DefaultId ? "" : value ?? "";
                if ((Data.active ?? "") == v) return;
                Data.active = v;
                Write();
            }
        }
        public static BraapRoster Active => Find(ActiveId) ?? Default;
        public static bool UsingDefault => ActiveId == DefaultId;

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        /// A new roster of the player's: a copy of from's riders (null: empty), saved. Null: too many rosters.
        public static BraapRoster Create(string name, BraapRoster from, string author = null)
        {
            if (Data.rosters.Count >= MaxRosters) return null;
            var r = from != null ? from.Clone() : new BraapRoster();
            r.id = NewId();
            r.name = CleanName(string.IsNullOrWhiteSpace(name) ? "My roster" : name);
            if (r.name.Length == 0) r.name = "My roster";
            r.author = CleanName(author ?? "");
            if (from == null || from.BuiltIn) r.note = "";
            Data.rosters.Add(r);
            Write();
            return r;
        }

        /// Saves a changed roster (the built-in ones are never stored).
        public static void Save(BraapRoster r)
        {
            if (r == null || r.BuiltIn) return;
            Sanitize(r);
            int i = Data.rosters.FindIndex(x => x.id == r.id);
            if (i < 0) { if (Data.rosters.Count >= MaxRosters) return; Data.rosters.Add(r); }
            else Data.rosters[i] = r;
            Write();
        }

        public static bool Delete(BraapRoster r)
        {
            if (r == null || r.BuiltIn) return false;
            bool gone = Data.rosters.RemoveAll(x => x.id == r.id) > 0;
            if ((Data.active ?? "") == r.id) Data.active = "";
            if (gone) Write();
            return gone;
        }

        public static string CleanName(string v)
        {
            var sb = new StringBuilder();
            foreach (char c in (v ?? "").Trim()) if (c >= 32 && c < 127 && sb.Length < MaxNameLength) sb.Append(c);
            return sb.ToString().Trim();
        }

        /// A plate name: capitals, digits, spaces and . - ' (what the plates' font draws), MaxPlateLength long.
        public static string CleanPlate(string v)
        {
            var sb = new StringBuilder();
            foreach (char ch in (v ?? "").Trim().ToUpperInvariant())
                if ((ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9' || ch == ' ' || ch == '.' || ch == '-' || ch == '\'') && sb.Length < MaxPlateLength) sb.Append(ch);
            return sb.ToString().Trim();
        }

        /// The name on the jersey and the plates: the rider's own, else his family name (everything after the first word).
        public static string PlateName(BraapRider r)
        {
            string p = CleanPlate(r.plate);
            if (p.Length > 0) return p;
            string n = CleanName(r.name);
            int sp = n.IndexOf(' ');
            p = CleanPlate(sp > 0 ? n.Substring(sp + 1) : n);
            if (p.Length == 0) p = CleanPlate(n);
            return p.Length == 0 ? "RIDER" : p;
        }

        /// A rider's SKILL: the event's (the race setup's AI OPPONENTS, as the game spreads it over the field), or his own.
        public static readonly string[] SkillNames = { "EVENT'S", "ROOKIE", "AMATEUR", "PRO", "ALL TIME" };
        /// The game's skill tier of a SKILL (the Player List's AIDifficulty bucket: 0 All Time, 1 Pro, 2 Amateur, 3 Rookie); -1: the event's.
        public static int SkillTier(int skill) => skill >= 1 && skill <= 4 ? 4 - skill : -1;

        static bool TokenOk(string t) => !string.IsNullOrEmpty(t) && t.Length <= 160 && t.All(c => c > 32 && c < 127);

        static void Sanitize(BraapRoster r)
        {
            r.id ??= "";
            r.name = CleanName(r.name);
            if (r.name.Length == 0) r.name = "Roster";
            r.author = CleanName(r.author);
            r.note = new string((r.note ?? "").Where(c => c >= 32 && c < 127).Take(200).ToArray());
            r.draw = r.draw == 1 ? 1 : 0;
            r.riders ??= new List<BraapRider>();
            r.riders.RemoveAll(x => x == null);
            if (r.riders.Count > MaxRiders) r.riders.RemoveRange(MaxRiders, r.riders.Count - MaxRiders);
            foreach (var x in r.riders)
            {
                x.name = CleanName(x.name);
                if (x.name.Length == 0) x.name = "Rider";
                x.plate = CleanPlate(x.plate);
                x.number = Mathf.Clamp(x.number, 0, 999);
                x.skill = Mathf.Clamp(x.skill, 0, 4);
                x.team = CleanName(x.team); x.lookName = CleanName(x.lookName); x.kitName = CleanName(x.kitName);
                x.look ??= new List<BraapPick>();
                x.look.RemoveAll(p => p == null || p.type < 0 || p.type > 64 || (string.IsNullOrEmpty(p.token) ? p.id < 0 : !TokenOk(p.token)));
                foreach (var p in x.look) { p.token ??= ""; if (p.token.Length > 0) p.id = -1; }
                x.look = x.look.GroupBy(p => p.type).Select(g => g.Last()).Take(MaxPicks).ToList();
                x.kits ??= new List<string>();
                x.kits = x.kits.Where(KitPacks.IsToken).Distinct().Take(MaxKits).ToList();
            }
        }

        /// The next number from cur in direction dir (±1, or a bigger step) that no other rider of the roster has.
        public static int NextFreeNumber(BraapRoster roster, BraapRider rider, int cur, int dir)
        {
            var used = new HashSet<int>(roster.riders.Where(x => x != rider).Select(x => x.number));
            int step = dir == 0 ? 1 : dir, n = cur;
            for (int i = 0; i < 1000; i++)
            {
                n = ((n + step) % 1000 + 1000) % 1000;
                if (!used.Contains(n)) return n;
                step = Math.Sign(step);   // a big step lands, then single steps past the taken ones
            }
            return cur;
        }

        /// The lowest number from 2 up that the roster does not use (#1 is a champion's).
        public static int FreeNumber(BraapRoster roster)
        {
            var used = new HashSet<int>(roster.riders.Select(x => x.number));
            for (int n = 2; n < 1000; n++) if (!used.Contains(n)) return n;
            return 0;
        }

        /// The riders of the roster sharing a number with another.
        public static HashSet<BraapRider> Duplicates(BraapRoster roster) =>
            new HashSet<BraapRider>(roster.riders.GroupBy(r => r.number).Where(g => g.Count() > 1).SelectMany(g => g));

        // ------------------------------------------------------------------------------------------------ the field of an event

        static readonly Dictionary<int, BraapRider> s_assigned = new Dictionary<int, BraapRider>();
        static List<BraapRider> s_pool;
        static int s_next;
        /// The event being made takes its computer riders' names and looks from the roster in use.
        public static bool EventOn { get; private set; }
        /// The roster of the running event ("" none): the pause / results may say whose field it is.
        public static string EventRoster { get; private set; } = "";

        /// A new event starts: its field is drawn again. allowed: an offline race outside the career.
        public static void BeginEvent(bool allowed)
        {
            s_assigned.Clear(); s_pool = null; s_next = 0;
            EventOn = allowed && !UsingDefault && Active.EnabledCount > 0;
            EventRoster = EventOn ? Active.name : "";
            if (EventOn) Debug.Log($"[Braap] this event's computer riders come from '{Active.name}' ({Active.EnabledCount} in the field, {(Active.draw == 1 ? "from the top" : "a random draw")})");
        }

        /// The roster rider a computer rider of the event is (key: its AIRiderId, so the next heat keeps him); null: the game's own.
        /// With more computer riders than the roster has in the field, the draw starts over.
        public static BraapRider RiderFor(int key)
        {
            if (!EventOn) return null;
            if (s_assigned.TryGetValue(key, out var r)) return r;
            if (s_pool == null)
            {
                var roster = Active;
                s_pool = roster.riders.Where(x => x.enabled).Select(x => x.Clone()).ToList();
                if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_BRAAP_SKILL"), out int sk) && sk >= 1 && sk <= 4) foreach (var x in s_pool) x.skill = sk;   // (tests: every rider at one skill)
                if (roster.draw == 0)
                {
                    int seed = int.TryParse(Environment.GetEnvironmentVariable("REFLEX_BRAAP_SEED"), out int s) ? s : Environment.TickCount;
                    var rnd = new System.Random(seed);
                    for (int i = s_pool.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (s_pool[i], s_pool[j]) = (s_pool[j], s_pool[i]); }
                }
            }
            if (s_pool.Count == 0) return null;
            r = s_pool[s_next++ % s_pool.Count];
            s_assigned[key] = r;
            return r;
        }

        // ------------------------------------------------------------------------------------------------ sharing

        public static string ShareCode(BraapRoster r)
        {
            var copy = r.Clone();
            copy.id = "";
            byte[] raw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(copy));
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
            return ShareTag + Convert.ToBase64String(ms.ToArray());
        }

        /// A roster from a share code anywhere in text (not saved); Open Unleashed's codes too. null + err when there is none / it is broken.
        public static BraapRoster FromShareText(string text, out string err)
        {
            err = null;
            int at = (text ?? "").IndexOf(AnyShareTag, StringComparison.Ordinal);
            if (at < 0) { err = "no B.R.A.A.P. share code on the clipboard"; return null; }
            int start = at + AnyShareTag.Length, end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '+' || text[end] == '/' || text[end] == '=')) end++;
            if (end - start > 8 << 20) { err = "the share code is too long"; return null; }
            try
            {
                byte[] zipped = Convert.FromBase64String(text.Substring(start, end - start));
                using var gz = new GZipStream(new MemoryStream(zipped), CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                var buf = new byte[65536]; int n;
                while ((n = gz.Read(buf, 0, buf.Length)) > 0) { outMs.Write(buf, 0, n); if (outMs.Length > MaxFileBytes) { err = "the share code is too big for a roster"; return null; } }
                var r = JsonUtility.FromJson<BraapRoster>(Encoding.UTF8.GetString(outMs.ToArray()));
                if (r == null || r.riders == null) { err = "the share code is empty"; return null; }
                r.id = "";
                Sanitize(r);
                return r;
            }
            catch (Exception e) { err = "the share code is broken (" + e.GetType().Name + ")"; return null; }
        }

        /// Imports a shared roster as a new one of the player's.
        public static BraapRoster Import(string text, out string err)
        {
            var r = FromShareText(text, out err);
            return r == null ? null : AddImported(r, out err);
        }

        static BraapRoster AddImported(BraapRoster r, out string err)
        {
            err = null;
            if (Data.rosters.Count >= MaxRosters) { err = "too many rosters (" + MaxRosters + ")"; return null; }
            r.id = NewId();
            Data.rosters.Add(r);
            Write();
            return r;
        }

        /// The picks and kits of a roster this game does not have (a friend's packs): how many riders are missing something.
        public static int MissingContent(BraapRoster r) =>
            r.riders.Count(x => x.look.Any(p => Resolve(p) < 0) || x.kits.Count > 0 && x.kits.All(k => KitPacks.FromToken(k) == null));

        // ------------------------------------------------------------------------------------------------ files

        /// A roster file (.braap): pretty JSON, the roster without its id. A file holding a share code reads too.
        public const string FileExtension = ".braap";
        const long MaxFileBytes = 4 << 20;
        [Serializable] class FileDoc { public string format = "Open Reflex B.R.A.A.P. roster"; public int version = 1; public BraapRoster roster; }

        /// Where EXPORT FILE writes and the screen looks for files: <the save's folder>/Braap.
        public static string Folder => Path.Combine(ReflexSave.Folder, "Braap");
        /// Files may be written: not by a sandboxed run without its own save folder (it must not touch the player's).
        public static bool FilesWritable => !ReflexSave.Sandboxed;

        static string FileStem(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "")
                if (c < 128 && char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            string stem = sb.ToString().Trim('_');
            return stem.Length == 0 ? "roster" : stem;
        }

        /// Writes the roster as <Folder>/<name>.braap (the same name again: the file is replaced). Returns the path, or null + err.
        public static string ExportFile(BraapRoster r, out string err)
        {
            err = null;
            if (r == null) { err = "no roster"; return null; }
            if (!FilesWritable) { err = "test runs don't write files"; return null; }
            try
            {
                Directory.CreateDirectory(Folder);
                var copy = r.Clone();
                copy.id = "";
                string path = Path.Combine(Folder, FileStem(r.name) + FileExtension);
                File.WriteAllText(path, JsonUtility.ToJson(new FileDoc { roster = copy }, true), new UTF8Encoding(false));
                return path;
            }
            catch (Exception e) { err = e.Message; return null; }
        }

        /// The .braap files in the folder, newest first.
        public static List<string> Files()
        {
            try
            {
                if (!Directory.Exists(Folder)) return new List<string>();
                return Directory.GetFiles(Folder, "*" + FileExtension).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        /// A roster file read (not saved): its JSON, or a share code in it. null + err when it isn't one.
        public static BraapRoster ReadFile(string path, out string err)
        {
            err = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { err = "the file is gone"; return null; }
                if (info.Length > MaxFileBytes) { err = "the file is too big for a roster"; return null; }
                string text = File.ReadAllText(path);
                if (text.Contains(AnyShareTag)) return FromShareText(text, out err);
                var doc = JsonUtility.FromJson<FileDoc>(text);
                var r = doc?.roster;
                if (r == null || r.riders == null || r.riders.Count == 0) r = JsonUtility.FromJson<BraapRoster>(text);   // a bare roster
                if (r == null || r.riders == null) { err = "not a B.R.A.A.P. roster"; return null; }
                r.id = "";
                Sanitize(r);
                return r;
            }
            catch (Exception) { err = "not a B.R.A.A.P. roster"; return null; }
        }

        /// Imports a roster file as a new roster of the player's.
        public static BraapRoster ImportFile(string path, out string err)
        {
            var r = ReadFile(path, out err);
            return r == null ? null : AddImported(r, out err);
        }
    }
}
