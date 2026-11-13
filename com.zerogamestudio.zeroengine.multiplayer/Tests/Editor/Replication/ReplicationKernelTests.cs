using System;
using NUnit.Framework;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Multiplayer.Tests.Replication
{
    public sealed class ReplicationKernelTests
    {
        [TestCase(1u, 0u, true)]
        [TestCase(1u, 1u, false)]
        [TestCase(0u, uint.MaxValue, true)]
        [TestCase(uint.MaxValue, 0u, false)]
        [TestCase(0x80000000u, 0u, false)]
        public void SequenceOrder_RejectsDuplicatesAndHalfRange(uint next, uint prior, bool expected)
            => Assert.That(MessageSequenceGate.IsNewer(next, prior), Is.EqualTo(expected));

        [Test] public void SequenceAdmission_ResetsAndWraps()
        {
            var gate = new MessageSequenceGate();
            Assert.That(gate.TryAccept(uint.MaxValue), Is.True);
            Assert.That(gate.TryAccept(0), Is.True);
            Assert.That(gate.TryAccept(uint.MaxValue), Is.False);
            gate.Reset(); Assert.That(gate.TryAccept(12), Is.True);
        }
        [Test] public void RevisionGate_RejectsZeroAndStale()
        {
            var gate = new AuthorityRevisionGate();
            Assert.That(gate.TryAccept(0), Is.False);
            Assert.That(gate.TryAccept(uint.MaxValue), Is.True);
            Assert.That(gate.TryAccept(1), Is.True);
            Assert.That(gate.TryAccept(1), Is.False);
        }
        [Test] public void Identity_PreservesAll64BitsAndGeneration()
        {
            var a = new NetworkEntityKey(new NetEntityId(1ul << 48), 3);
            var b = new NetworkEntityKey(new NetEntityId((1ul << 48) + 1), 3);
            Assert.That(a.EntityId.Value, Is.EqualTo(1ul << 48));
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(new NetworkEntityKey(a.EntityId, 4)));
        }
        [Test] public void Allocation_ExhaustsWithoutReusingAnIdentity()
        {
            var allocator = new NetworkEntityIdAllocator(ulong.MaxValue);
            Assert.That(allocator.Allocate().Value, Is.EqualTo(ulong.MaxValue));
            Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
        }
        [Test] public void Lifecycle_EndBeforeSpawnCannotResurrectGeneration()
        {
            var ledger = new NetworkEntityLifecycleLedger();
            var key = new NetworkEntityKey(new NetEntityId(7), 4);
            Assert.That(ledger.TryAcceptTerminal(key, out _), Is.True);
            Assert.That(ledger.TryAcceptSpawn(key, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(NetworkEntityLifecycleRejectReason.TerminalAlreadyAccepted));
            Assert.That(ledger.TryAcceptSpawn(new NetworkEntityKey(key.EntityId, 5), out _), Is.True);
            Assert.That(ledger.TryAcceptTerminal(key, out _), Is.False);
        }
        [Test] public void Lifecycle_DifferentIdentityRequiresANewerGeneration()
        {
            var ledger = new NetworkEntityLifecycleLedger();
            Assert.That(ledger.TryAcceptSpawn(new NetworkEntityKey(new NetEntityId(5), uint.MaxValue), out _), Is.True);
            Assert.That(ledger.TryAcceptSpawn(new NetworkEntityKey(new NetEntityId(6), uint.MaxValue), out _), Is.False);
            Assert.That(ledger.TryAcceptSpawn(new NetworkEntityKey(new NetEntityId(6), 1), out _), Is.True);
        }
        private static ReplicatedLeaseUpdate Update(uint id, uint revision, ReplicatedLeaseOperation operation, uint epoch = 3)
            => new ReplicatedLeaseUpdate(epoch, id, revision, operation);
        private static ReplicatedLeaseLedger Ledger(int active = 2, int tombstones = 2)
        { var ledger = new ReplicatedLeaseLedger(active, tombstones); ledger.Reset(3); return ledger; }
        [Test] public void LeaseLedger_RejectsZeroOrWrongEpoch()
        {
            var ledger = Ledger();
            Assert.That(ledger.Accept(Update(1, 1, ReplicatedLeaseOperation.Begin, 4)), Is.False);
            Assert.That(ledger.Accept(Update(0, 1, ReplicatedLeaseOperation.Begin)), Is.False);
            Assert.That(ledger.Accept(Update(1, 0, ReplicatedLeaseOperation.Begin)), Is.False);
            Assert.That(ledger.Accept(Update(1, 1, (ReplicatedLeaseOperation)9)), Is.False);
            Assert.That(ledger.ActiveCount, Is.Zero);
        }
        [Test] public void LeaseLedger_BaselineCanRestoreTheCurrentRevision()
        {
            var ledger = Ledger(); var begin = Update(1, 1, ReplicatedLeaseOperation.Begin);
            Assert.That(ledger.Accept(begin), Is.True);
            Assert.That(ledger.Accept(begin), Is.False);
            Assert.That(ledger.AcceptBaseline(begin), Is.True);
            Assert.That(ledger.Accept(Update(1, 2, ReplicatedLeaseOperation.Change)), Is.True);
            Assert.That(ledger.AcceptBaseline(begin), Is.False);
        }
        [Test] public void LeaseLedger_EndBeforeBeginAndLateBaselineStayEnded()
        {
            var ledger = Ledger();
            Assert.That(ledger.Accept(Update(1, 2, ReplicatedLeaseOperation.End)), Is.True);
            Assert.That(ledger.Accept(Update(1, 1, ReplicatedLeaseOperation.Begin)), Is.False);
            Assert.That(ledger.AcceptBaseline(Update(1, 3, ReplicatedLeaseOperation.Begin)), Is.False);
            Assert.That(ledger.ActiveCount, Is.Zero);
        }
        [Test] public void LeaseLedger_ActiveBudgetDoesNotBlockRetirement()
        {
            var ledger = Ledger(1);
            Assert.That(ledger.Accept(Update(1, 1, ReplicatedLeaseOperation.Begin)), Is.True);
            Assert.That(ledger.Accept(Update(2, 1, ReplicatedLeaseOperation.Emit)), Is.False);
            Assert.That(ledger.Accept(Update(1, 2, ReplicatedLeaseOperation.End)), Is.True);
            Assert.That(ledger.Accept(Update(2, 1, ReplicatedLeaseOperation.Begin)), Is.True);
        }
        [Test] public void LeaseLedger_BoundedTombstonesKeepARetirementFloor()
        {
            var ledger = Ledger();
            for (uint id = 1; id <= 5; id++) Assert.That(ledger.Accept(Update(id, 1, ReplicatedLeaseOperation.End)), Is.True);
            Assert.That(ledger.TombstoneCount, Is.EqualTo(2));
            Assert.That(ledger.Accept(Update(1, 2, ReplicatedLeaseOperation.Begin)), Is.False);
            Assert.That(ledger.Accept(Update(6, 1, ReplicatedLeaseOperation.Begin)), Is.True);
        }
        [Test] public void LeaseLedger_NewEpochReleasesOldTombstones()
        {
            var ledger = Ledger(); ledger.Accept(Update(1, 1, ReplicatedLeaseOperation.End));
            ledger.Reset(4);
            Assert.That(ledger.Accept(Update(1, 1, ReplicatedLeaseOperation.Begin, 4)), Is.True);
            Assert.That(ledger.Accept(Update(2, 1, ReplicatedLeaseOperation.Begin, 3)), Is.False);
        }
    }
}
