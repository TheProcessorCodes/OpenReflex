using System;
using System.IO;
using System.Linq;
using System.Threading;
using Reflex.Port.Content;
using UnityEditor;
using UnityEngine;

namespace Reflex.Port.EditorTools
{
    // Batch bridge for source conversion tools: all compression, dedupe, validation
    // and publication is performed by the same library packer as PACK GAME FILES.
    public static class UnleashedImportBuild
    {
        public static void PackBatch()
        {
            try { Pack(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
        public static void PackAndBuildMacBatch()
        {
            try { Pack(); }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); return; }
            OpenReflexBuild.BuildMacBatch();
        }
        static void Pack()
        {
            string source = Environment.GetEnvironmentVariable("REFLEX_UNLEASHED_STAGE");
            if (string.IsNullOrEmpty(source)) throw new InvalidOperationException("Set REFLEX_UNLEASHED_STAGE to the converted library.");
            source = Path.GetFullPath(source);
            Reflex.Port.RxCore.RxNativeLoader.Load(Environment.GetEnvironmentVariable("REFLEX_RXCORE_PATH"));
            string assets = Path.Combine(source,"Assets","unleashed"), tracks=Path.Combine(source,"Tracks");
            Directory.CreateDirectory(tracks);
            var levels=Directory.GetDirectories(assets).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(levels.Length==0)throw new InvalidDataException("No converted Unleashed levels.");
            foreach(string folder in levels)
            {
                string level=Path.GetFileName(folder);
                var info=JsonUtility.FromJson<UnleashedLevels.Entry>(File.ReadAllText(Path.Combine(folder,"level.json")));
                if(info==null||info.version!=1||info.level!=level)throw new InvalidDataException("Invalid converted level: "+level);
                // A self-contained archive per level can be installed into an existing
                // library without replacing that library's shared Assets.rxdata pool.
                string staging=Path.Combine(source,".packing",level);
                Directory.CreateDirectory(Path.Combine(staging,"Database"));
                string targetAssets=Path.Combine(staging,"Assets","unleashed",level);Directory.CreateDirectory(targetAssets);
                foreach(string file in Directory.GetFiles(folder))File.Copy(file,Path.Combine(targetAssets,Path.GetFileName(file)),true);
                foreach(string ext in new[]{"database","level","scene"})
                    File.Copy(Path.Combine(source,"Database",level+".dx9."+ext),Path.Combine(staging,"Database",level+".dx9."+ext),true);
                File.WriteAllText(Path.Combine(staging,ReflexAssetImport.ExtractedMarker),"OPEN REFLEX extracted assets v1\n");
                var result=ReflexDataPack.Pack(staging,null,CancellationToken.None,removeLoose:true).GetAwaiter().GetResult();
                string archive=Path.Combine(staging,"Tracks",level+".rxtrack");
                if(!File.Exists(archive))throw new InvalidDataException("Packer did not produce "+level);
                File.Copy(archive,Path.Combine(tracks,level+".rxtrack"),true);
                Debug.Log("[Unleashed] Packed and read-back verified "+level+" ("+result.packedBytes+" bytes)");
            }
            Debug.Log("[Unleashed] Packed "+levels.Length+" levels through ReflexDataPack.");
        }
    }
}
