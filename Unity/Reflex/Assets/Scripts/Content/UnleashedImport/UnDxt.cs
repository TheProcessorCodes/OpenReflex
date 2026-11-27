// MX vs ATV Unleashed .dxt textures decoded to RGBA8: port of Open Unleashed's native/src/dxt.c (mxa_dxt_parse, mxa_dxt_info_get,
// mxa_dxt_decode_rgba of mip 0), the same bytes.
//   u32 version, u32 format (0x25 / 0x26 DXT1, 0x2f-0x32 DXT2/3, 0x33-0x36 DXT4/5, 0x37 / 0x38 P8), u32 palette entries,
//   palette x { B, G, R, A }, u32 mips, u32 width, u32 height, mips x { u32 w, u32 h, u32 size, data } smallest first.
// DXT blocks are stored row by row; P8 is one index per pixel in the Xbox's swizzled order (undone here for power-of-two levels).
using System;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnDxtImage
    {
        public int width, height;
        public byte[] rgba;
    }

    public static class UnDxt
    {
        const int Bc1 = 1, Bc2 = 2, Bc3 = 3, P8 = 4, MaxMips = 16;

        static uint Rd32(byte[] b, long p) => (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);

        /// The largest level as RGBA8 (rows in stored order); null where mxa_dxt_parse returns NULL.
        public static UnDxtImage Decode(byte[] b)
        {
            if (b == null || b.Length < 24) return null;
            long size = b.Length;
            int raw = (int)Rd32(b, 4); uint pal = Rd32(b, 8);
            int kind, blockBytes = 0;
            switch (raw)
            {
                case 0x25: case 38: kind = Bc1; blockBytes = 8; break;
                case 0x2f: case 0x30: case 0x31: case 0x32: kind = Bc2; blockBytes = 16; break;
                case 0x33: case 0x34: case 0x35: case 54: kind = Bc3; blockBytes = 16; break;
                case 55: case 56: kind = P8; break;
                default: return null;
            }
            if (pal > 65536 || 12 + (ulong)pal * 4 + 12 > (ulong)size) return null;
            if (kind == P8 && pal == 0) return null;
            long p = 12 + (long)pal * 4;
            uint nm = Rd32(b, p), w = Rd32(b, p + 4), h = Rd32(b, p + 8);
            p += 12;
            if (nm == 0 || nm > MaxMips) return null;
            if (w == 0 || h == 0 || w > 16384 || h > 16384) return null;
            int w0 = 0, h0 = 0; long data0 = 0;
            for (uint i = 0; i < nm; i++)
            {
                if (size - p < 12) return null;
                uint mw = Rd32(b, p), mh = Rd32(b, p + 4), ms = Rd32(b, p + 8);
                p += 12;
                if ((ulong)(size - p) < ms) return null;
                if (i == nm - 1) { w0 = (int)mw; h0 = (int)mh; data0 = p; }
                p += ms;
                // expected_size in C int arithmetic (the sizes are checked as the library checks them, level by level)
                int imw = (int)mw, imh = (int)mh;
                int expected = kind == P8 ? unchecked(imw * imh) : unchecked(((imw + 3) / 4) * ((imh + 3) / 4) * blockBytes);
                if (mw == 0 || mh == 0 || (long)ms < expected) return null;
            }
            if (w0 != (int)w || h0 != (int)h) return null;
            var img = new UnDxtImage { width = w0, height = h0, rgba = new byte[(long)w0 * h0 * 4] };
            if (kind == P8) DecodeP8(b, pal, data0, img);
            else DecodeBc(b, kind, blockBytes, data0, img);
            return img;
        }

        static bool IsPow2(int v) => v > 0 && (v & (v - 1)) == 0;

        static uint SwizzleOffset(uint x, uint y, uint w, uint h)
        {
            uint off = 0, bit = 1;
            for (uint xb = 1, yb = 1; xb < w || yb < h;)
            {
                if (xb < w) { if ((x & xb) != 0) off |= bit; bit <<= 1; xb <<= 1; }
                if (yb < h) { if ((y & yb) != 0) off |= bit; bit <<= 1; yb <<= 1; }
            }
            return off;
        }

        static void DecodeP8(byte[] b, uint pal, long data, UnDxtImage img)
        {
            int w = img.width, h = img.height; bool swizzled = IsPow2(w) && IsPow2(h);
            var o = img.rgba;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = b[data + (swizzled ? SwizzleOffset((uint)x, (uint)y, (uint)w, (uint)h) : (uint)(y * w + x))];
                    int at = (y * w + x) * 4;
                    if (k < pal)
                    {
                        long c = 12 + k * 4L;
                        o[at] = b[c + 2]; o[at + 1] = b[c + 1]; o[at + 2] = b[c]; o[at + 3] = b[c + 3];
                    }
                }
        }

        static void Rgb565(int c, byte[] o, int at)
        {
            o[at] = (byte)(((c >> 11) & 31) * 255 / 31);
            o[at + 1] = (byte)(((c >> 5) & 63) * 255 / 63);
            o[at + 2] = (byte)((c & 31) * 255 / 31);
            o[at + 3] = 255;
        }

        static void Bc1Colors(byte[] b, long blk, byte[] px, bool four, byte[] pal)
        {
            int c0 = b[blk] | b[blk + 1] << 8, c1 = b[blk + 2] | b[blk + 3] << 8;
            Rgb565(c0, pal, 0); Rgb565(c1, pal, 4);
            if (four || c0 > c1)
            {
                for (int k = 0; k < 3; k++)
                {
                    pal[8 + k] = (byte)((2 * pal[k] + pal[4 + k] + 1) / 3);
                    pal[12 + k] = (byte)((pal[k] + 2 * pal[4 + k] + 1) / 3);
                }
                pal[11] = pal[15] = 255;
            }
            else
            {
                for (int k = 0; k < 3; k++) pal[8 + k] = (byte)((pal[k] + pal[4 + k]) / 2);
                pal[11] = 255;
                pal[12] = pal[13] = pal[14] = pal[15] = 0;
            }
            uint idx = Rd32(b, blk + 4);
            for (int i = 0; i < 16; i++)
            {
                int s = (int)((idx >> (2 * i)) & 3) * 4;
                px[i * 4] = pal[s]; px[i * 4 + 1] = pal[s + 1]; px[i * 4 + 2] = pal[s + 2]; px[i * 4 + 3] = pal[s + 3];
            }
        }

        static void Bc3Alpha(byte[] b, long blk, byte[] px, byte[] a)
        {
            a[0] = b[blk]; a[1] = b[blk + 1];
            if (a[0] > a[1]) for (int i = 1; i < 7; i++) a[i + 1] = (byte)(((7 - i) * a[0] + i * a[1] + 3) / 7);
            else
            {
                for (int i = 1; i < 5; i++) a[i + 1] = (byte)(((5 - i) * a[0] + i * a[1] + 2) / 5);
                a[6] = 0; a[7] = 255;
            }
            ulong bits = 0;
            for (int i = 0; i < 6; i++) bits |= (ulong)b[blk + 2 + i] << (8 * i);
            for (int i = 0; i < 16; i++) px[i * 4 + 3] = a[(int)((bits >> (3 * i)) & 7)];
        }

        static void Bc2Alpha(byte[] b, long blk, byte[] px)
        {
            for (int i = 0; i < 16; i++)
            {
                int nib = (b[blk + i / 2] >> ((i & 1) * 4)) & 15;
                px[i * 4 + 3] = (byte)(nib * 17);
            }
        }

        static void DecodeBc(byte[] b, int kind, int bb, long src, UnDxtImage img)
        {
            int w = img.width, h = img.height, bw = (w + 3) / 4, bh = (h + 3) / 4;
            var px = new byte[64]; var pal = new byte[16]; var a = new byte[8]; var o = img.rgba;
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, src += bb)
                {
                    if (kind == Bc1) Bc1Colors(b, src, px, false, pal);
                    else if (kind == Bc2) { Bc1Colors(b, src + 8, px, true, pal); Bc2Alpha(b, src, px); }
                    else { Bc1Colors(b, src + 8, px, true, pal); Bc3Alpha(b, src, px, a); }
                    for (int y = 0; y < 4; y++)
                    {
                        int py = by * 4 + y;
                        if (py >= h) break;
                        for (int x = 0; x < 4; x++)
                        {
                            int pxx = bx * 4 + x;
                            if (pxx >= w) break;
                            int d = (py * w + pxx) * 4, s = (y * 4 + x) * 4;
                            o[d] = px[s]; o[d + 1] = px[s + 1]; o[d + 2] = px[s + 2]; o[d + 3] = px[s + 3];
                        }
                    }
                }
        }
    }
}
