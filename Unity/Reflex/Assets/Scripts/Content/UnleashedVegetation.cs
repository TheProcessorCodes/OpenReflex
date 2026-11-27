using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Reflex.Port.Rendering;

namespace Reflex.Port.Content
{
    // Source ecosystem meshes nearby, source atlas billboards only in the distance.
    // Meshes/materials are shared. Only nearby instances acquire pooled scene objects.
    public sealed class UnleashedVegetation : MonoBehaviour
    {
        [Serializable] sealed class Plant { public int species; public float x,y,z,height,radius; }
        [Serializable] sealed class Data { public int version; public string[] groups; public Plant[] instances; }
        Data data;
        bool reported;
        public int ActiveModelCount => active.Count;
        Transform[] templates;
        Stack<GameObject>[] pool;
        readonly Dictionary<int,GameObject> active = new Dictionary<int,GameObject>();
        readonly Dictionary<GameObject,MeshRenderer[]> renderers = new Dictionary<GameObject,MeshRenderer[]>();
        readonly List<int> release = new List<int>();
        static readonly int CameraId=Shader.PropertyToID("_UnleashedTreeCamera"), RangeId=Shader.PropertyToID("_UnleashedTreeRange");

        internal static void Attach(Transform root,List<UnityEngine.Object> owned)
        {
            if(UnleashedLevels.Bound==null)return;
            string path=Path.Combine(ExperimentalTerrain.Path,"vegetation.json");
            if(!RxFiles.Exists(path))return;
            var d=JsonUtility.FromJson<Data>(System.Text.Encoding.UTF8.GetString(RxFiles.ReadAllBytes(path)));
            if(d==null||d.version!=1||d.groups==null||d.groups.Length>256||d.instances==null||d.instances.Length>100000)
                throw new InvalidDataException("Invalid Unleashed ecosystem");
            if(d.groups.Length==0)return;
            foreach(var p in d.instances)
                if(p==null||p.species<0||p.species>=d.groups.Length||!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)
                    ||!float.IsFinite(p.height)||!float.IsFinite(p.radius)||p.height<=0||p.radius<=0||p.height>1000||p.radius>1000)
                    throw new InvalidDataException("Invalid Unleashed plant instance");
            var component=root.gameObject.AddComponent<UnleashedVegetation>();component.data=d;
            component.templates=new Transform[d.groups.Length];component.pool=new Stack<GameObject>[d.groups.Length];
            var materials=new Dictionary<Material,Material>();
            for(int i=0;i<d.groups.Length;i++)
            {
                var t=root.Find(d.groups[i]);if(!t)throw new InvalidDataException("Missing ecosystem model "+d.groups[i]);
                component.templates[i]=t;component.pool[i]=new Stack<GameObject>();
                foreach(var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var original=r.sharedMaterial;
                    if(!materials.TryGetValue(original,out var m))
                    {
                        m=new Material(original){name=original.name+" 3D vegetation",enableInstancing=true};
                        m.SetFloat("_TreeLod",1);materials[original]=m;owned.Add(m);
                    }
                    r.sharedMaterial=m;r.receiveShadows=true;
                }
            }
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var m=r.sharedMaterial;
                // (the plants' distance cards; not an additive camera-facing flare)
                if(m&&m.HasProperty("_Billboard")&&m.GetFloat("_Billboard")>.5f&&m.GetFloat("_DstBlend")!=(float)BlendMode.One)m.SetFloat("_TreeLod",2);
            }
            Debug.Log($"[Unleashed] Ecosystem: {d.groups.Length} source 3D models, {d.instances.Length} plants; near meshes / distant billboards");
        }
        void OnEnable()=>RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        void OnDisable()=>RenderPipelineManager.beginCameraRendering-=BeforeCamera;
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(data==null||!camera||camera.cameraType!=CameraType.Game)return;
            float scale=Reflex.Port.RxCore.RxMapScale.Current,near=900*scale,far=1100*scale;
            Shader.SetGlobalVector(CameraId,camera.transform.position);
            // z: the cross-fade dither's frame offset (ReflexMtxScenery.shader TreeClip): it moves only while the TAA resolves it
            var taa=ReflexTemporalAA.Active;
            Shader.SetGlobalVector(RangeId,new Vector4(near,far,taa&&taa.active?Time.frameCount%64:0,0));
            var eye=transform.InverseTransformPoint(camera.transform.position);
            release.Clear();
            foreach(var kv in active)
            {
                var p=data.instances[kv.Key];float dx=p.x-eye.x,dz=p.z-eye.z;
                if(dx*dx+dz*dz>1100*1100)release.Add(kv.Key);
            }
            foreach(int i in release){var go=active[i];go.SetActive(false);pool[data.instances[i].species].Push(go);active.Remove(i);}
            for(int i=0;i<data.instances.Length;i++)
            {
                var p=data.instances[i];float dx=p.x-eye.x,dz=p.z-eye.z;
                if(dx*dx+dz*dz>1100*1100||active.ContainsKey(i))continue;
                var go=pool[p.species].Count>0?pool[p.species].Pop():Instantiate(templates[p.species].gameObject,transform);
                go.name="3D plant "+i;go.transform.localPosition=new Vector3(p.x,p.y,p.z);
                go.transform.localRotation=Quaternion.identity;go.transform.localScale=new Vector3(p.radius,p.height,p.radius);
                if(!renderers.ContainsKey(go))renderers[go]=go.GetComponentsInChildren<MeshRenderer>(true);
                go.SetActive(true);active[i]=go;
            }
            if(!reported && active.Count>0){Debug.Log($"[Unleashed] Active 3D vegetation: {active.Count} pooled model instances");reported=true;}
            var mode=ReflexEnhancedLighting.Requested?ShadowCastingMode.On:ShadowCastingMode.Off;
            foreach(var go in active.Values)foreach(var r in renderers[go])r.shadowCastingMode=mode;
        }
    }
}
