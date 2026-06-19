using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Party;

namespace ZeroEngine.Character.Editor.Tests
{
    public sealed class PartySlotTests
    {
        [Test]
        public void SetAndClearMemberUpdatesPartySlotIndex()
        {
            var member = new TestPartyMember("hero");
            var slot = new PartySlot(2, PartySlotType.Active);

            Assert.IsTrue(slot.SetMember(member));
            Assert.AreSame(member, slot.Member);
            Assert.AreEqual(2, member.PartySlotIndex);
            Assert.IsTrue(slot.IsOccupied);

            var removed = slot.Clear();

            Assert.AreSame(member, removed);
            Assert.IsTrue(slot.IsEmpty);
            Assert.AreEqual(-1, member.PartySlotIndex);
        }

        [Test]
        public void LockedSlotRejectsSetClearAndSwap()
        {
            var locked = new PartySlot(0, PartySlotType.Active)
            {
                IsLocked = true
            };
            var other = new PartySlot(1, PartySlotType.Reserve);
            var member = new TestPartyMember("hero");

            Assert.IsFalse(locked.SetMember(member));
            Assert.IsNull(locked.Clear());
            Assert.IsFalse(locked.SwapWith(other));
            Assert.IsTrue(locked.IsEmpty);
            Assert.AreEqual(-1, member.PartySlotIndex);
        }

        [Test]
        public void SwapWithUpdatesBothMemberSlotIndexes()
        {
            var first = new PartySlot(0, PartySlotType.Active);
            var second = new PartySlot(1, PartySlotType.Reserve);
            var firstMember = new TestPartyMember("first");
            var secondMember = new TestPartyMember("second");
            first.SetMember(firstMember);
            second.SetMember(secondMember);

            Assert.IsTrue(first.SwapWith(second));

            Assert.AreSame(secondMember, first.Member);
            Assert.AreSame(firstMember, second.Member);
            Assert.AreEqual(0, secondMember.PartySlotIndex);
            Assert.AreEqual(1, firstMember.PartySlotIndex);
        }

        private sealed class TestPartyMember : IPartyMember
        {
            public TestPartyMember(string id)
            {
                MemberId = id;
                DisplayName = id;
                PartySlotIndex = -1;
            }

            public string MemberId { get; }
            public string DisplayName { get; }
            public PartyMemberType MemberType => PartyMemberType.Companion;
            public bool IsAlive => true;
            public bool CanAct => true;
            public bool IsPlayerControlled => true;
            public int PartySlotIndex { get; set; }
            public int Level => 1;
            public Transform Transform => null;
            public void OnJoinParty(int slotIndex) => PartySlotIndex = slotIndex;
            public void OnLeaveParty() => PartySlotIndex = -1;
            public void OnSlotChanged(int oldSlot, int newSlot) => PartySlotIndex = newSlot;
        }
    }
}
