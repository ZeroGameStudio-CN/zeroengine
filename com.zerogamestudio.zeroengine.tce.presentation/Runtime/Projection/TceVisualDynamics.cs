using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

namespace ZeroEngine.TCE.Presentation
{
    /// <summary>Samples resolved producer clocks; the display samples clips without controllers or event receivers.</summary>
    public sealed class TceVisualDynamics : IDisposable
    {
        readonly GameObject root;
        readonly Transform[] nodes;
        readonly Animator[] animators;
        readonly PlayableDirector[] directors;
        readonly Light[] lights;
        readonly Renderer[] renderers;
        readonly TrailRenderer[] trails;
        readonly AnimationClip[][] clips;
        readonly int[] animatorNodes;
        readonly TimelineBinding[][] timelines;
        readonly MaterialPropertyBlock block=new();
        readonly List<AnimatorClipInfo> clipInfo=new();
        readonly Dictionary<AnimationClip,AnimationClip> displayClips=new();
        readonly Dictionary<int,ClipSampler> samplers=new();
        readonly bool display;
        TceVisualFrame frame;
        float elapsed;
        public Action<TceVisualFrame,float> SampleExtension { get; set; }
        public Vector3 PositionOffset => frame==null?Vector3.zero:frame.Velocity*elapsed+Vector3.up*(.5f*frame.Gravity*elapsed*elapsed);
        public float RotationOffset => frame==null?0:frame.AngularVelocity*elapsed;
        sealed class TimelineBinding
        {
            internal TrackAsset Track;
            internal int Node;
            internal TimelineClip[] Clips;
            internal int[] ControlNodes;
        }

        sealed class ClipSampler : IDisposable
        {
            readonly Animator animator;
            PlayableGraph graph;
            AnimationClipPlayable playable;
            AnimationClip current;
            internal ClipSampler(GameObject target)
            {
                animator=target.AddComponent<Animator>();animator.runtimeAnimatorController=null;
                animator.fireEvents=false;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            }
            internal void Sample(AnimationClip clip,float time)
            {
                if(!animator || !animator.gameObject.activeInHierarchy)return;
                if(current!=clip)
                {
                    if(graph.IsValid())graph.Destroy();
                    graph=PlayableGraph.Create("TCE visual clip");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);playable.SetSpeed(0);
                    var output=AnimationPlayableOutput.Create(graph,"Visual",animator);output.SetSourcePlayable(playable);graph.Play();current=clip;
                }
                playable.SetTime(time);playable.SetDone(false);graph.Evaluate(0);
            }
            public void Dispose()
            {
                if(graph.IsValid())graph.Destroy();
                if(animator){if(Application.isPlaying)UnityEngine.Object.Destroy(animator);else UnityEngine.Object.DestroyImmediate(animator);}
            }
        }
        void SampleClip(int node,AnimationClip clip,float time)
        {
            if(!samplers.TryGetValue(node,out var sampler))samplers[node]=sampler=new ClipSampler(nodes[node].gameObject);
            sampler.Sample(displayClips[clip],time);
        }

        public TceVisualDynamics(GameObject instance, GameObject authoredTemplate = null)
            : this(instance, authoredTemplate, false) { }

        /// <summary>Capture live controls for the authored graph, excluding runtime-only UI and helper children.</summary>
        public static TceVisualDynamics CreateSourceCapture(GameObject instance, GameObject authoredTemplate)
        {
            if (!instance) throw new ArgumentNullException(nameof(instance));
            if (!authoredTemplate) throw new ArgumentNullException(nameof(authoredTemplate));
            return new TceVisualDynamics(instance, authoredTemplate, true);
        }

        /// <summary>Suppress only this capture's bound graph, retaining runtime-only child ownership.</summary>
        public TceVisualVisibility CreateVisibility(bool preserveRendererSuppression = true)
            => new TceVisualVisibility(renderers, lights, preserveRendererSuppression);

