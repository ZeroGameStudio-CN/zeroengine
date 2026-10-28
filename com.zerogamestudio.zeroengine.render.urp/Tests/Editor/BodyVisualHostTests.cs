using System;
using NUnit.Framework;
using UnityEngine;
namespace ZeroEngine.Render.Tests
{
    public sealed class BodyVisualHostTests
    {
        [Test]
        public void SameKindSources_ShareOneView_AndReleaseOnlyAfterTheTail()
        {
            var root=new GameObject("Synthetic generic visual host");var texture=new Texture2D(8,8);
            var sprite=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f,16);var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;
            var source=root.AddComponent<RenderPoseSource>();source.Configure(renderer);float clock=0;int retained=0,released=0;
            var host=new BodyVisualHost(root.transform,()=>renderer,_=>source,()=>clock,ZeroEngine.Render.Urp.BodyVisualResourcesLoader.Load());
            try
            {
                Func<Action> retain=()=>{retained++;return ()=>released++;};
                var settings=BodyVisualSettings.For(BodyVisualKind.Bleeding);settings.SmallParticles=false;
                var first=host.Play(settings,retainResources:retain);var second=host.Play(settings,retainResources:retain);
                Assert.That(first!=null&&second!=null,Is.True);Assert.That(host.ActiveCount,Is.EqualTo(2));Assert.That(host.RenderedCount,Is.EqualTo(1));
                clock=.25f;host.Advance();first.Dispose();Assert.That(second.IsActive,Is.True);Assert.That(host.RenderedCount,Is.EqualTo(1));
                second.Dispose();Assert.That(host.ActiveCount,Is.Zero);Assert.That(host.RenderedCount,Is.EqualTo(1),"The view retains borrowed resources through its exit tail.");
                clock=.6f;host.Advance();Assert.That(host.RenderedCount,Is.Zero);Assert.That(released,Is.EqualTo(retained));
            }
            finally{host.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test]
        public void ClockOverride_SurvivesOwnerFreeze_AndSourceRemovalIsIdempotent()
        {
            var root=new GameObject("Synthetic independent visual clocks");var texture=new Texture2D(8,8);
            var sprite=Sprite.Create(texture,new Rect(0,0,8,8),Vector2.one*.5f,16);var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;
            var source=root.AddComponent<RenderPoseSource>();source.Configure(renderer);float ownerClock=0,globalClock=0;
            var host=new BodyVisualHost(root.transform,()=>renderer,_=>source,()=>ownerClock,ZeroEngine.Render.Urp.BodyVisualResourcesLoader.Load());
            try
            {
                var settings=BodyVisualSettings.For(BodyVisualKind.FrozenSolid);settings.SmallParticles=false;
                var lease=host.Play(settings,.5f,clockOverride:()=>globalClock);Assert.That(lease,Is.Not.Null);int ended=0;lease.Ended+=()=>ended++;
                globalClock=.3f;host.Advance();Assert.That(lease.IsActive,Is.True);
                globalClock=.6f;host.Advance();Assert.That(lease.IsActive,Is.False);lease.Dispose();Assert.That(ended,Is.EqualTo(1));
                host.Clear();Assert.That(host.ActiveCount,Is.Zero);Assert.That(host.RenderedCount,Is.Zero);
            }
            finally{host.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
