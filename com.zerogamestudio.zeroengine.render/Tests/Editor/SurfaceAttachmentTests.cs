using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Render.Attachments;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests
{
    public sealed class SurfaceAttachmentTests
    {
        private readonly List<Object> owned = new();
        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        [TearDown] public void Cleanup()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
        [Test]
        public void EmbeddedContact_AdaptsToSmallLargeThinAndMirroredBodies()
        {
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            texture.Apply();
            var material = Own(new Material(Shader.Find("Sprites/Default")));
            material.mainTexture = texture;
            var scales = new[] { Vector2.one, new Vector2(.25f, .25f), new Vector2(.125f, .125f),
                new Vector2(4f, 4f), new Vector2(.12f, 1.6f), new Vector2(-.12f, 1.6f) };
            var failures = new List<string>();
            for (int i = 0; i < scales.Length; i++)
            {
                var (body, box) = AttachmentBody(new Vector2(2f, 3f), Rect.MinMaxRect(-.9f, -.35f, .9f, .35f));
                var mesh = body.GetComponent<MeshFilter>().sharedMesh;
                mesh.SetUVs(0, Enumerable.Repeat(new Vector2(.5f, .5f), mesh.vertexCount).ToList());
                body.sharedMaterial = material;
                body.transform.localScale = new Vector3(scales[i].x, scales[i].y, 1f);
                body.transform.rotation = Quaternion.Euler(0f, 0f, i >= 4 ? 27f : 0f);
                Physics2D.SyncTransforms();
                var approach = (Vector2)body.transform.TransformDirection(Vector3.down);
                if (!SurfaceAttachmentPlacement.TryFindVisibleContact(body, box,
                    body.transform.position, approach, out _)) failures.Add(scales[i].ToString());
            }
            Assert.That(failures, Is.Empty, "Opaque centres rejected at scales: " + string.Join(", ", failures));
        }
        [Test]
        public void EmbeddedContact_FindsSmallBodyFromTheAuthoritativeHitAfterCropping()
        {
            var (body, box) = AttachmentBody(new Vector2(2f, 3f), Rect.MinMaxRect(-.9f, -.35f, .9f, .35f));
            body.transform.localScale = Vector3.one * .125f;
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            texture.Apply();
            var material = Own(new Material(Shader.Find("Sprites/Default")));
            material.mainTexture = texture;
            body.sharedMaterial = material;
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            mesh.SetUVs(0, Enumerable.Repeat(new Vector2(.5f, .5f), mesh.vertexCount).ToList());
            Physics2D.SyncTransforms();
            var endpoint = new Vector3(-.55f, 0f, 0f);
            Assert.That(SurfaceAttachmentPlacement.TryFindVisibleContact(body, box, endpoint,
                Vector2.right, out var shift, anchor: new Vector2(.55f, 0f)), Is.True);
            Assert.That(Vector2.Distance((Vector2)endpoint + shift, Vector2.zero), Is.LessThan(.01f));
        }
        [Test]
        public void EmbeddedContact_LeavesAnExposedShaftWhenTheHurtboxIsInsideAWideBody()
        {
            var (body, box) = AttachmentBody(new Vector2(.5f, .8f), Rect.MinMaxRect(-.9f, -.35f, .9f, .35f));
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());texture.Apply();
            var material = Own(new Material(Shader.Find("Sprites/Default")));material.mainTexture = texture;body.sharedMaterial = material;
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            mesh.SetUVs(0, Enumerable.Repeat(new Vector2(.5f, .5f), mesh.vertexCount).ToList());
            Assert.That(SurfaceAttachmentPlacement.TryFindVisibleContact(body, box, Vector3.zero,
                Vector2.right, out var shift, minimumExposed: .65f), Is.True);
            Assert.That(shift.x, Is.InRange(-.84f, -.25f));
            Assert.That(box.OverlapPoint(shift), Is.False, "Visual entry belongs to the rendered body; the authoritative hit still belongs to the hurtbox.");
        }
        [Test]
        public void EmbeddedContact_RejectsThinGeometryAndWaitsForOpaqueBodyCoverage()
        {
            var (body, box) = AttachmentBody(new Vector2(2f, 3f),
                Rect.MinMaxRect(-.9f, -.35f, .9f, .35f), Rect.MinMaxRect(-.9f, .50f, .9f, .53f));
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            texture.Apply();
            var material = Own(new Material(Shader.Find("Sprites/Default")));
            material.mainTexture = texture;
            body.sharedMaterial = material;
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            mesh.SetUVs(0, Enumerable.Repeat(new Vector2(.5f, .5f), mesh.vertexCount).ToList());

            var endpoint = new Vector3(0f, .52f, 0f);
            Assert.That(SurfaceAttachmentPlacement.TryFindVisibleContact(body, box, endpoint,
                Vector2.down, out var shift), Is.True);
            Assert.That(endpoint.y + shift.y, Is.LessThan(.12f), "A thin antenna cannot become the entry patch.");
            Assert.That(SurfaceAttachmentPlacement.TryFindVisibleContact(body, box,
                new Vector3(0f, 1.3f, 0f), Vector2.up, out _), Is.False, "Never invent contact outside the local search.");

            var transparent = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            transparent.SetPixels(Enumerable.Repeat(Color.clear, 16).ToArray());
            transparent.Apply();
            material.mainTexture = transparent;
            Assert.That(SurfaceAttachmentPlacement.TryFindVisibleContact(body, box, Vector3.zero,
                Vector2.down, out _), Is.False, "Triangle coverage alone must not prove a visible body.");
        }
        [Test]
        public void EmbeddedContact_ProjectsTheDisplayEndpointAcrossPerspectiveDepthWithoutScreenDrift()
        {
            var camera = Own(new GameObject("Attachment view")).AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, -9f), Quaternion.identity);
            camera.fieldOfView = 35f;
            camera.pixelRect = new Rect(0, 0, 1280, 720);
            var display = new Vector3(2.0f, 3.0f, 1.3f);
            Assert.That(EmbeddedSpriteAttachment.TryProjectDepth(camera, display, .07f, out var body), Is.True);
            Assert.That(Vector2.Distance(camera.WorldToScreenPoint(display), camera.WorldToScreenPoint(body)), Is.LessThan(.001f));
            Assert.That(Vector2.Distance(display, body), Is.GreaterThan(.1f), "Perspective changes the XY attachment at another depth.");
            Assert.That(EmbeddedSpriteAttachment.TryProjectDepth(camera, body, display.z, out var restored), Is.True);
            Assert.That(Vector3.Distance(restored, display), Is.LessThan(.001f));
        }
        [Test]
        public void EmbeddedVisual_HidesOnlyTheHeadAndRestoresTheFlightSpriteAndDepth()
        {
            var root = Own(new GameObject("Projectile root"));
            var marker = Own(new GameObject("Arrow display")).AddComponent<SpriteRenderer>();
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(.10f, -.05f, 1.3f);
            marker.flipY = true;
            marker.sortingOrder = 9;
            var body = Own(new GameObject("Target display")).AddComponent<SpriteRenderer>();
            body.transform.position = new Vector3(2f, 1f, 0.18f);
            var texture = Own(new Texture2D(64, 32));
            var source = Own(Sprite.Create(texture, new Rect(0, 0, 64, 32), new Vector2(.52f, .51f), 16f));
            marker.sprite = source;
            body.sprite = source;
            var embedded = new EmbeddedSpriteAttachment();
            var tip = new Vector2(.63f, 0f);

            embedded.Begin(marker, body, tip, .44f, .07f);
            var shaft = Own(marker.sprite);
            Assert.That(shaft, Is.Not.SameAs(source));
            Assert.That(source.bounds.max.x, Is.GreaterThan(tip.x));
            Assert.That(shaft.vertices.Max(vertex => vertex.x), Is.LessThan(tip.x - .42f), "Hide the complete head, including its dark base.");
            Assert.That(shaft.vertices.Min(vertex => vertex.x), Is.EqualTo(-source.pivot.x / 16f).Within(.001f));
            Assert.That(shaft.pivot, Is.EqualTo(source.pivot));
            Assert.That(marker.transform.position.z, Is.GreaterThan(body.bounds.center.z));
            Assert.That(marker.transform.localPosition.x, Is.EqualTo(.10f));
            Assert.That(marker.flipY, Is.True);
            Assert.That(marker.sortingOrder, Is.EqualTo(9));

            body.transform.position += Vector3.forward * .4f;
            embedded.UpdateDepth(marker);
            Assert.That(marker.transform.position.z - body.bounds.center.z, Is.InRange(.05f, .10f));
            embedded.Restore(marker);
            Assert.That(marker.sprite, Is.SameAs(source));
            Assert.That(marker.transform.localPosition.z, Is.EqualTo(1.3f));
        }
        [Test]
        public void AttachmentPlacement_UsesCompactCenterAndCapsLargeBodyDepth()
        {
            var (body, box) = AttachmentBody(new Vector2(1f, 2f), Rect.MinMaxRect(-0.5f, -1f, 0.5f, 1f));
            var left = new Vector3(-0.5f, -0.32f, -0.2f);
            var right = new Vector3(0.5f, 0.45f, -0.2f);
            Assert.That(Vector3.Distance(SurfaceAttachmentPlacement.Resolve(body, box, left, Vector2.right, 0.8f, 0.63f),
                new Vector3(0f, left.y, left.z)), Is.LessThan(0.003f));
            Assert.That(Vector3.Distance(SurfaceAttachmentPlacement.Resolve(body, box, right, Vector2.left, 0.8f, 0.63f),
                new Vector3(0f, right.y, right.z)), Is.LessThan(0.003f));

            (body, box) = AttachmentBody(new Vector2(8f, 3f), Rect.MinMaxRect(-4f, -1.5f, 4f, 1.5f));
            var hit = new Vector3(-4f, 0.2f, 0f);
            var near = SurfaceAttachmentPlacement.Resolve(body, box, hit, Vector2.right, 0.8f, 0.63f);
            Assert.That(near.x - hit.x, Is.InRange(0.2f, 0.7f), "A wide body must not pull the arrow across to its center.");
            Assert.That(near.y, Is.EqualTo(hit.y).Within(0.001f));
        }
        [Test]
        public void AttachmentPlacement_UsesTheFirstContinuousPartEvenOnDiagonalHits()
        {
            var (body, box) = AttachmentBody(new Vector2(4f, 2f),
                Rect.MinMaxRect(-2f, -1f, -1.65f, 1f), Rect.MinMaxRect(-1.2f, -1f, 2f, 1f));
            Vector3 hit = box.transform.TransformPoint(new Vector3(-2f, 0.4f, -0.2f));
            Vector2 approach = new Vector2(1f, -0.4f).normalized;
            var point = SurfaceAttachmentPlacement.Resolve(body, box, hit, approach, 1f, 0.63f);
            Assert.That(point.x, Is.InRange(-1.95f, -1.70f), "Even a broad second part cannot steal the hit across a gap.");
            Assert.That(point.y, Is.EqualTo(hit.y - (point.x - hit.x) * 0.4f).Within(0.001f));

            box.transform.SetPositionAndRotation(new Vector3(3f, 2f, 0f), Quaternion.Euler(0f, 0f, 49f));
            box.transform.localScale = new Vector3(-1f, 1.3f, 1f);
            Physics2D.SyncTransforms();
            hit = box.transform.TransformPoint(new Vector3(-2f, 0.4f, -0.2f));
            approach = box.transform.TransformVector(Vector2.right);
            point = SurfaceAttachmentPlacement.Resolve(body, box, hit, approach, 1f, 0.63f);
            Vector3 local = box.transform.InverseTransformPoint(point);
            Assert.That(local.x, Is.InRange(-1.95f, -1.70f));
            Assert.That(local.y, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(box.OverlapPoint(point), Is.True);
        }
        [Test]
        public void AttachmentPlacement_OnlyCorrectsWithinTheColliderThatWasHit()
        {
            var (body, box) = AttachmentBody(new Vector2(2f, 2f), Rect.MinMaxRect(-1f, -1f, 1f, 1f));
            var part = Own(new GameObject("Separate hit region")).AddComponent<BoxCollider2D>();
            part.size = new Vector2(0.5f, 1f);
            part.offset = new Vector2(-0.75f, 0f);
            Physics2D.SyncTransforms();
            var hit = new Vector3(-1f, 0f, 0f);
            var anchored = SurfaceAttachmentPlacement.Resolve(body, part, hit, Vector2.right, 1f, 0.63f);
            Assert.That(anchored.x, Is.InRange(-0.90f, -0.60f));
            Assert.That(part.OverlapPoint(anchored), Is.True);
            Assert.That(SurfaceAttachmentPlacement.Resolve(body, box, hit, Vector2.right, 0f, 0.63f), Is.EqualTo(hit));
        }
        [Test]
        public void AttachmentPlacement_UsesNearbyRowsAndPreservesUnprovenHits()
        {
            var (body, box) = AttachmentBody(new Vector2(3f, 2f), Rect.MinMaxRect(-1.5f, 0.09f, -0.6f, 0.23f));
            var hit = new Vector3(-1.5f, 0f, 0f);
            var nearby = SurfaceAttachmentPlacement.Resolve(body, box, hit, Vector2.right, 1f, 0.63f);
            Assert.That(nearby.x, Is.GreaterThan(hit.x));
            Assert.That(nearby.y, Is.InRange(0.09f, 0.23f));

            var tooFar = new Vector3(-1.5f, -0.6f, 0f);
            Assert.That(SurfaceAttachmentPlacement.Resolve(body, box, tooFar, Vector2.right, 1f, 0.63f), Is.EqualTo(tooFar));
            body.GetComponent<MeshFilter>().sharedMesh.UploadMeshData(true);
            Assert.That(SurfaceAttachmentPlacement.Resolve(body, box, hit, Vector2.right, 1f, 0.63f), Is.EqualTo(hit));

            var texture = Own(new Texture2D(64, 64));
            var sprite = Own(Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 32, 0, SpriteMeshType.FullRect));
            var renderer = Own(new GameObject("Uncertain rectangular sprite")).AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            Assert.That(SurfaceAttachmentPlacement.Resolve(renderer, box, hit, Vector2.right, 1f, 0.63f), Is.EqualTo(hit));
        }
        [Test]
        public void Spring_UsesCallerTimeAndFollowsMovingMirroredReferencesAtItsTip()
        {
            var reference = Own(new GameObject("Reference")).transform;
            var moving = Own(new GameObject("Moving")).transform;
            var marker = Own(new GameObject("Display")).AddComponent<SpriteRenderer>();
            marker.transform.SetParent(moving, false);marker.transform.localPosition = new Vector3(.03f, -.02f, .8f);
            marker.transform.localScale = Vector3.one * 1.25f;
            var tip = new Vector2(.45f, .04f);
            Vector3 localContact = new Vector3(2f, 1f, 0f);
            var pose = new SurfaceAttachmentPose();
            pose.Begin(moving, reference, localContact, 20f, tip, 10f);
            Assert.That(pose.Apply(moving, marker, reference, 10f), Is.False);
            Assert.That(Quaternion.Angle(moving.rotation, Quaternion.identity), Is.LessThan(.001f));
            pose.Apply(moving, marker, reference, 10.05f);
            var held = moving.position;var rotation = moving.rotation;
            pose.Apply(moving, marker, reference, 10.05f);
            Assert.That(Vector3.Distance(held, moving.position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(rotation, moving.rotation), Is.LessThan(.001f));
            reference.SetPositionAndRotation(new Vector3(3f, 2f, 0), Quaternion.Euler(0, 0, 27));
            reference.localScale = new Vector3(-1.2f, .7f, 1f);
            Assert.That(pose.Apply(moving, marker, reference, 10.3f), Is.True);
            var actual = marker.transform.TransformPoint(tip);
            Assert.That(Vector2.Distance(actual, reference.TransformPoint(localContact)), Is.LessThan(.001f));
        }

        [Test]
        public void Spring_GrazingContactsNeverSwingOutOfTheirSurface()
        {
            var moving = Own(new GameObject("Surface attachment")).transform;
            foreach (float angle in new[] { -2f, -178f })
            {
                moving.rotation = Quaternion.Euler(0, 0, angle);
                var pose = new SurfaceAttachmentPose();
                pose.Begin(moving, null, Vector3.zero, angle == -2f ? 20f : -20f, Vector2.zero, 5f, Vector2.up);
                for (int i = 0; i <= 32; i++)
                {
                    pose.Apply(moving, null, null, 5f + .23f * i / 32f);
                    Assert.That(Vector2.Dot(moving.right, Vector2.up), Is.LessThan(.003f));
                }
            }
        }

        [Test]
        public void Visual_InvalidArtDimensionsLeaveBorrowedRendererUntouched()
        {
            var marker = Own(new GameObject("Display")).AddComponent<SpriteRenderer>();
            var body = Own(new GameObject("Body")).AddComponent<SpriteRenderer>();
            var texture = Own(new Texture2D(8, 2));
            var sprite = Own(Sprite.Create(texture, new Rect(0, 0, 8, 2), Vector2.one * .5f, 8));
            marker.sprite = body.sprite = sprite;
            var visual = new EmbeddedSpriteAttachment();
            marker.transform.localPosition = new Vector3(.1f, .2f, .8f);
            visual.Begin(marker, body, new Vector2(.4f, 0), float.NaN, .07f);
            Assert.That(marker.sprite, Is.SameAs(sprite));
            Assert.That(marker.transform.localPosition, Is.EqualTo(new Vector3(.1f, .2f, .8f)));
            Assert.That(marker.forceRenderingOff, Is.False);
        }

        private (MeshRenderer, BoxCollider2D) AttachmentBody(Vector2 size, params Rect[] parts)
        {
            var target = Own(new GameObject("Attachment target"));
            var box = target.AddComponent<BoxCollider2D>();
            box.size = size;
            var body = target.AddComponent<MeshRenderer>();
            var mesh = Own(new Mesh());
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var part in parts)
            {
                int index = vertices.Count;
                vertices.Add(new Vector3(part.xMin, part.yMin, 0f));
                vertices.Add(new Vector3(part.xMax, part.yMin, 0f));
                vertices.Add(new Vector3(part.xMax, part.yMax, 0f));
                vertices.Add(new Vector3(part.xMin, part.yMax, 0f));
                triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            Physics2D.SyncTransforms();
            return (body, box);
        }
    }
}
