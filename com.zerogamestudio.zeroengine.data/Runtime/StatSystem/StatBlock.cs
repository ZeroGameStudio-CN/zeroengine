using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.StatSystem
{
    [Serializable]
    public struct StatValueEntry
    {
        public StatId Id;
        public float Value;
    }

    [Serializable]
    public sealed class StatBlock
    {
        [SerializeField] private List<StatValueEntry> _values = new();

        public IReadOnlyList<StatValueEntry> Values => _values;
        public int Count => _values.Count;

        public float Get(StatId id, float fallback = 0f)
        {
            var index = IndexOf(id);
            return index >= 0 ? _values[index].Value : fallback;
        }

        public bool TryGet(StatId id, out float value)
        {
            var index = IndexOf(id);
            if (index >= 0)
            {
                value = _values[index].Value;
                return true;
            }

            value = 0f;
            return false;
        }

        public void Set(StatId id, float value)
        {
            if (id.IsEmpty)
            {
                return;
            }

            var index = IndexOf(id);
            if (index >= 0)
            {
                var entry = _values[index];
                entry.Value = value;
                _values[index] = entry;
                return;
            }

            _values.Add(new StatValueEntry { Id = id, Value = value });
        }

        public void Add(StatId id, float delta)
        {
            Set(id, Get(id) + delta);
        }

        public bool Remove(StatId id)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                return false;
            }

            _values.RemoveAt(index);
            return true;
        }

        public void Clear()
        {
            _values.Clear();
        }

        public StatBlock Clone()
        {
            var block = new StatBlock();
            for (var i = 0; i < _values.Count; i++)
            {
                block.Set(_values[i].Id, _values[i].Value);
            }

            return block;
        }

        public StatBlock Scaled(float multiplier)
        {
            var block = new StatBlock();
            for (var i = 0; i < _values.Count; i++)
            {
                block.Set(_values[i].Id, _values[i].Value * multiplier);
            }

            return block;
        }

        public StatBlock Merged(StatBlock other)
        {
            var block = Clone();
            if (other == null)
            {
                return block;
            }

            foreach (var entry in other.Values)
            {
                block.Add(entry.Id, entry.Value);
            }

            return block;
        }

        public Dictionary<StatId, float> ToDictionary()
        {
            var result = new Dictionary<StatId, float>();
            for (var i = 0; i < _values.Count; i++)
            {
                if (!_values[i].Id.IsEmpty)
                {
                    result[_values[i].Id] = _values[i].Value;
                }
            }

            return result;
        }

        public static StatBlock FromDictionary(Dictionary<StatId, float> values)
        {
            var block = new StatBlock();
            if (values == null)
            {
                return block;
            }

            foreach (var kvp in values)
            {
                block.Set(kvp.Key, kvp.Value);
            }

            return block;
        }

        private int IndexOf(StatId id)
        {
            for (var i = 0; i < _values.Count; i++)
            {
                if (_values[i].Id.Equals(id))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
