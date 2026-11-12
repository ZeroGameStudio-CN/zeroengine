using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace ZeroEngine.Render
{
    public sealed class BloodVisualResources : IDisposable
    {
        private readonly Mesh[] meshes=new Mesh[3],puddles=new Mesh[3],volumes=new Mesh[3],debrisMeshes=new Mesh[6];
        private readonly Mesh[,] impactShapes=new Mesh[3,3];
        private readonly Stack<Texture2D> lookupPool=new(128);
        private Material material,shadowMaterial,impactMaterial,impactShadow,surfaceMaterial;
        // Borrowed authored input; the caller retains the asset and its dependencies.
        public ParticleSystem AuthoredSprayTemplate {get;set;}
        public Shader SurfaceShader {get;}
        private Shader SpatialShader {get;}
        public BloodVisualResources(Shader surface, Shader spatial, ParticleSystem authoredSprayTemplate=null)
        {
            SurfaceShader=surface;SpatialShader=spatial;AuthoredSprayTemplate=authoredSprayTemplate;
        }
        private static Material Make(Shader shader,string name,int queue)
        {
            if(!shader || !shader.isSupported)throw new InvalidOperationException("Runtime blood shader unavailable");
            return new Material(shader){name=name,hideFlags=HideFlags.HideAndDontSave,renderQueue=queue};
        }
        public Material DropMaterial {get{if(!material){material=Make(SpatialShader,"World pixel blood",2040);material.SetFloat("_ZWrite",1);}return material;}}
        public Material ShadowMaterial {get{if(!shadowMaterial){shadowMaterial=new Material(DropMaterial){name="World blood contact",hideFlags=HideFlags.HideAndDontSave,renderQueue=2999};shadowMaterial.SetFloat("_Shadow",1);shadowMaterial.SetFloat("_ZWrite",0);}return shadowMaterial;}}
        public Material ImpactMaterial {get{if(!impactMaterial){impactMaterial=Make(SpatialShader,"Actor pixel blood",2040);impactMaterial.SetFloat("_ZWrite",1);}return impactMaterial;}}
        public Material ImpactShadow {get{if(!impactShadow){impactShadow=new Material(ImpactMaterial){name="Actor blood contact",hideFlags=HideFlags.HideAndDontSave,renderQueue=2030};impactShadow.SetFloat("_Shadow",1);impactShadow.SetFloat("_ZWrite",0);}return impactShadow;}}
        public Material SurfaceMaterial=>surfaceMaterial?surfaceMaterial:surfaceMaterial=Make(SurfaceShader,"Actor blood surface",3005);
        public Mesh DropMeshFor(int v)=>meshes[v]?meshes[v]:meshes[v]=MakeMesh(v);
        public Mesh PuddleMeshFor(int v)=>puddles[v]?puddles[v]:puddles[v]=MakePuddle(v);
        public Mesh VolumeMeshFor(int v)=>volumes[v]?volumes[v]:volumes[v]=MakeVolume(v);
        public Mesh DebrisMeshFor(int v,bool bone){int i=v+(bone?3:0);return debrisMeshes[i]?debrisMeshes[i]:debrisMeshes[i]=MakeVolume(v,bone?2:1);}
        public Mesh ImpactShape(int v,int kind)=>impactShapes[v,kind]?impactShapes[v,kind]:impactShapes[v,kind]=CreateOwnedBloodMesh(v,kind==1,kind==2);
        public Texture2D TakeLookup()
        {
            while(lookupPool.Count>0){var t=lookupPool.Pop();if(t)return t;}
            return new Texture2D(64,64,TextureFormat.RGBA32,false,true){name="Actor blood lookup",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        }
        public void ReturnLookup(Texture2D texture){if(!texture)return;if(lookupPool.Count<128)lookupPool.Push(texture);else DestroyOwned(texture);}
        public static void DestroyOwned(UnityEngine.Object value){if(!value)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        public void Dispose()
        {
            foreach(var v in meshes)DestroyOwned(v);foreach(var v in puddles)DestroyOwned(v);foreach(var v in volumes)DestroyOwned(v);foreach(var v in debrisMeshes)DestroyOwned(v);foreach(var v in impactShapes)DestroyOwned(v);
            Array.Clear(meshes,0,3);Array.Clear(puddles,0,3);Array.Clear(volumes,0,3);Array.Clear(debrisMeshes,0,6);Array.Clear(impactShapes,0,9);
            DestroyOwned(material);DestroyOwned(shadowMaterial);DestroyOwned(impactMaterial);DestroyOwned(impactShadow);DestroyOwned(surfaceMaterial);material=shadowMaterial=impactMaterial=impactShadow=surfaceMaterial=null;
            while(lookupPool.Count>0)DestroyOwned(lookupPool.Pop());
        }
        private static Mesh MakeMesh(int variant) => MakeStamp(variant, false);
        private static Mesh MakePuddle(int variant) => MakeStamp(variant, true);

        // Pure geometry only; callers own these meshes independently of this candidate's caches.
        public static Mesh CreateOwnedBloodMesh(int variant, bool volume, bool surface) =>
            surface ? MakePuddle(variant) : volume ? MakeVolume(variant) : MakeMesh(variant);

        // Closed stepped volumes for the few large clots; smaller spray retains pixel silhouettes.
        private static Mesh MakeVolume(int variant, int kind = 0)
        {
            const int size = 5;
            const float cell = .4f;
            var filled = new bool[size, size, size];
            for (int z = 0; z < size; z++)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector3(x + .5f, y + .5f, z + .5f) * cell - Vector3.one;
                        p.x += p.y * (variant - 1) * .22f;
                        float radius = .94f + .13f * Mathf.Sin(x * 2f + z + variant * 1.7f);
                        filled[x,y,z] = kind == 2
                            ? z == 2 && (y == 2 || (x == 0 || x == 4) && (y == 1 || y == 3))
                            : kind == 1
                                ? Mathf.Abs(p.x) + Mathf.Abs(p.y) * 1.4f + Mathf.Abs(p.z) * 1.2f < 1.45f
                                    && !(x == 1 && y > 2 && z > 2) && !(x > 2 && y < 2 && z < 2)
                                : p.x * p.x + p.y * p.y * .8f + p.z * p.z * 1.15f < radius * radius;
                    }
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            bool Has(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < size && y < size && z < size && filled[x,y,z];
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            for (int z = 0; z < size; z++)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        if (!filled[x,y,z]) continue;
                        var p = new Vector3(x, y, z) * cell - Vector3.one;
                        Vector3 P(int a, int b, int c) => p + new Vector3(a,b,c) * cell;
                        if (!Has(x,y,z - 1)) Quad(P(0,0,0), P(0,1,0), P(1,1,0), P(1,0,0));
                        if (!Has(x,y,z + 1)) Quad(P(0,0,1), P(1,0,1), P(1,1,1), P(0,1,1));
                        if (!Has(x - 1,y,z)) Quad(P(0,0,1), P(0,1,1), P(0,1,0), P(0,0,0));
                        if (!Has(x + 1,y,z)) Quad(P(1,0,0), P(1,1,0), P(1,1,1), P(1,0,1));
                        if (!Has(x,y - 1,z)) Quad(P(0,0,1), P(0,0,0), P(1,0,0), P(1,0,1));
                        if (!Has(x,y + 1,z)) Quad(P(0,1,0), P(0,1,1), P(1,1,1), P(1,1,0));
                    }
            return FlatMesh(kind == 2 ? "Pixel bone chip" : kind == 1 ? "Pixel flesh piece" : "Stepped volume blood clot",
                vertices, triangles, Vector3.zero);
        }

        // One pixel silhouette for both flight and landing, with only a shallow edge.
        private static Mesh MakeStamp(int variant, bool surface)
        {
            const int size = 8;
            const float cell = .25f, depth = .13f;
            var filled = new bool[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + .5f) * cell - 1f, py = (y + .5f) * cell - 1f;
                    float angle = Mathf.Atan2(py, px), phase = variant * 1.73f;
                    float radius = .8f + .18f * Mathf.Sin(angle * 3f + phase) + .09f * Mathf.Cos(angle * 5f - phase);
                    filled[x, y] = px * px + py * py < radius * radius;
                }
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 Point(float x, float y, float z)
            {
                if (!surface) return new(x, y, z);
                float rise = (Mathf.Floor(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) * .65f) * 3f) + 1f) * .13f;
                return new(x, (depth - z) / (depth * 2f) * rise, y);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (!filled[x, y]) continue;
                    float left = x * cell - 1f, right = left + cell, bottom = y * cell - 1f, top = bottom + cell;
                    Vector3 P(float px, float py, float pz) => Point(px, py, pz);
                    Quad(P(left, bottom, -depth), P(left, top, -depth), P(right, top, -depth), P(right, bottom, -depth));
                    Quad(P(left, bottom, depth), P(right, bottom, depth), P(right, top, depth), P(left, top, depth));
                    if (x == 0 || !filled[x - 1, y])
                        Quad(P(left, bottom, depth), P(left, top, depth), P(left, top, -depth), P(left, bottom, -depth));
                    if (x == size - 1 || !filled[x + 1, y])
                        Quad(P(right, bottom, -depth), P(right, top, -depth), P(right, top, depth), P(right, bottom, depth));
                    if (y == 0 || !filled[x, y - 1])
                        Quad(P(left, bottom, depth), P(left, bottom, -depth), P(right, bottom, -depth), P(right, bottom, depth));
                    if (y == size - 1 || !filled[x, y + 1])
                        Quad(P(left, top, -depth), P(left, top, depth), P(right, top, depth), P(right, top, -depth));
                }
            return FlatMesh(surface ? "Pixel blood surface" : "Pixel blood silhouette", vertices, triangles,
                surface ? Vector3.up : Vector3.forward);
        }

        private static Mesh FlatMesh(string name, List<Vector3> points, List<int> faces, Vector3 faceNormal)
        {
            var vertices = new List<Vector3>(faces.Count);
            var triangles = new List<int>(faces.Count);
            var colors = new List<Color>(faces.Count);
            for (int i = 0; i < faces.Count; i += 3)
            {
                var a = points[faces[i]]; var b = points[faces[i + 1]]; var c = points[faces[i + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < .000001f) continue;
                var normal = Vector3.Cross(b - a, c - a).normalized;
                float shade = faceNormal == Vector3.zero ? normal.y > .5f ? 1f : normal.z < -.5f ? .82f : .58f
                    : Mathf.Abs(Vector3.Dot(normal, faceNormal)) > .8f ? 1f : .82f;
                for (int j = 0; j < 3; j++)
                {
                    triangles.Add(vertices.Count); vertices.Add(points[faces[i + j]]);
                    colors.Add(new Color(shade, shade, shade, 1f));
                }
            }
            var result = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            result.SetVertices(vertices); result.SetColors(colors); result.SetTriangles(triangles, 0);
            result.RecalculateNormals(); result.RecalculateBounds();
            return result;
        }

    }
}
