using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.UI.Toast;
using ToastApi = ZeroEngine.UI.Toast.Toast;

namespace ZeroEngine.UI.Tests.Editor.Toast
{
    public sealed class ToastRuntimePolicyTests
    {
        public static void RunBatchVerification()
        {
            var tests = new ToastRuntimePolicyTests();
            tests.ToastSettings_DefaultsAreUsableForAnyGame();
            tests.Resolver_UsesTextKeyBeforeFallbackMessage();
            tests.Manager_RefreshesDuplicateWhenPolicySaysRefresh();
            tests.Manager_DropOldestPolicyDismissesOldestWhenVisibleLimitIsReached();
            tests.Manager_DefaultQueuePolicyKeepsBurstRequestsForStaggeredDisplay();
            tests.Manager_QueuePolicyDrainsOneRequestPerShowInterval();
            tests.Manager_QueuedRequestDrainsAfterVisibleToastDismisses();
            tests.Manager_QueuedRequestKeepsSameHandleWhenShown();
            tests.Manager_QueuedDuplicateReturnsExistingHandleWithoutReplay();
            tests.Manager_QueuedHandleCanBeDismissedBeforeShown();
            tests.Manager_ClearAllDismissesQueuedHandles();
            tests.Manager_ClearGroupDismissesQueuedHandles();
            tests.Manager_QueueOverflowDismissesOldestQueuedHandle();
            tests.Manager_QueuePolicyDropsIncomingWhenQueueLimitIsZero();
            tests.Manager_ClearGroupRemovesQueuedRequests();
            tests.Manager_RepositionsToastsPerAnchor();
            tests.Manager_BottomAnchorsStackUpward();
            tests.Manager_UsesOverrideColorForAccentOnly();
            tests.RootPresenter_DisableClearsRuntimeState();
            tests.Manager_ClearGroupOnlyClearsMatchingGroup();
            tests.Manager_InvokesDismissedCallbackOnce();
            tests.RootPresenter_RoutesRequestsToMatchingAnchorContainers();
            tests.Container_ActivatesToastInstanceCreatedFromInactivePrefab();
            tests.RootPresenter_ActivatesToastInstanceCreatedFromInactivePrefab();
            tests.ToastSettings_DefaultSpacingFitsDefaultAlertItemHeight();
            tests.RuntimeBootstrap_SourceKeepsTopAlertsNearScreenTop();
            tests.Installer_SourceBindsClickableToastItemPrefab();
        }

        [Test]
        public void ToastSettings_DefaultsAreUsableForAnyGame()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();

            Assert.AreEqual(5, settings.MaxVisible);
            Assert.AreEqual(12, settings.MaxQueued);
            Assert.AreEqual(0.5f, settings.ShowInterval);
            Assert.AreEqual(ToastOverflowPolicy.Queue, settings.OverflowPolicy);
            Assert.AreEqual(ToastDuplicatePolicy.RefreshExisting, settings.DuplicatePolicy);
            Assert.Greater(settings.Spacing, 0f);
            Assert.Greater(settings.GetDuration(ToastSeverity.Info), 0f);
            Assert.Greater(settings.GetDuration(ToastSeverity.Critical), settings.GetDuration(ToastSeverity.Info));
        }

        [Test]
        public void ToastSettings_DefaultSpacingFitsDefaultAlertItemHeight()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();

            Assert.GreaterOrEqual(settings.Spacing, 112f);

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Resolver_UsesTextKeyBeforeFallbackMessage()
        {
            var request = new ToastRequest
            {
                Message = "Fallback message",
                TextKey = "PurchaseDeny"
            };
            var resolver = new TestResolver();

            Assert.AreEqual("Cannot buy this", ToastSettings.ResolveText(request, resolver));
            Assert.AreEqual("Fallback message", ToastSettings.ResolveText(request, null));

            var keyOnly = new ToastRequest { TextKey = "message_key" };
            Assert.AreEqual("message_key", ToastSettings.ResolveText(keyOnly, null));
        }

