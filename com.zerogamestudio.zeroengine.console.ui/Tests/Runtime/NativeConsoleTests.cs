using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Console.Unity;
using Object = UnityEngine.Object;

namespace ZeroEngine.Console.UI.Tests
{
    public sealed class NativeConsoleTests
    {
        [UnityTest]
        public IEnumerator NumberedChoicesReuseCandidateRowsWithoutSubmittingCommands()
        {
            var root = new GameObject("Native choice interaction");
            IDisposable commands = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                commands = host.Registry.Register("sample", new[]
                {
                    new ConsoleBinding(new ConsoleCommand("sample.plan", "sample-plan", "sample", "Pick a plan."),
                        async (invocation, args, token) =>
                        {
                            var choice = await invocation.ChooseAsync("Choose a plan.", new[]
                            {
                                new ConsoleCompletionItem("quick", "", "Quick"), new ConsoleCompletionItem("full", "", "Full")
                            });
                            return ConsoleResult.Success(data: choice);
                        })
                });
                view.Open();
                var execution = view.SubmitAsync("sample-plan");
                yield return null;
                var choices = root.GetComponentsInChildren<UnityEngine.UI.Button>().Where(button => button.name == "Completion").ToArray();
                Assert.That(choices.Select(button => button.GetComponentInChildren<UnityEngine.UI.Text>().text),
                    Does.Contain("2. Full"));
                view.InputField.onSubmit.Invoke("test-unknown");
                Assert.That(execution.IsCompleted, Is.False);
                view.InputField.text = "2";
                view.InputField.onSubmit.Invoke("2");
                yield return Wait(execution);
                Assert.That(execution.Result.Data, Is.EqualTo("full"));
                Assert.That(view.InputField.text, Is.Empty);
                Assert.That(host.Session.GetHistory(), Is.EqualTo(new[] { "sample-plan" }));
            }
            finally { commands?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator OnDemandSelector_DefersBothFrontendsAndCanReleaseTheView()
        {
            var root = new GameObject("On-demand console");
            try
            {
                root.SetActive(false);
                var host = root.AddComponent<ConsoleHost>();
                host.StartupMode = ConsoleStartupMode.OnDemand;
                var view = root.AddComponent<NativeConsole>();
                view.ReleaseViewOnClose = true;
                var projectFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                view.ConfigureFont(projectFont);
                var selector = root.AddComponent<ConsoleFrontendSelector>();
                var provider = root.AddComponent<OpeningFailureFrontend>();
                selector.ConfigureQuantum(provider);
                root.SetActive(true);
                yield return null;

                Assert.That(host.Session, Is.Null);
                Assert.That(selector.Current, Is.Null);
                Assert.That(provider.InitializeCount, Is.Zero);
                Assert.That(root.GetComponentsInChildren<Canvas>(true), Is.Empty);
                selector.Open();
                Assert.That(provider.InitializeCount, Is.EqualTo(1));
                Assert.That(view.IsOpen, Is.True);
                var session = host.Session;
                yield return Wait(view.SubmitAsync("ze-session"));
                session.Write(new ConsoleOutput("sample", ConsoleOutputLevel.Info, "needle kept"));
                session.Write(new ConsoleOutput("sample", ConsoleOutputLevel.Info, "other line"));

                var buttons = root.GetComponentsInChildren<UnityEngine.UI.Button>();
                buttons.Single(button => button.name == "搜索日志").onClick.Invoke();
                var search = root.GetComponentsInChildren<UnityEngine.UI.InputField>().Single(field => field.name == "LogSearch");
                search.text = "needle";
                view.InputField.text = "ze-help";
                view.InputField.caretPosition = 3;
                selector.Close();
                yield return null;

                Assert.That(root.GetComponentsInChildren<Canvas>(true), Is.Empty);
                Assert.That(view.InputField, Is.Null);
                Assert.That(host.Session, Is.SameAs(session));
                Assert.That(projectFont, Is.Not.Null);
                selector.Open();
                double until = Time.realtimeSinceStartupAsDouble + 4;
                while (view.InputField.caretPosition != 3 && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Assert.That(view.InputField.text, Is.EqualTo("ze-help"));
                Assert.That(view.InputField.caretPosition, Is.EqualTo(3));
                Assert.That(view.InputField.textComponent.font, Is.SameAs(projectFont));
                Assert.That(view.GetFilteredOutputText(), Is.EqualTo("needle kept"));
                Assert.That(host.Session.GetHistory(), Is.EqualTo(new[] { "ze-session" }));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Confirmation_UsesItsOwnButtons_AndClosesWithoutExecuting()
        {
            var root = new GameObject("Native confirmation integration");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                int applied = 0;
                module = host.Registry.Register("sample", new[]
                {
                    new ConsoleBinding(new ConsoleCommand("sample.apply", "sample-apply", "sample", "Apply."),
                        async (invocation, args, token) =>
                        {
                            if (!await invocation.ConfirmAsync("Apply this preview?"))
                                return ConsoleResult.Failure("declined", "Discarded.");
                            applied++;
                            return ConsoleResult.Success();
                        })
                });
                view.Open();
                Assert.That(view.transform.Find("ZE Console/Panel/Confirmation"), Is.Null);
                var confirmed = view.SubmitAsync("sample-apply");
                yield return null;
                var row = view.transform.Find("ZE Console/Panel/Confirmation");
                Assert.That(row, Is.Not.Null);
                Assert.That(row.Find("Question").GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("Apply this preview?"));
                view.InputField.onSubmit.Invoke("sample-apply");
                Assert.That(applied, Is.Zero);
                var accept = row.Find("Confirm").GetComponent<UnityEngine.UI.Button>();
                accept.onClick.Invoke();
                accept.onClick.Invoke();
                yield return Wait(confirmed);
                Assert.That(confirmed.Result.IsSuccess, Is.True);
                Assert.That(applied, Is.EqualTo(1));
                Assert.That(host.Session.GetHistory(), Is.EqualTo(new[] { "sample-apply" }));

                var declined = view.SubmitAsync("sample-apply");
                yield return null;
                row.Find("Decline").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                yield return Wait(declined);
                Assert.That(declined.Result.Code, Is.EqualTo("declined"));
                var closed = view.SubmitAsync("sample-apply");
                view.Close();
                yield return Wait(closed);
                Assert.That(closed.Result.Code, Is.EqualTo("declined"));
                Assert.That(applied, Is.EqualTo(1));
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator MacroAndOverloadHints_KeepTheDraftAndSubmitTheSelectedHandlerOnce()
        {
            var root = new GameObject("Native macro overload integration");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                int calls = 0;
                module = host.Registry.RegisterOverloads("sample", new[] {
                    new ConsoleBinding(new ConsoleCommand("sample.one", "sample-pick", "sample", "Pick a flag.",
                        new[] { new ConsoleParameter("enabled", typeof(bool)) }),
                        (context, args, token) => { calls++; return Task.FromResult(ConsoleResult.Success(data: false)); }),
                    new ConsoleBinding(new ConsoleCommand("sample.two", "sample-pick", "sample", "Pick a numbered flag.",
                        new[] { new ConsoleParameter("count", typeof(int)), new ConsoleParameter("enabled", typeof(bool)) }),
                        (context, args, token) => { calls++; return Task.FromResult(ConsoleResult.Success(data: args.Get<int>(0))); }) });
                host.Session.Macros = new ConsoleMacros();
                host.Session.Macros.Define("preset", "sample-pick 3");
                view.Open();
                yield return null;
                view.InputField.text = "#preset tr";
                view.InputField.caretPosition = view.InputField.text.Length;
                var hint = view.transform.Find("ZE Console/Panel/CommandHint");
                var detail = hint.Find("Parameter").GetComponent<UnityEngine.UI.Text>();
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                UnityEngine.UI.Button choice = null;
                while (!choice && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    choice = view.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(button => button.name == "Completion" &&
                        button.GetComponentInChildren<UnityEngine.UI.Text>().text == "true");
                    yield return null;
                }
                Assert.That(choice, Is.Not.Null);
                Assert.That(detail.text, Does.Contain("enabled: bool"));
                Assert.That(calls, Is.Zero);
                choice.onClick.Invoke();
                Assert.That(view.InputField.text, Is.EqualTo("#preset true"));
                Assert.That(calls, Is.Zero);
                var result = view.SubmitAsync(view.InputField.text);
                yield return Wait(result);
                Assert.That(result.Result.Data, Is.EqualTo(3));
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(host.Session.GetHistory().Last(), Is.EqualTo("#preset true"));
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator ThirdPartyInputOverlay_ShowsPolicyCheckedHintsWithoutSubmitting()
        {
            var root = new GameObject("Third-party input contract");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                int calls = 0;
                module = host.Registry.Register("sample", new[] { new ConsoleBinding(new ConsoleCommand("sample.flag", "sample-flag", "sample", "Set a flag.",
                    new[] { new ConsoleParameter("enabled", typeof(bool), "开关") }),
                    (context, args, token) => { calls++; return Task.FromResult(ConsoleResult.Success()); }) });
                string text = "sample-flag "; int caret = text.Length;
                var overlay = root.AddComponent<ConsoleInputOverlay>();
                overlay.Initialize(host, new ConsoleInputAccess(() => text, value => { text = value; caret = value.Length; },
                    () => caret, () => true, () => false));
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                UnityEngine.UI.Text detail = null;
                while (!detail && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    detail = root.GetComponentsInChildren<UnityEngine.UI.Text>().FirstOrDefault(label => label.name == "Parameter" && label.text.Contains("enabled"));
                    yield return null;
                }
                Assert.That(detail, Is.Not.Null);
                Assert.That(detail.text, Does.Contain("enabled: bool").And.Contain("开关"));
                Assert.That(calls, Is.Zero);
                var choice = root.GetComponentsInChildren<UnityEngine.UI.Button>().First(button => button.name == "Native completion");
                choice.onClick.Invoke();
                Assert.That(text, Is.EqualTo("sample-flag false"));
                Assert.That(calls, Is.Zero);
                overlay.ResetAfterSubmit();
                Assert.That(detail.transform.parent.gameObject.activeSelf, Is.False);
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator CommandHint_TracksCaretWithoutExecutingAndReleasesOutputAreaWhenCleared()
        {
            var root = new GameObject("Native contextual parameter hint");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                int calls = 0;
                module = host.Registry.Register("sample", new[] { new ConsoleBinding(new ConsoleCommand("sample.build", "sample-build", "sample", "Create an item.", new[] {
                    new ConsoleParameter("name", typeof(string), "物品名称"),
                    new ConsoleParameter("count", typeof(int), "数量", optional: true, defaultValue: 1) }),
                    (context, args, token) => { calls++; return Task.FromResult(ConsoleResult.Success()); }) });
                view.Open();
                yield return null;
                view.InputField.text = "sample-build \"中文 空格\" ";
                view.InputField.caretPosition = view.InputField.text.Length;
                var hint = view.transform.Find("ZE Console/Panel/CommandHint");
                var detail = hint.Find("Parameter").GetComponent<UnityEngine.UI.Text>();
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                while ((!hint.gameObject.activeSelf || !detail.text.Contains("count")) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(detail.text, Does.Contain("count: int").And.Contain("数量"));
                Assert.That(hint.Find("Signature").GetComponent<UnityEngine.UI.Text>().text, Does.Contain("[count: int = 1]"));
                Assert.That(calls, Is.Zero);
                Assert.That(host.Session.GetHistory(), Is.Empty);
                var output = view.transform.Find("ZE Console/Panel/Output").GetComponent<RectTransform>();
                Assert.That(output.offsetMin.y, Is.GreaterThan(90));
                view.InputField.text = string.Empty;
                yield return null;
                Assert.That(hint.gameObject.activeSelf, Is.False);
                Assert.That(output.offsetMin.y, Is.EqualTo(90));
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator DataOnlyResult_FormatsOnceThroughTheSharedHost()
        {
            var root = new GameObject("Native formatted result");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                module = host.Registry.Register("sample", new[] { new ConsoleBinding(new ConsoleCommand("sample.values", "sample-values", "sample", "Read values."),
                    (context, args, token) => Task.FromResult(ConsoleResult.Success(data: new object[] { "中文", 1.25 }))) });
                view.Open();
                var result = view.SubmitAsync("sample-values");
                yield return Wait(result);
                yield return null;
                Assert.That(result.Result.IsSuccess, Is.True);
                Assert.That(host.Session.GetOutput().Count(row => row.Message == "[\"中文\", 1.25]"), Is.EqualTo(1));
                Assert.That(view.GetFilteredOutputText(), Does.Contain("[\"中文\", 1.25]"));
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_EmptyDraftKeepsLogsVisibleAndClearingCancelsSuggestions()
        {
            var root = new GameObject("Quiet console draft");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.Open();
                var suggestions = view.transform.Find("ZE Console/Panel/Completions").gameObject;
                var rows = suggestions.transform.Find("Choices");
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(suggestions.activeSelf, Is.False);
                Assert.That(rows.childCount, Is.Zero);
                view.InputField.text = "ze-hel";
                view.InputField.caretPosition = 6;
                double singleDeadline = Time.realtimeSinceStartupAsDouble + 1;
                while (!suggestions.activeSelf && Time.realtimeSinceStartupAsDouble < singleDeadline) yield return null;
                Assert.That(suggestions.activeSelf, Is.True);
                Assert.That(rows.childCount, Is.EqualTo(1));
                Assert.That(suggestions.transform.Find("CompletionCaption"), Is.Null);
                view.InputField.text = "ze-";
                view.InputField.caretPosition = 3;
                double deadline = Time.realtimeSinceStartupAsDouble + 1;
                while (!suggestions.activeSelf && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(suggestions.activeSelf, Is.True);
                Assert.That(rows.childCount, Is.GreaterThan(0));
                view.InputField.text = string.Empty;
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(suggestions.activeSelf, Is.False);
                view.InputField.text = "   ";
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(suggestions.activeSelf, Is.False);
                Assert.That(view.Host.Session.GetHistory(), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_ConfigurableSizeAndTouchOrientationStayInsideCanvas()
        {
            var root = new GameObject("Console size and orientation");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                Assert.That(view.PreferredSize, Is.EqualTo(new Vector2(880, 480)));
                view.Open();
                yield return null;
                var canvas = view.GetComponentInChildren<Canvas>();
                var area = (RectTransform)canvas.transform;
                var panel = (RectTransform)canvas.transform.Find("Panel");
                var initial = panel.rect.size;
                view.PreferredSize = new Vector2(640, 360);
                yield return null;
                Assert.That(panel.rect.width, Is.LessThan(initial.x));
                Assert.That(panel.rect.height, Is.LessThan(initial.y));
                view.TouchLayoutPreview = true;
                yield return null;
                yield return null;
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                Assert.That(scaler.referenceResolution, Is.EqualTo(Screen.width >= Screen.height
                    ? new Vector2(854, 480) : new Vector2(480, 854)));
                view.PreferredSize = new Vector2(10000, 10000);
                yield return null;
                var corners = new Vector3[4]; panel.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = area.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(area.rect.xMin - 1, area.rect.xMax + 1));
                    Assert.That(point.y, Is.InRange(area.rect.yMin - 1, area.rect.yMax + 1));
                }
                view.Close();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_ClosedProjectOwnedInputRemainsIdle()
        {
            var root = new GameObject("Closed console allocation probe");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.KeyboardShortcutEnabled = false;
                var method = typeof(NativeConsole).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var update = (Action)Delegate.CreateDelegate(typeof(Action), view, method);
                for (int i = 0; i < 100; i++) update();
                long controlBefore = GC.GetAllocatedBytesForCurrentThread();
                var control = new byte[1024];
                GC.KeepAlive(control);
                bool allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() - controlBefore >= 1024;
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 10000; i++) update();
                clock.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(view.IsOpen, Is.False);
                Assert.That(view.InputField, Is.Null, "Closed updates must not construct the view.");
                Assert.That(view.Host.Session.GetHistory(), Is.Empty);
                Assert.That(view.Host.Session.GetOutput(), Is.Empty);
                if (allocationCounterAvailable)
                    Assert.That(allocated, Is.LessThanOrEqualTo(128), "The closed view must not allocate per update.");
                TestContext.WriteLine("closed-update probe: 10000 calls, allocated=" +
                    (allocationCounterAvailable ? allocated.ToString() : "unavailable (positive control failed)") + ", elapsedTicks=" + clock.ElapsedTicks);
                yield return null;
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator AccessController_ReusesHiddenSequenceAndRestoresShortcutOwnership()
        {
            var root = new GameObject("Shared console access");
            root.SetActive(false);
            try
            {
                var view = root.AddComponent<NativeConsole>();
                var access = root.AddComponent<ConsoleAccessController>();
                access.ConfigureHiddenPath("aaab", 0);
                root.SetActive(true);
                Assert.That(access.CanOpen, Is.True, "Editor access requires no unlock.");
                Assert.That(view.KeyboardShortcutEnabled, Is.False);
                Assert.That(access.IsReleaseUnlocked, Is.False);
                Assert.That(access.TryUnlock("incorrect"), Is.False);
                Assert.That(access.FeedHiddenInput("aaa"), Is.False);
                Assert.That(access.FeedHiddenInput("ab"), Is.True, "Overlapping prefixes must be accepted.");
                Assert.That(access.IsReleaseUnlocked, Is.True);
                access.Toggle();
                yield return null;
                Assert.That(view.IsOpen, Is.True);
                access.Lock();
                Assert.That(access.IsReleaseUnlocked, Is.False);
                Assert.That(view.IsOpen, Is.False);
                access.enabled = false;
                Assert.That(view.KeyboardShortcutEnabled, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Selector_InactiveNativeConfigurationOpensWithoutAQuantumWarning()
        {
            var root = new GameObject("Explicit project input owner");
            root.SetActive(false);
            try
            {
                var selector = root.AddComponent<ConsoleFrontendSelector>();
                selector.Preference = ConsoleFrontendPreference.Native;
                selector.KeyboardShortcutEnabled = false;
                root.SetActive(true);
                selector.Open();
                yield return null;
                Assert.That(selector.Current.BackendId, Is.EqualTo("native"));
                Assert.That(selector.Current.IsOpen, Is.True);
                Assert.That(root.GetComponent<ConsoleHost>().Session.GetOutput(), Is.Empty);
                selector.Close();
                selector.enabled = false;
                Assert.That(root.GetComponent<NativeConsole>().KeyboardShortcutEnabled, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Selector_OpeningFailureReleasesProviderBeforeNativeFallback()
        {
            var root = new GameObject("Frontend opening failure contract");
            int leases = 0;
            try
            {
                var provider = root.AddComponent<OpeningFailureFrontend>();
                var selector = root.AddComponent<ConsoleFrontendSelector>();
                selector.AcquireInputIsolation = () => { leases++; return new ActionLease(() => leases--); };
                selector.ConfigureQuantum(provider);
                selector.Open();
                yield return null;
                Assert.That(provider.OpenCount, Is.EqualTo(1));
                Assert.That(provider.IsOpen, Is.False);
                Assert.That(selector.Current.BackendId, Is.EqualTo("native"));
                Assert.That(selector.Current.IsOpen, Is.True);
                Assert.That(leases, Is.EqualTo(1));
                selector.Close();
                Assert.That(leases, Is.Zero);
                selector.enabled = false;
                Assert.That(provider.AcquireInputIsolation, Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Selector_WithoutQuantumReopensAndRestoresItsBorrowedInputCallback()
        {
            GameObject root = null;
            int leases = 0;
            try
            {
                root = new GameObject("Frontend selection contract");
                var view = root.AddComponent<NativeConsole>();
                Func<IDisposable> original = () => { leases++; return new ActionLease(() => leases--); };
                view.AcquireInputIsolation = original;
                var selector = root.AddComponent<ConsoleFrontendSelector>();
                Assert.That(selector.Current.BackendId, Is.EqualTo("native"));
                selector.Open();
                Assert.That(leases, Is.EqualTo(1));
                Assert.That(UnityEngine.EventSystems.EventSystem.current, Is.Not.Null);
                selector.Select(ConsoleFrontendPreference.Native);
                yield return null;
                Assert.That(view.IsOpen, Is.True);
                Assert.That(leases, Is.EqualTo(1));
                Assert.That(UnityEngine.EventSystems.EventSystem.current, Is.Not.Null);
                selector.enabled = false;
                Assert.That(leases, Is.Zero);
                Assert.That(view.AcquireInputIsolation, Is.SameAs(original));
                Assert.That(view.KeyboardShortcutEnabled, Is.True);
            }
            finally { if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_RunButtonUsesOneHandlerAndRestoresInputLease()
        {
            GameObject root = null;
            IDisposable module = null;
            int leases = 0, calls = 0;
            var previousLock = Cursor.lockState;
            bool previousCursor = Cursor.visible;
            try
            {
                root = new GameObject("Native Console vertical slice");
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                view.AcquireInputIsolation = () => { leases++; return new ActionLease(() => leases--); };
                module = host.Registry.Register("sample", new[] {
                    new ConsoleBinding(new ConsoleCommand("sample.set", "sample-set", "sample", "Set owned state.",
                        new[] { new ConsoleParameter("value", typeof(int)) }),
                        (context, args, token) => { calls++; context.Write("状态已更新：" + args.Get<int>(0)); return Task.FromResult(ConsoleResult.Success()); }) });
                view.Open();
                yield return null;
                yield return null;
                Assert.That(view.IsOpen, Is.True);
                Assert.That(leases, Is.EqualTo(1));
                Assert.That(view.InputField.textComponent.rectTransform.rect.width, Is.GreaterThan(100));
                view.InputField.text = "sample-set 17";
                Button(view, "运行").onClick.Invoke();
                while (host.Session.IsRunning) yield return null;
                yield return null;
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(host.Session.GetOutput().Any(entry => entry.Message == "状态已更新：17"), Is.True);
                Button(view, "关闭").onClick.Invoke();
                Assert.That(view.IsOpen, Is.False);
                Assert.That(leases, Is.Zero);
                Assert.That(Cursor.lockState, Is.EqualTo(previousLock));
                Assert.That(Cursor.visible, Is.EqualTo(previousCursor));
            }
            finally
            {
                module?.Dispose();
                if (root) Object.DestroyImmediate(root);
                Cursor.lockState = previousLock; Cursor.visible = previousCursor;
            }
        }

        [UnityTest]
        public IEnumerator NativeView_CompositionInputDefersRunUntilCommit()
        {
            GameObject root = null, eventsOwner = null;
            NativeConsole view = null;
            UnityEngine.EventSystems.BaseInputModule inputModule = null;
            UnityEngine.EventSystems.BaseInput previousInput = null;
            CompositionInputSource controlled = null;
            IDisposable module = null;
            int calls = 0, leases = 0;
            string received = null;
            var previousLock = Cursor.lockState;
            bool previousCursor = Cursor.visible;
            try
            {
                var events = UnityEngine.EventSystems.EventSystem.current;
                if (!events)
                {
                    eventsOwner = new GameObject("Composition event system", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                    events = eventsOwner.GetComponent<UnityEngine.EventSystems.EventSystem>();
                }
                inputModule = events.currentInputModule;
                if (!inputModule) inputModule = events.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                Assert.That(inputModule, Is.Not.Null);
                root = new GameObject("Composition submit contract");
                controlled = root.AddComponent<CompositionInputSource>();
                previousInput = inputModule.inputOverride;
                inputModule.inputOverride = controlled;
                var host = root.AddComponent<ConsoleHost>();
                view = root.AddComponent<NativeConsole>();
                view.AcquireInputIsolation = () => { leases++; return new ActionLease(() => leases--); };
                module = host.Registry.Register("composition", new[] {
                    new ConsoleBinding(new ConsoleCommand("composition.write", "composition-write", "composition", "Read committed Unicode input.",
                        new[] { new ConsoleParameter("value", typeof(string)) }),
                        (context, args, token) => { calls++; received = args.Get<string>(0); return Task.FromResult(ConsoleResult.Success(received)); }) });
                view.Open();
                yield return null;
                yield return null;
                inputModule.inputOverride = controlled;
                controlled.Composition = "ni'hao";
                view.InputField.text = "composition-write 你好";
                Button(view, "运行").onClick.Invoke();
                yield return null;
                Assert.That(view.IsComposing, Is.True);
                Assert.That(calls, Is.Zero);
                controlled.Composition = string.Empty;
                Button(view, "运行").onClick.Invoke();
                while (host.Session.IsRunning) yield return null;
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(received, Is.EqualTo("你好"));
                view.Close();
                Assert.That(leases, Is.Zero);
                Assert.That(inputModule.inputOverride, Is.SameAs(controlled));
                Assert.That(Cursor.lockState, Is.EqualTo(previousLock));
                Assert.That(Cursor.visible, Is.EqualTo(previousCursor));
            }
            finally
            {
                if (view) view.Close();
                if (inputModule && inputModule.inputOverride == controlled) inputModule.inputOverride = previousInput;
                module?.Dispose();
                if (root) Object.DestroyImmediate(root);
                if (eventsOwner) Object.DestroyImmediate(eventsOwner);
            }
        }

        [UnityTest]
        public IEnumerator DynamicCompletion_ButtonAppliesNativeQuotedArgument()
        {
            GameObject root = null;
            IDisposable module = null;
            try
            {
                root = new GameObject("Native completion slice");
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                module = host.Registry.Register("sample", new[] {
                    new ConsoleBinding(new ConsoleCommand("sample.query", "sample-query", "sample", "Read a resource ID.",
                        new[] { new ConsoleParameter("id", typeof(string)) }),
                        (context, args, token) => Task.FromResult(ConsoleResult.Success(args.Get<string>(0))),
                        (context, token) => Task.FromResult<System.Collections.Generic.IReadOnlyList<ConsoleCompletionItem>>(
                            new[] { new ConsoleCompletionItem("resource.one", "双手 剑") })) });
                view.Open();
                yield return null;
                view.InputField.text = "sample-query ";
                view.InputField.caretPosition = view.InputField.text.Length;
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                UnityEngine.UI.Button choice = null;
                while (!choice && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    choice = view.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(button => button.gameObject.name == "Completion");
                    yield return null;
                }
                Assert.That(choice, Is.Not.Null);
                choice.onClick.Invoke();
                Assert.That(view.InputField.text, Is.EqualTo("sample-query \"双手 剑\""));
                var result = view.SubmitAsync(view.InputField.text);
                yield return Wait(result);
                Assert.That(result.GetAwaiter().GetResult().Message, Is.EqualTo("双手 剑"));
            }
            finally { module?.Dispose(); if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator CancelButton_RemainsUsableWhenGameTimeIsPaused()
        {
            GameObject root = null;
            IDisposable module = null;
            float previousScale = Time.timeScale;
            try
            {
                root = new GameObject("Native cancellation slice");
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                module = host.Registry.Register("sample", new[] {
                    new ConsoleBinding(new ConsoleCommand("sample.wait", "sample-wait", "sample", "Cancellable asynchronous work."),
                        async (context, args, token) => { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return ConsoleResult.Success(); }) });
                view.Open();
                yield return null;
                Time.timeScale = 0;
                var result = view.SubmitAsync("sample-wait");
                Assert.That(host.Session.IsRunning, Is.True);
                Button(view, "取消").onClick.Invoke();
                yield return Wait(result);
                Assert.That(result.GetAwaiter().GetResult().Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
                view.Close();
                Assert.That(view.IsOpen, Is.False);
            }
            finally { Time.timeScale = previousScale; module?.Dispose(); if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_LowerLeftWindowKeepsInputAndControlsInsideCanvas()
        {
            var root = new GameObject("Lower-left Console layout");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.Open();
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var canvas = view.GetComponentInChildren<Canvas>();
                var canvasRect = (RectTransform)canvas.transform;
                var panel = (RectTransform)canvas.transform.Find("Panel");
                var corners = new Vector3[4]; panel.GetWorldCorners(corners);
                var lowerLeft = canvasRect.InverseTransformPoint(corners[0]);
                var upperRight = canvasRect.InverseTransformPoint(corners[2]);
                Assert.That(lowerLeft.x, Is.EqualTo(canvasRect.rect.xMin + 16).Within(1));
                Assert.That(lowerLeft.y, Is.EqualTo(canvasRect.rect.yMin + 16).Within(1));
                Assert.That(upperRight.x, Is.LessThanOrEqualTo(canvasRect.rect.xMax - 15));
                Assert.That(upperRight.y, Is.LessThanOrEqualTo(canvasRect.rect.yMax - 15));
                Assert.That(view.InputField.textComponent.rectTransform.rect.width, Is.GreaterThan(100));
                foreach (var control in view.GetComponentsInChildren<UnityEngine.UI.Button>())
                {
                    ((RectTransform)control.transform).GetWorldCorners(corners);
                    Assert.That(panel.rect.Contains(panel.InverseTransformPoint(corners[0])), Is.True, control.name);
                    Assert.That(panel.rect.Contains(panel.InverseTransformPoint(corners[2])), Is.True, control.name);
                }
                view.Close();
                view.Open();
                Assert.That(view.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_IconHintsExplainActionsWithoutTakingInputFocus()
        {
            var root = new GameObject("Native icon interaction");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.Open();
                yield return null;
                yield return null;
                var events = UnityEngine.EventSystems.EventSystem.current;
                var selected = events.currentSelectedGameObject;
                var panel = (RectTransform)view.GetComponentInChildren<Canvas>().transform.Find("Panel");
                Assert.That(panel.Find("Title").GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("ZE Console"));
                Assert.That(panel.Find("Backend"), Is.Null);
                Assert.That(panel.Find("ActionHint"), Is.Null);
                var pointer = new UnityEngine.EventSystems.PointerEventData(events);
                foreach (string name in new[] { "清空", "复制", "字号减小", "字号增大", "跟随", "运行", "取消", "关闭" })
                    Assert.That(Button(view, name).GetComponentsInChildren<UnityEngine.UI.Text>(true), Is.Empty, name);
                var copy = Button(view, "复制");
                UnityEngine.EventSystems.ExecuteEvents.Execute(copy.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                var hint = (RectTransform)panel.Find("ActionHint");
                Assert.That(hint.gameObject.activeSelf, Is.True);
                Assert.That(hint.GetComponentInChildren<UnityEngine.UI.Text>().text, Is.EqualTo("复制全部日志"));
                Assert.That(events.currentSelectedGameObject, Is.SameAs(selected));
                UnityEngine.EventSystems.ExecuteEvents.Execute(copy.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                Assert.That(hint.gameObject.activeSelf, Is.False);
                var close = Button(view, "关闭");
                UnityEngine.EventSystems.ExecuteEvents.Execute(close.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                var corners = new Vector3[4]; hint.GetWorldCorners(corners);
                Assert.That(panel.rect.Contains(panel.InverseTransformPoint(corners[0])), Is.True);
                Assert.That(panel.rect.Contains(panel.InverseTransformPoint(corners[2])), Is.True);
                var follow = Button(view, "跟随");
                UnityEngine.EventSystems.ExecuteEvents.Execute(follow.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                follow.onClick.Invoke();
                Assert.That(hint.GetComponentInChildren<UnityEngine.UI.Text>().text, Does.Contain("关"));
                view.Close();
                view.Open();
                yield return null;
                yield return null;
                Assert.That(hint.gameObject.activeSelf, Is.False);
                UnityEngine.EventSystems.ExecuteEvents.Execute(follow.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                Assert.That(hint.GetComponentInChildren<UnityEngine.UI.Text>().text, Does.Contain("关"));
                Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject, Is.SameAs(view.InputField.gameObject));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator NativeView_LogSearchPreservesCommandsAndFiltersNewMessagesAndCopy()
        {
            var root = new GameObject("Native log search");
            IDisposable module = null;
            int calls = 0;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                module = host.Registry.Register("search-test", new[] {
                    new ConsoleBinding(new ConsoleCommand("search.test", "search-test", "search-test", "Command draft must remain untouched."),
                        (context, args, token) => { calls++; return Task.FromResult(ConsoleResult.Success()); }) });
                host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "Connected to MAIN"));
                host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Warning, "中文查找：缓存命中"));
                host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Error, "connect error"));
                view.Open();
                yield return null;
                yield return null;
                Assert.That(view.GetComponentsInChildren<UnityEngine.UI.InputField>(true).Any(field => field.name == "LogSearch"), Is.False);
                view.InputField.text = "search-test";
                view.InputField.caretPosition = view.InputField.text.Length;
                Button(view, "搜索日志").onClick.Invoke();
                yield return null;
                yield return null;
                var search = view.GetComponentsInChildren<UnityEngine.UI.InputField>(true).Single(field => field.name == "LogSearch");
                var output = view.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
                Assert.That(search.isFocused, Is.True);
                Assert.That(view.InputField.isFocused, Is.False, "Only the selected search field may own input.");
                Assert.That(search.textComponent.rectTransform.rect.width, Is.GreaterThan(100));
                search.text = "cOnNeCt";
                yield return null;
                yield return null;
                CollectionAssert.AreEqual(new[] { "Connected to MAIN", "connect error" },
                    output.content.GetComponentsInChildren<UnityEngine.UI.Text>().Select(text => text.text).ToArray());
                Assert.That(view.GetFilteredOutputText(), Is.EqualTo("Connected to MAIN\nconnect error"));
                Button(view, "字号增大").onClick.Invoke();
                yield return null;
                yield return null;
                Assert.That(search.isFocused, Is.True, "Toolbar actions must preserve search focus.");
                Assert.That(view.InputField.isFocused, Is.False);
                search.onSubmit.Invoke("search-test");
                yield return null;
                Assert.That(calls, Is.Zero, "Submitting search must never run the command draft.");
                Assert.That(host.Session.GetHistory(), Is.Empty);
                Assert.That(view.InputField.text, Is.EqualTo("search-test"));
                Assert.That(view.transform.Find("ZE Console/Panel/Completions").gameObject.activeSelf, Is.False);
                search.text = "缓存";
                yield return null;
                host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "后台缓存刷新"));
                yield return null;
                CollectionAssert.AreEqual(new[] { "中文查找：缓存命中", "后台缓存刷新" },
                    output.content.GetComponentsInChildren<UnityEngine.UI.Text>().Select(text => text.text).ToArray());
                search.text = "没有这条日志";
                yield return null;
                Assert.That(output.content.GetComponentsInChildren<UnityEngine.UI.Text>(), Is.Empty);
                var empty = view.GetComponentsInChildren<UnityEngine.UI.Text>(true).Single(text => text.name == "EmptyOutput");
                Assert.That(empty.gameObject.activeSelf, Is.True);
                Assert.That(empty.text, Is.EqualTo("没有匹配日志"));
                Button(view, "清除搜索").onClick.Invoke();
                yield return null;
                Assert.That(output.content.GetComponentsInChildren<UnityEngine.UI.Text>().Length, Is.EqualTo(4));
                search.text = "cache";
                Button(view, "搜索日志").onClick.Invoke();
                yield return null;
                yield return null;
                Assert.That(output.content.GetComponentsInChildren<UnityEngine.UI.Text>().Length, Is.EqualTo(4));
                Assert.That(view.InputField.text, Is.EqualTo("search-test"));
                Assert.That(view.InputField.isFocused, Is.True);
                Assert.That(host.Session.GetOutput().Length, Is.EqualTo(4), "Filtering must preserve retained output.");
                Assert.That(view.GetFilteredOutputText(), Does.Contain("中文查找：缓存命中"));
                Assert.That(view.GetFilteredOutputText(), Does.Contain("connect error"));
                view.Close();
            }
            finally
            {
                module?.Dispose();
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator NativeView_OutputToolsKeepLongMessagesAndReadingPosition()
        {
            var root = new GameObject("Native output controls");
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                string longMessage = new string('长', 1100) + "完整结尾";
                host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Warning, longMessage));
                for (int i = 0; i < 80; i++) host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "日志 " + i));
                view.Open();
                yield return null;
                yield return null;
                var output = view.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
                var lines = output.content.GetComponentsInChildren<UnityEngine.UI.Text>();
                Assert.That(lines.Length, Is.EqualTo(81), "Earlier retained output must remain scrollable.");
                Assert.That(lines[0].text, Is.EqualTo(longMessage));
                Button(view, "字号增大").onClick.Invoke();
                Assert.That(view.InputField.textComponent.fontSize, Is.EqualTo(18));
                Button(view, "字号减小").onClick.Invoke();
                Assert.That(view.InputField.textComponent.fontSize, Is.EqualTo(16));
                Button(view, "跟随").onClick.Invoke();
                yield return null;
                output.verticalNormalizedPosition = .6f;
                float readingPosition = output.content.anchoredPosition.y;
                host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "阅读旧日志时的新消息"));
                yield return null;
                yield return null;
                Assert.That(output.content.anchoredPosition.y, Is.EqualTo(readingPosition).Within(1));
                Button(view, "跟随").onClick.Invoke();
                Assert.That(output.verticalNormalizedPosition, Is.EqualTo(0).Within(.01));
                Button(view, "清空").onClick.Invoke();
                yield return null;
                Assert.That(host.Session.GetOutput(), Is.Empty);
                Assert.That(output.content.GetComponentsInChildren<UnityEngine.UI.Text>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator DynamicCompletion_CanChooseBeyondFirstPageWithoutExecuting()
        {
            var root = new GameObject("Paged native completion");
            IDisposable module = null;
            int calls = 0;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var view = root.AddComponent<NativeConsole>();
                module = host.Registry.Register("pages", new[] {
                    new ConsoleBinding(new ConsoleCommand("pages.query", "pages-query", "pages", "Select a resource.",
                        new[] { new ConsoleParameter("id", typeof(string)) }),
                        (context, args, token) => { calls++; return Task.FromResult(ConsoleResult.Success(args.Get<string>(0))); },
                        (context, token) => Task.FromResult<System.Collections.Generic.IReadOnlyList<ConsoleCompletionItem>>(
                            Enumerable.Range(0, 17).Select(index => new ConsoleCompletionItem("resource." + index, "资源 " + index.ToString("00"))).ToArray())) });
                view.Open();
                yield return null;
                view.InputField.text = "pages-query ";
                view.InputField.caretPosition = view.InputField.text.Length;
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                while (!view.GetComponentsInChildren<UnityEngine.UI.Button>().Any(button => button.name == "Completion") && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                for (int i = 0; i < 9; i++) Button(view, "下一项").onClick.Invoke();
                var choice = view.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button => button.name == "Completion" &&
                    button.GetComponentInChildren<UnityEngine.UI.Text>().text == "资源 09");
                choice.onClick.Invoke();
                Assert.That(view.InputField.text, Is.EqualTo("pages-query \"资源 09\""));
                Assert.That(calls, Is.Zero);
                Button(view, "运行").onClick.Invoke();
                while (host.Session.IsRunning) yield return null;
                yield return null;
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(host.Session.GetOutput().Any(entry => entry.Message == "资源 09"), Is.True);
                Assert.That(view.GetComponentsInChildren<UnityEngine.UI.Button>().Any(button => button.name == "Completion"), Is.False);
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        private static UnityEngine.UI.Button Button(NativeConsole view, string name) =>
            view.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(button => button.gameObject.name == name);

        private static IEnumerator Wait(Task task)
        {
            double end = Time.realtimeSinceStartupAsDouble + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            if (task.IsFaulted) throw task.Exception;
        }

        private sealed class ActionLease : IDisposable
        {
            private Action action;
            public ActionLease(Action action) => this.action = action;
            public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
        }
    }
}
