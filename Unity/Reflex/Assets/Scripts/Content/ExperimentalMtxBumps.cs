using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Reflex.Port.Rendering;

namespace Reflex.Port.Content
{
    public sealed partial class ReflexLevelContent
    {
        Material mtxTerrainMaterial;

        [Serializable] sealed class UnleashedRutData { public float[] segments; }
        Mesh BuildUnleashedRuts(float scale)
        {
            string path=System.IO.Path.Combine(ExperimentalTerrain.Path,"ruts.json");
            if(!RxFiles.Exists(path)) return null;
            var data=JsonUtility.FromJson<UnleashedRutData>(System.Text.Encoding.UTF8.GetString(RxFiles.ReadAllBytes(path))).segments;
            Rfx.Check(data!=null && data.Length%6==0 && data.Length<600000,"Invalid Unleashed rut segments");
            if(data.Length==0)return null;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();
            float distance=0;
            for(int s=0;s<data.Length;s+=6)
            {
                var a=new Vector3(data[s],0,data[s+1])*scale;var b=new Vector3(data[s+3],0,data[s+4])*scale;
                var forward=b-a;float length=forward.magnitude;if(length<.01f)continue;
                var side=new Vector3(forward.z,0,-forward.x)/length;
                int start=vertices.Count;
                for(int k=0;k<2;k++)for(int j=0;j<3;j++)
                {
                    float offset=(j-1)*data[s+k*3+2]*scale;
                    vertices.Add((k==0?a:b)+side*offset);uv.Add(new Vector2(offset/24f+.5f,1-(distance+k*length)/48f));
                    colors.Add(new Color(1,1,1,j==1?(UnleashedLevels.Style?.rut_strength ?? .65f):0));
                }
                for(int j=0;j<2;j++){int v=start+j;indices.AddRange(new[]{v,v+3,v+1,v+1,v+3,v+4});}
                distance+=length;
            }
            var mesh=new Mesh{name="Unleashed source course rut strips",indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.DontSave};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();Track(mesh);return mesh;
        }

