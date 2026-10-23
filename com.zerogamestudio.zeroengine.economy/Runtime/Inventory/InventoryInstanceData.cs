using System;
using UnityEngine;

namespace ZeroEngine.Inventory
{
    /// <summary>Opaque, stable instance state. The owning domain interprets the payload.</summary>
    [Serializable]
    public sealed class InventoryInstanceData
    {
        [SerializeField] private string _instanceId;
        [SerializeField] private string _payloadType;
        [SerializeField] private int _payloadVersion;
        [SerializeField] private string _payloadJson;

        // Serializers construct the record before populating its serialized fields.
        public InventoryInstanceData() { }

        public InventoryInstanceData(string instanceId, string payloadType, int payloadVersion, string payloadJson)
        {
            _instanceId = instanceId;
            _payloadType = payloadType;
            _payloadVersion = payloadVersion;
            _payloadJson = payloadJson;
        }

        public string InstanceId => _instanceId;
        public string PayloadType => _payloadType;
        public int PayloadVersion => _payloadVersion;
        public string PayloadJson => _payloadJson;
        public bool IsValid => !string.IsNullOrWhiteSpace(_instanceId)
            && !string.IsNullOrWhiteSpace(_payloadType) && _payloadVersion > 0
            && !string.IsNullOrWhiteSpace(_payloadJson);

        public InventoryInstanceData Clone()
        {
            return new InventoryInstanceData(_instanceId, _payloadType, _payloadVersion, _payloadJson);
        }

        internal static bool SameState(InventoryInstanceData first, InventoryInstanceData second)
        {
            if (ReferenceEquals(first, second)) return true;
            return first != null && second != null
                && string.Equals(first.InstanceId, second.InstanceId, StringComparison.Ordinal)
                && string.Equals(first.PayloadType, second.PayloadType, StringComparison.Ordinal)
                && first.PayloadVersion == second.PayloadVersion
                && string.Equals(first.PayloadJson, second.PayloadJson, StringComparison.Ordinal);
        }
    }
}
