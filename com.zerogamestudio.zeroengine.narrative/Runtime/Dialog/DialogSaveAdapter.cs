using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Dialog
{
    /// <summary>
    /// Dialog 系统存档适配器
    /// 将 DialogVariables 的全局变量与 SaveSlotManager 连接
    /// </summary>
    public class DialogSaveAdapter : Singleton<DialogSaveAdapter>, ISaveable
    {
        private void Start()
        {
            SaveSlotManager.Instance?.Register(this);
        }

        protected override void OnDestroy()
        {
            SaveSlotManager.Instance?.Unregister(this);
            base.OnDestroy();
        }

        #region ISaveable Implementation

        /// <summary>
        /// ISaveable: 存档键名
        /// </summary>
        public string SaveKey => "Dialog";

        /// <summary>
        /// ISaveable: 导出存档数据
        /// </summary>
        public object ExportSaveData()
        {
            return DialogVariables.ExportGlobal();
        }

        /// <summary>
        /// ISaveable: 导入存档数据
        /// </summary>
        public void ImportSaveData(object data)
        {
            if (data is Dictionary<string, object> dict)
            {
                DialogVariables.ImportGlobal(dict);
            }
            else
            {
                DialogVariables.ClearGlobal();
            }
        }

        /// <summary>
        /// ISaveable: 重置为初始状态
        /// </summary>
        public void ResetToDefault()
        {
            DialogVariables.ClearGlobal();
        }

        #endregion
    }
}
