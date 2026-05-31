using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ZeroEngine.Timing
{
    internal static class TimeScaleMath
    {
        public static float Clamp(float scale)
        {
            return Mathf.Clamp(scale, 0f, 10f);
        }
    }

    public sealed class TimeControlService : ITimeControlService
    {
        private readonly Dictionary<TimeDomainId, DomainState> _domains = new();
        private readonly Dictionary<RequestKey, Request> _requestsByKey = new();
        private readonly List<Request> _expiredRequests = new();
        private int _anonymousTokenId;

        public event Action<TimeDomainId, float> DomainScaleChanged;

        public float GetScale(TimeDomainId domain)
        {
            return _domains.TryGetValue(domain, out DomainState state) ? state.CurrentScale : 1f;
        }

        public bool IsFrozen(TimeDomainId domain)
        {
            if (!_domains.TryGetValue(domain, out DomainState state)) return false;

            foreach (Request request in state.Requests)
            {
                if (request.IsActive && request.Scale <= 0f) return true;
            }

            return false;
        }

        public void SetBaseScale(TimeDomainId domain, float scale, float recoveryDuration = 0f)
        {
            DomainState state = GetOrCreateState(domain);
            state.BaseScale = TimeScaleMath.Clamp(scale);
            Recompute(state, recoveryDuration);
        }

        public ITimeScaleHandle SetScaleModifier(object token, TimeDomainId domain, float scale, float durationSeconds = 0f, float recoveryDuration = 0f, string reason = null)
        {
            return AddRequest(token, domain, TimeScaleMath.Clamp(scale), durationSeconds, recoveryDuration, reason);
        }

        public ITimeScaleHandle Freeze(object token, TimeDomainId domain, float durationSeconds = 0f, float recoveryDuration = 0f, string reason = null)
        {
            return AddRequest(token, domain, 0f, durationSeconds, recoveryDuration, reason);
        }

        public void Release(object token, TimeDomainId domain, float recoveryDuration = 0f)
        {
            if (token == null) return;

            var key = new RequestKey(domain, token);
            if (!_requestsByKey.TryGetValue(key, out Request request)) return;

            ReleaseRequest(request, recoveryDuration);
        }

        public void ResetDomain(TimeDomainId domain)
        {
            if (!_domains.TryGetValue(domain, out DomainState state)) return;

            foreach (Request request in state.Requests)
            {
                request.MarkInactive();
                _requestsByKey.Remove(request.Key);
            }

            state.Requests.Clear();
            state.BaseScale = 1f;
            state.CancelRecovery();
            SetCurrentScale(state, 1f);
        }

        public void ResetAll()
        {
            foreach (DomainState state in _domains.Values)
            {
                foreach (Request request in state.Requests)
                {
                    request.MarkInactive();
                }

                state.Requests.Clear();
                state.BaseScale = 1f;
                state.CancelRecovery();
                SetCurrentScale(state, 1f);
            }

            _requestsByKey.Clear();
        }

        public void Tick(float unscaledDeltaTime)
        {
            float delta = Mathf.Max(0f, unscaledDeltaTime);
            _expiredRequests.Clear();

            foreach (DomainState state in _domains.Values)
            {
                foreach (Request request in state.Requests)
                {
                    if (!request.IsActive || request.DurationSeconds <= 0f) continue;

                    request.ElapsedSeconds += delta;
                    if (request.ElapsedSeconds >= request.DurationSeconds)
                    {
                        _expiredRequests.Add(request);
                    }
                }
            }

            foreach (Request request in _expiredRequests)
            {
                ReleaseRequest(request, request.RecoveryDuration);
            }

            foreach (DomainState state in _domains.Values)
            {
                TickRecovery(state, delta);
            }
        }

        private ITimeScaleHandle AddRequest(object token, TimeDomainId domain, float scale, float durationSeconds, float recoveryDuration, string reason)
        {
            if (!domain.IsValid) return InactiveTimeScaleHandle.Instance;

            object requestToken = token ?? new AnonymousToken(++_anonymousTokenId);
            var key = new RequestKey(domain, requestToken);
            DomainState state = GetOrCreateState(domain);

            if (_requestsByKey.TryGetValue(key, out Request existing))
            {
                RemoveRequest(existing);
            }

            var request = new Request(this, key, scale, Mathf.Max(0f, durationSeconds), Mathf.Max(0f, recoveryDuration), reason);
            _requestsByKey[key] = request;
            state.Requests.Add(request);
            Recompute(state, recoveryDuration);
            return request;
        }

        private void ReleaseRequest(Request request, float recoveryDuration)
        {
            if (request == null || !request.IsActive) return;

            DomainState state = _domains[request.Domain];
            RemoveRequest(request);
            Recompute(state, recoveryDuration);
        }

        private void RemoveRequest(Request request)
        {
            request.MarkInactive();
            _requestsByKey.Remove(request.Key);

            if (_domains.TryGetValue(request.Domain, out DomainState state))
            {
                state.Requests.Remove(request);
            }
        }

        private DomainState GetOrCreateState(TimeDomainId domain)
        {
            if (!_domains.TryGetValue(domain, out DomainState state))
            {
                state = new DomainState(domain);
                _domains[domain] = state;
            }

            return state;
        }

        private void Recompute(DomainState state, float recoveryDuration)
        {
            float target = GetTargetScale(state);
            recoveryDuration = Mathf.Max(0f, recoveryDuration);

            if (recoveryDuration > 0f && target > state.CurrentScale)
            {
                state.StartRecovery(target, recoveryDuration);
                return;
            }

            state.CancelRecovery();
            SetCurrentScale(state, target);
        }

        private static float GetTargetScale(DomainState state)
        {
            float target = state.BaseScale;
            foreach (Request request in state.Requests)
            {
                if (request.IsActive)
                {
                    target = Mathf.Min(target, request.Scale);
                }
            }

            return TimeScaleMath.Clamp(target);
        }

        private void TickRecovery(DomainState state, float delta)
        {
            if (!state.IsRecovering) return;

            if (state.RecoveryDuration <= 0f)
            {
                state.CancelRecovery();
                SetCurrentScale(state, state.RecoveryTarget);
                return;
            }

            state.RecoveryElapsed += delta;
            float t = Mathf.Clamp01(state.RecoveryElapsed / state.RecoveryDuration);
            SetCurrentScale(state, Mathf.Lerp(state.RecoveryStart, state.RecoveryTarget, t));

            if (t >= 1f)
            {
                state.CancelRecovery();
            }
        }

        private void SetCurrentScale(DomainState state, float scale)
        {
            scale = TimeScaleMath.Clamp(scale);
            if (Mathf.Approximately(state.CurrentScale, scale)) return;

            state.CurrentScale = scale;
            DomainScaleChanged?.Invoke(state.Domain, scale);
        }

        private sealed class DomainState
        {
            public readonly TimeDomainId Domain;
            public readonly List<Request> Requests = new();
            public float BaseScale = 1f;
            public float CurrentScale = 1f;
            public bool IsRecovering;
            public float RecoveryStart;
            public float RecoveryTarget;
            public float RecoveryDuration;
            public float RecoveryElapsed;

            public DomainState(TimeDomainId domain)
            {
                Domain = domain;
            }

            public void StartRecovery(float target, float duration)
            {
                IsRecovering = true;
                RecoveryStart = CurrentScale;
                RecoveryTarget = target;
                RecoveryDuration = duration;
                RecoveryElapsed = 0f;
            }

            public void CancelRecovery()
            {
                IsRecovering = false;
                RecoveryDuration = 0f;
                RecoveryElapsed = 0f;
            }
        }

        private readonly struct RequestKey : IEquatable<RequestKey>
        {
            public readonly TimeDomainId Domain;
            public readonly object Token;

            public RequestKey(TimeDomainId domain, object token)
            {
                Domain = domain;
                Token = token;
            }

            public bool Equals(RequestKey other)
            {
                return Domain == other.Domain && ReferenceEquals(Token, other.Token);
            }

            public override bool Equals(object obj)
            {
                return obj is RequestKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Domain.GetHashCode() * 397) ^ RuntimeHelpers.GetHashCode(Token);
                }
            }
        }

        private sealed class Request : ITimeScaleHandle
        {
            private readonly TimeControlService _owner;

            public readonly RequestKey Key;
            public readonly float Scale;
            public readonly float DurationSeconds;
            public readonly float RecoveryDuration;
            public float ElapsedSeconds;

            public Request(TimeControlService owner, RequestKey key, float scale, float durationSeconds, float recoveryDuration, string reason)
            {
                _owner = owner;
                Key = key;
                Scale = scale;
                DurationSeconds = durationSeconds;
                RecoveryDuration = recoveryDuration;
                Reason = reason;
                IsActive = true;
            }

            public bool IsActive { get; private set; }
            public TimeDomainId Domain => Key.Domain;
            public object Token => Key.Token;
            public string Reason { get; }

            public void Release(float recoveryDuration = 0f)
            {
                _owner.ReleaseRequest(this, recoveryDuration);
            }

            public void Dispose()
            {
                Release();
            }

            public void MarkInactive()
            {
                IsActive = false;
            }
        }

        private sealed class AnonymousToken
        {
            private readonly int _id;

            public AnonymousToken(int id)
            {
                _id = id;
            }

            public override string ToString()
            {
                return $"AnonymousTimeScaleToken({_id})";
            }
        }

        private sealed class InactiveTimeScaleHandle : ITimeScaleHandle
        {
            public static readonly InactiveTimeScaleHandle Instance = new();

            public bool IsActive => false;
            public TimeDomainId Domain => default;
            public object Token => null;
            public string Reason => null;

            public void Release(float recoveryDuration = 0f)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