        private TceVisualDynamics(GameObject instance, GameObject authoredTemplate, bool captureSource)
        {
            root=instance;
            display=authoredTemplate && !captureSource;
            var template=authoredTemplate?authoredTemplate:instance;
            var templateNodes=template.GetComponentsInChildren<Transform>(true);
            Transform BindNode(Transform authored)
            {
                var path=new Stack<int>();
                for(var node=authored;node!=template.transform;node=node.parent)
                {
                    if(!node)throw new InvalidOperationException("authored-node-outside-template");
                    path.Push(node.GetSiblingIndex());
                }
                var target=instance.transform;
                while(path.Count!=0)
                {
                    int index=path.Pop();
                    if(index>=target.childCount)throw new InvalidOperationException("authored-node-missing");
                    target=target.GetChild(index);
                }
                if(authored!=template.transform && target.name!=authored.name)
                    throw new InvalidOperationException("authored-node-reordered");
                return target;
            }
            T BindComponent<T>(T authored) where T:Component
            {
                var components=BindNode(authored.transform).GetComponents<T>();
                int index=Array.IndexOf(authored.GetComponents<T>(),authored);
                if(index<0 || index>=components.Length || components[index].GetType()!=authored.GetType())
                    throw new InvalidOperationException("authored-component-missing");
                return components[index];
            }
            nodes=captureSource?templateNodes.Select(BindNode).ToArray():instance.GetComponentsInChildren<Transform>(true);
            var templateAnimators=template.GetComponentsInChildren<Animator>(true);
            animators=captureSource?templateAnimators.Select(BindComponent).ToArray():templateAnimators;
            var templateDirectors=template.GetComponentsInChildren<PlayableDirector>(true);
            directors=captureSource?templateDirectors.Select(BindComponent).ToArray():templateDirectors;
            lights=captureSource?template.GetComponentsInChildren<Light>(true).Select(BindComponent).ToArray():instance.GetComponentsInChildren<Light>(true);
            renderers=captureSource?template.GetComponentsInChildren<Renderer>(true).Select(BindComponent).ToArray():instance.GetComponentsInChildren<Renderer>(true);
            trails=captureSource?template.GetComponentsInChildren<TrailRenderer>(true).Select(BindComponent).ToArray():instance.GetComponentsInChildren<TrailRenderer>(true);
            clips=new AnimationClip[animators.Length][];animatorNodes=new int[animators.Length];
            for(int i=0;i<animators.Length;i++)
            {
                clips[i]=animators[i].runtimeAnimatorController?animators[i].runtimeAnimatorController.animationClips.Distinct().ToArray():Array.Empty<AnimationClip>();
                animatorNodes[i]=Array.IndexOf(templateNodes,templateAnimators[i].transform);
            }
            timelines=new TimelineBinding[directors.Length][];
            for(int i=0;i<directors.Length;i++)timelines[i]=PrepareTimeline(directors[i],captureSource?nodes:templateNodes);
            if(display)
            {
                foreach(var group in clips)foreach(var clip in group)PrepareClip(clip);
                foreach(var group in timelines)foreach(var binding in group)foreach(var clip in binding.Clips)
                    if(clip.asset is AnimationPlayableAsset animation)PrepareClip(animation.clip);
            }
        }

