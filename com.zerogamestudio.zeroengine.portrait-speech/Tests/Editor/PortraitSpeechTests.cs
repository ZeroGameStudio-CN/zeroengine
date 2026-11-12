using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.PortraitSpeech.Editor;

namespace ZeroEngine.PortraitSpeech.Tests
{
    public sealed class PortraitSpeechTests
    {
        [Test] public void SpeechHasFiveDistinctPosesAndSkipClosesImmediately()
        {
            var motion=new PortraitSpeechMotion();var characters=new[]{'x','a','o','e','m'};
            for(int i=0;i<characters.Length;i++)
            {
                motion.Reveal(characters[i],i,1,9,.12f);motion.Advance(1.04f,.04f,.035f,.065f,.045f);
                Assert.That(motion.Frame,Is.EqualTo(i+1));Assert.That(motion.Openness,Is.EqualTo(1));
                motion.Complete(true);Assert.That(motion.Frame,Is.Zero);Assert.That(motion.Openness,Is.Zero);
            }
        }
        [Test] public void PunctuationAndCompletionCloseWithoutAStaleQueue()
        {
            var motion=new PortraitSpeechMotion();motion.Reveal('a',7,1,9,.12f);motion.Advance(1.04f,.04f,.035f,.065f,.045f);
            motion.Reveal('，',8,1.05f,9,.12f);motion.Advance(1.07f,.02f,.035f,.065f,.045f);
            Assert.That(motion.Openness,Is.InRange(.1f,.99f));motion.Advance(1.2f,.13f,.035f,.065f,.045f);Assert.That(motion.Frame,Is.Zero);
            motion.Reveal('o',0,1.3f,9,.12f);motion.Advance(1.34f,.04f,.035f,.065f,.045f);Assert.That(motion.Frame,Is.EqualTo(3));
            motion.Complete(false);motion.Advance(1.5f,.2f,.035f,.065f,.045f);Assert.That(motion.Frame,Is.Zero);
        }
        [Test] public void RateLimitAndResetDoNotTouchGameplayRandom()
        {
            var state=UnityEngine.Random.state;var motion=new PortraitSpeechMotion();motion.Reveal('a',0,1,9,.12f);motion.Advance(1.04f,.04f,.035f,.065f,.045f);
            motion.Reveal('o',1,1.05f,9,.12f);Assert.That(motion.Frame,Is.EqualTo(2));
            motion.Reveal('e',0,1.06f,9,.12f);motion.Advance(1.1f,.04f,.035f,.065f,.045f);Assert.That(motion.Frame,Is.EqualTo(4));
            Assert.That(UnityEngine.Random.state,Is.EqualTo(state));
        }
        [Test] public void BlinkClosesHoldsAndReopensOnItsPresentationClock()
        {
            float Value(float now)=>PortraitBlinkMotion.Evaluate(now,2.8f,4.2f,.22f,.035f,.22f);
            Assert.That(Value(2.79f),Is.Zero);Assert.That(Value(2.83f),Is.InRange(.1f,.99f));
            Assert.That(Value(2.87f),Is.EqualTo(1));Assert.That(Value(2.98f),Is.InRange(.01f,.9f));Assert.That(Value(3.03f),Is.Zero);
            Assert.That(Value(2.87f),Is.EqualTo(1),"Clock rewind has no stale blink queue.");
        }
        [Test] public void BlinkIntervalsVaryDeterministicallyWithoutGameplayRandom()
        {
            var state=UnityEngine.Random.state;var starts=new System.Collections.Generic.List<float>();bool prior=false;
            for(int frame=0;frame<2200;frame++)
            {
                float now=frame*.01f;float amount=PortraitBlinkMotion.Evaluate(now,2.8f,4.2f,.22f,.035f,.22f);
                Assert.That(amount,Is.EqualTo(PortraitBlinkMotion.Evaluate(now,2.8f,4.2f,.22f,.035f,.22f)));
                bool active=amount>.01f;if(active&&!prior)starts.Add(now);prior=active;
            }
            Assert.That(starts.Count,Is.GreaterThanOrEqualTo(4));
            var intervals=starts.Skip(1).Select((time,index)=>time-starts[index]).ToArray();
            Assert.That(intervals.Max()-intervals.Min(),Is.GreaterThan(.15f));Assert.That(intervals.All(value=>value>2&&value<6.4f),Is.True);
            Assert.That(UnityEngine.Random.state,Is.EqualTo(state));
        }
        [Test] public void DragCoordinatesRoundTripAndClampToNativePixels()
        {
            var mouth=new Rect(66f/150,76f/150,6f/150,5f/150);var area=new Rect(40,90,450,450);
            var result=DialoguePortraitAuthoringUtility.FromGui(DialoguePortraitAuthoringUtility.ToGui(mouth,area),area,150,150);
            Assert.That(result,Is.EqualTo(mouth));Assert.That(DialoguePortraitAuthoringUtility.Snap(new Rect(-1,-1,4,4),150,150),Is.EqualTo(new Rect(0,0,1,1)));
        }
        [Test] public void MissingOnlyBatchPreservesExistingCalibration()
        {
            var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            try
            {
                var original=new PortraitSpeechProfile{actorId=3,spriteName="Existing",mouth=new Rect(.3f,.4f,.04f,.03f)};library.portraitSpeech=new[]{original};
                var changed=original;changed.mouth=new Rect(.1f,.1f,.1f,.1f);var sibling=original;sibling.spriteName="New";
                Assert.That(DialoguePortraitAuthoringUtility.Write(library,new[]{changed,sibling,sibling},false),Is.EqualTo(1));
                Assert.That(library.portraitSpeech.Length,Is.EqualTo(2));Assert.That(library.portraitSpeech[0].mouth,Is.EqualTo(original.mouth));
            }
            finally{Undo.ClearUndo(library);UnityEngine.Object.DestroyImmediate(library);}
        }
        [Test] public void BatchPlaybackRestoresAuthoredChannelsWithoutChangingCalibration()
        {
            var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            var texture=new Texture2D(20,20);var sprite=Sprite.Create(texture,new Rect(0,0,20,20),Vector2.one*.5f);
            sprite.name="Speaker";
            var mouth=new Rect(.4f,.3f,.2f,.15f);
            var eye=new PortraitBlinkEye{area=new Rect(.2f,.5f,.2f,.15f)};
            var canvas=new Vector2Int(20,20);var mask=new[]{new Vector2Int(5,10)};
            var authored=new PortraitSpeechProfile{actorId=1,spriteName=sprite.name,mouth=mouth,
                blink=new PortraitBlinkProfile{enabled=true,leftEye=eye,maskCanvasSize=canvas,protectedPixels=mask}};
            var covered=authored;covered.actorId=2;covered.spriteName="Covered";covered.blink.enabled=false;
            library.portraitSpeech=new[]{authored,covered};
            try
            {
                Assert.That(DialoguePortraitAuthoringUtility.SetPlayback(library,1,true,false,false),Is.EqualTo(1));
                Assert.That(library.TryGetPortraitSpeech(1,sprite,out _),Is.False);
                Assert.That(library.portraitSpeech[0].blink.CanRender(canvas),Is.True);
                Assert.That(library.portraitSpeech[1].CanSpeak,Is.True);
                Assert.That(DialoguePortraitAuthoringUtility.SetPlayback(library,null,true,true,false),Is.EqualTo(2));
                Assert.That(library.portraitSpeech.Any(p=>p.CanSpeak||p.blink.CanRender(canvas)),Is.False);
                Assert.That(DialoguePortraitAuthoringUtility.SetPlayback(library,null,true,true,false),Is.Zero);
                var edited=authored;edited.mouth.x+=.02f;
                DialoguePortraitAuthoringUtility.Write(library,new[]{edited},true);
                DialoguePortraitAuthoringUtility.WriteBlink(library,new[]{edited},true);
                Assert.That(library.portraitSpeech.Any(p=>p.CanSpeak||p.blink.CanRender(canvas)),Is.False);
                Assert.That(DialoguePortraitAuthoringUtility.SetPlayback(library,null,true,true,true),Is.EqualTo(2));
                Assert.That(library.TryGetPortraitSpeech(1,sprite,out var restored),Is.True);
                Assert.That(restored.mouth,Is.EqualTo(edited.mouth));Assert.That(restored.blink.leftEye.area,Is.EqualTo(eye.area));
                Assert.That(restored.blink.protectedPixels,Is.EqualTo(mask));Assert.That(restored.blink.CanRender(canvas),Is.True);
                Assert.That(library.portraitSpeech[1].blink.CanRender(canvas),Is.False);
            }
            finally{Undo.ClearUndo(library);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(library);}
        }

