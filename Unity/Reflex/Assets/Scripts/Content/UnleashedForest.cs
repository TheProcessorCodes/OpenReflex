using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed partial class ReflexLevelContent
    {
        [Serializable] sealed class ImportedForestPlant { public int species; public float x,y,z,height,radius; }
        [Serializable] sealed class ImportedForestData { public int version; public string donor; public ImportedForestPlant[] instances; }
        string UnleashedResource(string name,string type)
        {
            if(!level.StartsWith("UNL_",StringComparison.Ordinal)||!ExperimentalTerrain.For(level))return null;
            if(Path.GetFileName(name)!=name||name.Contains("\\"))return null;
            string path=Path.Combine(ExperimentalTerrain.Path,"reflex-resource-"+name+"."+type);
            return RxFiles.Exists(path)?path:null;
        }
        bool StageUnleashedForest(List<Action> jobs)
        {
            if(!level.StartsWith("UNL_",StringComparison.Ordinal)||!ExperimentalTerrain.For(level))return false;
            string path=Path.Combine(ExperimentalTerrain.Path,"reflex-vegetation.json");
            if(!RxFiles.Exists(path))return false;
            jobs.Add(()=>
            {
                var data=JsonUtility.FromJson<ImportedForestData>(System.Text.Encoding.UTF8.GetString(RxFiles.ReadAllBytes(path)));
                if(data==null||data.version!=1||data.instances==null||data.instances.Length>100000)throw new InvalidDataException("Invalid replacement forest");
                var f=ParseForest(data.donor,RxFiles.ReadAllBytes(Path.Combine(ExperimentalTerrain.Path,"reflex-forest.bin")));
                // Spatial clusters retain the normal Reflex forest culling and GPU instance path.
                const int wide=20;var cells=new List<float>[wide*wide];var bounds=new Bounds[wide*wide];
                var total=new Bounds();bool first=true;
                foreach(var p in data.instances)
                {
                    if(p==null||p.species<0||p.species>=f.types.Count||!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)
                        ||!float.IsFinite(p.height)||p.height<=0||p.height>1000)throw new InvalidDataException("Invalid replacement tree");
                    var position=new Vector3(p.x,p.y,p.z);
                    if(first){total=new Bounds(position,Vector3.zero);first=false;}else total.Encapsulate(position);
                }
                foreach(var p in data.instances)
                {
                    var t=f.types[p.species];float height=t.billboard[1];
                    if(height<=0)throw new InvalidDataException("Invalid donor tree height");
                    float scale=p.height/height;
                    int x=Mathf.Clamp((int)((p.x-total.min.x)/Mathf.Max(1,total.size.x)*wide),0,wide-1);
                    int z=Mathf.Clamp((int)((p.z-total.min.z)/Mathf.Max(1,total.size.z)*wide),0,wide-1),cell=z*wide+x;
                    float rotation=QuantiseRotation((p.x*.173f+p.z*.317f)%(2*Mathf.PI));TreeSinCos(rotation,out float sn,out float cs);
                    var b=new Bounds(new Vector3(p.x,p.y+p.height*.5f,p.z),new Vector3(t.billboard[0]*scale,p.height,t.billboard[0]*scale));
                    if(cells[cell]==null){cells[cell]=new List<float>();bounds[cell]=b;}else bounds[cell].Encapsulate(b);
                    cells[cell].AddRange(new[]{p.x,p.y,p.z,p.species+.5f,scale,rotation,sn,cs});
                }
                var trees=new List<float>();f.clusterStart=new int[cells.Length];f.clusterCount=new int[cells.Length];
                f.clusterCenter=new Vector3[cells.Length];f.clusterHalf=new Vector3[cells.Length];
                for(int i=0;i<cells.Length;i++)
                {
                    f.clusterStart[i]=trees.Count/8;
                    if(cells[i]!=null){trees.AddRange(cells[i]);total.Encapsulate(bounds[i]);}
                    f.clusterCount[i]=trees.Count/8-f.clusterStart[i];f.clusterCenter[i]=bounds[i].center;f.clusterHalf[i]=bounds[i].extents;
                }
                f.trees=trees.ToArray();f.treeCount=trees.Count/8;f.clustersWide=f.clustersHigh=wide;
                f.totalCenter=total.center;f.totalHalf=total.extents;forest=f;
                Debug.Log($"[Unleashed] Reflex scenery: {data.donor}, {f.treeCount} replacement plants");
            });
            return true;
        }
    }
}