        internal static string Validate(GameObject root)
        {
            if(root.GetComponentsInChildren<Animator>(true).Length>64 || root.GetComponentsInChildren<PlayableDirector>(true).Length>16)return "visual-clock-budget";
            try
            {
                var nodes=root.GetComponentsInChildren<Transform>(true);
                foreach(var director in root.GetComponentsInChildren<PlayableDirector>(true))PrepareTimeline(director,nodes);
            }
            catch(InvalidOperationException e){return e.Message;}
            return null;
        }
        static TimelineBinding[] PrepareTimeline(PlayableDirector director,Transform[] nodes)
        {
            if(!director.playableAsset)return Array.Empty<TimelineBinding>();
            if(director.playableAsset is not TimelineAsset timeline)throw new InvalidOperationException("unsupported-playable-asset");
            var result=new List<TimelineBinding>();
            foreach(var track in timeline.GetOutputTracks())
            {
                if(track.muted || track is AudioTrack)continue; // Audio stays with the original producer.
                if(track is not AnimationTrack && track is not ControlTrack)throw new InvalidOperationException("unsupported-visual-track:"+track.GetType().FullName);
                var binding=new TimelineBinding { Track=track,Node=-1,Clips=track.GetClips().ToArray() };
                if(track is AnimationTrack)
                {
                    var target=director.GetGenericBinding(track);
                    var transform=target is Component c?c.transform:target is GameObject go?go.transform:null;
                    binding.Node=Array.IndexOf(nodes,transform);
                    if(binding.Node<0)throw new InvalidOperationException("external-animation-binding");
                    foreach(var clip in binding.Clips)if(clip.asset is not AnimationPlayableAsset animation || !animation.clip)throw new InvalidOperationException("missing-animation-clip");
                }
                else
                {
                    binding.ControlNodes=new int[binding.Clips.Length];
                    for(int i=0;i<binding.Clips.Length;i++)
                    {
                        if(binding.Clips[i].asset is not ControlPlayableAsset control || control.prefabGameObject)throw new InvalidOperationException("unsupported-control-spawn");
                        var target=control.sourceGameObject.Resolve(director);int index=target?Array.IndexOf(nodes,target.transform):-1;
                        if(index<0)throw new InvalidOperationException("external-control-binding");
                        binding.ControlNodes[i]=index;
                    }
                }
                result.Add(binding);
            }
            return result.ToArray();
        }

        /// <summary>Timeline manually simulates particles, so isPlaying alone is not its playback clock.</summary>
        public bool TryGetParticlePlayback(ParticleSystem system,out bool playing,out float speed)
        {
            playing=false;speed=system?system.main.simulationSpeed:0;
            if(!system || display)return false;
            bool controlled=false;
            for(int d=0;d<directors.Length;d++)
            {
                var director=directors[d];
                foreach(var binding in timelines[d])
                {
                    if(binding.ControlNodes==null)continue;
                    for(int c=0;c<binding.Clips.Length;c++)
                    {
                        var control=(ControlPlayableAsset)binding.Clips[c].asset;var target=nodes[binding.ControlNodes[c]];
                        if(!control.updateParticle || !system.transform.IsChildOf(target)
                            || !control.searchHierarchy && !target.GetComponent<ParticleSystem>())continue;
                        controlled=true;var clip=binding.Clips[c];
                        if(director.time<clip.start || director.time>=clip.end)continue;
                        float rate=director.playableGraph.IsValid()&&director.playableGraph.GetRootPlayableCount()>0?(float)director.playableGraph.GetRootPlayable(0).GetSpeed():1;
                        speed=Mathf.Max(0,system.main.simulationSpeed*rate*(float)clip.timeScale);
                        playing=director.state==PlayState.Playing&&system.gameObject.activeInHierarchy&&speed>0;
                        return true;
                    }
                }
            }
            return controlled;
        }

