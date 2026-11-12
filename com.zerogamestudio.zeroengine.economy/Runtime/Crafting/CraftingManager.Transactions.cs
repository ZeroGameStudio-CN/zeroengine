using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Save;

namespace ZeroEngine.Crafting
{
    public partial class CraftingManager
    {
        private InventoryManager _inventory;
        private bool _dependenciesConfigured;
        private bool _saveRegistered;
        private bool _isMutating;
        private bool _stateImported;
        private Action<ISaveable> _registerSave;
        private Action<ISaveable> _unregisterSave;
        private Action<ISaveable> _registeredUnregister;
        private Func<CraftingRecipeSO, bool> _unlockCondition;
        private readonly Dictionary<string, CraftingReceipt> _receipts =
            new Dictionary<string, CraftingReceipt>(StringComparer.Ordinal);

        /// <summary>
        /// Bind the consumer's inventory and save owner before registration or starting work.
        /// Configured consumers never create legacy inventory/save singletons or write the default save file.
        /// </summary>
        public void ConfigureDependencies(InventoryManager inventory,
            Action<ISaveable> registerSave = null, Action<ISaveable> unregisterSave = null,
            Func<CraftingRecipeSO, bool> unlockCondition = null)
        {
            if (_saveRegistered || _activeProgress.Count != 0 || _isMutating)
                throw new InvalidOperationException("Configure crafting dependencies before registration or active work.");
            if ((registerSave == null) != (unregisterSave == null))
                throw new ArgumentException("Save registration and unregistration must have the same owner.");
            _inventory = inventory;
            _registerSave = registerSave;
            _unregisterSave = unregisterSave;
            _unlockCondition = unlockCondition;
            _dependenciesConfigured = true;
        }

        private InventoryManager ResolveInventory() => _dependenciesConfigured ? _inventory : InventoryManager.Instance;

        private void RegisterWithSaveProvider()
        {
            if (_saveRegistered) return;
            if (_dependenciesConfigured)
            {
                _registerSave?.Invoke(this);
                _registeredUnregister = _unregisterSave;
            }
            else
            {
                var owner = SaveSlotManager.Instance;
                if (owner == null) return;
                owner.Register(this);
                _registeredUnregister = saveable => { if (owner != null) owner.Unregister(saveable); };
            }
            _saveRegistered = true;
        }

        private void ReleaseSaveRegistration()
        {
            if (!_saveRegistered) return;
            var unregister = _registeredUnregister;
            _saveRegistered = false;
            _registeredUnregister = null;
            unregister?.Invoke(this);
        }

        /// <summary>Retries of the same request return the recorded result without consuming or awarding twice.</summary>
        public CraftingResult StartCraft(CraftingRecipeSO recipe, string currentWorkbench, int batchCount, string requestId)
        {
            if (_isMutating) return CraftingResult.Busy;
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128 || recipe == null)
                return CraftingResult.InvalidRequest;
            if (_receipts.TryGetValue(requestId, out var existing))
            {
                return existing.RecipeId == recipe.RecipeId && existing.BatchCount == batchCount
                    && existing.WorkbenchId == (currentWorkbench ?? string.Empty)
                    ? existing.Result : CraftingResult.InvalidRequest;
            }
            var canCraft = CanCraft(recipe, currentWorkbench, batchCount);
            if (canCraft != CraftingResult.Success) return canCraft;
            CraftingBatchPlan.TryCreate(recipe, batchCount, out var plan);
            var inventory = ResolveInventory();
            var progress = plan.Prepare(recipe, batchCount, requestId);
            var timed = plan.Duration > 0;
            var receipt = new CraftingReceipt
            {
                RequestId = requestId,
                RecipeId = recipe.RecipeId,
                WorkbenchId = currentWorkbench ?? string.Empty,
                BatchCount = batchCount,
                Pending = timed,
                Result = timed ? CraftingResult.Success : progress.Result
            };
            var produced = timed ? new List<InventoryItemAmount>() : ResolveCompletionItems(progress, inventory);
            if (produced == null) return CraftingResult.InvalidRecipe;
            _isMutating = true;
            try
            {
                if (!inventory.TryExchangeItems(plan.Consumed, produced, !_dependenciesConfigured, () =>
                {
                    _receipts.Add(requestId, receipt);
                    if (timed) _activeProgress.Add(progress);
                    else AwardExperience(progress);
                }))
                    return CraftingResult.InventoryFull;
                PublishCrafting(new CraftingEventArgs
                {
                    EventType = timed ? CraftingEventType.Started : CompletionEvent(progress.Result),
                    Recipe = recipe,
                    Result = receipt.Result,
                    BatchCount = batchCount,
                    ProgressId = requestId,
                    Outputs = timed ? null : ToEventOutputs(progress, inventory)
                });
                Log($"合成{(timed ? "开始" : "结束")}: {recipe.DisplayName} x{batchCount} ({receipt.Result})");
                return receipt.Result;
            }
            finally { _isMutating = false; }
        }

