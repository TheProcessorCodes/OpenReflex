// MX vs ATV Unleashed (PC, 2006) .pak archive reader: port of the reference pak.py.
// Layout (little-endian, no compression): u32 count; count x 108-byte entries { char[100] path (NUL-terminated, backslash separated,
// bytes after the NUL are garbage), u32 offset (relative to the data base), u32 size }; data base = 4 + count * 108.
// Names match as pak.py matches them: lower-cased, '/' turned into '\'. When two entries share a folded name the later one wins
// (Python's dict comprehension), and Names lists every entry in table order with the archive's own spelling.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnPak : IDisposable
    {
        public const int EntrySize = 108, NameLen = 100;

        public sealed class Entry
        {
            public string Name;
            public uint Offset, Size;
        }

        readonly FileStream _f;
        readonly object _lock = new object();
        readonly List<Entry> _entries = new List<Entry>();
        readonly List<string> _names = new List<string>();
        readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
        long _base;

        public string Path { get; }
        public IReadOnlyList<Entry> Entries => _entries;
        /// Every entry's path in table order, spelled as the archive spells it ("Data\Env\SX01\...").
        public IEnumerable<string> Names => _names;

        UnPak(string path, FileStream f) { Path = path; _f = f; }

        public static UnPak Open(string path)
        {
            var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var pak = new UnPak(path, f);
            try { pak.ReadToc(); }
            catch { f.Dispose(); throw; }
            return pak;
        }

        void ReadToc()
        {
            var head = new byte[4];
            ReadFully(head, 0, 4);
            uint count = BitConverter.ToUInt32(head, 0);
            long tocLen = (long)count * EntrySize;
            if (tocLen > _f.Length - 4) throw new InvalidDataException("not an Unleashed .pak (table larger than the file): " + Path);
            var toc = new byte[tocLen];
            ReadFully(toc, 0, toc.Length);
            _base = 4 + tocLen;
            for (int i = 0; i < count; i++)
            {
                int o = i * EntrySize, n = 0;
                while (n < NameLen && toc[o + n] != 0) n++;
                var e = new Entry
                {
                    Name = Latin1(toc, o, n),
                    Offset = BitConverter.ToUInt32(toc, o + NameLen),
                    Size = BitConverter.ToUInt32(toc, o + NameLen + 4),
                };
                _entries.Add(e);
                _names.Add(e.Name);
                _byName[Key(e.Name)] = e;
            }
        }

        /// latin-1: one byte, one char (no dependency on the runtime's code page tables).
        internal static string Latin1(byte[] b, int offset, int count)
        {
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++) sb.Append((char)b[offset + i]);
            return sb.ToString();
        }

        /// pak.py: name.lower().replace("/", "\\"). Names are latin-1, so the invariant lower-casing equals Python's for them
        /// (U+00B5 and U+00FF stay as they are in both).
        public static string Key(string name) => name.ToLowerInvariant().Replace('/', '\\');

        public bool Has(string name) => _byName.ContainsKey(Key(name));

        public Entry Find(string name) => _byName.TryGetValue(Key(name), out var e) ? e : null;

        /// The entry's bytes; KeyNotFoundException when the name is absent (Python: KeyError). A read past the end of the file
        /// returns the bytes that are there, as Python's f.read does.
        public byte[] Read(string name)
        {
            if (!_byName.TryGetValue(Key(name), out var e)) throw new KeyNotFoundException(name);
            return Read(e);
        }

        public byte[] Read(Entry e)
        {
            lock (_lock)
            {
                long at = _base + e.Offset;
                long avail = Math.Max(0, Math.Min(e.Size, _f.Length - at));
                var buf = new byte[avail];
                _f.Seek(at, SeekOrigin.Begin);
                ReadFully(buf, 0, buf.Length);
                return buf;
            }
        }

        void ReadFully(byte[] buf, int off, int len)
        {
            while (len > 0)
            {
                int n = _f.Read(buf, off, len);
                if (n <= 0) throw new EndOfStreamException("truncated .pak: " + Path);
                off += n; len -= n;
            }
        }

        public void Dispose() => _f.Dispose();
    }
}
