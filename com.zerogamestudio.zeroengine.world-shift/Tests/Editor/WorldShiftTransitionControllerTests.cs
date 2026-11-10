using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.WorldShift.Tests.Editor
{
    public sealed class WorldShiftTransitionControllerTests
    {
        [TearDown]
        public void TearDown()
        {
            WorldShiftShaderGlobals.SetInactive(Vector3.zero, 0f, 0.34f, 1f, 1.2f, 0.04f, Color.cyan);
            WorldShiftStencilRuntimeState.ClearForTests();
        }

        [Test]
        public void PlayToUnderworldCompletesWithOnlyUnderworldRenderersEnabled()
        {
            var fixture = WorldShiftFixture.Create();
            int started = 0;
            int completed = 0;
            fixture.Controller.TransitionStarted += _ => started++;
            fixture.Controller.TransitionCompleted += _ => completed++;

            fixture.Controller.PlayTo(WorldShiftLayer.Underworld, fixture.Origin);
            Assert.IsTrue(fixture.Controller.IsPlaying);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsTrue(fixture.UnderworldRenderer.enabled);
            Assert.AreEqual(1f, Shader.GetGlobalFloat(WorldShiftShaderGlobals.ScanActiveId));

            fixture.Controller.Tick(1f);

            Assert.IsFalse(fixture.Controller.IsPlaying);
            Assert.AreEqual(WorldShiftLayer.Underworld, fixture.Controller.CurrentLayer);
            Assert.IsFalse(fixture.OverworldRenderer.enabled);
            Assert.IsTrue(fixture.UnderworldRenderer.enabled);
            Assert.IsTrue(fixture.OverworldRoot.activeSelf);
            Assert.IsTrue(fixture.UnderworldRoot.activeSelf);
            Assert.AreEqual(0f, Shader.GetGlobalFloat(WorldShiftShaderGlobals.ScanActiveId));
            Assert.AreEqual(1, started);
            Assert.AreEqual(1, completed);
            fixture.Destroy();
        }

        [Test]
        public void PlayToOverworldContractsFromUnderworldAndRestoresOverworldRenderers()
        {
            var fixture = WorldShiftFixture.Create();
            fixture.Controller.SnapTo(WorldShiftLayer.Underworld);

            fixture.Controller.PlayTo(WorldShiftLayer.Overworld, fixture.Origin);
            fixture.Controller.Tick(1f);

            Assert.AreEqual(WorldShiftLayer.Overworld, fixture.Controller.CurrentLayer);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsFalse(fixture.UnderworldRenderer.enabled);
            Assert.IsFalse(fixture.Controller.IsPlaying);
            fixture.Destroy();
        }

        [Test]
        public void TickDuringTransitionKeepsBothLayerRenderersEnabledAndScanActive()
        {
            var fixture = WorldShiftFixture.Create();

            fixture.Controller.PlayTo(WorldShiftLayer.Underworld, fixture.Origin);
            fixture.OverworldRenderer.enabled = false;
            fixture.UnderworldRenderer.enabled = false;

            fixture.Controller.Tick(0.25f);

            Assert.IsTrue(fixture.Controller.IsPlaying);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsTrue(fixture.UnderworldRenderer.enabled);
            Assert.AreEqual(1f, Shader.GetGlobalFloat(WorldShiftShaderGlobals.ScanActiveId));
            Assert.That(Shader.GetGlobalFloat(WorldShiftShaderGlobals.ScanRadiusId), Is.EqualTo(4f).Within(0.01f));
            fixture.Destroy();
        }

        [Test]
        public void RevealDemoShaderUsesDeterministicClipInsteadOfFullSurfaceDither()
        {
            const string shaderPath = "Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftRevealDemo.shader";
            Assert.IsTrue(File.Exists(shaderPath), $"Missing shader source at {shaderPath}.");

            string shaderSource = File.ReadAllText(shaderPath);

            StringAssert.Contains("ZEWorldShiftBinaryVisibleMask", shaderSource);
            StringAssert.DoesNotContain("clip(visibleMask - ZEWorldShiftDitherThreshold", shaderSource);
        }

        [Test]
        public void ToggleWhilePlayingRestartsTowardOppositeLayerWithoutSticking()
        {
            var fixture = WorldShiftFixture.Create();
            int started = 0;
            int completed = 0;
            fixture.Controller.TransitionStarted += _ => started++;
            fixture.Controller.TransitionCompleted += _ => completed++;

            fixture.Controller.Toggle(fixture.Origin);
            fixture.Controller.Tick(0.25f);
            fixture.Controller.Toggle(fixture.Origin);
            fixture.Controller.Tick(1f);

            Assert.IsFalse(fixture.Controller.IsPlaying);
            Assert.AreEqual(WorldShiftLayer.Overworld, fixture.Controller.CurrentLayer);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsFalse(fixture.UnderworldRenderer.enabled);
            Assert.AreEqual(2, started);
            Assert.AreEqual(1, completed);
            fixture.Destroy();
        }

        [Test]
        public void PlayToCurrentLayerDoesNotStartTransition()
        {
            var fixture = WorldShiftFixture.Create();
            int started = 0;
            fixture.Controller.TransitionStarted += _ => started++;

            fixture.Controller.PlayTo(WorldShiftLayer.Overworld, fixture.Origin);

            Assert.IsFalse(fixture.Controller.IsPlaying);
            Assert.AreEqual(WorldShiftLayer.Overworld, fixture.Controller.CurrentLayer);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsFalse(fixture.UnderworldRenderer.enabled);
            Assert.AreEqual(0f, Shader.GetGlobalFloat(WorldShiftShaderGlobals.ScanActiveId));
            Assert.AreEqual(0, started);
            fixture.Destroy();
        }

        [Test]
        public void PlayToUsesProfileScanFrontPrefabWhenNoRootIsAssigned()
        {
            var fixture = WorldShiftFixture.Create();
            var scanFrontPrefab = new GameObject("Scan Front Prefab");
            fixture.Controller.ScanFrontRoot = null;
            fixture.Controller.Profile.ScanFrontPrefab = scanFrontPrefab;

            fixture.Controller.PlayTo(WorldShiftLayer.Underworld, fixture.Origin);

            Assert.NotNull(fixture.Controller.ScanFrontRoot);
            Assert.IsTrue(fixture.Controller.ScanFrontRoot.gameObject.activeSelf);
            fixture.Controller.Tick(1f);
            Assert.IsFalse(fixture.Controller.ScanFrontRoot.gameObject.activeSelf);

            Object.DestroyImmediate(scanFrontPrefab);
            fixture.Destroy();
        }

        [Test]
        public void RuntimeUpdateClampsLargeDeltaTimeSoPlayStartCannotSkipTransition()
        {
            MethodInfo method = typeof(WorldShiftTransitionController).GetMethod(
                "ClampUpdateDeltaTime",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            float clamped = (float)method.Invoke(null, new object[] { 1f });
            Assert.That(clamped, Is.LessThan(0.1f));
            Assert.That(clamped, Is.GreaterThan(0f));
        }

        [Test]
        public void PlayOnStartDelayDefaultsToVisibleStartupBuffer()
        {
            var controller = new GameObject("WorldShift Controller").AddComponent<WorldShiftTransitionController>();

            Assert.That(controller.PlayOnStartDelay, Is.EqualTo(0.25f).Within(0.001f));

            Object.DestroyImmediate(controller.gameObject);
        }

        [Test]
        public void PlayOnStartWarmupFramesDefaultsToRenderedGameViewBuffer()
        {
            var property = typeof(WorldShiftTransitionController).GetProperty("PlayOnStartWarmupFrames");
            Assert.NotNull(property);

            var controller = new GameObject("WorldShift Controller").AddComponent<WorldShiftTransitionController>();

            Assert.That((int)property.GetValue(controller), Is.GreaterThanOrEqualTo(3));

            Object.DestroyImmediate(controller.gameObject);
        }

        [Test]
        public void NewProfileDefaultsToMaterialRevealBackend()
        {
            var profile = ScriptableObject.CreateInstance<WorldShiftTransitionProfile>();

            Assert.AreEqual(WorldShiftRenderBackend.MaterialReveal, profile.RenderBackend);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void StencilBackendControllerRegistersRuntimeStateWhileEnabled()
        {
            WorldShiftStencilRuntimeState.ClearForTests();

            var profile = ScriptableObject.CreateInstance<WorldShiftTransitionProfile>();
            profile.RenderBackend = WorldShiftRenderBackend.StencilRendererFeature;

            var gameObject = new GameObject("WorldShift Controller");
            var controller = gameObject.AddComponent<WorldShiftTransitionController>();
            controller.Profile = profile;

            controller.enabled = false;
            controller.enabled = true;

            Assert.IsTrue(WorldShiftStencilRuntimeState.HasRegisteredController);

            controller.enabled = false;

            Assert.IsFalse(WorldShiftStencilRuntimeState.HasRegisteredController);

            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(profile);
            WorldShiftStencilRuntimeState.ClearForTests();
        }

        [Test]
        public void StencilRuntimeStateSeparatesRegistrationFromActiveMask()
        {
            WorldShiftStencilRuntimeState.ClearForTests();

            var gameObject = new GameObject("WorldShift Controller");
            var controller = gameObject.AddComponent<WorldShiftTransitionController>();

            WorldShiftStencilRuntimeState.Register(controller);

            Assert.IsTrue(WorldShiftStencilRuntimeState.HasRegisteredController);
            Assert.IsFalse(WorldShiftStencilRuntimeState.TryGetActiveState(out _));

            WorldShiftStencilRuntimeState.RegisterRadialScan(
                controller,
                WorldShiftLayer.Overworld,
                WorldShiftLayer.Underworld);

            Assert.IsTrue(WorldShiftStencilRuntimeState.TryGetActiveState(out WorldShiftStencilRuntimeState.State state));
            Assert.AreEqual(WorldShiftTransitionMode.RadialScan, state.Mode);
            Assert.AreEqual(WorldShiftLayer.Overworld, state.CurrentLayer);
            Assert.AreEqual(WorldShiftLayer.Underworld, state.TargetLayer);

            WorldShiftStencilRuntimeState.ClearActiveState(controller);

            Assert.IsTrue(WorldShiftStencilRuntimeState.HasRegisteredController);
            Assert.IsFalse(WorldShiftStencilRuntimeState.TryGetActiveState(out _));

            Object.DestroyImmediate(gameObject);
            WorldShiftStencilRuntimeState.ClearForTests();
        }

        [Test]
        public void DoorScanModeUsesRadialStencilRuntimeState()
        {
            WorldShiftStencilRuntimeState.ClearForTests();

            var root = new GameObject("Door Scan Root");
            var profile = ScriptableObject.CreateInstance<WorldShiftTransitionProfile>();
            profile.RenderBackend = WorldShiftRenderBackend.StencilRendererFeature;
            profile.Duration = 0.2f;

            try
            {
                var controller = root.AddComponent<WorldShiftTransitionController>();
                controller.Profile = profile;
                controller.SnapTo(WorldShiftLayer.Overworld);

                WorldShiftStencilRuntimeState.ClearForTests();
                controller.enabled = false;
                controller.enabled = true;
                controller.PlayTo(WorldShiftLayer.Underworld, root.transform);

                Assert.IsTrue(controller.IsPlaying);
                Assert.AreEqual(WorldShiftLayer.Overworld, controller.CurrentLayer);
                Assert.IsTrue(WorldShiftStencilRuntimeState.TryGetActiveState(out WorldShiftStencilRuntimeState.State state));
                Assert.AreEqual(WorldShiftTransitionMode.RadialScan, state.Mode);
                Assert.AreEqual(WorldShiftLayer.Overworld, state.CurrentLayer);
                Assert.AreEqual(WorldShiftLayer.Underworld, state.TargetLayer);

                controller.Tick(1f);

                Assert.AreEqual(WorldShiftLayer.Underworld, controller.CurrentLayer);
                Assert.IsFalse(WorldShiftStencilRuntimeState.TryGetActiveState(out _));
                Assert.IsTrue(WorldShiftStencilRuntimeState.HasRegisteredController);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
                WorldShiftStencilRuntimeState.ClearForTests();
            }
        }

        [Test]
        public void PlayStartResetRestoresInitialLayerAfterPreviousUnderworldState()
        {
            var fixture = WorldShiftFixture.Create();
            fixture.Controller.SnapTo(WorldShiftLayer.Underworld);

            MethodInfo method = typeof(WorldShiftTransitionController).GetMethod(
                "ResetToInitialLayer",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);
            method.Invoke(fixture.Controller, null);

            Assert.AreEqual(WorldShiftLayer.Overworld, fixture.Controller.CurrentLayer);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsFalse(fixture.UnderworldRenderer.enabled);
            Assert.IsFalse(fixture.Controller.IsPlaying);
            fixture.Destroy();
        }

        [Test]
        public void PlayModeBootstrapRestoresInitialLayerWhenSceneReloadIsDisabled()
        {
            var fixture = WorldShiftFixture.Create();
            fixture.Controller.SnapTo(WorldShiftLayer.Underworld);

            MethodInfo method = typeof(WorldShiftTransitionController).GetMethod(
                "ResetAllControllersForPlayMode",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            method.Invoke(null, null);

            Assert.AreEqual(WorldShiftLayer.Overworld, fixture.Controller.CurrentLayer);
            Assert.IsTrue(fixture.OverworldRenderer.enabled);
            Assert.IsFalse(fixture.UnderworldRenderer.enabled);
            Assert.IsFalse(fixture.Controller.IsPlaying);
            fixture.Destroy();
        }

        private sealed class WorldShiftFixture
        {
            public GameObject Root;
            public GameObject OverworldRoot;
            public GameObject UnderworldRoot;
            public Transform Origin;
            public WorldShiftTransitionController Controller;
            public Renderer OverworldRenderer;
            public Renderer UnderworldRenderer;

            public static WorldShiftFixture Create()
            {
                var root = new GameObject("WorldShift Test Root");
                var origin = new GameObject("Origin").transform;
                origin.position = new Vector3(1f, 2f, 0f);
                origin.SetParent(root.transform);

                var controller = root.AddComponent<WorldShiftTransitionController>();
                controller.Profile = ScriptableObject.CreateInstance<WorldShiftTransitionProfile>();
                controller.Profile.Duration = 0.5f;
                controller.Profile.ScanEndRadius = 8f;
                controller.Profile.EdgeWidth = 0.34f;
                controller.ScanOrigin = origin;

                var overworldRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                overworldRoot.name = "Overworld";
                overworldRoot.transform.SetParent(root.transform);
                var overworldRenderer = overworldRoot.GetComponent<Renderer>();
                var overworld = overworldRoot.AddComponent<WorldShiftParticipant>();
                overworld.Layer = WorldShiftLayer.Overworld;

                var underworldRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                underworldRoot.name = "Underworld";
                underworldRoot.transform.SetParent(root.transform);
                var underworldRenderer = underworldRoot.GetComponent<Renderer>();
                var underworld = underworldRoot.AddComponent<WorldShiftParticipant>();
                underworld.Layer = WorldShiftLayer.Underworld;

                controller.SetParticipants(new[] { overworld, underworld });
                controller.SnapTo(WorldShiftLayer.Overworld);

                return new WorldShiftFixture
                {
                    Root = root,
                    OverworldRoot = overworldRoot,
                    UnderworldRoot = underworldRoot,
                    Origin = origin,
                    Controller = controller,
                    OverworldRenderer = overworldRenderer,
                    UnderworldRenderer = underworldRenderer
                };
            }

            public void Destroy()
            {
                Object.DestroyImmediate(Root);
            }
        }
    }
}