        public bool TryGetReceipt(string requestId, out CraftingReceipt receipt)
        {
            receipt = null;
            if (string.IsNullOrEmpty(requestId) || !_receipts.TryGetValue(requestId, out var stored)) return false;
            receipt = stored.Clone();
            return true;
        }

        /// <summary>Cancellation always refunds escrow; a full inventory leaves the pending craft intact.</summary>
        public bool TryCancelCraft(string progressId, out CraftingResult result)
        {
            result = CraftingResult.InvalidRequest;
            if (_isMutating) { result = CraftingResult.Busy; return false; }
            if (string.IsNullOrEmpty(progressId)) return false;
            var progress = _activeProgress.Find(candidate => candidate.ProgressId == progressId);
            if (progress == null)
            {
                if (_receipts.TryGetValue(progressId, out var previous) && previous.Result == CraftingResult.Cancelled)
                { result = CraftingResult.Cancelled; return true; }
                return false;
            }
            var inventory = ResolveInventory();
            if (inventory == null) { result = CraftingResult.InventoryUnavailable; return false; }
            if (!PrepareLegacyProgress(progress)) { result = CraftingResult.InvalidRecipe; return false; }
            var refund = ResolveItems(progress.ConsumedItems, inventory);
            if (refund == null) { result = CraftingResult.InvalidRecipe; return false; }
            _isMutating = true;
            try
            {
                if (!inventory.TryExchangeItems(null, refund, !_dependenciesConfigured, () =>
                {
                    _activeProgress.Remove(progress);
                    _receipts[progressId].Result = CraftingResult.Cancelled;
                    _receipts[progressId].Pending = false;
                }))
                { result = CraftingResult.InventoryFull; return false; }
                result = CraftingResult.Cancelled;
                Log($"取消合成并退还材料: {progress.RecipeId}");
                PublishCrafting(new CraftingEventArgs
                {
                    EventType = CraftingEventType.Cancelled,
                    Recipe = GetRecipe(progress.RecipeId),
                    ProgressId = progressId,
                    BatchCount = progress.BatchCount,
                    Result = result
                });
                return true;
            }
            finally { _isMutating = false; }
        }

        private void TryComplete(CraftingProgress progress)
        {
            var inventory = ResolveInventory();
            if (inventory == null) { Block(progress, CraftingResult.InventoryUnavailable); return; }
            if (!PrepareLegacyProgress(progress)) { Block(progress, CraftingResult.InvalidRecipe); return; }
            var produced = ResolveCompletionItems(progress, inventory);
            if (produced == null) { Block(progress, CraftingResult.InvalidRecipe); return; }
            if (!CanAwardExperience(progress.SkillId, progress.ExperienceReward))
            { Block(progress, CraftingResult.InvalidRequest); return; }
            _isMutating = true;
            try
            {
                if (!inventory.TryExchangeItems(null, produced, !_dependenciesConfigured, () =>
                {
                    _activeProgress.Remove(progress);
                    _receipts[progress.ProgressId].Result = progress.Result;
                    _receipts[progress.ProgressId].Pending = false;
                    AwardExperience(progress);
                }))
                { Block(progress, CraftingResult.InventoryFull); return; }
                PublishCrafting(new CraftingEventArgs
                {
                    EventType = CompletionEvent(progress.Result),
                    Recipe = GetRecipe(progress.RecipeId),
                    Result = progress.Result,
                    BatchCount = progress.BatchCount,
                    ProgressId = progress.ProgressId,
                    Outputs = ToEventOutputs(progress, inventory)
                });
                Log($"合成结束: {progress.RecipeId} x{progress.BatchCount} ({progress.Result})");
            }
            finally { _isMutating = false; }
        }

        private bool PrepareLegacyProgress(CraftingProgress progress)
        {
            if (progress.OutcomePrepared) return true;
            var recipe = GetRecipe(progress.RecipeId);
            if (!CraftingBatchPlan.TryCreate(recipe, progress.BatchCount, out var plan)) return false;
            var prepared = plan.Prepare(recipe, progress.BatchCount, progress.ProgressId);
            progress.ConsumedItems = prepared.ConsumedItems;
            progress.OutputItems = prepared.OutputItems;
            progress.Result = prepared.Result;
            progress.SkillId = prepared.SkillId;
            progress.ExperienceReward = prepared.ExperienceReward;
            progress.OutcomePrepared = true;
            return true;
        }

