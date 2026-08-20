using System;
using System.Collections.Generic;

namespace ZeroEngine.Character.Exploration
{
    public sealed class ExplorationControlCoordinator
    {
        private readonly Dictionary<long, ControlToken> _tokens = new();
        private long _nextTokenId;

        public ExplorationControlCoordinator(ExplorationControlMode baseMode)
        {
            BaseMode = baseMode;
            EffectiveMode = baseMode;
        }

        public event Action<ExplorationControlMode, ExplorationControlMode> EffectiveModeChanged;

        public ExplorationControlMode BaseMode { get; private set; }
        public ExplorationControlMode EffectiveMode { get; private set; }
        public ExplorationMovementAuthority EffectiveAuthority => ToAuthority(EffectiveMode);
        public int ActiveTokenCount => _tokens.Count;

        public ExplorationControlLease Acquire(
            ExplorationControlMode mode,
            string owner,
            string reason,
            int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(owner))
            {
                throw new ArgumentException("A control token owner is required.", nameof(owner));
            }

            var tokenId = ++_nextTokenId;
            _tokens.Add(tokenId, new ControlToken(tokenId, mode, owner, reason, priority));
            RecalculateEffectiveMode();
            return new ExplorationControlLease(this, tokenId, mode, owner, reason, priority);
        }

        public void SetBaseMode(ExplorationControlMode mode)
        {
            if (BaseMode == mode)
            {
                return;
            }

            BaseMode = mode;
            RecalculateEffectiveMode();
        }

        public bool CanAccept(ExplorationMovementAuthority authority)
        {
            return authority != ExplorationMovementAuthority.None
                   && EffectiveAuthority == authority;
        }

        public void CopyActiveTokens(List<ExplorationControlTokenSnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var token in _tokens.Values)
            {
                results.Add(new ExplorationControlTokenSnapshot(
                    token.Id,
                    token.Mode,
                    token.Owner,
                    token.Reason,
                    token.Priority));
            }

            results.Sort(static (left, right) => left.TokenId.CompareTo(right.TokenId));
        }

        internal void Release(long tokenId)
        {
            if (!_tokens.Remove(tokenId))
            {
                return;
            }

            RecalculateEffectiveMode();
        }

        private void RecalculateEffectiveMode()
        {
            var nextMode = BaseMode;
            var winningPriority = int.MinValue;
            var winningTokenId = long.MinValue;
            foreach (var token in _tokens.Values)
            {
                if (token.Priority < winningPriority
                    || (token.Priority == winningPriority && token.Id < winningTokenId))
                {
                    continue;
                }

                winningPriority = token.Priority;
                winningTokenId = token.Id;
                nextMode = token.Mode;
            }

            if (EffectiveMode == nextMode)
            {
                return;
            }

            var previousMode = EffectiveMode;
            EffectiveMode = nextMode;
            EffectiveModeChanged?.Invoke(previousMode, nextMode);
        }

        public static ExplorationMovementAuthority ToAuthority(ExplorationControlMode mode)
        {
            return mode switch
            {
                ExplorationControlMode.Player => ExplorationMovementAuthority.Player,
                ExplorationControlMode.Scripted => ExplorationMovementAuthority.Scripted,
                ExplorationControlMode.Follower => ExplorationMovementAuthority.Follower,
                ExplorationControlMode.Recovering => ExplorationMovementAuthority.Recovery,
                _ => ExplorationMovementAuthority.None
            };
        }

        private readonly struct ControlToken
        {
            public ControlToken(
                long id,
                ExplorationControlMode mode,
                string owner,
                string reason,
                int priority)
            {
                Id = id;
                Mode = mode;
                Owner = owner;
                Reason = reason ?? string.Empty;
                Priority = priority;
            }

            public long Id { get; }
            public ExplorationControlMode Mode { get; }
            public string Owner { get; }
            public string Reason { get; }
            public int Priority { get; }
        }
    }

    public sealed class ExplorationControlLease : IDisposable
    {
        private ExplorationControlCoordinator _coordinator;

        internal ExplorationControlLease(
            ExplorationControlCoordinator coordinator,
            long tokenId,
            ExplorationControlMode mode,
            string owner,
            string reason,
            int priority)
        {
            _coordinator = coordinator;
            TokenId = tokenId;
            Mode = mode;
            Owner = owner;
            Reason = reason ?? string.Empty;
            Priority = priority;
        }

        public long TokenId { get; }
        public ExplorationControlMode Mode { get; }
        public string Owner { get; }
        public string Reason { get; }
        public int Priority { get; }
        public bool IsReleased => _coordinator == null;

        public void Dispose()
        {
            var coordinator = _coordinator;
            if (coordinator == null)
            {
                return;
            }

            _coordinator = null;
            coordinator.Release(TokenId);
        }
    }

    public readonly struct ExplorationControlTokenSnapshot
    {
        public ExplorationControlTokenSnapshot(
            long tokenId,
            ExplorationControlMode mode,
            string owner,
            string reason,
            int priority)
        {
            TokenId = tokenId;
            Mode = mode;
            Owner = owner;
            Reason = reason;
            Priority = priority;
        }

        public long TokenId { get; }
        public ExplorationControlMode Mode { get; }
        public string Owner { get; }
        public string Reason { get; }
        public int Priority { get; }
    }
}
