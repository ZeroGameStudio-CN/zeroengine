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