        private static List<InventoryItemAmount> ResolveCompletionItems(CraftingProgress progress, InventoryManager inventory)
        {
            return ResolveItems(progress.Result == CraftingResult.FailedKeepMaterials
                ? progress.ConsumedItems : progress.OutputItems, inventory);
        }

        private static List<InventoryItemAmount> ResolveItems(List<CraftingItemAmount> entries, InventoryManager inventory)
        {
            if (entries == null) return null;
            var result = new List<InventoryItemAmount>(entries.Count);
            foreach (var entry in entries)
            {
                var item = inventory.GetItemData(entry.ItemId);
                if (item == null || item.MaxStack <= 0 || entry.Amount <= 0) return null;
                result.Add(new InventoryItemAmount(item, entry.Amount));
            }
            return result;
        }

        private static List<RecipeOutput> ToEventOutputs(CraftingProgress progress, InventoryManager inventory)
        {
            var result = new List<RecipeOutput>();
            foreach (var entry in progress.OutputItems)
                result.Add(new RecipeOutput { Item = inventory.GetItemData(entry.ItemId), BaseAmount = entry.Amount });
            return result;
        }

        private bool CanAwardExperience(string skillId, int amount)
        {
            if (string.IsNullOrEmpty(skillId)) return true;
            var experience = _skillData.TryGetValue(skillId, out var skill) ? skill.Experience : 0;
            return amount >= 0 && experience >= 0 && (long)experience + amount <= int.MaxValue;
        }

        private void AwardExperience(CraftingProgress progress)
        {
            if (!string.IsNullOrEmpty(progress.SkillId) && progress.ExperienceReward > 0)
                GetSkillData(progress.SkillId).AddExperience(progress.ExperienceReward, out _);
        }

        private static CraftingEventType CompletionEvent(CraftingResult result) =>
            result == CraftingResult.Success || result == CraftingResult.GreatSuccess
                ? CraftingEventType.Completed : CraftingEventType.Failed;

        private void Block(CraftingProgress progress, CraftingResult reason)
        {
            if (progress.LastResult == reason) return;
            progress.LastResult = reason;
            PublishCrafting(new CraftingEventArgs
            {
                EventType = CraftingEventType.Blocked,
                Recipe = GetRecipe(progress.RecipeId),
                ProgressId = progress.ProgressId,
                BatchCount = progress.BatchCount,
                Result = reason
            });
        }