        [Test]
        public void Manager_RefreshesDuplicateWhenPolicySaysRefresh()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            var first = manager.Show(new ToastRequest { Message = "A", DedupeKey = "same" });
            var second = manager.Show(new ToastRequest
            {
                Message = "B",
                DedupeKey = "same",
                GroupKey = "new-group",
                Severity = ToastSeverity.Warning,
                Priority = ToastPriority.High,
                Anchor = ToastAnchor.BottomCenter,
                Duration = 5f,
                DismissOnClick = false
            });

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual("B", second.Request.Message);
            Assert.AreEqual("new-group", second.Request.GroupKey);
            Assert.AreEqual(ToastSeverity.Warning, second.Request.Severity);
            Assert.AreEqual(ToastPriority.High, second.Request.Priority);
            Assert.AreEqual(ToastAnchor.BottomCenter, second.Request.Anchor);
            Assert.AreEqual(5f, second.Request.Duration);
            Assert.IsFalse(second.Request.DismissOnClick);
        }

        [Test]
        public void Manager_DropOldestPolicyDismissesOldestWhenVisibleLimitIsReached()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.DropOldest, 3, 8, 0f);
            manager.Configure(settings, null, null);

            var first = manager.Show(ToastRequest.Text("one"));
            manager.Show(ToastRequest.Text("two"));
            manager.Show(ToastRequest.Text("three"));
            manager.Show(ToastRequest.Text("four"));

            Assert.IsTrue(first.IsDismissed);
            Assert.AreEqual(3, manager.ActiveCount);
        }

        [Test]
        public void Manager_DefaultQueuePolicyKeepsBurstRequestsForStaggeredDisplay()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var first = manager.Show(ToastRequest.Text("one"));
            var second = manager.Show(ToastRequest.Text("two"));

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.IsFalse(first.IsDismissed);
            Assert.IsFalse(second.IsDismissed);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(1, manager.QueuedCount);
            Assert.AreEqual(1, presenter.ShowCount);
        }

        [Test]
        public void Manager_QueuePolicyDrainsOneRequestPerShowInterval()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            manager.Show(ToastRequest.Text("one"));
            manager.Show(ToastRequest.Text("two"));
            manager.Show(ToastRequest.Text("three"));

            Tick(manager, 0.49f);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(2, manager.QueuedCount);

            Tick(manager, 0.5f);
            Assert.AreEqual(2, manager.ActiveCount);
            Assert.AreEqual(1, manager.QueuedCount);

            Tick(manager, 0.99f);
            Assert.AreEqual(2, manager.ActiveCount);
            Assert.AreEqual(1, manager.QueuedCount);

            Tick(manager, 1f);
            Assert.AreEqual(3, manager.ActiveCount);
            Assert.AreEqual(0, manager.QueuedCount);
            Assert.AreEqual(3, presenter.ShowCount);
        }

        [Test]
        public void Manager_QueuedRequestDrainsAfterVisibleToastDismisses()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var first = manager.Show(ToastRequest.Text("one"));
            manager.Show(ToastRequest.Text("two"));
            manager.Show(ToastRequest.Text("three"));
            manager.Show(ToastRequest.Text("four"));
            manager.Show(ToastRequest.Text("five"));
            manager.Show(ToastRequest.Text("six"));

            Tick(manager, 0.5f);
            Tick(manager, 1f);
            Tick(manager, 1.5f);
            Tick(manager, 2f);

            Assert.AreEqual(5, manager.ActiveCount);
            Assert.AreEqual(1, manager.QueuedCount);
            Assert.IsFalse(first.IsDismissed);

            first.Dismiss();
            Tick(manager, 2.49f);
            Assert.AreEqual(4, manager.ActiveCount);
            Assert.AreEqual(1, manager.QueuedCount);

            Tick(manager, 2.5f);
            Assert.AreEqual(5, manager.ActiveCount);
            Assert.AreEqual(0, manager.QueuedCount);
            Assert.IsTrue(presenter.WasTextShown("six"));
        }

        [Test]
        public void Manager_QueuedRequestKeepsSameHandleWhenShown()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            manager.Show(ToastRequest.Text("one"));
            var queued = manager.Show(ToastRequest.Text("two"));

            Assert.IsNotNull(queued);
            Assert.IsFalse(presenter.WasHandleShown(queued));

            Tick(manager, 0.5f);

            Assert.IsTrue(presenter.WasHandleShown(queued));
            Assert.AreEqual(0, manager.QueuedCount);
        }

        [Test]
        public void Manager_QueuedDuplicateReturnsExistingHandleWithoutReplay()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            manager.Show(ToastRequest.Text("one"));
            var queued = manager.Show(new ToastRequest { Message = "two", DedupeKey = "same" });
            var duplicate = manager.Show(new ToastRequest { Message = "two updated", DedupeKey = "same" });

            Assert.AreSame(queued, duplicate);
            Assert.AreEqual(1, manager.QueuedCount);
            Assert.AreEqual(0, presenter.RefreshCount);
            Assert.AreEqual("two updated", queued.Request.Message);

            Tick(manager, 0.5f);

            Assert.IsTrue(presenter.WasTextShown("two updated"));
            Assert.AreEqual(0, presenter.RefreshCount);
        }

        [Test]
        public void Manager_QueuedHandleCanBeDismissedBeforeShown()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            int dismissCount = 0;
            manager.Show(ToastRequest.Text("one"));
            var queued = manager.Show(new ToastRequest
            {
                Message = "two",
                DedupeKey = "queued",
                OnDismissed = _ => dismissCount++
            });

            queued.Dismiss();
            Tick(manager, 0.5f);
            var next = manager.Show(new ToastRequest { Message = "two again", DedupeKey = "queued" });

            Assert.IsTrue(queued.IsDismissed);
            Assert.AreEqual(1, dismissCount);
            Assert.AreEqual(0, manager.QueuedCount);
            Assert.IsFalse(presenter.WasTextShown("two"));
            Assert.AreNotSame(queued, next);
        }

        [Test]
        public void Manager_ClearAllDismissesQueuedHandles()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            int dismissCount = 0;
            manager.Show(ToastRequest.Text("one"));
            var queued = manager.Show(new ToastRequest { Message = "two", OnDismissed = _ => dismissCount++ });

            manager.ClearAll();

            Assert.IsTrue(queued.IsDismissed);
            Assert.AreEqual(1, dismissCount);
            Assert.AreEqual(0, manager.QueuedCount);
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [Test]
        public void Manager_ClearGroupDismissesQueuedHandles()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            int dismissCount = 0;
            manager.Show(ToastRequest.Text("one"));
            var queued = manager.Show(new ToastRequest
            {
                Message = "two",
                GroupKey = "interaction",
                OnDismissed = _ => dismissCount++
            });

            manager.ClearGroup("interaction");

            Assert.IsTrue(queued.IsDismissed);
            Assert.AreEqual(1, dismissCount);
            Assert.AreEqual(0, manager.QueuedCount);
        }

        [Test]
        public void Manager_QueueOverflowDismissesOldestQueuedHandle()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 5, 1, 0.5f);
            manager.Configure(settings, null, null);

            int dismissCount = 0;
            manager.Show(ToastRequest.Text("active"));
            var oldestQueued = manager.Show(new ToastRequest
            {
                Message = "old queued",
                DedupeKey = "old",
                OnDismissed = _ => dismissCount++
            });
            var newestQueued = manager.Show(new ToastRequest { Message = "new queued", DedupeKey = "new" });

            Assert.IsTrue(oldestQueued.IsDismissed);
            Assert.IsFalse(newestQueued.IsDismissed);
            Assert.AreEqual(1, dismissCount);
            Assert.AreEqual(1, manager.QueuedCount);

            var oldAfterOverflow = manager.Show(new ToastRequest { Message = "old again", DedupeKey = "old" });

            Assert.AreNotSame(oldestQueued, oldAfterOverflow);
        }

        [Test]
        public void Manager_QueuePolicyDropsIncomingWhenQueueLimitIsZero()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 1, 0);
            manager.Configure(settings, null, null);

            manager.Show(ToastRequest.Text("one"));
            var dropped = manager.Show(ToastRequest.Text("two"));

            Assert.IsNull(dropped);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(0, manager.QueuedCount);
        }

        [Test]
        public void Manager_ClearGroupRemovesQueuedRequests()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 1, 4);
            manager.Configure(settings, null, null);

            manager.Show(new ToastRequest { Message = "active", GroupKey = "interaction" });
            manager.Show(new ToastRequest { Message = "queued interaction", GroupKey = "interaction" });
            manager.Show(new ToastRequest { Message = "queued other", GroupKey = "other" });

            manager.ClearGroup("interaction");

            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(0, manager.QueuedCount);
        }

        [Test]
        public void Manager_RepositionsToastsPerAnchor()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 5, 12, 0f);
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var topOne = manager.Show(new ToastRequest { Message = "top 1", Anchor = ToastAnchor.TopRight });
            var bottomOne = manager.Show(new ToastRequest { Message = "bottom 1", Anchor = ToastAnchor.BottomCenter });
            var topTwo = manager.Show(new ToastRequest { Message = "top 2", Anchor = ToastAnchor.TopRight });

            Assert.AreEqual(1, presenter.GetIndex(topOne));
            Assert.AreEqual(0, presenter.GetIndex(bottomOne));
            Assert.AreEqual(0, presenter.GetIndex(topTwo));
        }

        [Test]
        public void Manager_BottomAnchorsStackUpward()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 5, 12, 0f);
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var first = manager.Show(new ToastRequest { Message = "bottom 1", Anchor = ToastAnchor.BottomCenter });
            var second = manager.Show(new ToastRequest { Message = "bottom 2", Anchor = ToastAnchor.BottomCenter });

            Assert.AreEqual(-1, presenter.GetStackDirection(first));
            Assert.AreEqual(0, presenter.GetStackDirection(second));
        }

        [Test]
        public void Manager_UsesOverrideColorForAccentOnly()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var request = new ToastRequest
            {
                Message = "colored",
                Severity = ToastSeverity.Warning,
                OverrideColor = Color.magenta
            };
            var handle = manager.Show(request);
            var normalStyle = settings.GetStyle(ToastSeverity.Warning);
            var shownStyle = presenter.GetStyle(handle);

            Assert.AreEqual(Color.magenta, shownStyle.AccentColor);
            Assert.AreEqual(normalStyle.BackgroundColor, shownStyle.BackgroundColor);
            Assert.AreEqual(normalStyle.TextColor, shownStyle.TextColor);
        }

        [Test]
        public void Manager_ClearGroupOnlyClearsMatchingGroup()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 5, 12, 0f);
            manager.Configure(settings, null, null);

            var combat = manager.Show(new ToastRequest { Message = "hit", GroupKey = "combat" });
            var ui = manager.Show(new ToastRequest { Message = "copy", GroupKey = "ui" });

            manager.ClearGroup("combat");

            Assert.IsTrue(combat.IsDismissed);
            Assert.IsFalse(ui.IsDismissed);
        }

        [Test]
        public void Manager_InvokesDismissedCallbackOnce()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            int callbackCount = 0;
            var handle = manager.Show(new ToastRequest
            {
                Message = "close me",
                OnDismissed = _ => callbackCount++
            });

            handle.Dismiss();
            handle.Dismiss();

            Assert.AreEqual(1, callbackCount);
        }

        [Test]
        public void RootPresenter_DisableClearsRuntimeState()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 5, 12, 0f);

            var itemPrefabObject = new GameObject("ToastItemPrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastItemView));
            var itemPrefab = itemPrefabObject.GetComponent<ToastItemView>();

            var rootObject = new GameObject("ToastRoot", typeof(RectTransform), typeof(ToastRootPresenter));
            var topContainer = CreateContainer(rootObject.transform, ToastAnchor.TopRight, itemPrefab);

            var presenter = rootObject.GetComponent<ToastRootPresenter>();
            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("settings").objectReferenceValue = settings;
            var containers = presenterObject.FindProperty("containers");
            containers.arraySize = 1;
            containers.GetArrayElementAtIndex(0).objectReferenceValue = topContainer;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();
            presenter.RebuildLookup();
            ToastApi.Configure(settings, null, presenter);

            ToastApi.Show("visible");
            typeof(ToastRootPresenter)
                .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(presenter, null);

            Assert.AreEqual(0, ToastApi.Runtime.ActiveCount);

            Object.DestroyImmediate(rootObject);
            Object.DestroyImmediate(itemPrefabObject);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void RootPresenter_RoutesRequestsToMatchingAnchorContainers()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 5, 12, 0f);

            var itemPrefabObject = new GameObject("ToastItemPrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastItemView));
            var itemPrefab = itemPrefabObject.GetComponent<ToastItemView>();

            var rootObject = new GameObject("ToastRoot", typeof(RectTransform), typeof(ToastRootPresenter));
            var topContainer = CreateContainer(rootObject.transform, ToastAnchor.TopRight, itemPrefab);
            var bottomContainer = CreateContainer(rootObject.transform, ToastAnchor.BottomCenter, itemPrefab);

            var presenter = rootObject.GetComponent<ToastRootPresenter>();
            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("settings").objectReferenceValue = settings;
            var containers = presenterObject.FindProperty("containers");
            containers.arraySize = 2;
            containers.GetArrayElementAtIndex(0).objectReferenceValue = topContainer;
            containers.GetArrayElementAtIndex(1).objectReferenceValue = bottomContainer;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();
            presenter.RebuildLookup();

            var manager = new ToastManager();
            manager.Configure(settings, null, presenter);
            manager.Show(new ToastRequest { Message = "top", Anchor = ToastAnchor.TopRight });
            manager.Show(new ToastRequest { Message = "bottom", Anchor = ToastAnchor.BottomCenter });

            Assert.AreEqual(1, topContainer.VisibleCount);
            Assert.AreEqual(1, bottomContainer.VisibleCount);

            Object.DestroyImmediate(rootObject);
            Object.DestroyImmediate(itemPrefabObject);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Container_ActivatesToastInstanceCreatedFromInactivePrefab()
        {
            var itemPrefabObject = new GameObject("ToastItemPrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastItemView));
            itemPrefabObject.SetActive(false);
            var itemPrefab = itemPrefabObject.GetComponent<ToastItemView>();

            var containerObject = new GameObject("ToastContainer", typeof(RectTransform), typeof(ToastContainer));
            var container = containerObject.GetComponent<ToastContainer>();
            var serialized = new SerializedObject(container);
            serialized.FindProperty("itemRoot").objectReferenceValue = containerObject.GetComponent<RectTransform>();
            serialized.FindProperty("itemPrefab").objectReferenceValue = itemPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            container.ShowToast(CreateHandle(1, "inactive prefab"), "inactive prefab", new ToastStyle(), ToastAnimationTimings.Default);

            var views = containerObject.GetComponentsInChildren<ToastItemView>(true);
            Assert.AreEqual(1, views.Length);
            Assert.IsTrue(views[0].gameObject.activeSelf);

            Object.DestroyImmediate(containerObject);
            Object.DestroyImmediate(itemPrefabObject);
        }

        [Test]
        public void RootPresenter_ActivatesToastInstanceCreatedFromInactivePrefab()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();

            var itemPrefabObject = new GameObject("ToastItemPrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastItemView));
            itemPrefabObject.SetActive(false);
            var itemPrefab = itemPrefabObject.GetComponent<ToastItemView>();

            var rootObject = new GameObject("ToastRoot", typeof(RectTransform), typeof(ToastRootPresenter));
            var topContainer = CreateContainer(rootObject.transform, ToastAnchor.TopRight, itemPrefab);

            var presenter = rootObject.GetComponent<ToastRootPresenter>();
            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("settings").objectReferenceValue = settings;
            var containers = presenterObject.FindProperty("containers");
            containers.arraySize = 1;
            containers.GetArrayElementAtIndex(0).objectReferenceValue = topContainer;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();
            presenter.RebuildLookup();

            var manager = new ToastManager();
            manager.Configure(settings, null, presenter);
            manager.Show(ToastRequest.Text("inactive presenter prefab"));

            var views = topContainer.GetComponentsInChildren<ToastItemView>(true);
            Assert.AreEqual(1, views.Length);
            Assert.IsTrue(views[0].gameObject.activeSelf);

            Object.DestroyImmediate(rootObject);
            Object.DestroyImmediate(itemPrefabObject);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Installer_SourceBindsClickableToastItemPrefab()
        {
            var source = System.IO.File.ReadAllText("Packages/com.zerogamestudio.zeroengine.ui/Editor/Toast/ToastInstaller.cs");

            StringAssert.Contains("typeof(Button)", source);
            StringAssert.Contains("serialized.FindProperty(\"button\").objectReferenceValue = button", source);
        }

        [Test]
        public void RuntimeBootstrap_SourceKeepsTopAlertsNearScreenTop()
        {
            var runtimeSource = System.IO.File.ReadAllText("Packages/com.zerogamestudio.zeroengine.ui/Runtime/UI/Toast/ToastManager.cs");
            var installerSource = System.IO.File.ReadAllText("Packages/com.zerogamestudio.zeroengine.ui/Editor/Toast/ToastInstaller.cs");

            StringAssert.Contains("new Vector2(0f, -96f)", runtimeSource);
            StringAssert.Contains("new Vector2(-174f, -74f)", runtimeSource);
            StringAssert.DoesNotContain("new Vector2(0f, -200f)", runtimeSource);

            StringAssert.Contains("new Vector2(0f, -96f)", installerSource);
            StringAssert.Contains("new Vector2(-174f, -74f)", installerSource);
            StringAssert.DoesNotContain("new Vector2(0f, -200f)", installerSource);
        }

        private sealed class TestResolver : IToastTextResolver
        {
            public string ResolveToastText(ToastRequest request)
            {
                return request.TextKey == "PurchaseDeny" ? "Cannot buy this" : request.Message;
            }
        }

        private sealed class RecordingPresenter : IToastPresenter
        {
            private readonly System.Collections.Generic.Dictionary<int, ToastStyle> styles = new System.Collections.Generic.Dictionary<int, ToastStyle>();
            private readonly System.Collections.Generic.Dictionary<int, int> indices = new System.Collections.Generic.Dictionary<int, int>();
            private readonly System.Collections.Generic.Dictionary<int, int> directions = new System.Collections.Generic.Dictionary<int, int>();
            private readonly System.Collections.Generic.HashSet<string> shownTexts = new System.Collections.Generic.HashSet<string>();
            private readonly System.Collections.Generic.HashSet<int> shownHandleIds = new System.Collections.Generic.HashSet<int>();

            public int ShowCount { get; private set; }
            public int RefreshCount { get; private set; }

            public void ShowToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
            {
                styles[handle.Id] = style;
                shownTexts.Add(resolvedText);
                shownHandleIds.Add(handle.Id);
                ShowCount++;
            }

            public void RefreshToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
            {
                styles[handle.Id] = style;
                shownTexts.Add(resolvedText);
                RefreshCount++;
            }

            public void DismissToast(ToastHandle handle, ToastDismissReason reason) { }

            public void RepositionToast(ToastHandle handle, int index, float spacing)
            {
                indices[handle.Id] = Mathf.Abs(index);
                directions[handle.Id] = index;
            }

            public void ClearAll() { }

            public ToastStyle GetStyle(ToastHandle handle)
            {
                return styles[handle.Id];
            }

            public int GetIndex(ToastHandle handle)
            {
                return indices[handle.Id];
            }

            public int GetStackDirection(ToastHandle handle)
            {
                return directions[handle.Id];
            }

            public bool WasTextShown(string text)
            {
                return shownTexts.Contains(text);
            }

            public bool WasHandleShown(ToastHandle handle)
            {
                return handle != null && shownHandleIds.Contains(handle.Id);
            }
        }

        private static void SetSettingsPolicy(ToastSettings settings, ToastOverflowPolicy overflowPolicy, int maxVisible, int maxQueued, float showInterval = 0f)
        {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("overflowPolicy").enumValueIndex = (int)overflowPolicy;
            serialized.FindProperty("maxVisible").intValue = maxVisible;
            serialized.FindProperty("maxQueued").intValue = maxQueued;
            var showIntervalProperty = serialized.FindProperty("showInterval");
            Assert.IsNotNull(showIntervalProperty);
            showIntervalProperty.floatValue = showInterval;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Tick(ToastManager manager, float unscaledTime)
        {
            var method = typeof(ToastManager).GetMethod(
                "Tick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(manager, new object[] { unscaledTime });
        }

        private static ToastContainer CreateContainer(Transform parent, ToastAnchor anchor, ToastItemView itemPrefab)
        {
            var containerObject = new GameObject(anchor.ToString(), typeof(RectTransform), typeof(ToastContainer));
            containerObject.transform.SetParent(parent, false);
            var container = containerObject.GetComponent<ToastContainer>();
            var serialized = new SerializedObject(container);
            serialized.FindProperty("anchor").enumValueIndex = (int)anchor;
            serialized.FindProperty("itemRoot").objectReferenceValue = containerObject.GetComponent<RectTransform>();
            serialized.FindProperty("itemPrefab").objectReferenceValue = itemPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return container;
        }

        private static ToastHandle CreateHandle(int id, string message)
        {
            var constructor = typeof(ToastHandle).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(ToastRequest), typeof(System.Action<ToastHandle, ToastDismissReason>) },
                null);
            Assert.IsNotNull(constructor);

            return (ToastHandle)constructor.Invoke(new object[]
            {
                id,
                ToastRequest.Text(message),
                (System.Action<ToastHandle, ToastDismissReason>)((_, __) => { })
            });
        }
    }
}
