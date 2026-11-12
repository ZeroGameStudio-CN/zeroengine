using System;
using UnityEngine;
using ZeroEngine.Save;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// Explicit bridge for projects using ZE's slot store. Bind before the slot is loaded.
    /// Core achievements and hosts with their own save bridge do not need this component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AchievementManager))]
    public sealed class AchievementSaveSlotAdapter : MonoBehaviour, ISaveable
    {
        private AchievementManager _manager;
        private SaveSlotManager _store;

        public string SaveKey => Manager.SaveKey;
        private AchievementManager Manager => _manager ? _manager : (_manager = GetComponent<AchievementManager>());

        public void Bind(SaveSlotManager store)
        {
            if (!store) throw new ArgumentNullException(nameof(store));
            if (_store == store) return;
            Unbind();
            _store = store;
            _store.Register(this);
        }

        public void Unbind()
        {
            if (_store) _store.Unregister(this);
            _store = null;
        }

        private void OnDestroy() => Unbind();

        public object ExportSaveData() => Manager.ExportSaveData();
        public void ImportSaveData(object data) => Manager.ImportSaveData(data);
        public void ResetToDefault() => Manager.ResetToDefault();
    }
}
