using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ZeroEngine.Tutorial.Guidance
{
    internal sealed class TutorialFocusInputScope : IDisposable
    {
        private readonly struct State
        {
            internal readonly bool Before, During;
            internal State(bool before, bool during) { Before = before; During = during; }
        }
        private readonly Dictionary<InputAction, State> states = new Dictionary<InputAction, State>();
        private readonly InputSystemUIInputModule module;
        private readonly TutorialFocusContext context;

        internal TutorialFocusInputScope(InputSystemUIInputModule module, TutorialFocusRequest request, TutorialFocusContext context)
        {
            this.module = module;
            this.context = context;
            var allowed = new HashSet<Guid>();
            var maps = new HashSet<InputActionMap>();
            var actions = new HashSet<InputAction>();
            foreach (var reference in new[] { module.point, module.leftClick, module.move, module.submit, module.cancel })
            {
                var action = reference?.action;
                if (action == null) continue;
                allowed.Add(action.id); actions.Add(action);
                if (action.actionMap != null) maps.Add(action.actionMap);
            }
            foreach (var reference in request.AllowedActions ?? Array.Empty<InputActionReference>())
                if (reference?.action != null) allowed.Add(reference.action.id);
            if (context.InputMaps != null)
                foreach (var map in context.InputMaps()) if (map != null) maps.Add(map);
            foreach (var map in maps) foreach (var action in map.actions) actions.Add(action);
            try
            {
                foreach (var action in actions)
                {
                    bool enable = allowed.Contains(action.id);
                    states.Add(action, new State(action.enabled, enable));
                    if (enable) action.Enable(); else action.Disable();
                }
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            foreach (var item in states)
            {
                var action = item.Key;
                if (!module || !EventSystem.current || EventSystem.current.currentInputModule != module) continue;
                if (action.actionMap != null && context.CanRestoreInputMap != null && !context.CanRestoreInputMap(action.actionMap)) continue;
                if (action.enabled != item.Value.During) continue; // A newer owner changed this action.
                if (item.Value.Before) action.Enable(); else action.Disable();
            }
            states.Clear();
        }
    }
}
