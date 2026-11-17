using System;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ZeroEngine.TCE.Presentation.Tests
{
    public sealed class TceVisualDynamicsTests
    {
        GameObject source,copy;
        AnimationClip clip;
        AnimatorController controller;
        TimelineAsset timeline;
        TceVisualDynamics player;
        [TearDown] public void Cleanup()
        {
            player?.Dispose();TceVisualPrefab.Destroy(copy);TceVisualPrefab.Destroy(source);
            if(controller)UnityEngine.Object.DestroyImmediate(controller);
            if(clip)UnityEngine.Object.DestroyImmediate(clip);
            if(timeline)UnityEngine.Object.DestroyImmediate(timeline);
        }
        GameObject Source()
        {
            source=new GameObject("Authored producer");source.SetActive(false);source.AddComponent<SpriteRenderer>();return source;
        }
        void Copy(TceVisualProjectionPolicy policy=null)
        {
            Assert.That(TceVisualPrefab.TryCreate(source,policy,out var recipe,out var reason),Is.True,reason);
            copy=recipe.Instantiate();player=new TceVisualDynamics(copy,source);
        }
        [Test] public void AnimationAdvancesLocallyWithoutControllerOrEventsAndKeepsNetworkRootPose()
        {
            Source();var child=new GameObject("Child");child.transform.SetParent(source.transform,false);
            clip=new AnimationClip();clip.SetCurve("Child",typeof(Transform),"m_LocalPosition.x",AnimationCurve.Linear(0,0,1,10));
            UnityEditor.AnimationUtility.SetAnimationEvents(clip,new[]{new AnimationEvent{time=.25f,functionName="ForbiddenEvent"}});
            controller=new AnimatorController();controller.AddLayer("base");var state=controller.layers[0].stateMachine.AddState("visual");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
            source.AddComponent<Animator>().runtimeAnimatorController=controller;source.AddComponent<ForbiddenVisualReceiver>();ForbiddenVisualReceiver.Events=0;
            Copy(new TceVisualProjectionPolicy { SourceOnly=c=>c is ForbiddenVisualReceiver });copy.SetActive(true);copy.transform.position=new Vector3(12,3,0);
            Assert.That(player.Receive(new TceVisualFrame { Animations=new[]{new TceVisualClock{Slot=0,Clip=0,Time=0,Speed=1,Playing=true}} },0),Is.True);
            player.Advance(.5f);
            Assert.That(copy.transform.GetChild(0).localPosition.x,Is.EqualTo(5).Within(.01));Assert.That(copy.transform.position,Is.EqualTo(new Vector3(12,3,0)));
            Assert.That(copy.GetComponent<Animator>().runtimeAnimatorController,Is.Null);Assert.That(copy.GetComponent<Animator>().fireEvents,Is.False);Assert.That(copy.GetComponent<ForbiddenVisualReceiver>(),Is.Null);Assert.That(ForbiddenVisualReceiver.Events,Is.Zero);
            Assert.That(clip.events.Length,Is.EqualTo(1));
        }
        [Test] public void SourcePhysicsAndAudioNeverReachTheDisplay()
        {
            Source();source.AddComponent<Rigidbody2D>();source.AddComponent<BoxCollider2D>();source.AddComponent<AudioSource>();
            Copy(new TceVisualProjectionPolicy { SourceOnly=c=>c is Rigidbody2D or Collider2D });
            Assert.That(copy.GetComponent<Rigidbody2D>(),Is.Null);Assert.That(copy.GetComponent<Collider2D>(),Is.Null);Assert.That(copy.GetComponent<AudioSource>(),Is.Null);
            player.Receive(new TceVisualFrame { Velocity=new Vector3(4,2),Gravity=-10,AngularVelocity=20 },0);player.Advance(.5f);
            Assert.That(player.PositionOffset.x,Is.EqualTo(2));Assert.That(player.PositionOffset.y,Is.EqualTo(-.25f).Within(.001));Assert.That(player.RotationOffset,Is.EqualTo(10));
        }
        [Test] public void TimelineOnlyEvaluatesPreparedVisualBindings()
        {
            Source();var child=new GameObject("Controlled particles");child.transform.SetParent(source.transform,false);child.AddComponent<ParticleSystem>();
            var director=source.AddComponent<PlayableDirector>();timeline=ScriptableObject.CreateInstance<TimelineAsset>();director.playableAsset=timeline;
            var track=timeline.CreateTrack<ControlTrack>();var control=track.CreateClip<ControlPlayableAsset>();control.start=1;control.duration=1;
            var asset=(ControlPlayableAsset)control.asset;asset.sourceGameObject=new ExposedReference<GameObject>{exposedName=new PropertyName("visual-target")};director.SetReferenceValue(asset.sourceGameObject.exposedName,child);
            Copy();var frame=new TceVisualFrame { Directors=new[]{new TceVisualClock{Slot=0,Time=0,Speed=1,Playing=true}} };
            player.Receive(frame,0);Assert.That(copy.transform.GetChild(0).gameObject.activeSelf,Is.False);player.Advance(1.25f);Assert.That(copy.transform.GetChild(0).gameObject.activeSelf,Is.True);
            Assert.That(copy.GetComponent<PlayableDirector>(),Is.Null);
        }
        [Test] public void UnknownTimelineTracksAndExternalControlTargetsAreDiagnosed()
        {
            Source();var d=source.AddComponent<PlayableDirector>();timeline=ScriptableObject.CreateInstance<TimelineAsset>();d.playableAsset=timeline;
            var track=timeline.CreateTrack<ControlTrack>();track.CreateClip<ControlPlayableAsset>();
            Assert.That(TceVisualPrefab.TryCreate(source,null,out _,out var reason),Is.False);Assert.That(reason,Is.EqualTo("external-control-binding"));
        }
        [Test] public void TimelineControlledParticleClockIsResolvedWithoutRunningTheDirector()
        {
            Source();var child=new GameObject("Timeline particles");child.transform.SetParent(source.transform,false);var particles=child.AddComponent<ParticleSystem>();var main=particles.main;main.simulationSpeed=1.5f;
            var director=source.AddComponent<PlayableDirector>();timeline=ScriptableObject.CreateInstance<TimelineAsset>();director.playableAsset=timeline;
            var track=timeline.CreateTrack<ControlTrack>();var clip=track.CreateClip<ControlPlayableAsset>();clip.start=0;clip.duration=1;clip.timeScale=2;
            var control=(ControlPlayableAsset)clip.asset;control.sourceGameObject=new ExposedReference<GameObject>{exposedName=new PropertyName("clock-target")};director.SetReferenceValue(control.sourceGameObject.exposedName,child);director.time=.25;
            using var capture=new TceVisualDynamics(source);
            Assert.That(capture.TryGetParticlePlayback(particles,out var playing,out var speed),Is.True);Assert.That(playing,Is.False);Assert.That(speed,Is.EqualTo(3));
            control.updateParticle=false;Assert.That(capture.TryGetParticlePlayback(particles,out _,out _),Is.False);
        }
        [Test] public void FrameWireRejectsTruncationAndNonFiniteValues()
        {
            var frame=new TceVisualFrame { Lights=new[]{new TceVisualLight{Enabled=true,Color=Color.red,Intensity=3,Range=4}},Animations=new[]{new TceVisualClock{Slot=0,Clip=2,Time=.2f,Speed=1,Playing=true}} };
            var bytes=frame.Encode();Assert.That(TceVisualFrame.TryDecode(bytes,out var value),Is.True);Assert.That(value.Lights[0].Intensity,Is.EqualTo(3));
            Array.Resize(ref bytes,bytes.Length-1);Assert.That(TceVisualFrame.TryDecode(bytes,out _),Is.False);
            frame.Velocity=new Vector3(float.NaN,0);Assert.Throws<System.IO.InvalidDataException>(()=>frame.Encode());
        }
        [Test] public void LightAndPropertyStateDoesNotBorrowSourceSuppression()
        {
            Source();var light=source.AddComponent<Light>();light.intensity=2;light.range=7;light.color=Color.blue;light.cullingMask=16;
            Copy();var state=new TceVisualDynamics(source).Capture();player.Receive(state,0);var target=copy.GetComponent<Light>();Assert.That(target.intensity,Is.EqualTo(2));Assert.That(target.color,Is.EqualTo(Color.blue));
            Assert.That(target.cullingMask,Is.EqualTo(16));
        }
        [Test] public void AuthoredSourceCaptureReadsLiveControlsAndExcludesRuntimeOnlyChildren()
        {
            Source();source.AddComponent<Light>().intensity=1;
            var live=UnityEngine.Object.Instantiate(source);
            try
            {
                live.GetComponent<Light>().intensity=7;
                var runtimeUi=new GameObject("Runtime UI");runtimeUi.transform.SetParent(live.transform,false);
                runtimeUi.AddComponent<SpriteRenderer>();var extraLight=runtimeUi.AddComponent<Light>();extraLight.intensity=99;extraLight.cullingMask=16;
                using var capture=TceVisualDynamics.CreateSourceCapture(live,source);
                var frame=capture.Capture();
                Assert.That(frame.Lights.Length,Is.EqualTo(1));Assert.That(frame.Lights[0].Intensity,Is.EqualTo(7));
                Copy();Assert.That(player.Receive(frame,0),Is.True);Assert.That(copy.GetComponent<Light>().intensity,Is.EqualTo(7));
                var visibility=capture.CreateVisibility();visibility.SetHidden(true);
                Assert.That(live.GetComponent<Light>().cullingMask,Is.Zero);Assert.That(extraLight.cullingMask,Is.EqualTo(16));
                visibility.Restore();
                Assert.That(live.transform.childCount,Is.EqualTo(1));Assert.That(runtimeUi.activeSelf,Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(live); }
        }
        [Test] public void AuthoredSourceCaptureRefusesAChangedAuthoredHierarchy()
        {
            Source();new GameObject("Authored child").transform.SetParent(source.transform,false);
            var live=UnityEngine.Object.Instantiate(source);
            try
            {
                UnityEngine.Object.DestroyImmediate(live.transform.GetChild(0).gameObject);
                Assert.Throws<InvalidOperationException>(()=>TceVisualDynamics.CreateSourceCapture(live,source));
            }
            finally { UnityEngine.Object.DestroyImmediate(live); }
        }
    }
    public sealed class ForbiddenVisualReceiver:MonoBehaviour
    {
        public static int Events;
        public void ForbiddenEvent(){Events++;}
    }
}
