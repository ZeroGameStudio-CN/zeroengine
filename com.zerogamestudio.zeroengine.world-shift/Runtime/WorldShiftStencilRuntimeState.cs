using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.WorldShift
{
    public static class WorldShiftStencilRuntimeState
    {
        public readonly struct State
        {
            public State(
                MonoBehaviour owner,
                WorldShiftTransitionMode mode,
                WorldShiftLayer currentLayer,
                WorldShiftLayer targetLayer)
            {
                Owner = owner;
                Mode = mode;
                CurrentLayer = currentLayer;
                TargetLayer = targetLayer;
            }

            public MonoBehaviour Owner { get; }
            public WorldShiftTransitionMode Mode { get; }
            public WorldShiftLayer CurrentLayer { get; }
            public WorldShiftLayer TargetLayer { get; }
        }

        private static readonly HashSet<MonoBehaviour> RegisteredControllers = new();
        private static State activeState;

        public static bool HasRegisteredController
        {
            get
            {
                RemoveInactiveControllers();
                return RegisteredControllers.Count > 0;
            }
        }

        public static bool TryGetActiveState(out State state)
        {
            RemoveInactiveControllers();
            if (activeState.Owner != null &&
                activeState.Owner.isActiveAndEnabled &&
                RegisteredControllers.Contains(activeState.Owner))
            {
                state = activeState;
                return true;
            }

            state = default;
            return false;
        }

        public static void Register(MonoBehaviour controller)
        {
            if (controller != null)
            {
                RegisteredControllers.Add(controller);
            }
        }

        public static void RegisterRadialScan(MonoBehaviour controller, WorldShiftLayer currentLayer, WorldShiftLayer targetLayer)
        {
            RegisterScan(controller, WorldShiftTransitionMode.RadialScan, currentLayer, targetLayer);
        }

        public static void RegisterScan(
            MonoBehaviour controller,
            WorldShiftTransitionMode mode,
            WorldShiftLayer currentLayer,
            WorldShiftLayer targetLayer)
        {
            if (controller == null)
            {
                return;
            }

            RegisteredControllers.Add(controller);
            activeState = new State(
                controller,
                mode,
                currentLayer,
                targetLayer);
        }

        public static void ClearActiveState(MonoBehaviour controller)
        {
            if (controller != null && activeState.Owner == controller)
            {
                activeState = default;
            }
        }

        public static void Unregister(MonoBehaviour controller)
        {
            if (controller != null)
            {
                RegisteredControllers.Remove(controller);
                ClearActiveState(controller);
            }
        }

        public static void ClearForTests()
        {
            RegisteredControllers.Clear();
            activeState = default;
        }

        private static void RemoveInactiveControllers()
        {
            RegisteredControllers.RemoveWhere(controller => controller == null || !controller.isActiveAndEnabled);
            if (activeState.Owner == null ||
                !activeState.Owner.isActiveAndEnabled ||
                !RegisteredControllers.Contains(activeState.Owner))
            {
                activeState = default;
            }
        }
    }
}
