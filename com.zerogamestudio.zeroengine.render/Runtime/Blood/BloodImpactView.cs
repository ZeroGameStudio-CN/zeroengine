using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render
{
    [DisallowMultipleComponent]
    public class BloodImpactView : MonoBehaviour
    {
        private const int HitNodesPerJet=5;
        private struct Drop
        {
            public Transform Body,Rim;
            public MeshRenderer Renderer,RimRenderer;
            public MeshFilter Filter;
            public Vector3 Position,Velocity,Shape;
            public Quaternion Tilt;
            public Vector2 Normal;
            public float Radius,Shade,ImpactAt,ThreadWidth;
            public int Variant;
            public bool Landed,Volume,Alive;
        }
        private readonly Drop[] drops=new Drop[36];
        private readonly RaycastHit2D[] hits=new RaycastHit2D[6];
        private readonly List<ParticleSystemRenderer> muted=new();
        private readonly List<Vector3> vertices=new(1024);
        private readonly List<Color> colors=new(1024);
        private readonly List<int> triangles=new(4096);
        private readonly ParticleSystem.Particle[] sprayParticles=new ParticleSystem.Particle[96];
        private ParticleSystem authoredSpray;
        private bool pendingSprayCleanup;
        private System.Random pendingSprayRandom;
        private Vector2 pendingSprayDirection;
        private float pendingSprayIntensity;
        private BloodImpactStyle hitStyle;
        private bool Composite => !IsDeath && hitStyle!=BloodImpactStyle.Original;
        private Transform visuals;
        private Mesh threads;
        private MeshRenderer threadRenderer;
        private MaterialPropertyBlock properties;
        private ContactFilter2D terrain;
        private IDisposable lifetime;
        private Func<float> clock;
        private BloodVisualContext context;
        private BloodVisualResources resources=>context.Resources;
        private Camera camera;
        private Vector2 pixel;
        private Color tint;
        private int count,roomVersion;
        private float elapsed;
        public bool Playing { get; private set; }
        public bool IsDeath { get; private set; }
        public int Landed { get; private set; }
        public int Contacts { get; private set; }
        public bool HasAirborneBlood
        {
            get
            {
                if(!Playing)return false;
                if(authoredSpray && authoredSpray.particleCount>0)return true;
                for(int i=0;i<count;i++)if(drops[i].Alive && !drops[i].Landed)return true;
                return false;
            }
        }
        public int GroundStains
        {
            get { int active=0;for(int i=0;i<count;i++)if(drops[i].Alive && drops[i].Landed)active++;return Playing?active:0; }
        }
        public Transform VisualRoot=>visuals;
        private void Awake()=>properties=new MaterialPropertyBlock();
        public void Play(BloodVisualContext visualContext,bool death,uint seed,Vector2 pixelSize,Vector2 direction,Func<float> sourceClock,float intensity,IDisposable lease,BloodImpactStyle style=BloodImpactStyle.Original)
        {
            Stop();
            context=visualContext??throw new ArgumentNullException(nameof(visualContext));
            lifetime=lease;clock=sourceClock;IsDeath=death;pixel=pixelSize;hitStyle=style;
            elapsed=0; Landed=Contacts=0;roomVersion=context.ScopeVersion;
            tint=Color.red;float redness=0;
            foreach(var particles in GetComponentsInChildren<ParticleSystem>(true))
            {
                var authored=particles.main.startColor.Evaluate(.5f,.5f);
                float red=authored.r-Mathf.Max(authored.g,authored.b);
                if(red>redness){redness=red;tint=authored;}
                var renderer=particles.GetComponent<ParticleSystemRenderer>();
                // Death keeps the authored lens rush, dense spatial drops and fragments intact.
                if(renderer && renderer.enabled && !death && particles.name=="3d")
                {muted.Add(renderer);renderer.enabled=false;}
            }
            tint.a=1;
            camera=context.ViewCamera?.Invoke();
            terrain=new ContactFilter2D{useTriggers=false};
            terrain.SetLayerMask(context.TerrainMask);
            var random=new System.Random(unchecked((int)(seed*747796405u+2891336453u)));
            float Next(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            count=death?Mathf.Clamp(Mathf.RoundToInt(30*intensity),8,30):Mathf.Clamp(Mathf.RoundToInt(18*intensity),6,18);
            if(Composite)count=Mathf.Clamp(Mathf.RoundToInt((hitStyle==BloodImpactStyle.Dense?15:20)*intensity),6,20);
            if(direction.sqrMagnitude>.0001f)direction.Normalize();
            // A few related jets form a cohesive wound, rather than independent confetti.
            int jetCount=death?5:3,nodesPerJet=Composite?HitNodesPerJet:6;
            if(Composite)jetCount=hitStyle==BloodImpactStyle.Dense?3:4;
            var jets=new Vector3[jetCount];
            int depthSign=(Composite || death) && random.Next(2)==0?-1:1;
            for(int jet=0;jet<jetCount;jet++)
            {
                float spread=jet-(jetCount-1)*.5f;
                Vector2 velocity;
                if(direction==Vector2.zero)
                {
                    float angle=death?jet*Mathf.PI*2f/jetCount:Mathf.PI*.5f+spread*.6f;
                    velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Next(death?5f:4.5f,death?8.5f:6.5f);
                }
                else velocity=direction*Next(death?6.8f:8f,death?9.5f:11f)
                    +new Vector2(-direction.y,direction.x)*(spread*(death?1.25f:.45f)+Next(-.15f,.15f));
                velocity.y+=death?3.1f:1.35f;
                jets[jet]=new Vector3(velocity.x,velocity.y,death?Next(-1.65f,1.65f):(jet==0?-1f:jet==1?.2f:1f)*Next(1.4f,2.2f));
                if(death)
                {
                    if(direction==Vector2.zero)
                    {
                        float angle=jet*Mathf.PI*2f/jetCount+Next(-.2f,.2f);
                        velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Next(5.8f,9.3f)+Vector2.up*Next(2.3f,3.8f);
                    }
                    else velocity=direction*Next(6.5f,11f)+new Vector2(-direction.y,direction.x)*((spread+Next(-.5f,.5f))*1.5f)
                        +Vector2.up*Next(2.3f,4.3f);
                    jets[jet]=new Vector3(velocity.x,velocity.y,depthSign*(jet%2==0?-1:1)*Next(1.2f,3f));
                }
                if(Composite)
                {
                    var forward=direction==Vector2.zero?Vector2.up:direction;
                    var across=new Vector2(-forward.y,forward.x);
                    float speed=hitStyle==BloodImpactStyle.Dense?Next(7.2f,10.5f):hitStyle==BloodImpactStyle.Viscous?Next(5.1f,8.7f):Next(6.3f,11.2f);
                    var motion=forward*speed+across*((spread+Next(-.45f,.45f))*(hitStyle==BloodImpactStyle.Spatial?1.7f:1.15f))
                        +Vector2.up*((hitStyle==BloodImpactStyle.Dense?1.7f:2.5f)+Next(-.55f,.85f));
                    float depth=hitStyle==BloodImpactStyle.Spatial?2.8f:hitStyle==BloodImpactStyle.Viscous?1.7f:1.25f;
                    jets[jet]=new Vector3(motion.x,motion.y,depthSign*(jet%2==0?-1:1)*Next(depth*.35f,depth*1.1f));
                }
            }
            for(int i=0;i<drops.Length;i++)
            {
                ref var drop=ref drops[i];
                if(i>=count){if(drop.Body)drop.Body.gameObject.SetActive(false);if(drop.Rim)drop.Rim.gameObject.SetActive(false);continue;}
                if(!drop.Body)drop.Body=MakeRenderer("Pixel player blood",out drop.Renderer,out drop.Filter);
                int jet=i/nodesPerJet,node=i%nodesPerJet;
                bool foreground=death && (node==0 && jet%2==0 || node==1 && jet==2);
                drop.Variant=(i+random.Next(3))%3;drop.Volume=node==0 || foreground;
                drop.Filter.sharedMesh=resources.ImpactShape(drop.Variant,drop.Volume?1:0);
                drop.Renderer.sharedMaterial=resources.ImpactMaterial;
                if(!death)
                {
                    if(!drop.Rim)drop.Rim=MakeRenderer("Player blood contact",out drop.RimRenderer,out var rimFilter);
                    drop.Rim.GetComponent<MeshFilter>().sharedMesh=resources.ImpactShape(drop.Variant,2);
                    drop.RimRenderer.sharedMaterial=resources.ImpactShadow;
                }
                if(drop.Rim)drop.Rim.gameObject.SetActive(false);
                drop.Body.gameObject.SetActive(true);drop.Alive=true;drop.Landed=false;
                var axis=jets[jet].normalized;
                drop.Position=transform.position+new Vector3(Next(-.035f,.035f),Next(-.025f,.06f),-.62f)-axis*(node%3)*.025f;
                if(!death)drop.Position.z+=Next(-.5f,.5f);
                float lead=node%3==0?1f:node%3==1?.76f:.48f;
                drop.Velocity=jets[jet]*lead+new Vector3(Next(-.18f,.18f),Next(-.14f,.14f),Next(-.12f,.12f));
                if(node>=3)
                {
                    drop.Velocity+=new Vector3(Next(-2f,2f),Next(-1.4f,1.4f),Next(-.7f,.7f))*(death?1f:.55f);
                }
                drop.Radius=Next(.09f,.18f)*(death?1f:.7f)*(node==0?1.32f:node%3==2?.78f:1f);
                if(!death)drop.Radius=node==0?Next(.075f,.11f):node<3?Next(.035f,.065f):Next(.018f,.036f);
                drop.Shape=new Vector3(Next(.9f,1.25f),Next(.78f,1.05f),Next(.4f,.7f));
                drop.Tilt=Quaternion.Euler(Next(-18f,18f),Next(-24f,24f),Next(-8f,8f));
                drop.Shade=Next(.62f,.92f);drop.ImpactAt=0;
                if(death)
                {
                    drop.Position=transform.position+new Vector3(Next(-.11f,.11f),Next(-.03f,.13f),Next(-.85f,.15f))+axis*node*Next(.025f,.055f);
                    drop.Velocity=jets[jet]*(.78f+node*.11f+Next(-.07f,.07f))+new Vector3(Next(-.65f,.65f),Next(-.45f,.45f),Next(-.45f,.45f));
                    drop.Radius=node==0?Next(.14f,.22f):node<3?Next(.065f,.105f):Next(.028f,.055f);
                    drop.Shape=new Vector3(Next(.65f,1.3f),Next(.75f,1.5f),Next(.6f,1.15f));
                    drop.Shade=drop.Volume?Next(.76f,.94f):node<3?Next(.87f,1f):Next(.93f,1f);
                    drop.ThreadWidth=node>0 && random.NextDouble()<.12?0f:Next(.75f,1.1f);
                    if(foreground)
                    {
                        // A few real pixel clots cross the sprite plane toward the lens; no screen-space blobs.
                        var toward=camera?-camera.transform.forward:Vector3.back;
                        var lateral=drop.Velocity-Vector3.Dot(drop.Velocity,toward)*toward;
                        drop.Position+=toward*.15f;
                        drop.Velocity=lateral*.72f+toward*Next(5.5f,8f);
                        drop.Radius=node==0?Next(.17f,.25f):Next(.12f,.18f);
                    }
                }
                if(!death){drop.Shape.x*=.75f;drop.Shape.y*=1.2f;drop.Shape.z=Next(.42f,.78f);drop.Shade=drop.Volume?Next(.84f,.9f):node<3?Next(.91f,.96f):Next(.98f,1f);}
                if(Composite)
                {
                    drop.Position=transform.position+new Vector3(Next(-.07f,.07f),Next(-.04f,.1f),Next(-.95f,-.25f))+axis*node*Next(.03f,.055f);
                    drop.Velocity=jets[jet]*(.8f+node*.1f+Next(-.08f,.08f))+new Vector3(Next(-.5f,.5f),Next(-.4f,.4f),Next(-.6f,.6f));
                    drop.Radius=node==0?Next(.075f,.115f):node<3?Next(.04f,.07f):Next(.02f,.04f);
                    if(hitStyle==BloodImpactStyle.Viscous)drop.Radius*=1.03f;
                    drop.Shape=new Vector3(Next(.7f,1.15f),Next(.9f,1.35f),Next(.65f,1f));
                    drop.Shade=drop.Volume?Next(.76f,.96f):node<3?Next(.86f,1f):Next(.9f,1f);
                    drop.ThreadWidth=node>0 && random.NextDouble()<.16?0f:Next(.65f,1.05f);
                }
                Draw(ref drop);
            }
            Playing=true;context.PaletteChanged+=RefreshPalette;if(visuals)visuals.gameObject.SetActive(true);
            if(Composite)
            {
                PrepareAuthoredSpray(random,direction,intensity);
                // Hitstop should hold an opened spray, not overlapping birth particles or stale pooled poses.
                Advance(.05f);
            }
            else if(death)Advance(.035f);
            else DrawThreads();
        }
        private Transform MakeRenderer(string label,out MeshRenderer renderer,out MeshFilter filter)
        {
            if(!visuals)visuals=new GameObject("Player pixel blood visuals"){hideFlags=HideFlags.DontSave}.transform;
            var child=new GameObject(label){hideFlags=HideFlags.DontSave,layer=gameObject.layer};
            child.transform.SetParent(visuals,false);filter=child.AddComponent<MeshFilter>();renderer=child.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            return child.transform;
        }
        private void Update()
        {
            if(!Playing)return;
            if(roomVersion!=context.ScopeVersion){Stop();return;}
            Advance(clock?.Invoke()??0f);
        }
        private void LateUpdate()=>PrepareForRender();
        /// <summary>Finish first-use native spray preparation in a safe update phase, before rendering.</summary>
        public void PrepareForRender()
        {
            if(!pendingSprayCleanup || !Playing || !authoredSpray)return;
            // Native hits may arrive inside animation/physics callbacks. Keep the clone inactive
            // until this safe phase so copied scripts never Awake or Enable before removal.
            var child=authoredSpray.gameObject;
            foreach(var script in child.GetComponentsInChildren<MonoBehaviour>(true))DestroyImmediate(script);
            foreach(var audio in child.GetComponentsInChildren<AudioSource>(true))DestroyImmediate(audio);
            foreach(var collider in child.GetComponentsInChildren<Collider2D>(true))DestroyImmediate(collider);
            foreach(var collider in child.GetComponentsInChildren<Collider>(true))DestroyImmediate(collider);
            context.PrepareAuthoredSpray?.Invoke(child);
            pendingSprayCleanup=false;child.SetActive(true);
            if(pendingSprayRandom!=null && Composite)
            {
                EmitAuthoredSpray(pendingSprayRandom,pendingSprayDirection,pendingSprayIntensity);
                authoredSpray.Simulate(Mathf.Min(elapsed,.1f),false,false,false);
            }
            pendingSprayRandom=null;
        }
        private void RefreshPalette()
        {
            if(!Playing)return;
            for(int i=0;i<count;i++)if(drops[i].Alive)Draw(ref drops[i]);
            DrawThreads();
        }
        public void Advance(float delta)
        {
            if(!Playing || !float.IsFinite(delta) || delta<=0)return;
            delta=Mathf.Min(delta,.05f);elapsed+=delta;int alive=0;
            for(int i=0;i<count;i++)
            {
                ref var drop=ref drops[i];if(!drop.Alive)continue;
                if(!drop.Landed)Move(ref drop,delta);
                if((drop.Landed && elapsed-drop.ImpactAt>(IsDeath?3.4f:6f)) || elapsed>10f)
                {drop.Alive=false;drop.Body.gameObject.SetActive(false);if(drop.Rim)drop.Rim.gameObject.SetActive(false);continue;}
                Draw(ref drop);alive++;
            }
            if(Composite && authoredSpray)
            {
                if(elapsed<1.1f && !pendingSprayCleanup)authoredSpray.Simulate(delta,false,false,false);
                else if(authoredSpray.particleCount!=0)authoredSpray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            DrawThreads();if(alive==0 && (!authoredSpray || authoredSpray.particleCount==0))Stop(false);
        }
        private void Move(ref Drop drop,float delta)
        {
            drop.Velocity.y-=18f*delta;
            drop.Velocity.x*=Mathf.Exp(-delta*1.05f);drop.Velocity.z*=Mathf.Exp(-delta*2.1f);
            var travel=drop.Velocity*delta;var xy=new Vector2(travel.x,travel.y);float distance=xy.magnitude;
            int found=distance>.00001f?Physics2D.CircleCast(drop.Position,drop.Radius*.45f,xy/distance,terrain,hits,distance):0;
            for(int i=0;i<found;i++)
            {
                var hit=hits[i];
                if(!hit.collider || hit.distance<=.00001f || Vector2.Dot(xy,hit.normal)>=0f)continue;
                if(hit.collider.gameObject.layer==context.OneWayLayer && (xy.y>=0 || hit.normal.y<.5f))continue;
                Contacts++;drop.Position=new Vector3(hit.point.x,hit.point.y,drop.Position.z+travel.z*hit.fraction)+new Vector3(hit.normal.x,hit.normal.y,0)*.006f;
                drop.Normal=hit.normal;drop.Velocity=Vector3.zero;drop.Landed=true;drop.ImpactAt=elapsed;Landed++;
                drop.Filter.sharedMesh=resources.ImpactShape(drop.Variant,2);return;
            }
            drop.Position+=travel;
        }
        private void Draw(ref Drop drop)
        {
            drop.Body.position=drop.Position;
            if(drop.Landed)
            {
                float spread=Mathf.SmoothStep(0f,1f,Mathf.Clamp01((elapsed-drop.ImpactAt)*5f));
                drop.Body.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(drop.Normal.x,drop.Normal.y,0));
                drop.Body.localScale=Vector3.Scale(drop.Shape,new Vector3(drop.Radius*Mathf.Lerp(1.2f,IsDeath?2.4f:2.7f,spread),drop.Radius*(IsDeath?.28f:.2f),drop.Radius*Mathf.Lerp(.8f,1.8f,spread)));
                if(!IsDeath)
                {
                    float footprint=Mathf.Sqrt(drop.Radius*.1f);
                    drop.Body.localScale=Vector3.Scale(drop.Shape,new Vector3(Mathf.Lerp(drop.Radius*1.1f,footprint*2.7f,spread),drop.Radius*Mathf.Lerp(1.1f,.26f,spread),Mathf.Lerp(drop.Radius*.85f,footprint*2.3f,spread)));
                    // A thin floor stamp still needs screen area after source-pixel snapping.
                    var scale=drop.Body.localScale;var size=drop.Filter.sharedMesh.bounds.size;
                    float depthProjection=camera?Mathf.Abs(Vector3.Dot(camera.transform.up,drop.Body.forward)):.35f;
                    scale.x=Mathf.Max(scale.x,pixel.x*2.2f/Mathf.Max(.001f,size.x));
                    scale.z=Mathf.Max(scale.z,pixel.y*2.2f/(Mathf.Max(.001f,size.z)*Mathf.Max(.2f,depthProjection)));
                    drop.Body.localScale=scale;
                }
            }
            else
            {
                var facing=camera?camera.transform.rotation:Quaternion.identity;
                var local=Quaternion.Inverse(facing)*drop.Velocity;float angle=Mathf.Atan2(local.y,local.x)*Mathf.Rad2Deg-90f;
                drop.Body.rotation=facing*Quaternion.Euler(0,0,angle)*drop.Tilt;
                float stretch=Mathf.Clamp(drop.Velocity.magnitude*(IsDeath?.17f:.24f),1f,IsDeath?1.8f:2.6f);
                drop.Body.localScale=Vector3.Scale(drop.Shape,new Vector3(drop.Radius/Mathf.Sqrt(stretch),drop.Radius*stretch,drop.Radius*(IsDeath?.65f:1f)));
                if(!IsDeath || !drop.Volume)
                {
                    var scale=drop.Body.localScale;var size=drop.Filter.sharedMesh.bounds.size;
                    scale.x=Mathf.Max(scale.x,pixel.x*1.05f/Mathf.Max(.001f,size.x));
                    scale.y=Mathf.Max(scale.y,pixel.y*1.05f/Mathf.Max(.001f,size.y));
                    drop.Body.localScale=scale;
                }
            }
            var shade=context.RemapBlood(tint*(drop.Shade*(drop.Landed?(IsDeath?.82f:.92f):1f)));
            float tail=IsDeath?1f:1.4f,hold=IsDeath?3.4f:6f;
            shade.a=drop.Landed?Mathf.Clamp01((hold-(elapsed-drop.ImpactAt))/tail):1f;
            properties.SetColor("_BaseColor",shade);properties.SetVector("_PixelWorldSize",new Vector4(pixel.x,pixel.y,0,0));
            drop.Renderer.SetPropertyBlock(properties);
            if(!IsDeath)DrawContact(ref drop,shade.a);
        }
        private void DrawContact(ref Drop drop,float fade)
        {
            if(!drop.Rim)return;
            var point=drop.Position;var normal=drop.Normal;
            if(!drop.Landed)
            {
                if(!drop.Volume){drop.Rim.gameObject.SetActive(false);return;}
                int found=Physics2D.Raycast(drop.Position,Vector2.down,terrain,hits,6f);bool contact=false;
                for(int i=0;i<found;i++)if(hits[i].normal.y>.5f)
                {point=new Vector3(hits[i].point.x,hits[i].point.y,drop.Position.z);normal=hits[i].normal;contact=true;break;}
                if(!contact){drop.Rim.gameObject.SetActive(false);return;}
            }
            float height=Mathf.Max(0,drop.Position.y-point.y);
            float size=drop.Landed?Mathf.Sqrt(drop.Radius*.1f)*2.65f:drop.Radius*(1.5f+height*.12f);
            drop.Rim.gameObject.SetActive(true);drop.Rim.position=point+new Vector3(normal.x,normal.y,0)*.004f;
            drop.Rim.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(normal.x,normal.y,0));
            drop.Rim.localScale=drop.Landed?new Vector3(drop.Body.localScale.x*1.12f,.008f,drop.Body.localScale.z*1.12f):new Vector3(size,.008f,size*.8f);
            properties.SetColor("_BaseColor",new Color(.008f,.005f,.01f,fade*(drop.Landed?.3f:.44f)/(1f+height)));
            drop.RimRenderer.SetPropertyBlock(properties);
        }
        private void DrawThreads()
        {
            if(!threadRenderer)
            {
                if(threads)DestroyImmediate(threads);
                var root=MakeRenderer("Short pixel blood strands",out threadRenderer,out var filter);
                threads=new Mesh{name="Player blood strands",hideFlags=HideFlags.HideAndDontSave};threads.MarkDynamic();filter.sharedMesh=threads;
                threadRenderer.sharedMaterial=resources.ImpactMaterial;
            }
            float span=IsDeath?.32f:.22f;
            if(elapsed>=span && !threadRenderer.enabled)return;
            vertices.Clear();triangles.Clear();colors.Clear();
            if(Composite || IsDeath)
            {
                DrawSpatialThreads();
                UploadThreads();return;
            }
            if(elapsed<span)
                for(int i=0;i+2<count;i+=6)
                {
                    ref var head=ref drops[i];ref var neck=ref drops[i+1];ref var tail=ref drops[i+2];
                    if(head.Landed || neck.Landed || tail.Landed || !head.Alive)continue;
                    var axis=(head.Position-tail.Position).normalized;
                    if(axis.sqrMagnitude<.01f)axis=head.Velocity.normalized;
                    var side=Vector3.Cross(camera?camera.transform.forward:Vector3.forward,axis).normalized;
                    float width=Mathf.Max(pixel.x*(IsDeath?.8f:.45f),head.Radius*(IsDeath?.5f:.32f))*(1f-Mathf.SmoothStep(.35f,1f,elapsed/span));
                    int first=vertices.Count;
                    for(int section=0;section<5;section++)
                    {
                        float t=section*.25f;
                        var center=t<=.5f?Vector3.Lerp(tail.Position,neck.Position,t*2f):Vector3.Lerp(neck.Position,head.Position,(t-.5f)*2f);
                        float bulge=section==0?.12f:section==4?.55f:section==2?1.05f:.72f;
                        vertices.Add(center+side*width*bulge);vertices.Add(center-side*width*bulge);
                        colors.Add(Color.white);colors.Add(Color.white);
                        if(section==0)continue;
                        int a=first+(section-1)*2,b=first+section*2;
                        triangles.Add(a);triangles.Add(b);triangles.Add(b+1);triangles.Add(a);triangles.Add(b+1);triangles.Add(a+1);
                    }
                }
            UploadThreads();
        }
        private void UploadThreads()
        {
            threads.Clear();threadRenderer.enabled=vertices.Count>0;
            if(vertices.Count==0)return;
            threads.SetVertices(vertices);threads.SetColors(colors);threads.SetTriangles(triangles,0);threads.RecalculateBounds();
            var color=context.RemapBlood(tint*.78f);color.a=1f;
            properties.SetColor("_BaseColor",color);properties.SetVector("_PixelWorldSize",new Vector4(pixel.x,pixel.y,0,0));threadRenderer.SetPropertyBlock(properties);
        }
        private void DrawSpatialThreads()
        {
            float life=IsDeath?.48f:hitStyle==BloodImpactStyle.Viscous?.52f:hitStyle==BloodImpactStyle.Spatial?.38f:.3f;
            if(elapsed>=life)return;
            const int sections=6,sides=4;
            int nodes=IsDeath?6:HitNodesPerJet;
            for(int i=0;i+1<count;i++)
            {
                if(i%nodes==nodes-1)continue;
                ref var a=ref drops[i];ref var b=ref drops[i+1];
                if(!a.Alive || !b.Alive || a.Landed && b.Landed || a.ThreadWidth<=0f)continue;
                var delta=b.Position-a.Position;float distance=delta.magnitude;
                if(distance<.02f || distance>(IsDeath?2.1f:hitStyle==BloodImpactStyle.Viscous?1.7f:1.3f))continue;
                var along=delta/distance;var normal=camera?camera.transform.forward:Vector3.forward;
                var side=Vector3.Cross(along,normal);if(side.sqrMagnitude<.001f)side=Vector3.Cross(along,Vector3.up);
                side.Normalize();normal=Vector3.Cross(side,along).normalized;
                float neck=Mathf.Pow(1f-elapsed/life,.65f);
                float width=Mathf.Min(IsDeath?.08f:.06f,Mathf.Min(a.Radius,b.Radius)*.9f)*neck*a.ThreadWidth;
                if(width<.007f)continue;
                width=Mathf.Max(width,Mathf.Max(pixel.x,pixel.y)*(IsDeath?.8f:hitStyle==BloodImpactStyle.Viscous?.65f:.45f));
                int first=vertices.Count;
                for(int section=0;section<=sections;section++)
                {
                    float t=section/(float)sections;
                    var center=Vector3.Lerp(a.Position,b.Position,t)+Vector3.down*Mathf.Sin(t*Mathf.PI)*(distance*.13f+elapsed*.04f);
                    float radius=width*(.7f+.3f*Mathf.Abs(Mathf.Cos(t*Mathf.PI)));
                    for(int s=0;s<sides;s++)
                    {
                        float angle=s*Mathf.PI*2f/sides;
                        vertices.Add(center+side*Mathf.Cos(angle)*radius+normal*Mathf.Sin(angle)*radius*.15f);
                        colors.Add(Color.white);
                    }
                    if(section==0)continue;
                    int ring=first+section*sides,previous=ring-sides;
                    for(int s=0;s<sides;s++)
                    {int next=(s+1)%sides;triangles.Add(previous+s);triangles.Add(ring+s);triangles.Add(ring+next);triangles.Add(previous+s);triangles.Add(ring+next);triangles.Add(previous+next);}
                }
            }
        }
        private void PrepareAuthoredSpray(System.Random random,Vector2 direction,float intensity)
        {
            if(!authoredSpray)
            {
                ParticleSystem template=resources.AuthoredSprayTemplate;
                if(!template)throw new InvalidOperationException("Death blood needs a visible sprite spray layer.");
                // Clone under an inactive owner so prefab scripts cannot run; retain only cosmetic native data.
                var staging=new GameObject("Blood spray preparation"){hideFlags=HideFlags.DontSave};staging.SetActive(false);
                var child=Instantiate(template.gameObject,staging.transform,false);child.name="Authored blood spray";child.hideFlags=HideFlags.DontSave;child.layer=gameObject.layer;
                child.SetActive(false);pendingSprayCleanup=true;
                authoredSpray=child.GetComponent<ParticleSystem>();authoredSpray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                child.transform.SetParent(visuals,false);Destroy(staging);
                var main=authoredSpray.main;main.playOnAwake=false;main.loop=false;main.maxParticles=96;
                main.stopAction=ParticleSystemStopAction.None;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                main.simulationSpace=ParticleSystemSimulationSpace.World;
            }
            if(pendingSprayCleanup)
            {pendingSprayRandom=random;pendingSprayDirection=direction;pendingSprayIntensity=intensity;return;}
            EmitAuthoredSpray(random,direction,intensity);
        }
        private void EmitAuthoredSpray(System.Random random,Vector2 direction,float intensity)
        {
            authoredSpray.transform.position=transform.position+Vector3.back*.55f;
            authoredSpray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var settings=authoredSpray.main;
            settings.startSizeMultiplier=hitStyle==BloodImpactStyle.Dense?.22f:hitStyle==BloodImpactStyle.Viscous?.24f:.2f;
            settings.startLifetime=new ParticleSystem.MinMaxCurve(.55f,1.05f);
            var emission=authoredSpray.emission;emission.enabled=false;
            authoredSpray.useAutoRandomSeed=false;authoredSpray.randomSeed=(uint)random.Next(1,int.MaxValue);
            authoredSpray.Simulate(0f,false,true,false);
            int amount=Mathf.Clamp(Mathf.RoundToInt((hitStyle==BloodImpactStyle.Dense?50:hitStyle==BloodImpactStyle.Viscous?38:56)*(.86f+(float)random.NextDouble()*.24f)*intensity),8,64);
            authoredSpray.Emit(amount);
            int found=authoredSpray.GetParticles(sprayParticles);var forward=direction==Vector2.zero?Vector2.up:direction.normalized;var across=new Vector2(-forward.y,forward.x);
            for(int i=0;i<found;i++)
            {
                float Next(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
                var velocity=forward*Next(3f,hitStyle==BloodImpactStyle.Dense?9.5f:8.5f)+across*Next(-3.4f,3.4f)+Vector2.up*Next(.2f,2.7f);
                sprayParticles[i].velocity=new Vector3(velocity.x,velocity.y,Next(-1.1f,1.1f));
                // The shared blood palette material handles live ink changes, including paused particles.
                var color=tint*Next(.76f,1f);color.a=1f;sprayParticles[i].startColor=color;
            }
            authoredSpray.SetParticles(sprayParticles,found);
        }
        public void Stop(bool restoreNative=true)
        {
            if(context!=null)context.PaletteChanged-=RefreshPalette;
            Playing=false;lifetime?.Dispose();lifetime=null;clock=null;
            pendingSprayRandom=null;
            if(authoredSpray)authoredSpray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(visuals)visuals.gameObject.SetActive(false);
            if(restoreNative)
            {foreach(var renderer in muted)if(renderer&&!renderer.enabled)renderer.enabled=true;muted.Clear();}
        }
        protected virtual void OnDisable()=>Stop();
        protected virtual void OnDestroy()
        {
            Stop();
            if(visuals)DestroyImmediate(visuals.gameObject);if(threads)DestroyImmediate(threads);
        }
    }
}
