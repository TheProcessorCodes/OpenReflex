// MX vs ATV Alive (Xbox 360): the audio helpers the importers share (core/plan/ALIVE_ENGINE_SOUNDS.md section 2).
//   FSB4     Alive's banks: a 0x40-byte wrapper (bank file name, size) + an FSB4 image, little-endian: a 0x30-byte header (u32 samples,
//            u32 header bytes, u32 data bytes, u32 version 0x40000, u32 mode 0x40, 24-byte hash), per sample a header of its own size
//            (u16 size, char[30] name, u32 samples, bytes, loop start, loop end, mode, rate, u16 volume, pan, priority, channels,
//            f32 min / max distance, u32 x2 = 80 bytes; then 12 zero bytes, the XMA seek block (u32 size, u32 1, u32 n, (n-1) x u32) and
//            the SYNC block: u32 count, count x { u32 sample, char[256] name }), then the sample data back to back. XMA samples have mode
//            bit 0x01000000; the engine banks are mono 44.1 kHz, the data in 2048-byte XMA packets.
//   XMA2     Unity can't decode XMA: the data goes into a RIFF WAVE with an XMA2WAVEFORMATEX, which ffmpeg's xma2 decoder reads. Samples
//            encoded = play length = the FSB's sample count (without it the decoder adds a gap of padding at every loop), 32 KiB blocks
//            (the banks' seek-table block).
//   ffmpeg   the client's own (FfmpegBinkSource.Locate, which the caller passes in): one process per sample, a PCM16 WAV out (its data
//            chunk read back), any stderr line or a non-zero exit is a failure; the PCM is cut or zero-padded to the FSB's sample count
//            (the decode was measured exact for every engine sample of the disc and the LIVE packs). Not raw s16le: ffmpeg's raw muxer
//            prints "non monotonically increasing dts" for some of the recordings (450F, 400, 250R, ATV 1000) with the same samples.
// Pure C# (System only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Reflex.Port.Content.AliveImport
{
    /// One sample of an FSB4 bank. offset / length: its data in the bank's bytes (the array AliveFsb.Read was given); header: the whole
    /// sample header (fixed part + extra bytes); syncs: its SYNC points (never null).
    public sealed class FsbSample
    {
        public int index; public string name; public long samples; public int offset, length; public long loopStart, loopEnd;
        public uint mode; public int freq, channels; public byte[] header; public List<(long sample, string name)> syncs;
        public bool Xma => (mode & 0x01000000) != 0;
    }

    public static class AliveFsb
    {
        /// Every sample of the FSB4 bank found in the first 0x400 bytes of bank (a .soundbnk asset or a bare FSB4).
        public static List<FsbSample> Read(byte[] bank)
        {
            Bin.Require(bank != null, "no sound bank");
            int i = Bin.IndexOf(bank, new[] { (byte)'F', (byte)'S', (byte)'B', (byte)'4' }, 0, Math.Min(bank.Length, 0x400));
            Bin.Require(i >= 0 && i + 0x30 <= bank.Length, "not an FSB4 sound bank");
            int n = Bin.I32LE(bank, i + 4), headers = Bin.I32LE(bank, i + 8), data = Bin.I32LE(bank, i + 12);
            uint hmode = Bin.U32LE(bank, i + 20);
            Bin.Require((hmode & 0x2) == 0, "FSB4 basic sample headers are not supported");
            Bin.Require(n >= 0 && n <= 100000 && headers >= 0 && data >= 0 && i + 0x30L + headers + data <= bank.Length, "truncated FSB4 sound bank");
            var list = new List<FsbSample>(n);
            int p = i + 0x30, end = i + 0x30 + headers, at = end;
            for (int k = 0; k < n; k++)
            {
                Bin.Require(p + 80 <= end, "FSB4 sample header " + k + " outside the header block");
                int size = Bin.U16LE(bank, p);
                Bin.Require(size >= 80 && p + size <= end, "FSB4 sample header " + k + ": bad size " + size);
                int e = p + 2; while (e < p + 32 && bank[e] != 0) e++;
                var s = new FsbSample
                {
                    index = k, name = Encoding.GetEncoding(28591).GetString(bank, p + 2, e - p - 2),
                    samples = Bin.U32LE(bank, p + 32), length = Bin.I32LE(bank, p + 36), loopStart = Bin.U32LE(bank, p + 40), loopEnd = Bin.U32LE(bank, p + 44),
                    mode = Bin.U32LE(bank, p + 48), freq = Bin.I32LE(bank, p + 52), header = Bin.Slice(bank, p, size), syncs = new List<(long, string)>(), offset = at
                };
                int ch = Bin.U16LE(bank, p + 62);
                s.channels = ch != 0 ? ch : (s.mode & 0x40) != 0 ? 2 : 1;
                Bin.Require(s.length >= 0 && at + (long)s.length <= end + (long)data, "FSB4 sample " + s.name + " outside the bank");
                // the SYNC block: searched for in the extra bytes, taken when its count fits inside the header
                for (int q = Bin.IndexOf(bank, Sync, p + 80, p + size); q >= 0; q = Bin.IndexOf(bank, Sync, q + 1, p + size))
                {
                    if (q + 8 > p + size) break;
                    long c = Bin.U32LE(bank, q + 4);
                    if (c > 10000 || q + 8 + c * 260 > p + size) continue;
                    for (int j = 0, r = q + 8; j < c; j++, r += 260)
                    {
                        int z = r + 4; while (z < r + 260 && bank[z] != 0) z++;
                        s.syncs.Add((Bin.U32LE(bank, r), Encoding.GetEncoding(28591).GetString(bank, r + 4, z - r - 4)));
                    }
                    break;
                }
                list.Add(s);
                at += s.length; p += size;
            }
            Bin.Require(at - end == data, "FSB4 sample sizes do not add up to the data size");
            return list;
        }
        static readonly byte[] Sync = { (byte)'S', (byte)'Y', (byte)'N', (byte)'C' };
    }

    public static class AliveXma
    {
        /// bank[offset, offset + length) (XMA packets) in a RIFF WAVE with an XMA2WAVEFORMATEX: format 0x166, one stream, 32 KiB blocks,
        /// samples encoded = play length = samples, no loop.
        public static byte[] Wave(byte[] bank, int offset, int length, long samples, int channels, int rate)
        {
            Bin.Require(bank != null && offset >= 0 && length > 0 && offset + (long)length <= bank.Length, "XMA data outside the bank");
            Bin.Require(channels == 1 || channels == 2, "XMA with " + channels + " channels");
            var ms = new MemoryStream(length + 128);
            var w = new BinaryWriter(ms);
            int blocks = (length + 0x7FFF) / 0x8000;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(4 + 8 + 52 + 8 + length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(52);
            w.Write((ushort)0x166); w.Write((ushort)channels); w.Write(rate); w.Write(rate * 2 * channels); w.Write((ushort)2048); w.Write((ushort)16); w.Write((ushort)34);
            w.Write((ushort)1); w.Write(channels == 1 ? 4 : 3);                // streams, channel mask (front centre / front left, right)
            w.Write((uint)samples); w.Write(0x8000);                           // samples encoded, bytes per block
            w.Write(0); w.Write((uint)samples); w.Write(0); w.Write(0);        // play begin, length; loop begin, length
            w.Write((byte)0); w.Write((byte)4); w.Write((ushort)blocks);        // loop count, encoder version, blocks
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(length);
            w.Write(bank, offset, length);
            w.Flush();
            return ms.ToArray();
        }
    }

    public static class AliveFfmpeg
    {
        /// Runs ffmpeg: (exit code, its output, its error output). Killed when cancelled (OperationCanceledException) or after timeoutMs (TimeoutException).
        public static (int code, string output, string errors) Run(string ffmpeg, IList<string> args, CancellationToken cancel, int timeoutMs)
        {
            var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = false, CreateNoWindow = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            var output = new StringBuilder(); var errors = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                var clock = Stopwatch.StartNew();
                while (!p.WaitForExit(100))
                {
                    if (cancel.IsCancellationRequested || clock.ElapsedMilliseconds > timeoutMs)
                    {
                        try { p.Kill(); } catch (Exception) { }
                        try { p.WaitForExit(2000); } catch (Exception) { }
                        cancel.ThrowIfCancellationRequested();
                        throw new TimeoutException("ffmpeg did not finish in " + (timeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " s");
                    }
                }
                p.WaitForExit();   // (the output read to its end)
                lock (output) lock (errors) return (p.ExitCode, output.ToString(), errors.ToString());
            }
        }

        /// Whether this ffmpeg runs and lists the decoder (e.g. "xma2") in -decoders.
        public static bool HasDecoder(string ffmpeg, string name)
        {
            if (string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(name) || !File.Exists(ffmpeg)) return false;
            try
            {
                var (code, output, _) = Run(ffmpeg, new[] { "-hide_banner", "-decoders" }, CancellationToken.None, 20000);
                if (code != 0) return false;
                foreach (string line in output.Split('\n'))
                {
                    var f = line.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length >= 2 && f[0].Length == 6 && f[0][0] == 'A' && f[1] == name) return true;   // " A....D xma2  Xbox 360 ..."
                }
            }
            catch (Exception e) when (e is IOException || e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is TimeoutException) { }
            return false;
        }

        /// A RIFF/XMA2 file (AliveXma.Wave) decoded to s16le mono, exactly `samples` long (cut or zero-padded). The temporary files go in
        /// scratchDir and are deleted. AliveImportException when ffmpeg exits non-zero or prints any error line.
        public static short[] XmaToPcm(string ffmpeg, byte[] riffXma, long samples, string scratchDir, CancellationToken cancel)
        {
            Bin.Require(!string.IsNullOrEmpty(ffmpeg) && File.Exists(ffmpeg), "ffmpeg not found");
            Bin.Require(samples >= 0 && samples <= int.MaxValue, "bad sample count " + samples);
            Directory.CreateDirectory(scratchDir);
            string stem = Path.Combine(scratchDir, "." + Guid.NewGuid().ToString("N").Substring(0, 8));
            string wav = stem + ".xma.wav", raw = stem + ".pcm.wav";
            try
            {
                File.WriteAllBytes(wav, riffXma);
                var (code, _, errors) = Run(ffmpeg, new[] { "-nostdin", "-hide_banner", "-v", "error", "-y", "-i", wav, "-f", "wav", "-acodec", "pcm_s16le", "-ac", "1", "-bitexact", raw }, cancel, 30000);
                string first = FirstLine(errors);
                if (code != 0 || !File.Exists(raw)) throw new AliveImportException("ffmpeg could not decode the XMA sample (" + (first.Length > 0 ? first : "exit code " + code.ToString(CultureInfo.InvariantCulture)) + ")");
                if (first.Length > 0) throw new AliveImportException("ffmpeg reported an error decoding the XMA sample (" + first + ")");
                byte[] b = File.ReadAllBytes(raw);
                int at = WaveData(b, out int size);
                var pcm = new short[samples];
                Buffer.BlockCopy(b, at, pcm, 0, (int)Math.Min(size & ~1, samples * 2));
                return pcm;
            }
            finally
            {
                try { File.Delete(wav); } catch (Exception) { }
                try { File.Delete(raw); } catch (Exception) { }
            }
        }

        /// The data chunk of a RIFF WAVE (offset, size cut to the file).
        static int WaveData(byte[] b, out int size)
        {
            Bin.Require(b.Length >= 12 && Bin.StartsWith(b, "RIFF") && Bin.StartsWith(b, "WAVE", 8), "ffmpeg wrote no WAVE file");
            for (int p = 12; p + 8 <= b.Length; )
            {
                int n = Bin.I32LE(b, p + 4);
                if (Bin.StartsWith(b, "data", p)) { size = (int)Math.Min((uint)n, (uint)(b.Length - p - 8)); return p + 8; }
                Bin.Require(n >= 0 && p + 8L + n <= b.Length, "bad chunk in ffmpeg's WAVE file");
                p += 8 + n + (n & 1);
            }
            throw new AliveImportException("ffmpeg's WAVE file has no data chunk");
        }

        static string FirstLine(string s) { s = (s ?? "").Trim(); int n = s.IndexOf('\n'); return (n < 0 ? s : s.Substring(0, n)).Trim(); }
    }
}
