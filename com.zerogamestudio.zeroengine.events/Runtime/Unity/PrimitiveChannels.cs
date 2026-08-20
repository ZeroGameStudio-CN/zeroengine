using UnityEngine;

namespace ZeroEngine.Events.Unity
{
    public readonly struct VoidEventValue
    {
        public static readonly VoidEventValue Instance = new VoidEventValue();
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/Void Event Channel", fileName = "VoidEventChannel")]
    public sealed class VoidEventChannel : EventChannel<VoidEventValue>
    {
        public void Raise() => Raise(VoidEventValue.Instance);
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/Bool Event Channel", fileName = "BoolEventChannel")]
    public sealed class BoolEventChannel : EventChannel<bool>
    {
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/Int Event Channel", fileName = "IntEventChannel")]
    public sealed class IntEventChannel : EventChannel<int>
    {
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/Float Event Channel", fileName = "FloatEventChannel")]
    public sealed class FloatEventChannel : EventChannel<float>
    {
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/String Event Channel", fileName = "StringEventChannel")]
    public sealed class StringEventChannel : EventChannel<string>
    {
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/Vector2 Event Channel", fileName = "Vector2EventChannel")]
    public sealed class Vector2EventChannel : EventChannel<Vector2>
    {
    }

    [CreateAssetMenu(menuName = "ZeroEngine/Events/Vector3 Event Channel", fileName = "Vector3EventChannel")]
    public sealed class Vector3EventChannel : EventChannel<Vector3>
    {
    }
}
