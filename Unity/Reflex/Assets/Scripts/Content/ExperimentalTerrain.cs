using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
namespace Reflex.Port.Content
{
    public static class ExperimentalTerrain
    {
        [Serializable] public sealed class Metadata { public string format, name, kind, id; public int version, raceVersion, defaultScale;
            public float[] course;
            /// The hard ground layer's own picture: the id of a texture in the package's scenery (0: Reflex's slate) and its tiling (an Untamed venue's asphalt / concrete).
            public int groundHardTexture; public float groundHardTile; }
        public static string Path => UnleashedLevels.Bound != null ? UnleashedLevels.Folder(UnleashedLevels.Bound.level) : TrackMods.Bound?.folder;
        static bool Imported(string path) => !string.IsNullOrEmpty(path) && RxFiles.Exists(System.IO.Path.Combine(path, "level.json"));
        static byte[] Asset(string path, string file) => RxFiles.ReadAllBytes(System.IO.Path.Combine(path, file));
        public static Metadata Info(string path)
        {
            if (Imported(path)) return new Metadata { format="reflex-imported-level", version=1, course=Array.Empty<float>() };
            using var stream=new GZipStream(File.OpenRead(path),CompressionMode.Decompress);
            using var r=new BinaryReader(stream);
            if(r.ReadUInt32()!=0x4b505852 || !PackageVersion(r.ReadInt32())) throw new InvalidDataException("Unsupported RXPK");
            int n=r.ReadInt32(); if(n<2||n>65536) throw new InvalidDataException("Invalid metadata size");
            var m=JsonUtility.FromJson<Metadata>(Encoding.UTF8.GetString(r.ReadBytes(n)));
            if(m==null||m.format!="reflex-mod-track"||m.version!=1) throw new InvalidDataException("Invalid metadata");
            return m;
        }
        public static int Spacing(string path)
        {
            if (Imported(path)) { using var importedReader = new BinaryReader(RxFiles.OpenRead(System.IO.Path.Combine(path, "terrain.rxheight"))); importedReader.BaseStream.Position=12; return ReadSpacing(importedReader); }
            using var stream=new GZipStream(File.OpenRead(path),CompressionMode.Decompress);
            using var r=new BinaryReader(stream);
            if(r.ReadUInt32()!=0x4b505852 || !PackageVersion(r.ReadInt32()))throw new InvalidDataException("Unsupported RXPK");
            int n=r.ReadInt32();if(n<2||n>65536)throw new InvalidDataException("Invalid metadata");
            if(r.ReadBytes(n).Length!=n)throw new EndOfStreamException();
            if(r.ReadUInt32()!=0x314d5852||r.ReadInt32()!=1||r.ReadInt32()!=2048)throw new InvalidDataException("Invalid terrain");
            return ReadSpacing(r);
        }
        static int ReadSpacing(BinaryReader r) { float s=r.ReadSingle();if(s!=2 && s!=4 && s!=8 && s!=16)throw new InvalidDataException("Invalid terrain spacing");return (int)s; }
        public static Texture2D CourseMask(string path)
        {
            const int n=2048; var pixels=new Color32[n*n];
            for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(0,255,0,0);
            var c=Info(path).course;float spacing=Spacing(path),half=1023.5f*spacing;
            if(c==null||c.Length%5!=0||c.Length>20000) throw new InvalidDataException("Invalid course");
            for(int j=0;j<c.Length;j+=5)
            {
                int k=(j+5)%c.Length;
                var a=new Vector2((c[j]+half)/spacing,(half-c[j+2])/spacing);
                var b=new Vector2((c[k]+half)/spacing,(half-c[k+2])/spacing);
                float radius=Mathf.Clamp(Mathf.Max(c[j+3],c[k+3])/(2*spacing),4,40); var d=b-a;
                int x0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius)), x1=Mathf.Min(n-1,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius));
                int y0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius)), y1=Mathf.Min(n-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius));
                for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
                {
                    var q=new Vector2(x,y); float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude):0;
                    float dist=(q-a-t*d).magnitude;
                    byte green=(byte)(255*Mathf.Clamp01(dist-radius+1));
                    if(green<pixels[y*n+x].g) pixels[y*n+x].g=green;
                }
            }
            var texture=new Texture2D(n,n,TextureFormat.RGBA32,true,true){name="MTX course material mask",wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels32(pixels);texture.Apply(true,true);return texture;
        }
        public static bool For(string level) => Active && string.Equals(level, UnleashedLevels.Bound?.level ?? TrackMods.Bound?.level, StringComparison.OrdinalIgnoreCase);
        public static bool Active => UnleashedLevels.Bound != null || TrackMods.Bound?.experimentalTerrain == true;
        static bool PackageVersion(int v) => v>=1 && v<=4;
        static byte[] ReadScenerySection(BinaryReader r)
        {
            if(r.ReadUInt32()!=0x454e4353) throw new InvalidDataException("Missing scenery section");
            int n=r.ReadInt32(); if(n<8 || n>128*1024*1024) throw new InvalidDataException("Invalid scenery length");
            var bytes=r.ReadBytes(n);if(bytes.Length!=n) throw new EndOfStreamException("Truncated scenery");return bytes;
        }
        static void ReadCollisionSection(BinaryReader r)
        {
            if(r.ReadUInt32()!=0x4c4c4f43)throw new InvalidDataException("Missing collision section");
            int n=r.ReadInt32();if(n<12||n>32*1024*1024)throw new InvalidDataException("Invalid collision length");
            while(n>0){int count=Math.Min(n,8192);if(r.ReadBytes(count).Length!=count)throw new EndOfStreamException();n-=count;}
        }
        /// A texture of the package's scenery by id as RGBA8 (the scenery's row order), null if it has none.
        public static (int w, int h, byte[] px)? SceneryTexture(string path, uint id)
        {
            var data = Scenery(path); if (data == null || id == 0) return null;
            using var r = new BinaryReader(new MemoryStream(data, false));
            if (r.ReadInt32() != 0x43535852) return null; r.ReadInt32();
            int n = r.ReadInt32(); if (n < 0 || n >= 8192) return null;
            for (int i = 0; i < n; i++)
            {
                uint tid = r.ReadUInt32(); int w = r.ReadInt32(), h = r.ReadInt32();
                if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || (long)w * h * 4 > r.BaseStream.Length - r.BaseStream.Position) return null;
                if (tid == id) return (w, h, r.ReadBytes(w * h * 4));
                r.BaseStream.Position += (long)w * h * 4;
            }
            return null;
        }
        public static byte[] Scenery(string path)
        {
            if (Imported(path)) return Asset(path,"scenery.rxscenery");
            using var stream=new GZipStream(File.OpenRead(path),CompressionMode.Decompress);
            using var r=new BinaryReader(stream);
            if(r.ReadUInt32()!=0x4b505852) throw new InvalidDataException("Invalid RXPK");
            int version=r.ReadInt32(); if(!PackageVersion(version)) throw new InvalidDataException("Unsupported RXPK version");
            if(version==1) return null;
            int n=r.ReadInt32(); if(n<2||n>65536) throw new InvalidDataException("Invalid metadata length");
            long skip=n+40L+2048L*2048*4;
            while(skip>0) { int count=(int)Math.Min(skip,8192);if(r.ReadBytes(count).Length!=count)throw new EndOfStreamException();skip-=count; }
            var data=ReadScenerySection(r);ReadCollisionSection(r);if(version>=3)ReadSurface(r);if(version>=4)ReadColour(r);ReadRaceEnd(r);return data;
        }
        static byte[] ReadSurface(BinaryReader r)
        {
            if(r.ReadUInt32()!=0x46525553 || r.ReadUInt32()!=12+2048*2048*4 || r.ReadUInt32()!=0x4d535852 || r.ReadUInt32()!=1 || r.ReadUInt32()!=2048)
                throw new InvalidDataException("Invalid surface map");
            var bytes=r.ReadBytes(2048*2048*4);if(bytes.Length!=2048*2048*4)throw new EndOfStreamException();
            for(int i=0;i<bytes.Length;i+=4)if(bytes[i]+bytes[i+1]+bytes[i+2]>255)throw new InvalidDataException("Invalid surface weights");return bytes;
        }
        public static byte[] Surface(string path) => Maps(path,false);
        public static byte[] Colour(string path) => Maps(path,true);
        static byte[] Maps(string path,bool colour)
        {
            if (Imported(path)) {
                if (!colour) {
                    string surfacePath=System.IO.Path.Combine(path,"surface.rxsurface");
                    if (!RxFiles.Exists(surfacePath)) return new byte[2048*2048*4];
                    using var reader=new BinaryReader(RxFiles.OpenRead(surfacePath));
                    if(reader.ReadUInt32()!=0x4d535852 || reader.ReadUInt32()!=1 || reader.ReadUInt32()!=2048 || reader.BaseStream.Length!=12+2048*2048*4)
                        throw new InvalidDataException("Invalid imported surface map");
                    var weights=reader.ReadBytes(2048*2048*4);
                    for(int i=0;i<weights.Length;i+=4)if(weights[i]+weights[i+1]+weights[i+2]>255)throw new InvalidDataException("Invalid surface weights");
                    return weights;
                }
                var bytes=Asset(path,"colour.rxcolour");
                if(bytes.Length!=12+2048*2048*4 || BitConverter.ToUInt32(bytes,0)!=0x4d435852 || BitConverter.ToInt32(bytes,4)!=1 || BitConverter.ToInt32(bytes,8)!=2048) throw new InvalidDataException("Invalid imported colour map");
                var pixels=new byte[bytes.Length-12];Buffer.BlockCopy(bytes,12,pixels,0,pixels.Length);return pixels;
            }
            using var stream=new GZipStream(File.OpenRead(path),CompressionMode.Decompress);
            using var r=new BinaryReader(stream);
            if(r.ReadUInt32()!=0x4b505852)throw new InvalidDataException("Invalid RXPK");
            int v=r.ReadInt32();if(!PackageVersion(v))throw new InvalidDataException("Unsupported RXPK");
            if(v<3 || (colour && v<4))return null;
            int n=r.ReadInt32();if(n<2||n>65536)throw new InvalidDataException("Invalid metadata");
            long skip=n+40L+2048L*2048*4;
            while(skip>0){int count=(int)Math.Min(skip,8192);if(r.ReadBytes(count).Length!=count)throw new EndOfStreamException();skip-=count;}
            ReadScenerySection(r);ReadCollisionSection(r);var data=ReadSurface(r);
            if(v>=4){var rgb=ReadColour(r);if(colour)data=rgb;}
            ReadRaceEnd(r);return data;
        }
        static bool ReadRaceEnd(BinaryReader r)
        {
            int first=r.BaseStream.ReadByte();if(first<0)return false;
            if(first!='R'||r.ReadByte()!='A'||r.ReadByte()!='C'||r.ReadByte()!='E')throw new InvalidDataException("Invalid race section");
            uint size=r.ReadUInt32();
            if(size<100||size>164000||r.ReadUInt32()!=0x41525852||r.ReadUInt32()!=1||r.ReadUInt32()>1)throw new InvalidDataException("Invalid RXRA");
            uint lap=r.ReadUInt32(),lead=r.ReadUInt32();
            if(lap<10||lap>4096||lead<2||lead>4096||size!=100+20*(lap+lead))throw new InvalidDataException("Invalid race route");
            for(uint i=0;i<20+5*(lap+lead);i++) { float f=r.ReadSingle();if(!float.IsFinite(f)||Mathf.Abs(f)>100000)throw new InvalidDataException("Invalid race point"); }
            if(r.BaseStream.ReadByte()!=-1)throw new InvalidDataException("Unexpected package data");return true;
        }
        static byte[] ReadColour(BinaryReader r)
        {
            if(r.ReadUInt32()!=0x524c4f43 || r.ReadUInt32()!=12+2048*2048*4 || r.ReadUInt32()!=0x4d435852 || r.ReadUInt32()!=1 || r.ReadUInt32()!=2048)
                throw new InvalidDataException("Invalid terrain colour map");
            var data=r.ReadBytes(2048*2048*4);if(data.Length!=2048*2048*4)throw new EndOfStreamException();return data;
        }
        public static float[] Read(string folder)
        {
            if(Imported(folder)) {
                using var reader=new BinaryReader(RxFiles.OpenRead(System.IO.Path.Combine(folder,"terrain.rxheight")));
                if(reader.BaseStream.Length!=40+2048*2048*4 || reader.ReadUInt32()!=0x314d5852 || reader.ReadInt32()!=1 || reader.ReadInt32()!=2048)throw new InvalidDataException("Invalid imported terrain");
                ReadSpacing(reader);for(int i=0;i<6;i++)if(!float.IsFinite(reader.ReadSingle()))throw new InvalidDataException("Invalid imported spawn");
                var heights=new float[2048*2048];for(int i=0;i<heights.Length;i++){heights[i]=reader.ReadSingle()/2048f;if(!float.IsFinite(heights[i]))throw new InvalidDataException("Invalid imported height");}return heights;
            }
            using var compressed = new GZipStream(File.OpenRead(folder),CompressionMode.Decompress);
            using var r = new BinaryReader(compressed);
            if(r.ReadUInt32()!=0x4b505852) throw new InvalidDataException("Unsupported RXPK package");
            int version=r.ReadInt32(); if(!PackageVersion(version)) throw new InvalidDataException("Unsupported RXPK version");
            int count=r.ReadInt32(); if(count<2||count>65536) throw new InvalidDataException("Invalid metadata");
            var metadataBytes=r.ReadBytes(count);if(metadataBytes.Length!=count)throw new EndOfStreamException();
            var metadata=JsonUtility.FromJson<Metadata>(Encoding.UTF8.GetString(metadataBytes));
            if(metadata==null || metadata.format!="reflex-mod-track" || metadata.version!=1 || metadata.raceVersion<0 || metadata.raceVersion>1)throw new InvalidDataException("Invalid metadata");
            if (r.ReadUInt32()!=0x314d5852 || r.ReadInt32()!=1 || r.ReadInt32()!=2048)
                throw new InvalidDataException("Unsupported RXM1 terrain");
            ReadSpacing(r);
            for(int i=0;i<6;i++) if(!float.IsFinite(r.ReadSingle())) throw new InvalidDataException("Invalid spawn");
            var h=new float[2048*2048];
            for(int i=0;i<h.Length;i++) { h[i]=r.ReadSingle()/2048f; if(!float.IsFinite(h[i])) throw new InvalidDataException("Invalid height"); }
            if(version>=2) { ReadScenerySection(r); ReadCollisionSection(r); }
            if(version>=3) ReadSurface(r);
            if(version>=4) ReadColour(r);
            if(ReadRaceEnd(r)!=(metadata.raceVersion==1))throw new InvalidDataException("Race metadata does not match package");
            return h;
        }
    }
}
