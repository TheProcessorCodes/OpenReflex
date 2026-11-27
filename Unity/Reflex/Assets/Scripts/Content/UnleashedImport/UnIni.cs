// The INI reader the Unleashed converter needs: Python 3.9 configparser.ConfigParser(strict=False, interpolation=None[, allow_no_value=True])
// as the reference tools use it on .scn / recipe text (read_string of latin-1 decoded bytes).
//   lines             split on '\n' only (a '\r' stays on the line and falls to strip())
//   comments          a line whose first non-space character is '#' or ';' (no inline comments)
//   sections          "[name]" at the start of the stripped line (name = [^]]+, text after the ']' is ignored); names keep their case;
//                     a repeated section continues the first one (strict=False); "DEFAULT" feeds every section's fallback values
//   options           "key = value" or "key: value", split at the first '=' or ':'; the key is lower-cased (optionxform), the value
//                     stripped; a repeated key replaces the earlier value and keeps its first position (dict semantics)
//   continuation      a non-empty line indented deeper than the line that opened the current option is appended to its value ('\n'
//                     joined); empty lines inside a value are kept and the joined value is right-stripped
//   no value          a line without a delimiter: with allowNoValue the key maps to null, without it it is a parsing error, raised
//                     after the whole text was read (Python's ParsingError), as is an empty key ("= x")
// Whitespace is Python's str.strip() / \s set for the characters latin-1 text can hold.
using System;
using System.Collections.Generic;
using System.Text;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnIniException : Exception
    {
        public UnIniException(string message) : base(message) { }
    }

    /// One section (configparser's SectionProxy): its own options first, then the DEFAULT ones it does not override.
    public sealed class UnIniSection
    {
        public readonly string Name;
        readonly UnIni _owner;
        internal readonly List<string> order = new List<string>();
        internal readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);   // string, List<string> while reading, or null

        internal UnIniSection(UnIni owner, string name) { _owner = owner; Name = name; }

        bool IsDefault => ReferenceEquals(this, _owner.Defaults);

        /// `key in section`: the key (lower-cased like every option name) is in the section or in DEFAULT.
        public bool Has(string key)
        {
            key = UnIni.Lower(key);
            return values.ContainsKey(key) || (!IsDefault && _owner.Defaults.values.ContainsKey(key));
        }

        /// section.get(key, fallback): the value (null for a valueless option), or fallback when the key is absent.
        public string Get(string key, string fallback = null)
        {
            key = UnIni.Lower(key);
            if (values.TryGetValue(key, out var v)) return (string)v;
            if (!IsDefault && _owner.Defaults.values.TryGetValue(key, out v)) return (string)v;
            return fallback;
        }

        /// section[key]: KeyNotFoundException when absent (Python: KeyError).
        public string this[string key]
        {
            get
            {
                if (!Has(key)) throw new KeyNotFoundException(key);
                return Get(key);
            }
        }

        /// Iteration order of the proxy: the section's own keys, then DEFAULT's keys it does not have.
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var k in order) yield return k;
                if (!IsDefault) foreach (var k in _owner.Defaults.order) if (!values.ContainsKey(k)) yield return k;
            }
        }

        /// section.items(): (key, value) in Keys order.
        public IEnumerable<KeyValuePair<string, string>> Items
        {
            get { foreach (var k in Keys) yield return new KeyValuePair<string, string>(k, Get(k)); }
        }

        internal void Put(string key, object value)
        {
            if (!values.ContainsKey(key)) order.Add(key);
            values[key] = value;
        }
    }

    public sealed class UnIni
    {
        public const string DefaultSection = "DEFAULT";

        readonly List<UnIniSection> _sections = new List<UnIniSection>();
        readonly Dictionary<string, UnIniSection> _byName = new Dictionary<string, UnIniSection>(StringComparer.Ordinal);
        public readonly UnIniSection Defaults;

        UnIni() { Defaults = new UnIniSection(this, DefaultSection); }

        /// sections(): every section but DEFAULT, in the order they first appear.
        public IReadOnlyList<UnIniSection> Sections => _sections;

        public IEnumerable<string> SectionNames { get { foreach (var s in _sections) yield return s.Name; } }

        /// `name in parser` / has_section (DEFAULT counts for the former, as in Python's mapping protocol).
        public bool HasSection(string name) => name == DefaultSection || _byName.ContainsKey(name);

        /// parser[name]; null when there is no such section (Python raises KeyError: test with HasSection or for null).
        public UnIniSection Section(string name) => name == DefaultSection ? Defaults : _byName.TryGetValue(name, out var s) ? s : null;

        /// parser[name]: KeyNotFoundException when absent.
        public UnIniSection this[string name] => Section(name) ?? throw new KeyNotFoundException(name);

        /// parser.get(section, option, fallback=fallback): fallback when the section or the option is absent.
        public string Get(string section, string option, string fallback = null)
        {
            var s = Section(section);
            return s == null ? fallback : s.Get(option, fallback);
        }

        // ------------------------------------------------------------------------------------------------ Python text rules

        /// Python's str.isspace() for the characters this converter meets (latin-1 text; the wider Unicode spaces are included for safety).
        public static bool IsSpace(char c)
        {
            int u = c;
            return (u >= 0x09 && u <= 0x0d) || (u >= 0x1c && u <= 0x20) || u == 0x85 || u == 0xa0
                || u == 0x1680 || (u >= 0x2000 && u <= 0x200a) || u == 0x2028 || u == 0x2029 || u == 0x202f || u == 0x205f || u == 0x3000;
        }

        /// str.strip().
        public static string Strip(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsSpace(s[a])) a++;
            while (b > a && IsSpace(s[b - 1])) b--;
            return a == 0 && b == s.Length ? s : s.Substring(a, b - a);
        }

        /// str.rstrip().
        public static string RStrip(string s)
        {
            int b = s.Length;
            while (b > 0 && IsSpace(s[b - 1])) b--;
            return b == s.Length ? s : s.Substring(0, b);
        }

        /// str.lower() for latin-1 text (A-Z and U+00C0-U+00DE but U+00D7; other characters are left as they are).
        public static string Lower(string s)
        {
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i], l = c;
                if (c >= 'A' && c <= 'Z') l = (char)(c + 32);
                else if (c >= 0xc0 && c <= 0xde && c != 0xd7) l = (char)(c + 32);
                else if (c > 0xff) l = char.ToLowerInvariant(c);
                if (l != c && sb == null) { sb = new StringBuilder(s.Length); sb.Append(s, 0, i); }
                if (sb != null) sb.Append(l);
            }
            return sb == null ? s : sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------ reading

        /// ConfigParser(strict=False, interpolation=None, allow_no_value=allowNoValue).read_string(text).
        public static UnIni Parse(string text, bool allowNoValue = false)
        {
            var ini = new UnIni();
            UnIniSection cursect = null;
            string optname = null;
            int indentLevel = 0, lineno = 0;
            List<string> errors = null;
            int pos = 0;
            while (pos < text.Length)
            {
                int nl = text.IndexOf('\n', pos);
                string line = nl < 0 ? text.Substring(pos) : text.Substring(pos, nl - pos + 1);   // the line keeps its '\n', as in Python
                pos = nl < 0 ? text.Length : nl + 1;
                lineno++;
                string stripped = Strip(line);
                bool comment = stripped.Length > 0 && (stripped[0] == '#' || stripped[0] == ';');
                string value = comment ? "" : stripped;
                if (value.Length == 0)
                {
                    // an empty line inside a value is kept (empty_lines_in_values), unless the line was a comment
                    if (!comment && cursect != null && !string.IsNullOrEmpty(optname) && cursect.values[optname] != null)
                        ((List<string>)cursect.values[optname]).Add("");
                    continue;
                }
                int curIndent = 0;
                while (curIndent < line.Length && IsSpace(line[curIndent])) curIndent++;
                if (cursect != null && !string.IsNullOrEmpty(optname) && curIndent > indentLevel)
                {
                    // Python 3.9 fails with AttributeError when a valueless option is continued
                    if (!(cursect.values[optname] is List<string> list)) throw new UnIniException("line " + lineno + ": a continuation line follows an option without a value");
                    list.Add(value);
                    continue;
                }
                indentLevel = curIndent;
                int close = value[0] == '[' ? value.IndexOf(']', 1) : -1;
                if (close > 1)   // \[(?P<header>[^]]+)\] matched at the start
                {
                    string sectname = value.Substring(1, close - 1);
                    if (ini._byName.TryGetValue(sectname, out var known)) cursect = known;
                    else if (sectname == DefaultSection) cursect = ini.Defaults;
                    else
                    {
                        cursect = new UnIniSection(ini, sectname);
                        ini._byName[sectname] = cursect; ini._sections.Add(cursect);
                    }
                    optname = null;
                    continue;
                }
                if (cursect == null) throw new UnIniException("File contains no section headers (line " + lineno + ")");
                int delim = -1;
                for (int i = 0; i < value.Length; i++) if (value[i] == '=' || value[i] == ':') { delim = i; break; }
                if (delim < 0 && !allowNoValue)
                {
                    (errors ?? (errors = new List<string>())).Add("line " + lineno + ": " + stripped);
                    continue;   // optname stays what it was
                }
                string rawName = delim < 0 ? value : value.Substring(0, delim);
                if (RStrip(rawName).Length == 0) (errors ?? (errors = new List<string>())).Add("line " + lineno + ": " + stripped);
                optname = Lower(RStrip(rawName));
                if (delim >= 0) cursect.Put(optname, new List<string> { Strip(value.Substring(delim + 1)) });
                else cursect.Put(optname, null);
            }
            // _join_multiline_values
            Join(ini.Defaults);
            foreach (var s in ini._sections) Join(s);
            if (errors != null) throw new UnIniException("Source contains parsing errors: " + string.Join("; ", errors));
            return ini;
        }

        static void Join(UnIniSection s)
        {
            foreach (var k in s.order)
                if (s.values[k] is List<string> list) s.values[k] = RStrip(string.Join("\n", list));
        }
    }
}
