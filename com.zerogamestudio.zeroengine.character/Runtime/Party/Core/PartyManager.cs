using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Party
{
    /// <summary>
    /// 队伍管理器
    /// 管理队伍成员、槽位、出战/后备切换
    /// </summary>
    public class PartyManager : MonoSingleton<PartyManager>, ISaveable
    {
        [Header("配置")]
        [SerializeField] private PartyConfigSO _config;

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        // 所有槽位
        private readonly List<PartySlot> _allSlots = new List<PartySlot>();

        // 按类型分组的槽位
        private readonly List<PartySlot> _activeSlots = new List<PartySlot>();
        private readonly List<PartySlot> _reserveSlots = new List<PartySlot>();
        private readonly List<PartySlot> _temporarySlots = new List<PartySlot>();
        private readonly List<PartySlot> _petSlots = new List<PartySlot>();

        // 成员 ID 到成员的映射
        private readonly Dictionary<string, IPartyMember> _memberLookup = new Dictionary<string, IPartyMember>();

        // 队伍领袖
        private IPartyMember _leader;

        // 队伍状态
        private PartyState _currentState = PartyState.Idle;

        // 性能优化：缓存列表
        private readonly List<IPartyMember> _tempMemberList = new List<IPartyMember>(16);

        #region Events

        /// <summary>成员加入队伍</summary>
        public event Action<MemberJoinedEventArgs> OnMemberJoined;

        /// <summary>成员离开队伍</summary>
        public event Action<MemberLeftEventArgs> OnMemberLeft;

        /// <summary>成员槽位变化</summary>
        public event Action<SlotChangedEventArgs> OnSlotChanged;

        /// <summary>成员交换</summary>
        public event Action<MembersSwappedEventArgs> OnMembersSwapped;

        /// <summary>队伍领袖变更</summary>
        public event Action<LeaderChangedEventArgs> OnLeaderChanged;

        /// <summary>队伍状态变化</summary>
        public event Action<PartyStateChangedEventArgs> OnStateChanged;

        /// <summary>队伍全灭</summary>
        public event Action OnPartyWiped;

        #endregion

        #region Properties

        /// <summary>队伍配置</summary>
        public PartyConfigSO Config => _config;

        /// <summary>队伍领袖</summary>
        public IPartyMember Leader => _leader;

        /// <summary>当前状态</summary>
        public PartyState CurrentState => _currentState;

        /// <summary>出战成员数</summary>
        public int ActiveMemberCount => _activeSlots.Count(s => s.IsOccupied);

        /// <summary>后备成员数</summary>
        public int ReserveMemberCount => _reserveSlots.Count(s => s.IsOccupied);

        /// <summary>总成员数</summary>
        public int TotalMemberCount => _memberLookup.Count;

        /// <summary>出战队伍是否已满</summary>
        public bool IsActiveFull => ActiveMemberCount >= _config.MaxActiveMembers;

        /// <summary>队伍是否为空</summary>
        public bool IsEmpty => TotalMemberCount == 0;

        /// <summary>队伍平均等级</summary>
        public float AverageLevel
        {
            get
            {
                if (TotalMemberCount == 0) return 0;
                return (float)_memberLookup.Values.Average(m => m.Level);
            }
        }

        /// <summary>出战队伍存活人数</summary>
        public int AliveActiveCount => _activeSlots.Count(s => s.IsOccupied && s.Member.IsAlive);

        /// <summary>队伍是否全灭</summary>
        public bool IsWiped => ActiveMemberCount > 0 && AliveActiveCount == 0;

        #endregion

        #region ISaveable

        public string SaveKey => "Party";

        public void Register()
        {
            SaveSlotManager.Instance?.Register(this);
        }

        public void Unregister()
        {
            SaveSlotManager.Instance?.Unregister(this);
        }

        public object ExportSaveData()
        {
            var data = new PartyManagerSaveData
            {
                LeaderId = _leader?.MemberId
            };

            // 保存所有槽位信息
            foreach (var slot in _allSlots)
            {
                if (slot.IsOccupied)
                {
                    data.SlotData.Add(new PartySlotSaveData
                    {
                        SlotIndex = slot.Index,
                        SlotType = slot.SlotType,
                        MemberId = slot.Member.MemberId,
                        IsLocked = slot.IsLocked
                    });
                }
            }

            return data;
        }

        public void ImportSaveData(object data)
        {
            if (data is not PartyManagerSaveData saveData) return;

            // 重建需要外部提供成员查找机制
            // 这里仅恢复槽位锁定状态
            foreach (var slotData in saveData.SlotData)
            {
                var slot = GetSlotByIndex(slotData.SlotIndex);
                if (slot != null)
                {
                    slot.IsLocked = slotData.IsLocked;
                }
            }
        }

        public void ResetToDefault()
        {
            ClearAll();
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            InitializeSlots();
        }

        private void Start()
        {
            Register();
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化槽位
        /// </summary>
        private void InitializeSlots()
        {
            if (_config == null)
            {
                Debug.LogWarning("[PartyManager] 未配置 PartyConfigSO，使用默认值");
                _config = ScriptableObject.CreateInstance<PartyConfigSO>();
            }

            _allSlots.Clear();
            _activeSlots.Clear();
            _reserveSlots.Clear();
            _temporarySlots.Clear();
            _petSlots.Clear();

            int index = 0;

            // 创建出战槽位
            for (int i = 0; i < _config.MaxActiveMembers; i++)
            {
                var slot = new PartySlot(index++, PartySlotType.Active);
                _allSlots.Add(slot);
                _activeSlots.Add(slot);
            }

            // 创建后备槽位
            for (int i = 0; i < _config.MaxReserveMembers; i++)
            {
                var slot = new PartySlot(index++, PartySlotType.Reserve);
                _allSlots.Add(slot);
                _reserveSlots.Add(slot);
            }

            // 创建临时槽位
            for (int i = 0; i < _config.MaxTemporarySlots; i++)
            {
                var slot = new PartySlot(index++, PartySlotType.Temporary);
                _allSlots.Add(slot);
                _temporarySlots.Add(slot);
            }

            // 创建宠物槽位
            for (int i = 0; i < _config.MaxPetSlots; i++)
            {
                var slot = new PartySlot(index++, PartySlotType.Pet);
                _allSlots.Add(slot);
                _petSlots.Add(slot);
            }

            Log($"初始化完成: {_config.MaxActiveMembers} 出战, {_config.MaxReserveMembers} 后备, " +
                $"{_config.MaxTemporarySlots} 临时, {_config.MaxPetSlots} 宠物");
        }

        #endregion

        #region Public API - Add/Remove

        /// <summary>
        /// 添加成员到队伍
        /// </summary>
        /// <param name="member">成员</param>
        /// <param name="preferActive">是否优先添加到出战队伍</param>
        /// <returns>是否添加成功</returns>
        public bool AddMember(IPartyMember member, bool preferActive = true)
        {
            if (member == null)
            {
                LogWarning("尝试添加空成员");
                return false;
            }

            if (!_config.AllowDuplicateMembers && _memberLookup.ContainsKey(member.MemberId))
            {
                LogWarning($"成员 {member.DisplayName} 已在队伍中");
                return false;
            }

            // 根据成员类型找槽位
            PartySlot targetSlot = null;

            switch (member.MemberType)
            {
                case PartyMemberType.Pet:
                    targetSlot = FindEmptySlot(_petSlots);
                    break;

                case PartyMemberType.Summon:
                case PartyMemberType.Temporary:
                    targetSlot = FindEmptySlot(_temporarySlots);
                    break;

                default:
                    if (preferActive)
                    {
                        targetSlot = FindEmptySlot(_activeSlots) ?? FindEmptySlot(_reserveSlots);
                    }
                    else
                    {
                        targetSlot = FindEmptySlot(_reserveSlots) ?? FindEmptySlot(_activeSlots);
                    }
                    break;
            }

            if (targetSlot == null)
            {
                LogWarning($"没有空闲槽位可添加 {member.DisplayName}");
                return false;
            }

            return AddMemberToSlot(member, targetSlot);
        }

        /// <summary>
        /// 添加成员到指定槽位
        /// </summary>
        public bool AddMemberToSlot(IPartyMember member, int slotIndex)
        {
            var slot = GetSlotByIndex(slotIndex);
            if (slot == null)
            {
                LogWarning($"无效的槽位索引: {slotIndex}");
                return false;
            }

            return AddMemberToSlot(member, slot);
        }

        private bool AddMemberToSlot(IPartyMember member, PartySlot slot)
        {
            if (!slot.SetMember(member))
            {
                return false;
            }

            _memberLookup[member.MemberId] = member;
            member.OnJoinParty(slot.Index);

            // 如果是第一个成员，设为领袖
            if (_leader == null && slot.SlotType == PartySlotType.Active)
            {
                SetLeader(member);
            }

            OnMemberJoined?.Invoke(new MemberJoinedEventArgs(member, slot.Index, slot.SlotType));
            Log($"{member.DisplayName} 加入队伍，槽位 {slot.Index} ({slot.SlotType})");

            return true;
        }

        /// <summary>
        /// 移除成员
        /// </summary>
        public bool RemoveMember(IPartyMember member, LeaveReason reason = LeaveReason.Removed)
        {
            if (member == null) return false;

            return RemoveMemberById(member.MemberId, reason);
        }

        /// <summary>
        /// 通过 ID 移除成员
        /// </summary>
        public bool RemoveMemberById(string memberId, LeaveReason reason = LeaveReason.Removed)
        {
            if (!_memberLookup.TryGetValue(memberId, out var member))
            {
                return false;
            }

            int slotIndex = member.PartySlotIndex;
            var slot = GetSlotByIndex(slotIndex);

            if (slot != null)
            {
                slot.Clear();
            }

            _memberLookup.Remove(memberId);
            member.OnLeaveParty();

            // 如果移除的是领袖，选择新领袖
            if (_leader == member)
            {
                AutoSelectNewLeader();
            }

            OnMemberLeft?.Invoke(new MemberLeftEventArgs(member, slotIndex, reason));
            Log($"{member.DisplayName} 离开队伍 (原因: {reason})");

            // 检查是否全灭
            if (IsWiped)
            {
                OnPartyWiped?.Invoke();
            }

            return true;
        }

        /// <summary>
        /// 清空队伍
        /// </summary>
        public void ClearAll()
        {
            _tempMemberList.Clear();
            _tempMemberList.AddRange(_memberLookup.Values);

            foreach (var member in _tempMemberList)
            {
                RemoveMember(member, LeaveReason.Removed);
            }

            _tempMemberList.Clear();
            _leader = null;
        }

        #endregion

        #region Public API - Slot Operations

        /// <summary>
        /// 交换两个槽位的成员
        /// </summary>
        public bool SwapSlots(int slotA, int slotB)
        {
            var a = GetSlotByIndex(slotA);
            var b = GetSlotByIndex(slotB);

            if (a == null || b == null)
            {
                LogWarning($"无效的槽位索引: {slotA} 或 {slotB}");
                return false;
            }

            // 检查战斗中限制
            if (_currentState == PartyState.InCombat && !_config.AllowSwitchInCombat)
            {
                LogWarning("战斗中不允许切换");
                return false;
            }

            var memberA = a.Member;
            var memberB = b.Member;

            if (!a.SwapWith(b))
            {
                return false;
            }

            // 通知成员槽位变化
            if (memberA != null)
            {
                memberA.OnSlotChanged(slotA, slotB);
                OnSlotChanged?.Invoke(new SlotChangedEventArgs(memberA, slotA, slotB, a.SlotType, b.SlotType));
            }

            if (memberB != null)
            {
                memberB.OnSlotChanged(slotB, slotA);
                OnSlotChanged?.Invoke(new SlotChangedEventArgs(memberB, slotB, slotA, b.SlotType, a.SlotType));
            }

            OnMembersSwapped?.Invoke(new MembersSwappedEventArgs(memberA, memberB, slotA, slotB));
            Log($"交换槽位 {slotA} <-> {slotB}");

            return true;
        }

        /// <summary>
        /// 移动成员到指定槽位
        /// </summary>
        public bool MoveMemberToSlot(IPartyMember member, int targetSlotIndex)
        {
            if (member == null) return false;

            int currentSlot = member.PartySlotIndex;
            if (currentSlot < 0)
            {
                LogWarning($"{member.DisplayName} 不在队伍中");
                return false;
            }

            return SwapSlots(currentSlot, targetSlotIndex);
        }

        /// <summary>
        /// 将后备成员激活到出战
        /// </summary>
        public bool ActivateMember(IPartyMember member)
        {
            if (member == null) return false;

            var currentSlot = GetSlotByIndex(member.PartySlotIndex);
            if (currentSlot == null || currentSlot.SlotType != PartySlotType.Reserve)
            {
                LogWarning($"{member.DisplayName} 不在后备队伍中");
                return false;
            }

            // 找一个空的出战槽位
            var activeSlot = FindEmptySlot(_activeSlots);
            if (activeSlot == null)
            {
                LogWarning("出战队伍已满，无法激活");
                return false;
            }

            return SwapSlots(currentSlot.Index, activeSlot.Index);
        }

        /// <summary>
        /// 将出战成员移至后备
        /// </summary>
        public bool DeactivateMember(IPartyMember member)
        {
            if (member == null) return false;

            var currentSlot = GetSlotByIndex(member.PartySlotIndex);
            if (currentSlot == null || currentSlot.SlotType != PartySlotType.Active)
            {
                LogWarning($"{member.DisplayName} 不在出战队伍中");
                return false;
            }

            // 不能将最后一个出战成员移至后备
            if (AliveActiveCount <= 1)
            {
                LogWarning("无法移除最后一个出战成员");
                return false;
            }

            // 找一个空的后备槽位
            var reserveSlot = FindEmptySlot(_reserveSlots);
            if (reserveSlot == null)
            {
                LogWarning("后备队伍已满，无法移至后备");
                return false;
            }

            return SwapSlots(currentSlot.Index, reserveSlot.Index);
        }

        #endregion

        #region Public API - Query

        /// <summary>
        /// 获取指定索引的槽位
        /// </summary>
        public PartySlot GetSlotByIndex(int index)
        {
            if (index < 0 || index >= _allSlots.Count)
                return null;
            return _allSlots[index];
        }

        /// <summary>
        /// 获取成员
        /// </summary>
        public IPartyMember GetMemberById(string memberId)
        {
            return _memberLookup.TryGetValue(memberId, out var member) ? member : null;
        }

        /// <summary>
        /// 检查成员是否在队伍中
        /// </summary>
        public bool HasMember(IPartyMember member)
        {
            return member != null && _memberLookup.ContainsKey(member.MemberId);
        }

        /// <summary>
        /// 检查成员是否在队伍中
        /// </summary>
        public bool HasMemberById(string memberId)
        {
            return _memberLookup.ContainsKey(memberId);
        }

        /// <summary>
        /// 获取所有出战成员
        /// </summary>
        public IEnumerable<IPartyMember> GetActiveMembers()
        {
            return _activeSlots
                .Where(s => s.IsOccupied)
                .Select(s => s.Member);
        }

        /// <summary>
        /// 获取所有后备成员
        /// </summary>
        public IEnumerable<IPartyMember> GetReserveMembers()
        {
            return _reserveSlots
                .Where(s => s.IsOccupied)
                .Select(s => s.Member);
        }

        /// <summary>
        /// 获取所有成员
        /// </summary>
        public IEnumerable<IPartyMember> GetAllMembers()
        {
            return _memberLookup.Values;
        }

        /// <summary>
        /// 获取存活的出战成员
        /// </summary>
        public IEnumerable<IPartyMember> GetAliveActiveMembers()
        {
            return GetActiveMembers().Where(m => m.IsAlive);
        }

        /// <summary>
        /// 获取所有槽位
        /// </summary>
        public IReadOnlyList<PartySlot> GetAllSlots() => _allSlots;

        /// <summary>
        /// 获取指定类型的槽位
        /// </summary>
        public IReadOnlyList<PartySlot> GetSlotsByType(PartySlotType slotType)
        {
            return slotType switch
            {
                PartySlotType.Active => _activeSlots,
                PartySlotType.Reserve => _reserveSlots,
                PartySlotType.Temporary => _temporarySlots,
                PartySlotType.Pet => _petSlots,
                _ => _allSlots
            };
        }

        #endregion

        #region Public API - Leader

        /// <summary>
        /// 设置队伍领袖
        /// </summary>
        public bool SetLeader(IPartyMember member)
        {
            if (member == null) return false;

            if (!_memberLookup.ContainsKey(member.MemberId))
            {
                LogWarning($"{member.DisplayName} 不在队伍中，无法设为领袖");
                return false;
            }

            var slot = GetSlotByIndex(member.PartySlotIndex);
            if (slot?.SlotType != PartySlotType.Active)
            {
                LogWarning("只有出战成员可以成为领袖");
                return false;
            }

            var previous = _leader;
            _leader = member;

            OnLeaderChanged?.Invoke(new LeaderChangedEventArgs(previous, member));
            Log($"领袖变更: {previous?.DisplayName ?? "无"} -> {member.DisplayName}");

            return true;
        }

        private void AutoSelectNewLeader()
        {
            var newLeader = GetActiveMembers().FirstOrDefault(m => m.IsAlive);
            if (newLeader != null)
            {
                SetLeader(newLeader);
            }
            else
            {
                _leader = null;
            }
        }

        #endregion

        #region Public API - State

        /// <summary>
        /// 设置队伍状态
        /// </summary>
        public void SetState(PartyState newState)
        {
            if (_currentState == newState) return;

            var oldState = _currentState;
            _currentState = newState;

            OnStateChanged?.Invoke(new PartyStateChangedEventArgs(oldState, newState));
            Log($"队伍状态: {oldState} -> {newState}");
        }

        #endregion

        #region Helper Methods

        private PartySlot FindEmptySlot(List<PartySlot> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsEmpty && !slots[i].IsLocked)
                {
                    return slots[i];
                }
            }
            return null;
        }

        #endregion

        #region Logging

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[PartyManager] {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[PartyManager] {message}");
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class PartyManagerSaveData
    {
        public string LeaderId;
        public List<PartySlotSaveData> SlotData = new List<PartySlotSaveData>();
    }

    [Serializable]
    public class PartySlotSaveData
    {
        public int SlotIndex;
        public PartySlotType SlotType;
        public string MemberId;
        public bool IsLocked;
    }

    #endregion
}