        private static Texture2D PoseFixture()
        {
            var texture=new Texture2D(24,24,TextureFormat.RGBA32,false);
            var pixels=Enumerable.Repeat(new Color32(255,225,215,255),24*24).ToArray();
            for(int x=8;x<=15;x++)for(int y=9+(x<10?1:0);y<=13-(x>13?1:0);y++)
                pixels[y*24+x]=new Color32(190,62,64,255);
            texture.SetPixels32(pixels);texture.Apply();return texture;
        }
        [Test] public void MouthCandidateReconstructsTwoPosesWithoutChangingSource()
        {
            var source=PoseFixture();var baseline=source.GetPixels32();
            try
            {
                using var pixels=new PortraitMouthPoseDraft(source,new Rect(7f/24,8f/24,10f/24,7f/24));
                pixels.Generate(new Vector2(18.5f/24,10.5f/24),true);
                var half=Enumerable.Range(0,70).Select(i=>pixels.Composed(0,i%10,i/10)).ToArray();
                var closed=Enumerable.Range(0,70).Select(i=>pixels.Composed(1,i%10,i/10)).ToArray();
                Assert.That(half.SequenceEqual(closed),Is.False);
                Assert.That(pixels.Texture.width,Is.EqualTo(10));Assert.That(pixels.Texture.height,Is.EqualTo(14));
                Assert.That(pixels.Texture.GetPixels32().All(p=>p.a==0||p.a==255),Is.True);
                Assert.That(source.GetPixels32(),Is.EqualTo(baseline));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }
        [Test] public void MouthCandidateClearsOriginalToothWhitesInsideTheLipOutline()
        {
            var source=PoseFixture();
            try
            {
                for(int x=10;x<=13;x++)for(int y=10;y<=12;y++)source.SetPixel(x,y,Color.white);
                source.Apply();
                using var pixels=new PortraitMouthPoseDraft(source,new Rect(7f/24,8f/24,10f/24,7f/24));
                pixels.Generate(new Vector2(18.5f/24,10.5f/24),true);
                for(int pose=0;pose<2;pose++)for(int x=10;x<=13;x++)for(int y=10;y<=12;y++)
                    Assert.That(pixels.Composed(pose,x-7,y-8),Is.Not.EqualTo(new Color32(255,255,255,255)),
                        "An old tooth highlight cannot remain behind the reconstructed closed mouth.");
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }

        [Test] public void MouthCandidateClearsDisconnectedLipBitsInsideItsCalibratedRegion()
        {
            var source=PoseFixture();var marker=new Color32(244,0,0,255);
            try
            {
                for(int y=8;y<=14;y++)for(int x=7;x<=16;x++)source.SetPixel(x,y,new Color32(255,225,215,255));
                for(int y=9;y<=10;y++)for(int x=8;x<=10;x++)source.SetPixel(x,y,new Color32(190,62,64,255));
                for(int y=12;y<=13;y++)for(int x=14;x<=15;x++)source.SetPixel(x,y,marker);
                source.SetPixel(12,11,Color.white);source.Apply();
                using var pixels=new PortraitMouthPoseDraft(source,new Rect(7f/24,8f/24,10f/24,7f/24));
                pixels.Generate(new Vector2(18.5f/24,10.5f/24),true);
                Assert.That(pixels.Composed(1,7,5),Is.Not.EqualTo(marker),"The separate original corner cannot survive as a second lip.");
                Assert.That(pixels.Composed(1,5,3),Is.Not.EqualTo(new Color32(255,255,255,255)),"Enclosed original highlights must be restored.");
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }

        [Test] public void MouthBrushUndoRedoAndRestoreKeepPoseRowsIndependent()
        {
            var source=PoseFixture();
            try
            {
                using var pixels=new PortraitMouthPoseDraft(source,new Rect(7f/24,8f/24,10f/24,7f/24));
                var original=pixels.Original(3,3);var ink=new Color32(50,10,20,255);
                pixels.Paint(1,3,3,ink);pixels.EndStroke();
                Assert.That(pixels.Composed(1,3,3),Is.EqualTo(ink));Assert.That(pixels.Composed(0,3,3),Is.EqualTo(original));
                Assert.That(pixels.Undo(),Is.True);Assert.That(pixels.Composed(1,3,3),Is.EqualTo(original));
                Assert.That(pixels.Redo(),Is.True);Assert.That(pixels.Composed(1,3,3),Is.EqualTo(ink));
                pixels.Paint(1,3,3,null);pixels.EndStroke();Assert.That(pixels.Composed(1,3,3),Is.EqualTo(original));
                pixels.Undo();Assert.That(pixels.Composed(1,3,3),Is.EqualTo(ink));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
        }
        [Test] public void MouthReuseRequiresMatchingArtAndBorderRatherThanCanvasSize()
        {
            var source=PoseFixture();var other=PoseFixture();var region=new Rect(7f/24,8f/24,10f/24,7f/24);
            try
            {
                using var draft=new PortraitMouthPoseDraft(source,region);
                Assert.That(draft.Matches(other,region),Is.True);
                other.SetPixel(9,10,Color.cyan);other.Apply();Assert.That(draft.Matches(other,region),Is.False);
                other.SetPixels32(source.GetPixels32());other.SetPixel(6,9,Color.cyan);other.Apply();
                Assert.That(draft.Matches(other,region),Is.False,"The border restores cheek color and must also match.");
                other.SetPixels32(source.GetPixels32());other.Apply();
                Assert.That(draft.Matches(other,new Rect(8f/24,8f/24,10f/24,7f/24)),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(other);}
        }
        [Test] public void TransientMouthSheetsCannotLeakIntoLibraryAndFailedGenerationKeepsEdits()
        {
            var source=PoseFixture();var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            try
            {
                using var pixels=new PortraitMouthPoseDraft(source,new Rect(7f/24,8f/24,10f/24,7f/24));
                pixels.Paint(0,2,2,new Color32(30,4,6,255));pixels.EndStroke();
                var before=pixels.Texture.GetPixels32();
                Assert.Throws<InvalidOperationException>(()=>pixels.Generate(new Vector2(11.5f/24,11.5f/24),false));
                Assert.That(pixels.Texture.GetPixels32(),Is.EqualTo(before));
                var profile=new PortraitSpeechProfile{actorId=3,spriteName="V",mouth=new Rect(.3f,.3f,.2f,.2f),mouthPoseSheet=pixels.Texture};
                Assert.Throws<InvalidOperationException>(()=>DialoguePortraitAuthoringUtility.Write(library,new[]{profile},true));
                Assert.That(library.portraitSpeech,Is.Empty);
            }
            finally{Undo.ClearUndo(library);UnityEngine.Object.DestroyImmediate(library);UnityEngine.Object.DestroyImmediate(source);}
        }

        [Test] public void ExpressionSetSelectsItsOwnProfileAndRejectsAmbiguousKeys()
        {
            var texture=new Texture2D(8,8);var first=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);
            var second=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);first.name="Neutral";second.name="Smile";
            var bank=ScriptableObject.CreateInstance<PortraitExpressionSet>();var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            try
            {
                bank.actorId=3;bank.library=library;
                bank.expressions=new[]{new PortraitExpression{key="neutral",sprite=first},new PortraitExpression{key="smile",sprite=second}};
                library.portraitSpeech=new[]{new PortraitSpeechProfile{actorId=3,spriteName=first.name,mouth=new Rect(.2f,.2f,.2f,.2f)},
                    new PortraitSpeechProfile{actorId=3,spriteName=second.name,mouth=new Rect(.4f,.3f,.1f,.1f)}};
                Assert.That(bank.TryGet("smile",out var selected,out var profile),Is.True);
                Assert.That(selected.sprite,Is.EqualTo(second));Assert.That(profile.mouth,Is.EqualTo(library.portraitSpeech[1].mouth));
                Assert.That(bank.TryGet("missing",out _,out _),Is.False);
                bank.expressions=new[]{new PortraitExpression{key="same",sprite=first},new PortraitExpression{key="same",sprite=second}};
                Assert.That(bank.TryGet("same",out _,out _),Is.False);
                bank.expressions=new[]{new PortraitExpression{key="static",sprite=second}};bank.library=null;
                Assert.That(bank.TryGet("static",out _,out profile),Is.True);Assert.That(profile.HasMouth||profile.blink.HasEyes,Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(bank);UnityEngine.Object.DestroyImmediate(library);UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(texture);}
        }
        // These synchronous EditMode fixtures invoke runtime callbacks explicitly.
        private static void EditorLifecycle(PortraitSpeechImage target,string method)
            => typeof(PortraitSpeechImage).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(target,null);

        [Test] public void ExpressionSwitchResetsMotionAndUnknownKeyLeavesCurrentSpriteUntouched()
        {
            var texture=new Texture2D(8,8);var first=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);
            var second=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);first.name="Neutral";second.name="Curious";
            var bank=ScriptableObject.CreateInstance<PortraitExpressionSet>();var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            var go=new GameObject("Expression driver",typeof(RectTransform),typeof(Image),typeof(PortraitSpeechImage));
            try
            {
                bank.actorId=3;bank.library=library;bank.expressions=new[]{new PortraitExpression{key="neutral",sprite=first},new PortraitExpression{key="curious",sprite=second}};
                library.portraitSpeech=new[]{new PortraitSpeechProfile{actorId=3,spriteName=first.name,mouth=new Rect(.2f,.2f,.2f,.2f)},
                    new PortraitSpeechProfile{actorId=3,spriteName=second.name,mouth=new Rect(.3f,.3f,.2f,.2f)}};
                var player=go.GetComponent<PortraitSpeechImage>();player.expressionSet=bank;int changes=0;player.ExpressionChanged+=_=>changes++;
                EditorLifecycle(player,"OnEnable");
                Assert.That(player.SetExpression("neutral"),Is.True);
                player.Motion.Reveal('a',1,1,9,.3f);player.Motion.Advance(1.1f,.1f,.035f,.065f,.045f);Assert.That(player.Motion.Frame,Is.EqualTo(2));
                Assert.That(player.SetExpression("curious"),Is.True);Assert.That(player.Motion.Frame,Is.Zero);
                Assert.That(player.profile.spriteName,Is.EqualTo(second.name));Assert.That(changes,Is.EqualTo(2));
                Assert.That(player.SetExpression("missing"),Is.False);Assert.That(go.GetComponent<Image>().overrideSprite,Is.EqualTo(second));
                Assert.That(player.CurrentExpression,Is.EqualTo("curious"));Assert.That(changes,Is.EqualTo(2));
                Assert.That(player.SetExpression("curious"),Is.True);Assert.That(changes,Is.EqualTo(2),"Repeated keys should not restart a connection.");
                EditorLifecycle(player,"OnDisable");player.enabled=false;player.initialExpression="neutral";player.enabled=true;EditorLifecycle(player,"OnEnable");
                Assert.That(player.CurrentExpression,Is.EqualTo("neutral"));Assert.That(player.Motion.Frame,Is.Zero);
                Assert.That(go.GetComponent<Image>().overrideSprite,Is.EqualTo(first));
            }
            finally{EditorLifecycle(go.GetComponent<PortraitSpeechImage>(),"OnDisable");UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(bank);UnityEngine.Object.DestroyImmediate(library);UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(texture);}
        }

        [Test] public void ExpressionTransitionChangesAtSwapAndCanCompleteImmediately()
        {
            var texture=new Texture2D(8,8);
            var first=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);
            var second=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);
            first.name="Neutral";second.name="Closed";
            var bank=ScriptableObject.CreateInstance<PortraitExpressionSet>();
            var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            var go=new GameObject("Expression transition",typeof(RectTransform),typeof(Image),typeof(PortraitSpeechImage));
            var player=go.GetComponent<PortraitSpeechImage>();
            try
            {
                bank.actorId=3;bank.library=library;
                bank.expressions=new[]{
                    new PortraitExpression{key="neutral",sprite=first,transitionGroup="outfit",transitionEyes=PortraitExpressionEyes.Open},
                    new PortraitExpression{key="closed",sprite=second,transitionGroup="outfit",transitionEyes=PortraitExpressionEyes.Closed}};
                library.portraitSpeech=new[]{
                    new PortraitSpeechProfile{actorId=3,spriteName=first.name,blink=new PortraitBlinkProfile{enabled=true,
                        leftEye=new PortraitBlinkEye{area=new Rect(.2f,.5f,.2f,.2f),closedHeight=.4f}}},
                    new PortraitSpeechProfile{actorId=3,spriteName=second.name}};
                player.expressionSet=bank;EditorLifecycle(player,"OnEnable");
                Assert.That(player.SetExpression("neutral"),Is.True);
                Assert.That(player.BeginManualPresentation(),Is.True);
                var changes=new System.Collections.Generic.List<string>();player.ExpressionChanged+=changes.Add;
                Assert.That(player.TransitionToExpression("closed"),Is.True);
                Assert.That(player.IsTransitioning,Is.True);
                Assert.That(player.CurrentExpression,Is.EqualTo("neutral"));
                Assert.That(go.GetComponent<Image>().overrideSprite,Is.EqualTo(first));
                Assert.That(changes,Is.Empty,"Requesting a transition must not report a visible swap early.");
                float start=Time.unscaledTime;
                for(int step=0;step<50;step++)player.PresentManually(start+step*.02f,.02f);
                Assert.That(player.IsTransitioning,Is.False);
                Assert.That(player.CurrentExpression,Is.EqualTo("closed"));
                Assert.That(go.GetComponent<Image>().overrideSprite,Is.EqualTo(second));
                Assert.That(changes,Is.EqualTo(new[]{"closed"}));
                Assert.That(player.SetExpression("neutral"),Is.True);
                Assert.That(player.IsTransitioning,Is.False,"The existing switch remains immediate.");
                Assert.That(player.TransitionToExpression("closed"),Is.True);
                player.Complete(true);
                Assert.That(player.IsTransitioning,Is.False);
                Assert.That(player.CurrentExpression,Is.EqualTo("closed"));
                Assert.That(go.GetComponent<Image>().overrideSprite,Is.EqualTo(second));
                Assert.That(player.Motion.Openness,Is.Zero);
                Assert.That(changes,Is.EqualTo(new[]{"closed","neutral","closed"}));
            }
            finally
            {
                EditorLifecycle(player,"OnDisable");UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(bank);UnityEngine.Object.DestroyImmediate(library);
                UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test] public void SharedExpressionSwitchCanPreserveSpeechAndBlinkClock()
        {
            var texture=new Texture2D(8,8);var first=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);var second=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f);
            first.name="Neutral";second.name="Brow";var bank=ScriptableObject.CreateInstance<PortraitExpressionSet>();
            var go=new GameObject("Shared face clock",typeof(RectTransform),typeof(Image),typeof(PortraitSpeechImage));var player=go.GetComponent<PortraitSpeechImage>();
            try
            {
                bank.expressions=new[]{new PortraitExpression{key="neutral",sprite=first},new PortraitExpression{key="brow",sprite=second}};player.expressionSet=bank;EditorLifecycle(player,"OnEnable");
                Assert.That(player.SetExpression("neutral"),Is.True);player.Motion.Reveal('a',1,1,9,.3f);player.Motion.Advance(1.04f,.04f,.035f,.065f,.045f);
                var clock=typeof(PortraitSpeechImage).GetField("boundAt",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);clock.SetValue(player,123f);
                int pose=player.Motion.Frame;float open=player.Motion.Openness;Assert.That(pose,Is.Not.Zero);
                Assert.That(player.SetExpression("brow",true),Is.True);Assert.That(player.Motion.Frame,Is.EqualTo(pose));Assert.That(player.Motion.Openness,Is.EqualTo(open));Assert.That(clock.GetValue(player),Is.EqualTo(123f));
                Assert.That(go.GetComponent<Image>().overrideSprite,Is.EqualTo(second));
                Assert.That(player.SetExpression("neutral"),Is.True);Assert.That(player.Motion.Frame,Is.Zero);Assert.That(clock.GetValue(player),Is.Not.EqualTo(123f));
            }
            finally{EditorLifecycle(player,"OnDisable");UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(bank);UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(texture);}
        }

