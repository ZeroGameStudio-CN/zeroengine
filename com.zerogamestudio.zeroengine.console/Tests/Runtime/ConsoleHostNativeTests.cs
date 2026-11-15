using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Console.Unity;
using Object = UnityEngine.Object;

namespace ZeroEngine.Console.Tests.Runtime
{
    public sealed class ConsoleHostNativeTests
    {
        [TestCase(typeof(Vector2), "(-1.5, 2)")]
        [TestCase(typeof(Vector3), "(-1.5, 2, 3)")]
        [TestCase(typeof(Vector4), "[-1.5, 2, 3, 4]")]
        [TestCase(typeof(Vector2Int), "(-1, 2)")]
        [TestCase(typeof(Vector3Int), "(-1, 2, 3)")]
        [TestCase(typeof(Quaternion), "(0, 0, 0, 1)")]
        [TestCase(typeof(Color), "red")]
        [TestCase(typeof(Color), "#ff0000")]
        [TestCase(typeof(Color), "0xFF0000FF")]
        [TestCase(typeof(Color), "(1,0,0)")]
        [TestCase(typeof(Color32), "#ff0000ff")]
        public void UnityValues_ParseInvariantComponentsAndColors(Type type, string text)
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                var values = new ConsoleValues(UnityConsoleValues.TryConvert);
                Assert.That(values.TryConvert(new ConsoleToken(text, text, 0), type, out var parsed, out var error), Is.True, error);
                object expected = type == typeof(Vector2) ? (object)new Vector2(-1.5f, 2) :
                    type == typeof(Vector3) ? new Vector3(-1.5f, 2, 3) :
                    type == typeof(Vector4) ? new Vector4(-1.5f, 2, 3, 4) :
                    type == typeof(Vector2Int) ? new Vector2Int(-1, 2) :
                    type == typeof(Vector3Int) ? new Vector3Int(-1, 2, 3) :
                    type == typeof(Quaternion) ? Quaternion.identity :
                    type == typeof(Color32) ? (object)new Color32(255, 0, 0, 255) : Color.red;
                Assert.That(parsed, Is.TypeOf(type));
                Assert.That(parsed, Is.EqualTo(expected));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        }

