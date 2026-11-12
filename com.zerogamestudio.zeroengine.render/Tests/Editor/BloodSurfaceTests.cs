using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class BloodSurfaceTests
    {
        [Test] public void QueuedFirstUploadNeverDisplaysPreviousPooledPixels_AndMaterialRebinds()
        {
            var shader=Shader.Find("Sprites/Default");Assert.That(shader,Is.Not.Null);
            var resources=new BloodVisualResources(shader,shader);var surface=new BloodSurface(resources,41);
            var root=new GameObject("actor");var body=new GameObject("body");body.transform.SetParent(root.transform,false);
            var renderer=body.AddComponent<SpriteRenderer>();var texture=new Texture2D(2,2);var sprite=Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f);
            renderer.sprite=sprite;
            try
            {
                surface.Bind();surface.Add(.2f);surface.Render(new[]{renderer},Color.red);
                Assert.That(surface.OverlayCount,Is.Zero,"Queued new actors stay hidden until their own lookup is uploaded.");
                surface.FlushPending();surface.Render(new[]{renderer},Color.red);
                var overlay=root.GetComponentsInChildren<SpriteRenderer>().Single(value=>value!=renderer);
                Assert.That(overlay.enabled,Is.True);surface.ResetForPool();surface.Bind();surface.Add(.1f);
                surface.Render(new[]{renderer},Color.red);Assert.That(overlay.enabled,Is.False,"Reused actors cannot display the previous life's GPU pixels.");
                surface.ResetForPool();resources.Dispose();surface.Bind();surface.Add(.1f);surface.FlushPending();surface.Render(new[]{renderer},Color.red);
                Assert.That(overlay.sharedMaterial,Is.SameAs(resources.SurfaceMaterial));Assert.That(overlay.sharedMaterial,Is.Not.Null);
            }
            finally{surface.Dispose();resources.Dispose();Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);Object.DestroyImmediate(root);}
        }
        [Test] public void UploadsCoalesceAndDiscardOldGeneration_WithoutStarvingAnotherActor()
        {
            var shader=Shader.Find("Sprites/Default");var resources=new BloodVisualResources(shader,shader);
            var a=new BloodSurface(resources,1);var b=new BloodSurface(resources,2);var queue=new BloodSurfaceUploadQueue{MaximumPerFrame=1};
            try
            {
                a.Bind();b.Bind();a.Add(.1f);queue.Enqueue(a);queue.Enqueue(a);Assert.That(queue.Count,Is.EqualTo(1));
                a.ResetForPool();a.Bind();a.Add(.1f);b.Add(.1f);queue.Enqueue(b);queue.Enqueue(a);
                queue.Pump(10);Assert.That(b.MaskUploads,Is.EqualTo(1));Assert.That(a.MaskUploads,Is.Zero);
                queue.Pump(10);Assert.That(a.MaskUploads,Is.Zero,"The budget is shared by the whole frame.");
                queue.Pump(11);Assert.That(a.MaskUploads,Is.EqualTo(1));Assert.That(queue.Count,Is.Zero);Assert.That(queue.MaximumObserved,Is.EqualTo(1));
            }
            finally{queue.Clear();a.Dispose();b.Dispose();resources.Dispose();}
        }
        [Test] public void IndependentContextsKeepResidueAndScopeCleanupSeparate()
        {
            var shader=Shader.Find("Sprites/Default");var first=new BloodVisualContext(new BloodVisualResources(shader,shader)){WorldEnabled=true};
            var second=new BloodVisualContext(new BloodVisualResources(shader,shader)){WorldEnabled=true};
            var body=new GameObject("clot");var rim=new GameObject("contact");
            try
            {
                Assert.That(BloodResidueStore.Retain(first,body.transform,body.AddComponent<MeshRenderer>(),rim.transform,rim.AddComponent<MeshRenderer>(),Color.red,false),Is.True);
                Assert.That(BloodResidueStore.RetainedCount(first),Is.EqualTo(1));Assert.That(BloodResidueStore.RetainedCount(second),Is.Zero);
                first.ClearScope();Assert.That(BloodResidueStore.RetainedCount(first),Is.Zero);Assert.That(second.ScopeVersion,Is.Zero);
            }
            finally{first.Dispose();second.Dispose();if(body)Object.DestroyImmediate(body);if(rim)Object.DestroyImmediate(rim);}
        }
        [Test] public void InvalidClockDeltaDoesNotPoisonThePersistentDye()
        {
            var mask=new BloodStainMask(41);mask.Deposit(.3f);mask.Advance(float.NaN);mask.Advance(float.PositiveInfinity);mask.Advance(-1);
            Assert.That(mask.Intensity,Is.EqualTo(.3f).Within(.0001));mask.Advance(8);Assert.That(mask.Intensity,Is.Zero);
        }
    }
}
