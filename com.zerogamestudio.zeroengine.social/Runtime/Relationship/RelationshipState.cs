using System;
using System.Collections.Generic;
using System.Globalization;
using ZeroEngine.Inventory;

namespace ZeroEngine.Relationship
{
    /// <summary>One owner's relationship state. Clock, content and inventory are explicit consumer inputs.</summary>
    public sealed class RelationshipState : IDisposable
    {
        private const string DateFormat = "yyyy-MM-dd";
        private Func<string, RelationshipDataSO> _resolve;
        private Func<DateTime> _gameDate;
        private Frame _frame;
        private Guid _epoch = Guid.NewGuid();
        private bool _disposed;
        public string OwnerId { get; }
        public long Revision => Current.Revision;
        public string CurrentDate => Format(Current.Date);

        public RelationshipState(string ownerId, Func<string, RelationshipDataSO> resolve, Func<DateTime> gameDate)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("A stable relationship owner is required.", nameof(ownerId));
            OwnerId = ownerId;
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            _gameDate = gameDate ?? throw new ArgumentNullException(nameof(gameDate));
            _frame = new Frame(_gameDate().Date, 0, new Dictionary<string, RelationshipProgress>(StringComparer.Ordinal), null);
        }

        public RelationshipCommandContext CaptureContext(string requestId)
        {
            var frame = Current;
            if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("A request identity is required.", nameof(requestId));
            return new RelationshipCommandContext(_epoch, OwnerId, frame.Revision, Format(frame.Date), requestId);
        }

        public RelationshipProgress GetProgress(string npcId)
        {
            var frame = Current;
            if (!TryDefinition(npcId, out var definition)) return null;
            return Clone(frame.Progress.TryGetValue(npcId, out var row) ? row : Initial(definition, frame.Date));
        }
        public int GetPoints(string npcId) => GetProgress(npcId)?.Points ?? 0;
        public RelationshipLevel GetLevel(string npcId) => GetProgress(npcId)?.Level ?? RelationshipLevel.Stranger;

        /// <summary>Called from the consumer's calendar change, not from a per-frame wall-clock poll.</summary>
        public bool AdvanceGameDate()
        {
            var frame = Current;
            var date = _gameDate().Date;
            if (date < frame.Date || frame.Revision == long.MaxValue) return false;
            if (date == frame.Date) return true;
            var rows = new Dictionary<string, RelationshipProgress>(StringComparer.Ordinal);
            var days = (date - frame.Date).Days;
            foreach (var pair in frame.Progress)
            {
                if (!TryDefinition(pair.Key, out var definition)) return false;
                var row = Clone(pair.Value);
                row.GiftCountToday = 0; row.TalkCountToday = 0;
                row.Points = (int)Math.Max(0L, (long)row.Points - (long)definition.DailyDecay * days);
                row.Level = definition.GetLevelForPoints(row.Points);
                rows.Add(pair.Key, row);
            }
            _frame = new Frame(date, frame.Revision + 1, rows, frame.LastReceipt);
            return true;
        }

        public bool TryTalk(RelationshipCommandContext context, string npcId,
            out RelationshipActionReceipt receipt, out string reason)
        {
            receipt = null;
            if (!ValidateContext(context, out reason)) return false;
            if (TryReplay(context, npcId, RelationshipActionKind.Talk, null, null, 0, out receipt, out reason)) return true;
            if (reason.Length != 0) return false;
            if (!TryDefinition(npcId, out var definition)) { reason = "relationship.unknown-npc"; return false; }
            var row = ReadOrInitial(definition);
            if (row.TalkCountToday >= definition.MaxTalksPerDay) { reason = "relationship.talk-limit"; return false; }
            row = Clone(row); row.TalkCountToday++;
            var change = Prepare(context, definition, row, RelationshipActionKind.Talk, definition.TalkPoints, null, null, GiftPreference.Neutral);
            return CommitDirect(change, out receipt, out reason);
        }

