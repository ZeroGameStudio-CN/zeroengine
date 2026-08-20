using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.StatSystem
{
    [Serializable]
    public struct StatBlockEntry
    {
        public StatId Id;
        public float Value;

        public StatBlockEntry(StatId id, float value)
        {
            Id = id;
            Value = value;
        }
    }

    [Serializable]
    public class StatBlock
    {
        [SerializeField] private List<StatBlockEntry> _values = new();

        public IReadOnlyList<StatBlockEntry> Values => _values;

        public float Get(StatId id, float defaultValue = 0f)
        {
            return TryGet(id, out var value) ? value : defaultValue;
        }

        public bool TryGet(StatId id, out float value)
        {
            id = new StatId(id.Value);
            if (id.IsEmpty)
            {
                value = 0f;
                return false;
            }

            var index = IndexOf(id);
            if (index < 0)
            {
                value = 0f;
                return false;
            }

            value = _values[index].Value;
            return true;
        }

        public void Set(StatId id, float value)
        {
            id = new StatId(id.Value);
            if (id.IsEmpty)
            {
                return;
            }

            var index = IndexOf(id);
            if (index >= 0)
            {
                _values[index] = new StatBlockEntry(id, value);
                RemoveDuplicateEntries(id, index);
                return;
            }

            _values.Add(new StatBlockEntry(id, value));
        }

        public void Add(StatId id, float value)
        {
            Set(id, Get(id) + value);
        }

        public StatBlock Scaled(float scale)
        {
            var result = new StatBlock();
            for (var i = 0; i < _values.Count; i++)
            {
                var entry = _values[i];
                result.Set(entry.Id, entry.Value * scale);
            }

            return result;
        }

        public StatBlock Merged(StatBlock other)
        {
            var result = Clone();
            if (other == null)
            {
                return result;
            }

            foreach (var entry in other.Values)
            {
                result.Add(entry.Id, entry.Value);
            }

            return result;
        }

        public StatBlock Clone()
        {
            var result = new StatBlock();
            for (var i = 0; i < _values.Count; i++)
            {
                var entry = _values[i];
                result.Set(entry.Id, entry.Value);
            }

            return result;
        }

        private int IndexOf(StatId id)
        {
            for (var i = 0; i < _values.Count; i++)
            {
                if (_values[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveDuplicateEntries(StatId id, int keepIndex)
        {
            for (var i = _values.Count - 1; i >= 0; i--)
            {
                if (i != keepIndex && _values[i].Id == id)
                {
                    _values.RemoveAt(i);
                    if (i < keepIndex)
                    {
                        keepIndex--;
                    }
                }
            }
        }
    }
}
