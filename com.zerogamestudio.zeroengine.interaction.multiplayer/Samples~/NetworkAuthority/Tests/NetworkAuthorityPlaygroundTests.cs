using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Interaction.Multiplayer.Samples.Tests
{
    public sealed class NetworkAuthorityPlaygroundTests
    {
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<NetworkAuthorityPlayground> demos = new List<NetworkAuthorityPlayground>();
        private bool background;
        [SetUp] public void SetUp() { background = Application.runInBackground; }
        [UnityTearDown] public IEnumerator TearDown()
        {
            foreach (var root in roots) root.SetActive(false);
            double end = Time.realtimeSinceStartupAsDouble + 4;
            while (!demos.TrueForAll(p => p.WorkersEnded) && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Assert.That(demos.TrueForAll(p => p.WorkersEnded), Is.True, "Sample transport workers must stop.");
            foreach (var root in roots) Object.Destroy(root);
            roots.Clear(); demos.Clear(); yield return null;
            Assert.That(Application.runInBackground, Is.EqualTo(background));
        }
        private NetworkAuthorityPlayground Create(AuthorityPlaygroundMode mode, int port = 0)
        {
            var root = new GameObject("isolated authority sample"); root.SetActive(false); roots.Add(root);
            var demo = root.AddComponent<NetworkAuthorityPlayground>(); demos.Add(demo); demo.Configure(mode, port); root.SetActive(true); return demo;
        }
        private static IEnumerator Wait(Func<bool> ready)
        {
            double end = Time.realtimeSinceStartupAsDouble + 5;
            while (!ready() && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Assert.That(ready(), Is.True, "Expected authority/sample state was not observed.");
        }
        private static void Edge(NetworkAuthorityPlayground demo, bool pressed)
        {
            var data = new PointerEventData(EventSystem.current);
            if (pressed) ExecuteEvents.Execute(demo.InteractButton.gameObject, data, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(demo.InteractButton.gameObject, data, ExecuteEvents.pointerUpHandler);
        }
        [UnityTest] public IEnumerator OfflineUiRetainsTapHoldAndDeferredPurchase()
        {
            var demo = Create(AuthorityPlaygroundMode.Offline);
            demo.Approach("door"); Edge(demo, true); Edge(demo, false); Assert.That(demo.State.View.DoorOpen, Is.True);
            demo.Approach("npc"); Edge(demo, true); yield return Wait(() => demo.State.Self.Gifts == 1); Edge(demo, false);
            Assert.That(demo.State.Self.Conversations, Is.Zero); Assert.That(demo.State.Self.Coins, Is.EqualTo(4));
            demo.Approach("shop"); Edge(demo, true); Edge(demo, false);
            Assert.That(demo.State.Self.Reserved, Is.EqualTo(2)); demo.Confirm(); demo.Cancel();
            Assert.That(demo.State.Self.Coins, Is.EqualTo(2)); Assert.That(demo.State.Self.Purchases, Is.EqualTo(1));
            Assert.That(demo.State.View.Refunds, Is.Zero);
        }
        [UnityTest] public IEnumerator TcpClientProjectsAuthorityWithoutRunningAnotherGameplayWorld()
        {
            var host = Create(AuthorityPlaygroundMode.Host); var guest = Create(AuthorityPlaygroundMode.Client, host.ListeningPort);
            yield return Wait(() => guest.IsConnected && guest.State.Self != null);
            Assert.That(host.IsAuthority, Is.True); Assert.That(guest.IsAuthority, Is.False);
            guest.Approach("door"); Edge(guest, true); Edge(guest, false);
            yield return Wait(() => host.State.View.DoorOpen && guest.State.View.DoorOpen);
            Assert.That(host.State.Self.Coins, Is.EqualTo(5)); Assert.That(guest.State.Self.Coins, Is.EqualTo(3));
            guest.Approach("shop"); Edge(guest, true); Edge(guest, false);
            yield return Wait(() => guest.State.Self.Reserved == 2);
            guest.Cancel(); guest.Confirm();
            yield return Wait(() => guest.State.Self.Reserved == 0 && guest.State.Self.Coins == 3);
            Assert.That(guest.State.Self.Purchases, Is.Zero);
        }
        [UnityTest] public IEnumerator DisconnectAndPooledTargetReleaseTheAuthorityReservation()
        {
            var host = Create(AuthorityPlaygroundMode.Host); var guest = Create(AuthorityPlaygroundMode.Client, host.ListeningPort);
            yield return Wait(() => guest.IsConnected && guest.State.Self != null);
            guest.Approach("shop"); Edge(guest, true); Edge(guest, false);
            yield return Wait(() => guest.State.Self.Reserved == 2);
            guest.gameObject.SetActive(false);
            yield return Wait(() => host.State.View.Actors.Any(a => a.Name.StartsWith("guest-") && !a.Connected && a.Reserved == 0));
            var actor = host.State.View.Actors.Single(a => a.Name.StartsWith("guest-"));
            Assert.That(actor.Coins, Is.EqualTo(3)); Assert.That(host.State.View.Refunds, Is.EqualTo(1));
        }
    }
}