        private void PublishCrafting(CraftingEventArgs args)
        {
            var handlers = OnCraftingEvent;
            if (handlers == null) return;
            foreach (Action<CraftingEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(args); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private CraftingSaveData CaptureState()
        {
            var save = new CraftingSaveData
            {
                UnlockedRecipes = new List<string>(_unlockedRecipes),
                ActiveProgress = new List<CraftingProgress>(),
                SkillData = new Dictionary<string, CraftingSkillData>(),
                Receipts = new List<CraftingReceipt>()
            };
            foreach (var progress in _activeProgress) save.ActiveProgress.Add(CloneProgress(progress));
            foreach (var pair in _skillData) save.SkillData.Add(pair.Key, CloneSkill(pair.Value));
            foreach (var receipt in _receipts.Values) save.Receipts.Add(receipt.Clone());
            return save;
        }

        private void RestoreState(CraftingSaveData data)
        {
            if (_isMutating) throw new InvalidOperationException("Cannot import during a crafting transaction.");
            var progress = new List<CraftingProgress>();
            var receipts = new Dictionary<string, CraftingReceipt>(StringComparer.Ordinal);
            var skills = new Dictionary<string, CraftingSkillData>();
            if (data.Receipts != null)
            {
                foreach (var receipt in data.Receipts)
                {
                    if (receipt == null || string.IsNullOrWhiteSpace(receipt.RequestId)
                        || string.IsNullOrWhiteSpace(receipt.RecipeId) || receipt.BatchCount <= 0
                        || (!IsTerminalOutcome(receipt.Result) && receipt.Result != CraftingResult.Cancelled)
                        || receipts.ContainsKey(receipt.RequestId))
                        throw new ArgumentException("Invalid or duplicate crafting receipt.");
                    var copy = receipt.Clone();
                    copy.WorkbenchId ??= string.Empty;
                    receipts.Add(copy.RequestId, copy);
                }
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (data.ActiveProgress != null)
            {
                for (var index = 0; index < data.ActiveProgress.Count; index++)
                {
                    var entry = data.ActiveProgress[index];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.RecipeId) || entry.BatchCount <= 0
                        || entry.EndTime < entry.StartTime || entry.ExperienceReward < 0)
                        throw new ArgumentException("Invalid crafting progress.");
                    var copy = CloneProgress(entry);
                    if (string.IsNullOrEmpty(copy.ProgressId))
                        copy.ProgressId = $"legacy:{copy.RecipeId}:{copy.StartTime}:{copy.EndTime}:{copy.BatchCount}:{index}";
                    if (string.IsNullOrWhiteSpace(copy.ProgressId) || !ids.Add(copy.ProgressId))
                        throw new ArgumentException("Invalid or duplicate crafting progress identity.");
                    if (copy.OutcomePrepared && (!IsTerminalOutcome(copy.Result)
                        || !ValidAmounts(copy.ConsumedItems) || !ValidAmounts(copy.OutputItems)
                        || (CompletionEvent(copy.Result) == CraftingEventType.Failed
                            && (copy.OutputItems.Count != 0 || copy.ExperienceReward != 0))))
                        throw new ArgumentException("Invalid crafting escrow or output.");
                    if (receipts.TryGetValue(copy.ProgressId, out var receipt))
                    {
                        if (receipt.RecipeId != copy.RecipeId || receipt.BatchCount != copy.BatchCount
                            || receipt.Result != CraftingResult.Success || !receipt.Pending)
                            throw new ArgumentException("Crafting progress/receipt mismatch.");
                    }
                    else
                    {
                        if (copy.OutcomePrepared)
                            throw new ArgumentException("Prepared crafting progress is missing its receipt.");
                        receipts.Add(copy.ProgressId, new CraftingReceipt
                        {
                            RequestId = copy.ProgressId, RecipeId = copy.RecipeId,
                            BatchCount = copy.BatchCount, WorkbenchId = string.Empty, Result = CraftingResult.Success, Pending = true
                        });
                    }
                    progress.Add(copy);
                }
            }
            foreach (var receipt in receipts.Values)
            {
                if (receipt.Pending != ids.Contains(receipt.RequestId))
                    throw new ArgumentException("Crafting receipt is missing its pending progress.");
            }
            if (data.SkillData != null)
            {
                foreach (var pair in data.SkillData)
                {
                    if (string.IsNullOrEmpty(pair.Key) || pair.Value == null || pair.Value.SkillId != pair.Key
                        || pair.Value.Level <= 0 || pair.Value.Level > int.MaxValue / 100 || pair.Value.Experience < 0)
                        throw new ArgumentException("Invalid crafting skill data.");
                    skills.Add(pair.Key, CloneSkill(pair.Value));
                }
            }
            var unlocked = new HashSet<string>(StringComparer.Ordinal);
            if (data.UnlockedRecipes != null)
            {
                foreach (var id in data.UnlockedRecipes)
                {
                    if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Invalid unlocked recipe ID.");
                    unlocked.Add(id);
                }
            }
            _stateImported = true;
            _unlockedRecipes.Clear();
            _unlockedRecipes.UnionWith(unlocked);
            _activeProgress.Clear();
            _activeProgress.AddRange(progress);
            _receipts.Clear();
            foreach (var pair in receipts) _receipts.Add(pair.Key, pair.Value);
            _skillData.Clear();
            foreach (var pair in skills) _skillData.Add(pair.Key, pair.Value);
        }

        private static bool IsTerminalOutcome(CraftingResult result) => result == CraftingResult.Success
            || result == CraftingResult.GreatSuccess || result == CraftingResult.FailedKeepMaterials
            || result == CraftingResult.FailedLoseMaterials;

        private static bool ValidAmounts(List<CraftingItemAmount> entries)
        {
            if (entries == null) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Amount <= 0 || !ids.Add(entry.ItemId))
                    return false;
            }
            return true;
        }

        private static CraftingSkillData CloneSkill(CraftingSkillData skill) => new CraftingSkillData
        { SkillId = skill.SkillId, Level = skill.Level, Experience = skill.Experience };

        private static CraftingProgress CloneProgress(CraftingProgress progress) => new CraftingProgress
        {
            ProgressId = progress.ProgressId, RecipeId = progress.RecipeId, BatchCount = progress.BatchCount,
            StartTime = progress.StartTime, EndTime = progress.EndTime, OutcomePrepared = progress.OutcomePrepared,
            Result = progress.Result, LastResult = progress.LastResult, SkillId = progress.SkillId,
            ExperienceReward = progress.ExperienceReward,
            ConsumedItems = CloneAmounts(progress.ConsumedItems), OutputItems = CloneAmounts(progress.OutputItems)
        };

        private static List<CraftingItemAmount> CloneAmounts(List<CraftingItemAmount> entries)
        {
            if (entries == null) return null;
            var result = new List<CraftingItemAmount>(entries.Count);
            foreach (var entry in entries) result.Add(entry?.Clone());
            return result;
        }
    }
}
