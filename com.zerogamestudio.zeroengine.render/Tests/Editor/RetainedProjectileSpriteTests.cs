using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Render.Attachments.Tests
{
    public sealed class RetainedProjectileSpriteTests
    {
        [Test]
        public void AVisualTipFollowsAMovingAnchorAndEachLeaseOwnsItsExpiry()
        {
            var target = new GameObject("target");
            var left = new GameObject("first weapon contact");
            var right = new GameObject("second weapon contact");
            try
            {
                var first = left.AddComponent<RetainedProjectileSprite>();
                var second = right.AddComponent<RetainedProjectileSprite>();
                var tip = Vector3.right * .4f;
                var anchor = target.transform;
                var firstPoint = Vector3.up;
                var secondPoint = Vector3.up * 2f;
                double now = 0d;
                bool living = true;
                int releases = 0;
                first.Begin(anchor, firstPoint, Quaternion.identity, Vector3.forward, tip,
                    .55f, .18f, 7f, .10f, () => living, _ => releases++, () => now);
                second.Begin(anchor, secondPoint, Quaternion.identity, Vector3.forward, tip,
                    1.4f, .18f, -5f, .10f, () => living, _ => releases++, () => now);

                anchor.position = new Vector3(2f, .2f, 1f);
                anchor.rotation = Quaternion.Euler(0f, 20f, 40f);
                now = .045d;
                first.Tick(now);
                second.Tick(now);
                Assert.That(Vector3.Distance(left.transform.TransformPoint(tip), anchor.TransformPoint(firstPoint)),
                    Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(right.transform.TransformPoint(tip), anchor.TransformPoint(secondPoint)),
                    Is.LessThan(.0001f));

                now = .53d;
                first.Tick(now);
                Assert.That(left.GetComponent<SpriteRenderer>().color.a, Is.LessThan(.2f));
                now = .56d;
                first.Tick(now);
                second.Tick(now);
                Assert.That(first.IsPlaying, Is.False);
                Assert.That(second.IsPlaying, Is.True);
                living = false;
                second.Tick(now);
                second.End();
                Assert.That(releases, Is.EqualTo(2));
                Assert.That(right.GetComponent<SpriteRenderer>().color.a, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(left);
                Object.DestroyImmediate(right);
                Object.DestroyImmediate(target);
            }
        }
    }
}
