using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace ZeroEngine.Interaction.Samples.Tests
{
    public sealed class InteractionPlaygroundTests
    {
        private GameObject root;
        private InteractionPlayground demo;
        [UnitySetUp] public IEnumerator SetUp()
        { root = new GameObject("Playground test"); demo = root.AddComponent<InteractionPlayground>(); yield return null; }
        [UnityTearDown] public IEnumerator TearDown() { Object.Destroy(root); yield return null; }

        private void ClickInteraction()
        {
            var data = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(demo.InteractButton.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(demo.InteractButton.gameObject, data, ExecuteEvents.pointerUpHandler);
        }

        [UnityTest] public IEnumerator ButtonsOperateDoorTalkPickupAndDelayedShopWithoutGameAssets()
        {
            demo.Approach("door"); ClickInteraction(); Assert.That(demo.DoorOpen, Is.True);
            demo.Approach("npc"); ClickInteraction(); Assert.That(demo.Conversations, Is.EqualTo(1));
            demo.Approach("pickup"); ClickInteraction(); ClickInteraction(); Assert.That(demo.Pickups, Is.EqualTo(1));
            demo.RespawnPickup(); ClickInteraction(); Assert.That(demo.Pickups, Is.EqualTo(2));
            demo.Approach("shop"); ClickInteraction(); Assert.That(demo.Coins, Is.EqualTo(5));
            Assert.That(demo.ConfirmPurchase(), Is.True); Assert.That(demo.Coins, Is.EqualTo(3));
            Assert.That(demo.ConfirmPurchase(), Is.False); Assert.That(demo.Purchases, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest] public IEnumerator LeavingTheShopRejectsItsOldConfirmationAndKeepsTheOtherWalletSeparate()
        {
            demo.Approach("shop"); ClickInteraction(); var stale = demo.PendingConfirmation;
            demo.LeaveRange(); Assert.That(stale.TryCommit(), Is.False); Assert.That(demo.Coins, Is.EqualTo(5));
            demo.SelectActor(1); Assert.That(demo.Coins, Is.EqualTo(3));
            demo.Approach("shop"); ClickInteraction(); Assert.That(demo.ConfirmPurchase(), Is.True); Assert.That(demo.Coins, Is.EqualTo(1));
            demo.SelectActor(0); Assert.That(demo.Coins, Is.EqualTo(5));
            yield return null;
        }

        [UnityTest] public IEnumerator HoldingTheNpcUsesTheSameUiInputPathAndStopsOnRelease()
        {
            demo.Approach("npc"); var data = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(demo.InteractButton.gameObject, data, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSeconds(.85f);
            ExecuteEvents.Execute(demo.InteractButton.gameObject, data, ExecuteEvents.pointerUpHandler);
            Assert.That(demo.Gifts, Is.EqualTo(1)); Assert.That(demo.Coins, Is.EqualTo(4));
            yield return new WaitForSeconds(.5f); Assert.That(demo.Gifts, Is.EqualTo(1)); Assert.That(demo.Conversations, Is.Zero);
        }
    }
}