        public TceVisualFrame Capture()
        {
            var value=new TceVisualFrame();var animation=new List<TceVisualClock>();
            for(int i=0;i<animators.Length;i++)
            {
                var a=animators[i];if(!a || !a.isInitialized || a.layerCount==0)continue;
                clipInfo.Clear();a.GetCurrentAnimatorClipInfo(0,clipInfo);
                AnimatorClipInfo selected=default;foreach(var candidate in clipInfo)if(candidate.weight>selected.weight)selected=candidate;
                int index=Array.IndexOf(clips[i],selected.clip);if(index<0)continue;
                var state=a.GetCurrentAnimatorStateInfo(0);
                animation.Add(new TceVisualClock { Slot=i,Clip=index,Time=Mathf.Max(0,state.normalizedTime*selected.clip.length),Speed=Mathf.Max(0,a.speed*state.speed*state.speedMultiplier),Playing=a.enabled&&a.gameObject.activeInHierarchy,Loop=state.loop });
            }
            value.Animations=animation.ToArray();var directorClocks=new List<TceVisualClock>();
            for(int i=0;i<directors.Length;i++)
            {
                var d=directors[i];float speed=d.playableGraph.IsValid()&&d.playableGraph.GetRootPlayableCount()>0?(float)d.playableGraph.GetRootPlayable(0).GetSpeed():1;
                directorClocks.Add(new TceVisualClock { Slot=i,Clip=0,Time=Mathf.Max(0,(float)d.time),Speed=Mathf.Max(0,speed),Playing=d.state==PlayState.Playing,Loop=d.extrapolationMode==DirectorWrapMode.Loop });
            }
            value.Directors=directorClocks.ToArray();value.Lights=new TceVisualLight[lights.Length];
            for(int i=0;i<lights.Length;i++){var l=lights[i];value.Lights[i]=new TceVisualLight { Enabled=l.enabled,Intensity=l.intensity,Range=l.range,Color=l.color };}
            var properties=new List<TceVisualProperty>();
            for(int i=0;i<renderers.Length;i++)
            {
                var renderer=renderers[i];renderer.GetPropertyBlock(block);if(block.isEmpty)continue;
                var material=renderer.sharedMaterial;if(!material || !material.shader)continue;var shader=material.shader;
                for(int p=0;p<shader.GetPropertyCount();p++)
                {
                    string name=shader.GetPropertyName(p);int id=shader.GetPropertyNameId(p);if(!block.HasProperty(id))continue;
                    var type=shader.GetPropertyType(p);byte kind;Vector4 data;
                    if(type is ShaderPropertyType.Float or ShaderPropertyType.Range){kind=0;data=new Vector4(block.GetFloat(id),0,0,0);}
                    else if(type==ShaderPropertyType.Color){kind=1;data=block.GetColor(id);}
                    else if(type==ShaderPropertyType.Vector){kind=2;data=block.GetVector(id);}
                    else continue;
                    if(properties.Count==1024)throw new InvalidOperationException("visual-property-budget");
                    properties.Add(new TceVisualProperty { Renderer=i,Name=name,Kind=kind,Value=data });
                }
            }
            value.Properties=properties.ToArray();
            value.Trails=new bool[trails.Length];for(int i=0;i<trails.Length;i++)value.Trails[i]=trails[i].enabled&&trails[i].emitting;
            var physics=root.GetComponent<Rigidbody2D>();
            if(physics){value.Velocity=physics.linearVelocity;value.AngularVelocity=physics.angularVelocity;value.Gravity=physics.bodyType==RigidbodyType2D.Dynamic&&!physics.IsSleeping()?physics.gravityScale*Physics2D.gravity.y:0;}
            return value;
        }