        // The same three layers as a native Reflex venue: surface bump, authored rut
        // overlays, then deformation reveal. The compressed mod supplies the course.
        List<ReflexRTBump.Draw> BuildMtxBumpModels(Shader shader)
        {
            var draws=new List<ReflexRTBump.Draw>();
            bool unleashed = UnleashedLevels.Bound != null;
            Rfx.Check(mtxTerrainMaterial,"MTX terrain must be built before its bump overlays");
            float scale=Reflex.Port.RxCore.RxMapScale.Current,span=(unleashed ? 2047f*terrainData.spacing : 4094f)*scale,half=span*.5f;
            bool paradise=TrackMods.Bound?.id!="McKennaRock";
            Texture2D Tex(string name)
            {
                var t=TerrainTexture(name,terrainData.material.samplerStates[1],DetailAniso);
                Rfx.Check(t,"Missing MTX bump texture "+name);return t;
            }
            Material Mat(string pass)
            {
                var m=new Material(shader){name="MTX "+pass,hideFlags=HideFlags.DontSave};
                m.SetFloat("_Cull",(float)CullMode.Off);Track(m);return m;
            }
            var quad=new Mesh{name="MTX terrain bump coverage",hideFlags=HideFlags.DontSave};
            quad.vertices=new[]{new Vector3(-half,0,-half),new Vector3(half,0,-half),new Vector3(-half,0,half),new Vector3(half,0,half)};
            quad.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
            quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            quad.triangles=new[]{0,2,1,1,2,3};quad.RecalculateBounds();Track(quad);
            void Add(Mesh mesh,Material material,string pass,string name)
            {
                int index=material.FindPass(pass);Rfx.Check(index>=0,"MTX bump shader pass "+pass);
                draws.Add(new ReflexRTBump.Draw{mesh=mesh,material=material,pass=index,matrix=Matrix4x4.identity,name=name});
            }
            var basic=Mat("HFB_4tex");
            basic.SetFloat("_UnleashedDetail",unleashed?1:0);
            basic.SetTexture("_Bump0",Tex(paradise?"NAT_BE_Dirt01_BumpNew":"NAT_CO_B_SandBUMP"));
            basic.SetTexture("_Bump1",Tex(paradise?"NAT_BE_Dirt02_Bump":"NAT_CO_B_DirtLightBUMP"));
            basic.SetTexture("_Bump2",Tex("NAT_CO_B_SandInsideBUMP"));
            basic.SetTexture("_Bump3",Tex("NAT_CO_B_GrassBUMPS"));
            if (unleashed && UnleashedLevels.Style != null)
                for(int i=0;i<4;i++) basic.SetTexture("_Bump"+i,Tex(UnleashedLevels.Style.layers[i].bump));
            basic.SetTexture("_Mask",mtxTerrainMaterial.GetTexture("_DetailMask"));
            basic.SetVector("_TileRate",mtxTerrainMaterial.GetVector("_DetailTile"));
            Add(quad,basic,"HFB_4tex","MTX surface relief");

            if (unleashed)
            {
                var rutMesh=BuildUnleashedRuts(scale);
                if(rutMesh)
                {
                    var rutMat=Mat("MTX_Ruts");rutMat.SetTexture("_Bump0",Tex("NAT_CO_B_Ruts"));
                    rutMat.SetTexture("_Mask",mtxTerrainMaterial.GetTexture("_DetailMask"));rutMat.SetFloat("_TerrainSize",span);rutMat.SetFloat("_Tuner",UnleashedLevels.Style?.cover=="snow"?0:1);
                    Add(rutMesh,rutMat,"MTX_Ruts","Unleashed authored track ruts");
                }
                var deformation=Mat("HFB_1tex_deformed");deformation.SetTexture("_Bump0",Tex("HFT_Default_Deform_Reveal"));
                deformation.SetVector("_TileRate",Vector4.one*(160*terrainData.spacing*scale));
                Add(quad,deformation,"HFB_1tex_deformed","Unleashed live deformation reveal");
                Debug.Log($"[Unleashed] Terrain depth: {draws.Count} Reflex bump layers");
                return draws;
            }
            var course=ExperimentalTerrain.Info(ExperimentalTerrain.Path).course;
            Rfx.Check(course!=null && course.Length>=20 && course.Length%5==0,"MTX rut course");
            int count=course.Length/5;
            Vector3 Point(int i) { i=(i%count+count)%count;return new Vector3(course[i*5],0,course[i*5+2])*scale; }
            var centers=new List<Vector3>();var widths=new List<float>();
            for(int i=0;i<count;i++)
            {
                var a=Point(i-1);var b=Point(i);var c=Point(i+1);var d=Point(i+2);
                int steps=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(b,c)/8f));
                for(int k=0;k<steps;k++)
                {
                    float t=k/(float)steps,t2=t*t,t3=t2*t;
                    centers.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t2+(-a+3*b-3*c+d)*t3));
                    widths.Add(Mathf.Clamp(Mathf.Lerp(course[i*5+3],course[((i+1)%count)*5+3],t)*.4f,12,42)*scale);
                }
            }
            int rows=centers.Count;var distance=new float[rows+1];
            for(int i=1;i<=rows;i++)distance[i]=distance[i-1]+Vector3.Distance(centers[i%rows],centers[i-1]);
            float repeats=Mathf.Max(1,Mathf.Round(distance[rows]/48f));
            const int columns=9;
            var vertices=new Vector3[(rows+1)*columns];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];
            var triangles=new int[rows*(columns-1)*6];int at=0;
            for(int i=0;i<=rows;i++)
            {
                int r=i%rows;
                var forward=(centers[(r+1)%rows]-centers[(r+rows-1)%rows]).normalized;
                var side=new Vector3(forward.z,0,-forward.x);
                for(int j=0;j<columns;j++)
                {
                    float u=j/(float)(columns-1),offset=(2*u-1)*widths[r];int v=i*columns+j;
                    vertices[v]=centers[r]+side*offset;
                    // Keep groove spacing in world feet as the track is resized. The V seam repeats exactly.
                    uv[v]=new Vector2(offset/24f+.5f,1-distance[i]/distance[rows]*repeats);
                    colors[v]=new Color(1,1,1,.75f*Mathf.SmoothStep(0,1,Mathf.Min(u,1-u)*5));
                    if(i<rows && j<columns-1)
                    {
                        triangles[at++]=v;triangles[at++]=v+columns;triangles[at++]=v+1;
                        triangles[at++]=v+1;triangles[at++]=v+columns;triangles[at++]=v+columns+1;
                    }
                }
            }
            var ribbon=new Mesh{name="MTX course-following rut ribbons",hideFlags=HideFlags.DontSave,indexFormat=IndexFormat.UInt32};
            ribbon.vertices=vertices;ribbon.uv=uv;ribbon.colors=colors;ribbon.triangles=triangles;ribbon.RecalculateBounds();Track(ribbon);
            var ruts=Mat("MTX_Ruts");ruts.SetTexture("_Bump0",Tex("NAT_CO_B_Ruts"));
            ruts.SetTexture("_Mask",mtxTerrainMaterial.GetTexture("_DetailMask"));ruts.SetFloat("_TerrainSize",span);
            // McKenna's legacy mask uses green for off-course; Paradise's blue/green are grass/rock.
            ruts.SetFloat("_Tuner",paradise?1:0);
            Add(ribbon,ruts,"MTX_Ruts","MTX course ruts");
            var reveal=Mat("HFB_1tex_deformed");reveal.SetTexture("_Bump0",Tex("HFT_Default_Deform_Reveal"));
            reveal.SetVector("_TileRate",Vector4.one*(160*2*scale));
            Add(quad,reveal,"HFB_1tex_deformed","MTX live deformation reveal");
            Debug.Log($"[MTX] Terrain depth: {draws.Count} native bump layers, {rows} course sections, {triangles.Length/3} rut triangles");
            return draws;
        }
    }
}
