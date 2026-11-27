// import_library.py descriptors(): a level's three Database files, built from a level of the player's own Reflex library (the donor,
// NAT_Concord): its .level with the two subscenes renamed, its .scene cut down to the Base and FreeRide sections under the level's
// name, and its .database marked with the donor it borrows its common terrain resources from. BXML through the Alive importer's
// reader / writer (bxml.Document.to_element, alive_install.encode_element).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnDescriptors
    {
        public const string Donor = "NAT_Concord";

        /// read: relative path ("Database/NAT_Concord.dx9.level") -> the file of the Reflex library. Returns extension ("dx9.level",
        /// "dx9.database", "dx9.scene") -> bytes.
        public static Dictionary<string, byte[]> Build(Func<string, byte[]> read, string level, string donor = Donor)
        {
            var doc = Bxml.Parse(read("Database/" + donor + ".dx9.level")).ToElement();
            var subs = doc.Find("Subscenes") ?? throw new InvalidDataException("The Reflex level " + donor + " lists no subscenes");
            subs.children.Clear();
            foreach (var name in new[] { "Base", "FreeRide" })
            {
                var e = new Elem("Subscene");
                e.Set("name", EVal.S(level + name)); e.Set("path", EVal.S("."));
                subs.children.Add(e);
            }
            byte[] scene = read("Database/" + donor + ".dx9.scene");
            if (scene.Length < 8) throw new InvalidDataException("The Reflex scene of " + donor + " is damaged");
            long count = BitConverter.ToUInt32(scene, 4);
            if (8 + count * 264 > scene.Length) throw new InvalidDataException("The Reflex scene of " + donor + " is damaged");
            var parts = new List<KeyValuePair<string, byte[]>>();
            var utf8 = new UTF8Encoding(false, true);
            for (int i = 0; i < count; i++)
            {
                int at = 8 + i * 264, n = 0;
                while (n < 256 && scene[at + n] != 0) n++;
                string name = utf8.GetString(scene, at, n);
                if (name != donor + "Base" && name != donor + "FreeRide") continue;
                long start = BitConverter.ToUInt32(scene, at + 260);
                long end = i + 1 < count ? BitConverter.ToUInt32(scene, 8 + (i + 1) * 264 + 260) : scene.Length;
                if (end > scene.Length) end = scene.Length;       // Python slices clamp
                if (start > end) start = end;
                var data = new byte[end - start];
                Buffer.BlockCopy(scene, (int)start, data, 0, data.Length);
                parts.Add(new KeyValuePair<string, byte[]>(name.Replace(donor, level), data));
            }
            if (parts.Count != 2) throw new UnValueError("Reflex template lacks Base/FreeRide sections");
            var o = new MemoryStream();
            var head = new byte[8 + 264 * parts.Count];
            UnTerrain.PutU32(head, 0, 0xc0fffeee); UnTerrain.PutU32(head, 4, (uint)parts.Count);
            long pos = head.Length;
            for (int i = 0; i < parts.Count; i++)
            {
                var nb = Encoding.UTF8.GetBytes(parts[i].Key);
                Buffer.BlockCopy(nb, 0, head, 8 + i * 264, Math.Min(256, nb.Length));
                UnTerrain.PutU32(head, 8 + i * 264 + 260, (uint)pos);
                pos += parts[i].Value.Length;
            }
            o.Write(head, 0, head.Length);
            foreach (var p in parts) o.Write(p.Value, 0, p.Value.Length);
            var empty = Bxml.Parse(read("Database/" + donor + ".dx9.database")).ToElement();
            empty.Set("assetSource", EVal.S(donor));
            return new Dictionary<string, byte[]>
            {
                ["dx9.level"] = Bxml.EncodeElement(doc),
                ["dx9.database"] = Bxml.EncodeElement(empty),
                ["dx9.scene"] = o.ToArray(),
            };
        }
    }
}