        [TestCase(typeof(Vector2), "(1,2,3)")]
        [TestCase(typeof(Vector3), "(1,2)")]
        [TestCase(typeof(Vector4), "(1,2,3,4,5)")]
        [TestCase(typeof(Vector3), "(NaN,2,3)")]
        [TestCase(typeof(Vector3), "(Infinity,2,3)")]
        [TestCase(typeof(Vector2Int), "(1.5,2)")]
        [TestCase(typeof(Vector3Int), "(2147483648,2,3)")]
        [TestCase(typeof(Quaternion), "(0,0,1)")]
        [TestCase(typeof(Color), "(1.1,0,0)")]
        [TestCase(typeof(Color), "(-1,0,0)")]
        [TestCase(typeof(Color), "not-a-color")]
        public void UnityValues_RejectInvalidComponents(Type type, string text)
        {
            var values = new ConsoleValues(UnityConsoleValues.TryConvert);
            Assert.That(values.TryConvert(new ConsoleToken(text, text, 0), type, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [UnityTest]
        public IEnumerator Host_ParsesUnityArgumentsByDefaultAndRetainsTypedCalls()
        {
            var root = new GameObject("Console Unity value owner");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var command = new ConsoleCommand("unity.values", "unity-values", "unity", "Read structured values.", new[] {
                    new ConsoleParameter("points", typeof(Vector3[])), new ConsoleParameter("color", typeof(Color?)) });
                module = host.Registry.Register("unity", new[] { new ConsoleBinding(command, (context, args, token) =>
                    Task.FromResult(ConsoleResult.Success(data: new object[] { args.Get<Vector3[]>(0), args.Get<Color?>(1) }))) });
                var result = host.ExecuteAsync("unity-values [(1,2,3),(4,5,6)] null");
                yield return Wait(result);
                Assert.That(result.Result.IsSuccess, Is.True, result.Result.Message);
                var data = (object[])result.Result.Data;
                Assert.That(data[0], Is.EqualTo(new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) }));
                Assert.That(data[1], Is.Null);
                result = host.ExecuteAsync(command.Id, new object[] { new[] { Vector3.one }, Color.red });
                yield return Wait(result);
                Assert.That(result.Result.IsSuccess, Is.True, result.Result.Message);
                Assert.That(((object[])result.Result.Data)[1], Is.EqualTo(Color.red));
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator CancelledBeforeDispatch_ReturnsStructuredResultWithoutInvokingTheHandler()
        {
            var root = new GameObject("Console cancelled request owner");
            IDisposable module = null;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                int calls = 0;
                var command = new ConsoleCommand("cancel.write", "cancel-write", "cancel", "Must not run.");
                module = host.Registry.Register("cancel", new[] { new ConsoleBinding(command, (context, args, token) =>
                { calls++; return Task.FromResult(ConsoleResult.Success()); }) });
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    var text = host.ExecuteAsync(command.Name, cancellation.Token);
                    var typed = host.ExecuteAsync(command.Id, Array.Empty<object>(), cancellation.Token);
                    yield return Wait(text);
                    yield return Wait(typed);
                    Assert.That(text.GetAwaiter().GetResult().Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
                    Assert.That(typed.GetAwaiter().GetResult().Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
                    var workerText = Task.Run(() => host.ExecuteAsync(command.Name, cancellation.Token));
                    var workerTyped = Task.Run(() => host.ExecuteAsync(command.Id, Array.Empty<object>(), cancellation.Token));
                    yield return Wait(workerText);
                    yield return Wait(workerTyped);
                    Assert.That(workerText.GetAwaiter().GetResult().Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
                    Assert.That(workerTyped.GetAwaiter().GetResult().Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
                }
                Assert.That(calls, Is.Zero);
                Assert.That(host.Session.GetHistory(), Is.Empty);
            }
            finally { module?.Dispose(); Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator WorkerInvocation_ExecutesActualUnityWriteOnMainThread()
        {
            GameObject root = null;
            IDisposable module = null;
            try
            {
                int mainThread = Thread.CurrentThread.ManagedThreadId;
                root = new GameObject("Console dispatcher owner");
                var host = root.AddComponent<ConsoleHost>();
                var command = new ConsoleCommand("test.write", "test-write", "test", "Create an owned child.",
                    new[] { new ConsoleParameter("name", typeof(string)) });
                module = host.Registry.Register("test", new[] { new ConsoleBinding(command, (context, args, token) =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                    var child = new GameObject(args.Get<string>(0));
                    child.transform.SetParent(host.transform);
                    return Task.FromResult(ConsoleResult.Success(data: child.name));
                }) });
                Task<ConsoleResult> result = Task.Run(() => host.ExecuteAsync("test-write \"工作线程请求\""));
                yield return Wait(result);
                Assert.That(result.GetAwaiter().GetResult().IsSuccess, Is.True);
                Assert.That(root.transform.childCount, Is.EqualTo(1));
                Assert.That(root.transform.GetChild(0).name, Is.EqualTo("工作线程请求"));
                result = Task.Run(() => host.ExecuteAsync(command.Id, new object[] { "Typed worker request" }));
                yield return Wait(result);
                Assert.That(result.GetAwaiter().GetResult().IsSuccess, Is.True);
                Assert.That(root.transform.childCount, Is.EqualTo(2));
                Assert.That(root.transform.GetChild(1).name, Is.EqualTo("Typed worker request"));
            }
            finally { module?.Dispose(); if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator UnityLogCapture_IsOptInAndEndsWithTheHost()
        {
            GameObject root = null;
            try
            {
                root = new GameObject("Console log owner");
                var host = root.AddComponent<ConsoleHost>();
                Debug.Log("Console uncaptured log");
                Assert.That(host.Session.GetOutput(), Is.Empty);
                host.CaptureUnityLogs = true;
                Debug.Log("Console captured log");
                Assert.That(host.Session.GetOutput(), Has.Length.EqualTo(1));
                Assert.That(host.Session.GetOutput()[0].Message, Is.EqualTo("Console captured log"));
                host.CaptureUnityLogs = false;
                Debug.Log("Console capture disabled");
                Assert.That(host.Session.GetOutput(), Has.Length.EqualTo(1));
                host.CaptureUnityLogs = true;
                var ended = host.Session;
                root.SetActive(false);
                Debug.Log("Console owner ended");
                Assert.That(ended.GetOutput(), Is.Empty);
                root.SetActive(true);
                Debug.Log("Console fresh owner");
                Assert.That(host.Session.GetOutput(), Has.Length.EqualTo(1));
                yield return null;
            }
            finally { if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator Disable_CancelsModuleAndReenableCreatesOneFreshCatalog()
        {
            GameObject root = null;
            IDisposable module = null;
            try
            {
                root = new GameObject("Console lifecycle owner");
                root.SetActive(false);
                var host = root.AddComponent<ConsoleHost>();
                var entered = new TaskCompletionSource<bool>();
                host.Started += owner => module = owner.Registry.Register("test", new[] {
                    new ConsoleBinding(new ConsoleCommand("test.wait", "test-wait", "test", "Wait until cancelled."),
                        async (context, args, token) => { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return ConsoleResult.Success(); }) });
                host.Stopping += _ => module?.Dispose();
                root.SetActive(true);
                Task<ConsoleResult> pending = host.ExecuteAsync("test-wait");
                yield return Wait(entered.Task);
                root.SetActive(false);
                yield return Wait(pending);
                Assert.That(pending.GetAwaiter().GetResult().Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
                Assert.That(host.Session, Is.Null);
                root.SetActive(true);
                Assert.That(host.Session.GetCommands(host.CreateCaller()), Has.Length.EqualTo(4));
                root.SetActive(false);
                root.SetActive(true);
                Assert.That(host.Session.GetCommands(host.CreateCaller()), Has.Length.EqualTo(4));
            }
            finally { module?.Dispose(); if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator SeparateHosts_KeepModuleStateAndOutputIndependent()
        {
            GameObject firstRoot = null, secondRoot = null;
            IDisposable module = null;
            try
            {
                firstRoot = new GameObject("First console host");
                secondRoot = new GameObject("Second console host");
                var first = firstRoot.AddComponent<ConsoleHost>();
                var second = secondRoot.AddComponent<ConsoleHost>();
                module = first.Registry.Register("test", new[] {
                    new ConsoleBinding(new ConsoleCommand("test.only", "test-only", "test", "First host only."),
                        (context, args, token) => Task.FromResult(ConsoleResult.Success("first"))) });
                var result = first.ExecuteAsync("test-only");
                yield return Wait(result);
                Assert.That(result.GetAwaiter().GetResult().IsSuccess, Is.True);
                Assert.That(first.Session.GetOutput(), Has.Length.EqualTo(1));
                Assert.That(second.Session.GetOutput(), Is.Empty);
                Assert.That(second.Session.GetCommands(second.CreateCaller()), Has.Length.EqualTo(3));
            }
            finally
            {
                module?.Dispose();
                if (firstRoot) Object.DestroyImmediate(firstRoot);
                if (secondRoot) Object.DestroyImmediate(secondRoot);
            }
        }

        private static IEnumerator Wait(Task task)
        {
            double end = Time.realtimeSinceStartupAsDouble + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "The owned native test exceeded its bounded task wait.");
            if (task.IsFaulted) throw task.Exception;
        }
    }
}