        public bool TryGiveGift(RelationshipCommandContext context, string npcId, InventoryItemSO item,
            InventoryManager inventory, out RelationshipActionReceipt receipt, out string reason)
        {
            receipt = null;
            if (!ValidateContext(context, out reason)) return false;
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) { reason = "relationship.invalid-gift"; return false; }
            if (TryReplay(context, npcId, RelationshipActionKind.Gift, item.Id, null, 0, out receipt, out reason)) return true;
            if (reason.Length != 0) return false;
            if (inventory == null) { reason = "relationship.missing-inventory"; return false; }
            if (!TryDefinition(npcId, out var definition)) { reason = "relationship.unknown-npc"; return false; }
            var row = ReadOrInitial(definition);
            if (row.GiftCountToday >= definition.MaxGiftsPerDay) { reason = "relationship.gift-limit"; return false; }
            row = Clone(row); row.GiftCountToday++;
            var delta = definition.CalculateGiftPoints(item, out var preference);
            var change = Prepare(context, definition, row, RelationshipActionKind.Gift, delta, item.Id, null, preference);
            // The receipt and relationship root commit before inventory saving or observers can run.
            if (!inventory.TryExchangeItems(new[] { new InventoryItemAmount(item, 1) }, null, change, false))
            { reason = "relationship.cost-or-context"; return false; }
            receipt = change.Receipt.Clone(); reason = string.Empty; return true;
        }

        public bool TryChangePoints(RelationshipCommandContext context, string npcId, int amount,
            out RelationshipActionReceipt receipt, out string reason)
        {
            receipt = null;
            if (!ValidateContext(context, out reason)) return false;
            if (TryReplay(context, npcId, RelationshipActionKind.Points, null, null, amount, out receipt, out reason)) return true;
            if (reason.Length != 0) return false;
            if (!TryDefinition(npcId, out var definition)) { reason = "relationship.unknown-npc"; return false; }
            var change = Prepare(context, definition, Clone(ReadOrInitial(definition)), RelationshipActionKind.Points, amount, null, null, GiftPreference.Neutral);
            return CommitDirect(change, out receipt, out reason);
        }

        public bool TryTriggerEvent(RelationshipCommandContext context, string npcId, string eventId,
            out RelationshipActionReceipt receipt, out string reason)
        {
            receipt = null;
            if (!ValidateContext(context, out reason)) return false;
            if (TryReplay(context, npcId, RelationshipActionKind.Event, null, eventId, 0, out receipt, out reason)) return true;
            if (reason.Length != 0) return false;
            if (!TryDefinition(npcId, out var definition)) { reason = "relationship.unknown-npc"; return false; }
            var evt = definition.Events.Find(value => value.EventId == eventId);
            var row = ReadOrInitial(definition);
            if (evt == null || row.Points < evt.RequiredPoints || (int)row.Level < (int)evt.RequiredLevel
                || (evt.OneTime && row.TriggeredEvents.Contains(eventId))) { reason = "relationship.event-unavailable"; return false; }
            row = Clone(row);
            if (evt.OneTime) row.TriggeredEvents.Add(eventId);
            var change = Prepare(context, definition, row, RelationshipActionKind.Event, 0, null, eventId, GiftPreference.Neutral);
            return CommitDirect(change, out receipt, out reason);
        }

        public RelationshipStateSnapshot GetSnapshot()
        {
            var frame = Current;
            return new RelationshipStateSnapshot { SchemaVersion = 1, OwnerId = OwnerId, Date = Format(frame.Date),
                Revision = frame.Revision, ProgressData = CloneRows(frame.Progress), LastReceipt = frame.LastReceipt?.Clone() };
        }
        public bool CanRestore(RelationshipStateSnapshot snapshot, out string reason) => TryBuild(snapshot, out _, out reason);
        public bool TryRestore(RelationshipStateSnapshot snapshot, out string reason)
        {
            if (!TryBuild(snapshot, out var frame, out reason)) return false;
            _frame = frame; _epoch = Guid.NewGuid(); return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _frame = null; _resolve = null; _gameDate = null; _epoch = Guid.Empty;
        }

        private bool ValidateContext(RelationshipCommandContext context, out string reason)
        {
            var frame = Current;
            reason = "relationship.stale-context";
            if (context.Epoch != _epoch || !string.Equals(context.OwnerId, OwnerId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(context.RequestId) || context.Date != Format(frame.Date)
                || _gameDate().Date != frame.Date || frame.Revision == long.MaxValue) return false;
            // A repeated last commit may carry its pre-commit revision; TryReplay checks its exact payload.
            if (context.Revision != frame.Revision && (frame.LastReceipt == null
                || frame.LastReceipt.RequestId != context.RequestId || frame.LastReceipt.FromRevision != context.Revision)) return false;
            reason = string.Empty; return true;
        }
        private bool TryReplay(RelationshipCommandContext context, string npcId, RelationshipActionKind kind,
            string itemId, string eventId, int requestedPoints, out RelationshipActionReceipt receipt, out string reason)
        {
            receipt = null; reason = string.Empty;
            var last = Current.LastReceipt;
            if (last == null || last.RequestId != context.RequestId) return false;
            if (last.FromRevision != context.Revision || last.NpcId != npcId || last.Kind != kind
                || last.ItemId != itemId || last.EventId != eventId
                || (kind == RelationshipActionKind.Points && last.RequestedPoints != requestedPoints))
            { reason = "relationship.request-id-reused"; return false; }
            receipt = last.Clone(); receipt.Replayed = true; return true;
        }
        private PendingChange Prepare(RelationshipCommandContext context, RelationshipDataSO definition, RelationshipProgress row,
            RelationshipActionKind kind, int delta, string itemId, string eventId, GiftPreference preference)
        {
            var before = Current;
            var oldPoints = row.Points;
            row.Points = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)row.Points + delta));
            row.Level = definition.GetLevelForPoints(row.Points); row.LastInteractionDate = Format(before.Date);
            var receipt = new RelationshipActionReceipt { RequestId = context.RequestId, OwnerId = OwnerId, NpcId = definition.NpcId,
                Kind = kind, ItemId = itemId, EventId = eventId, RequestedPoints = delta, PreviousPoints = oldPoints,
                CurrentPoints = row.Points, Preference = preference, FromRevision = before.Revision, Date = Format(before.Date) };
            var rows = new Dictionary<string, RelationshipProgress>(before.Progress, StringComparer.Ordinal) { [definition.NpcId] = row };
            return new PendingChange(this, context, before, new Frame(before.Date, before.Revision + 1, rows, receipt), receipt);
        }
        private static bool CommitDirect(PendingChange change, out RelationshipActionReceipt receipt, out string reason)
        {
            receipt = null; reason = "relationship.stale-context";
            if (!change.TryPrepare()) return false;
            change.Commit(); receipt = change.Receipt.Clone(); reason = string.Empty; return true;
        }
        private RelationshipProgress ReadOrInitial(RelationshipDataSO data) =>
            Current.Progress.TryGetValue(data.NpcId, out var row) ? row : Initial(data, Current.Date);
        private static RelationshipProgress Initial(RelationshipDataSO data, DateTime date) =>
            new RelationshipProgress { NpcId = data.NpcId, Points = data.InitialPoints,
                Level = data.GetLevelForPoints(data.InitialPoints), LastInteractionDate = Format(date) };

        private bool TryBuild(RelationshipStateSnapshot snapshot, out Frame frame, out string reason)
        {
            _ = Current; frame = null; reason = "relationship.invalid-snapshot";
            if (snapshot == null || snapshot.SchemaVersion != 1 || snapshot.OwnerId != OwnerId
                || !TryDate(snapshot.Date, out var date) || snapshot.Revision < 0 || snapshot.Revision >= long.MaxValue
                || snapshot.ProgressData == null) return false;
            var rows = new Dictionary<string, RelationshipProgress>(StringComparer.Ordinal);
            foreach (var pair in snapshot.ProgressData)
            {
                var row = pair.Value;
                if (!TryDefinition(pair.Key, out var data) || row == null || row.NpcId != pair.Key || row.Points < 0
                    || row.GiftCountToday < 0 || row.TalkCountToday < 0 || row.Level != data.GetLevelForPoints(row.Points)
                    || !TryDate(row.LastInteractionDate, out var interacted) || interacted > date
                    || row.TriggeredEvents == null || row.CustomData == null) return false;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in row.TriggeredEvents)
                    if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || !data.Events.Exists(evt => evt.EventId == id)) return false;
                foreach (var value in row.CustomData) if (string.IsNullOrWhiteSpace(value.Key) || value.Value == null) return false;
                rows.Add(pair.Key, Clone(row));
            }
            var receipt = snapshot.LastReceipt;
            if (receipt != null && (receipt.Replayed || receipt.OwnerId != OwnerId || string.IsNullOrWhiteSpace(receipt.RequestId)
                || string.IsNullOrWhiteSpace(receipt.NpcId) || !rows.ContainsKey(receipt.NpcId) || !Enum.IsDefined(typeof(RelationshipActionKind), receipt.Kind)
                || receipt.FromRevision < 0 || receipt.FromRevision >= snapshot.Revision
                || !TryDate(receipt.Date, out var receiptDate) || receiptDate > date || receipt.PreviousPoints < 0 || receipt.CurrentPoints < 0)) return false;
            frame = new Frame(date, snapshot.Revision + 1, rows, receipt?.Clone()); reason = string.Empty; return true;
        }
        private bool TryDefinition(string id, out RelationshipDataSO data)
        {
            data = string.IsNullOrWhiteSpace(id) ? null : _resolve(id);
            if (data == null || data.NpcId != id || data.InitialPoints < 0 || data.DailyDecay < 0
                || data.MaxGiftsPerDay < 0 || data.MaxTalksPerDay < 0 || data.Thresholds == null
                || data.LikedGifts == null || data.DislikedGifts == null || data.Events == null) return false;
            int lastLevel = -1, lastPoints = -1;
            foreach (var threshold in data.Thresholds)
            {
                if (threshold == null || !Enum.IsDefined(typeof(RelationshipLevel), threshold.Level)
                    || (int)threshold.Level <= lastLevel || threshold.RequiredPoints < lastPoints || threshold.RequiredPoints < 0) return false;
                lastLevel = (int)threshold.Level; lastPoints = threshold.RequiredPoints;
            }
            var gifts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in new[] { data.LikedGifts, data.DislikedGifts })
                foreach (var gift in list)
                    if (gift == null || gift.Item == null || string.IsNullOrWhiteSpace(gift.Item.Id)
                        || !gifts.Add(gift.Item.Id) || !Enum.IsDefined(typeof(GiftPreference), gift.Preference)) return false;
            var events = new HashSet<string>(StringComparer.Ordinal);
            foreach (var evt in data.Events)
                if (evt == null || string.IsNullOrWhiteSpace(evt.EventId) || !events.Add(evt.EventId)
                    || !Enum.IsDefined(typeof(RelationshipLevel), evt.RequiredLevel) || evt.RequiredPoints < 0) return false;
            return true;
        }
        private static RelationshipProgress Clone(RelationshipProgress row) => new RelationshipProgress
        {
            NpcId = row.NpcId, Points = row.Points, Level = row.Level, GiftCountToday = row.GiftCountToday,
            TalkCountToday = row.TalkCountToday, LastInteractionDate = row.LastInteractionDate,
            TriggeredEvents = new List<string>(row.TriggeredEvents), CustomData = new Dictionary<string, string>(row.CustomData, StringComparer.Ordinal)
        };
        private static Dictionary<string, RelationshipProgress> CloneRows(Dictionary<string, RelationshipProgress> source)
        {
            var result = new Dictionary<string, RelationshipProgress>(StringComparer.Ordinal);
            foreach (var pair in source) result.Add(pair.Key, Clone(pair.Value));
            return result;
        }
        private static string Format(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
        private static bool TryDate(string text, out DateTime date) =>
            DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        private Frame Current => !_disposed ? _frame : throw new ObjectDisposedException(nameof(RelationshipState));

        private sealed class Frame
        {
            public readonly DateTime Date;
            public readonly long Revision;
            public readonly Dictionary<string, RelationshipProgress> Progress;
            public readonly RelationshipActionReceipt LastReceipt;
            public Frame(DateTime date, long revision, Dictionary<string, RelationshipProgress> progress, RelationshipActionReceipt receipt)
            { Date = date; Revision = revision; Progress = progress; LastReceipt = receipt; }
        }
        private sealed class PendingChange : IInventoryExchangeParticipant
        {
            private readonly RelationshipState _owner;
            private readonly RelationshipCommandContext _context;
            private readonly Frame _before, _after;
            public RelationshipActionReceipt Receipt { get; }
            public PendingChange(RelationshipState owner, RelationshipCommandContext context, Frame before, Frame after, RelationshipActionReceipt receipt)
            { _owner = owner; _context = context; _before = before; _after = after; Receipt = receipt; }
            public bool TryPrepare() => !_owner._disposed && ReferenceEquals(_owner._frame, _before)
                && _owner._epoch == _context.Epoch && _owner._gameDate().Date == _before.Date;
            public void Commit()
            {
                if (!TryPrepare()) throw new InvalidOperationException("Relationship preparation is stale.");
                _owner._frame = _after;
            }
            public void Rollback()
            {
                if (ReferenceEquals(_owner._frame, _after)) _owner._frame = _before;
                else if (!ReferenceEquals(_owner._frame, _before)) throw new InvalidOperationException("A later relationship state must not be overwritten.");
            }
        }
    }
}