        public bool Receive(TceVisualFrame state,float age)
        {
            if(state==null || !float.IsFinite(age) || age<0)return false;
            foreach(var a in state.Animations)if(a.Slot<0 || a.Slot>=clips.Length || a.Clip<0 || a.Clip>=clips[a.Slot].Length)return false;
            foreach(var d in state.Directors)if(d.Slot<0 || d.Slot>=timelines.Length)return false;
            if(state.Lights.Length!=lights.Length || state.Trails.Length!=trails.Length)return false;
            foreach(var p in state.Properties)if(p.Renderer<0 || p.Renderer>=renderers.Length)return false;
            frame=state;elapsed=age;Sample();return true;
        }
        public void Advance(float delta)
        {
            if(frame==null || !float.IsFinite(delta) || delta<=0)return;
            elapsed=Mathf.Min(3,elapsed+delta);Sample();
        }
        void Sample()
        {
            Vector3 position=root.transform.localPosition,scale=root.transform.localScale;Quaternion rotation=root.transform.localRotation;
            foreach(var a in frame.Animations)
            {
                var clip=clips[a.Slot][a.Clip];float time=a.Time+(a.Playing?elapsed*a.Speed:0);
                if(a.Loop&&clip.length>0)time%=clip.length;else time=Mathf.Min(time,clip.length);
                // No controller, StateMachineBehaviour or source script exists on the display.
                if(display)SampleClip(animatorNodes[a.Slot],clip,time);else clip.SampleAnimation(nodes[animatorNodes[a.Slot]].gameObject,time);
            }
            foreach(var d in frame.Directors)
            {
                double time=d.Time+(d.Playing?elapsed*d.Speed:0);double duration=directors[d.Slot].duration;
                if(d.Loop&&duration>0&&double.IsFinite(duration))time%=duration;
                foreach(var binding in timelines[d.Slot])
                {
                    if(binding.Track is AnimationTrack)
                    {
                        foreach(var clip in binding.Clips)
                            if(time>=clip.start && time<=clip.end)
                            {
                                var animation=(AnimationPlayableAsset)clip.asset;
                                if(display)SampleClip(binding.Node,animation.clip,(float)clip.ToLocalTime(time));else animation.clip.SampleAnimation(nodes[binding.Node].gameObject,(float)clip.ToLocalTime(time));
                            }
                    }
                    else for(int i=0;i<binding.Clips.Length;i++)
                    {
                        var control=(ControlPlayableAsset)binding.Clips[i].asset;if(!control.active)continue;
                        var target=nodes[binding.ControlNodes[i]].gameObject;bool active=false;
                        foreach(var clip in binding.Clips)if(binding.ControlNodes[Array.IndexOf(binding.Clips,clip)]==binding.ControlNodes[i] && time>=clip.start&&time<clip.end){active=true;break;}
                        if(target.activeSelf!=active)target.SetActive(active);
                    }
                }
            }
            root.transform.SetLocalPositionAndRotation(position,rotation);root.transform.localScale=scale;
            for(int i=0;i<lights.Length;i++){var x=frame.Lights[i];lights[i].enabled=x.Enabled;lights[i].intensity=x.Intensity;lights[i].range=x.Range;lights[i].color=x.Color;}
            for(int i=0;i<trails.Length;i++){trails[i].enabled=frame.Trails[i];trails[i].emitting=frame.Trails[i];}
            foreach(var p in frame.Properties)
            {
                var r=renderers[p.Renderer];r.GetPropertyBlock(block);int id=Shader.PropertyToID(p.Name);
                if(p.Kind==0)block.SetFloat(id,p.Value.x);else if(p.Kind==1)block.SetColor(id,p.Value);else block.SetVector(id,p.Value);r.SetPropertyBlock(block);
            }
            SampleExtension?.Invoke(frame,elapsed);
        }
        void PrepareClip(AnimationClip source)
        {
            if(!source || displayClips.ContainsKey(source))return;
            var copy=UnityEngine.Object.Instantiate(source);
            // Imported clip copies retain Editor event metadata. Clear only the private copy.
#if UNITY_EDITOR
            UnityEditor.AnimationUtility.SetAnimationEvents(copy,Array.Empty<AnimationEvent>());
#else
            copy.events=Array.Empty<AnimationEvent>();
#endif
            copy.hideFlags=HideFlags.DontSave;displayClips.Add(source,copy);
        }
        public void Dispose()
        {
            foreach(var sampler in samplers.Values)sampler.Dispose();samplers.Clear();
            foreach(var clip in displayClips.Values)if(Application.isPlaying)UnityEngine.Object.Destroy(clip);else UnityEngine.Object.DestroyImmediate(clip);
            displayClips.Clear();frame=null;
        }
    }
}