        [Test] public void LibraryRequiresActorAndExactSprite()
        {
            var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();var texture=new Texture2D(4,4);var sprite=Sprite.Create(texture,new Rect(0,0,4,4),Vector2.one*.5f);sprite.name="A";
            try
            {
                library.portraitSpeech=new[]{new PortraitSpeechProfile{actorId=2,spriteName="A",mouth=new Rect(.2f,.2f,.1f,.1f)}};
                Assert.That(library.TryGetPortraitSpeech(2,sprite,out _),Is.True);Assert.That(library.TryGetPortraitSpeech(3,sprite,out _),Is.False);
                sprite.name="B";Assert.That(library.TryGetPortraitSpeech(2,sprite,out _),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(library);}
        }
        [Test] public void ShaderAndMaterialUseTheActualSpriteCanvas()
        {
            var shader=Resources.Load<Shader>(PortraitSpeechMaterial.ResourceName);Assert.That(shader,Is.Not.Null);Assert.That(shader.isSupported,Is.True);
            var texture=new Texture2D(20,30);var sprite=Sprite.Create(texture,new Rect(0,0,20,30),Vector2.one*.5f);var go=new GameObject("Speech test",typeof(RectTransform),typeof(Image));var material=new Material(shader);
            try
            {
                var image=go.GetComponent<Image>();image.sprite=sprite;var motion=new PortraitSpeechMotion();motion.Reveal('a',0,1,9,.12f);motion.Advance(1.04f,.04f,.035f,.065f,.045f);
                PortraitSpeechMaterial.Apply(image,material,new PortraitSpeechProfile{mouth=new Rect(.2f,.3f,.1f,.1f),usePixelPoses=true,openPixels=new Vector4(3,2,.5f,-.5f)},motion);
                Assert.That(material.GetVector("_SourceSize"),Is.EqualTo(new Vector4(20,30,0,0)));Assert.That(material.GetFloat("_MouthFrame"),Is.EqualTo(2));
                Assert.That(material.GetFloat("_UsePixelPoses"),Is.EqualTo(1));Assert.That(material.GetVector("_PixelOpen"),Is.EqualTo(new Vector4(3,2,.5f,-.5f)));
                PortraitSpeechMaterial.Apply(image,material,null,motion);Assert.That(material.GetFloat("_MouthFrame"),Is.Zero);Assert.That(material.GetFloat("_UsePixelPoses"),Is.Zero);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test] public void BlinkDraftSavePreservesMouthsAndExistingEyes()
        {
            var library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            try
            {
                var eye=new PortraitBlinkEye{area=new Rect(.2f,.5f,.2f,.2f),closedHeight=.4f};
                var blink=new PortraitBlinkProfile{enabled=true,leftEye=eye};
                var original=new PortraitSpeechProfile{actorId=3,spriteName="Variant",mouth=new Rect(.4f,.2f,.1f,.08f),mouthColor=Color.red};
                library.portraitSpeech=new[]{original};
                var draft=original;draft.mouth=new Rect(.1f,.1f,.1f,.1f);draft.blink=blink;
                Assert.That(DialoguePortraitAuthoringUtility.WriteBlink(library,new[]{draft},false),Is.EqualTo(1));
                Assert.That(library.portraitSpeech[0].mouth,Is.EqualTo(original.mouth));
                Assert.That(library.portraitSpeech[0].mouthColor,Is.EqualTo(original.mouthColor));
                draft.blink.leftEye.area=new Rect(.55f,.5f,.2f,.2f);
                Assert.That(DialoguePortraitAuthoringUtility.WriteBlink(library,new[]{draft},false),Is.Zero);
                Assert.That(DialoguePortraitAuthoringUtility.Write(library,new[]{draft},true),Is.EqualTo(1));
                Assert.That(library.portraitSpeech[0].blink.leftEye.area,Is.EqualTo(eye.area));
            }
            finally{Undo.ClearUndo(library);UnityEngine.Object.DestroyImmediate(library);}
        }

        [Test] public void BlinkShaderProtectsForegroundAndKeepsMouthPixels()
        {
            const int size=16;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            var eye=new RectInt(4,9,7,6);
            var lashUnderHair=new Vector2Int(7,14);
            var lashTip=new Vector2Int(7,13);
            var longLash=new Vector2Int(8,12);
            var longLashOccluder=new Vector2Int(8,13);
            var foreground=new[]{new Vector2Int(5,13),new Vector2Int(6,9),new Vector2Int(6,11),
                new Vector2Int(7,12),lashUnderHair,longLashOccluder};
            var belowHair=new Vector2Int(5,12);
            var fringe=new Vector2Int(6,10);
            var pixels=Enumerable.Repeat(new Color32(240,202,190,255),size*size).ToArray();
            for(int y=eye.yMin;y<eye.yMax;y++)for(int x=eye.xMin;x<eye.xMax;x++)pixels[y*size+x]=new Color32(230,190,40,255);
            foreach(var point in foreground)pixels[point.y*size+point.x]=new Color32(45,90,150,255);
            pixels[fringe.y*size+fringe.x]=new Color32(57,40,91,255);
            pixels[lashTip.y*size+lashTip.x]=new Color32(0,0,0,255);
            pixels[longLash.y*size+longLash.x]=new Color32(0,0,0,255);
            texture.SetPixels32(pixels);texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,size,size),Vector2.one*.5f);
            var go=EditorUtility.CreateGameObjectWithHideFlags("Blink shader test",HideFlags.HideAndDontSave,typeof(RectTransform),typeof(Image));
            var image=go.GetComponent<Image>();image.sprite=sprite;
            var material=new Material(Resources.Load<Shader>(PortraitSpeechMaterial.ResourceName));
            material.mainTexture=texture;material.SetFloat("_Layer",2);material.SetFloat("_Connection",1);material.SetFloat("_GlitchStrength",0);
            var target=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32);
            var prior=RenderTexture.active;bool srgb=GL.sRGBWrite;
            using var blink=new PortraitBlinkMaterial();
            try
            {
                var profile=new PortraitSpeechProfile{mouth=new Rect(5f/size,4f/size,6f/size,5f/size),skinSample=new Vector2(.1f,.1f),mouthColor=Color.black,tongueColor=Color.red,teethColor=Color.white};
                var speech=new PortraitSpeechMotion();speech.Reveal('a',0,1,9,.12f);speech.Advance(1.04f,.04f,.035f,.065f,.045f);
                PortraitSpeechMaterial.Apply(image,material,profile,speech);
                var eyes=new PortraitBlinkProfile{enabled=true,maskCanvasSize=new Vector2Int(size,size),protectedPixels=foreground,lidColor=Color.black,
                    leftEye=new PortraitBlinkEye{area=new Rect((float)eye.x/size,(float)eye.y/size,(float)eye.width/size,(float)eye.height/size),skinSample=new Vector2(.1f,.1f),closedHeight=.4f}};
                Color32[] Render(float amount,Vector2Int canvas)
                {
                    blink.Apply(material,eyes,amount,canvas);
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.clear);GL.PushMatrix();
                    try
                    {
                        GL.LoadPixelMatrix(0,size,0,size);Assert.That(material.SetPass(0),Is.True);GL.Begin(GL.QUADS);GL.Color(Color.white);
                        GL.TexCoord2(0,0);GL.Vertex3(0,0,0);GL.TexCoord2(1,0);GL.Vertex3(size,0,0);
                        GL.TexCoord2(1,1);GL.Vertex3(size,size,0);GL.TexCoord2(0,1);GL.Vertex3(0,size,0);GL.End();
                    }
                    finally{GL.PopMatrix();}
                    var read=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
                    try{read.ReadPixels(new Rect(0,0,size,size),0,0);return read.GetPixels32();}
                    finally{UnityEngine.Object.DestroyImmediate(read);}
                }
                var dimensions=new Vector2Int(size,size);
                var open=Render(0,dimensions);var partial=Render(.4f,dimensions);
                var half=Render(.5f,dimensions);var closed=Render(1,dimensions);
                int below=belowHair.y*size+belowHair.x;
                int edge=fringe.y*size+fringe.x;
                Assert.That(partial[below],Is.EqualTo(open[below]),"The visible lash below the painted hair must not become a skin block.");
                Assert.That(closed[edge],Is.EqualTo(open[edge]),"An isolated detail separated by foreground stays in the original artwork.");
                int tip=lashTip.y*size+lashTip.x;
                int hair=lashUnderHair.y*size+lashUnderHair.x;
                Assert.That(half[tip],Is.EqualTo(open[hair]),"An occluded lash tip reveals the adjacent foreground instead of hanging in place.");
                Assert.That(closed[tip],Is.EqualTo(open[hair]),"Full closure must not leave a detached lash dot.");
                Assert.That(partial[longLash.y*size+longLash.x],Is.EqualTo(open[longLashOccluder.y*size+longLashOccluder.x]),
                    "A clamped long-run lash cannot be stretched across its foreground boundary.");
                Assert.That(open.Any(p=>p.a>200),Is.True);
                Assert.That(half.SequenceEqual(open)||half.SequenceEqual(closed),Is.False,"Half closure is a different rendered pose.");
                Assert.That(closed.SequenceEqual(open),Is.False,"Eye closure actually changes pixels.");
                for(int i=0;i<open.Length;i++)
                    if(!eye.Contains(new Vector2Int(i%size,i/size))||foreground.Contains(new Vector2Int(i%size,i/size)))
                    {Assert.That(half[i],Is.EqualTo(open[i]));Assert.That(closed[i],Is.EqualTo(open[i]));}
                Assert.That(Render(1,new Vector2Int(size+1,size)),Is.EqualTo(open),"A resized source cannot reuse a painted mask.");
            }
            finally{RenderTexture.active=prior;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }

        [Test] public void NativePixelPosesRenderFiveDifferentSilhouettes()
        {
            var texture=new Texture2D(16,16,TextureFormat.RGBA32,false);texture.filterMode=FilterMode.Point;
            var pixels=Enumerable.Repeat(new Color32(255,225,211,255),256).ToArray();pixels[7*16+7]=new Color32(70,5,3,255);pixels[7*16+8]=new Color32(243,0,0,255);texture.SetPixels32(pixels);texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,16,16),Vector2.one*.5f);var go=EditorUtility.CreateGameObjectWithHideFlags("Native mouth test",HideFlags.HideAndDontSave,typeof(RectTransform),typeof(Image));var image=go.GetComponent<Image>();image.sprite=sprite;
            var material=new Material(Resources.Load<Shader>(PortraitSpeechMaterial.ResourceName));material.mainTexture=texture;material.SetFloat("_Layer",2);material.SetFloat("_Connection",1);material.SetFloat("_GlitchStrength",0);
            var entry=new PortraitSpeechProfile{mouth=new Rect(5f/16,5f/16,6f/16,5f/16),skinSample=new Vector2(2f/16,2f/16),mouthColor=new Color(.12f,.008f,.012f,1),tongueColor=new Color(.45f,.025f,.045f,1),teethColor=Color.white,usePixelPoses=true,lightPixels=new Vector4(2,1,0,0),openPixels=new Vector4(3,2,.5f,-.5f),roundPixels=new Vector4(2,2,0,-.5f),widePixels=new Vector4(4,1,0,0),tightPixels=new Vector4(1,1,.5f,0)};
            var target=RenderTexture.GetTemporary(16,16,0,RenderTextureFormat.ARGB32);var prior=RenderTexture.active;bool srgb=GL.sRGBWrite;var hashes=new string[6];Color32[] closed=null;
            try
            {
                for(int pose=0;pose<6;pose++)
                {
                    PortraitSpeechMaterial.Apply(image,material,entry,new PortraitSpeechMotion());material.SetFloat("_MouthFrame",pose);material.SetFloat("_MouthPrevious",pose);material.SetFloat("_MouthOpen",pose>0?1:0);material.SetFloat("_MouthBlend",1);
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.clear);GL.PushMatrix();
                    try
                    {
                        GL.LoadPixelMatrix(0,16,0,16);Assert.That(material.SetPass(0),Is.True);GL.Begin(GL.QUADS);GL.Color(Color.white);
                        GL.TexCoord2(0,0);GL.Vertex3(0,0,0);GL.TexCoord2(1,0);GL.Vertex3(16,0,0);GL.TexCoord2(1,1);GL.Vertex3(16,16,0);GL.TexCoord2(0,1);GL.Vertex3(0,16,0);GL.End();
                    }
                    finally{GL.PopMatrix();}
                    var output=new Texture2D(16,16,TextureFormat.RGBA32,false,true);
                    try
                    {
                        output.ReadPixels(new Rect(0,0,16,16),0,0);output.Apply();var rendered=output.GetPixels32();hashes[pose]=Convert.ToBase64String(output.GetRawTextureData<byte>().ToArray());
                        if(pose==0)closed=rendered;
                        else for(int i=0;i<rendered.Length;i++)if(!rendered[i].Equals(closed[i]))Assert.That(new Rect(5,5,6,5).Contains(new Vector2(i%16+.5f,i/16+.5f)),Is.True,"A pose cannot alter cheek pixels.");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(output);}
                }
                Assert.That(hashes.Skip(1).Distinct().Count(),Is.EqualTo(5),"Rendered silhouettes must differ, not just logical pose numbers.");
                Assert.That(hashes.Distinct().Count(),Is.EqualTo(6),"Closed samples the untouched source mouth.");
            }
            finally{RenderTexture.active=prior;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
