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
