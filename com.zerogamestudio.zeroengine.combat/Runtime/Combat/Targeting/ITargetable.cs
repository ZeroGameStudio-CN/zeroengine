using UnityEngine;

namespace ZeroEngine.Combat
{
    /// <summary>
    /// 可被选中的目标接口
    /// </summary>
    public interface ITargetable
    {
        /// <summary>目标ID</summary>
        string TargetId { get; }

        /// <summary>是否可被选中</summary>
        bool IsTargetable { get; }

        /// <summary>是否存活</summary>
        bool IsAlive { get; }

        /// <summary>队伍ID</summary>
        int TeamId { get; }

        /// <summary>目标优先级（用于自动选择）</summary>
        int TargetPriority { get; }

        /// <summary>目标位置</summary>
        Vector3 Position { get; }

        /// <summary>目标Transform</summary>
        Transform Transform { get; }

        /// <summary>目标碰撞体中心（用于射线检测）</summary>
        Vector3 GetTargetCenter();

        /// <summary>目标半径（用于范围检测）</summary>
        float GetTargetRadius();
    }

    /// <summary>
    /// 可被选中目标的基础组件
    /// </summary>
    public class TargetableBase : MonoBehaviour, ITargetable
    {
        [Header("目标配置")]
        [SerializeField] protected string _targetId;
        [SerializeField] protected int _teamId;
        [SerializeField] protected int _targetPriority;
        [SerializeField] protected float _targetRadius = 0.5f;
        [SerializeField] protected Vector3 _centerOffset = Vector3.up;

        [Header("状态")]
        [SerializeField] protected bool _isTargetable = true;
        [SerializeField] protected bool _isAlive = true;

        public virtual string TargetId => string.IsNullOrEmpty(_targetId) ? gameObject.name : _targetId;
        public virtual bool IsTargetable => _isTargetable && _isAlive && gameObject.activeInHierarchy;
        public virtual bool IsAlive => _isAlive;
        public virtual int TeamId => _teamId;
        public virtual int TargetPriority => _targetPriority;
        public virtual Vector3 Position => transform.position;
        public virtual Transform Transform => transform;

        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(_targetId))
            {
#if UNITY_6000_6_OR_NEWER
                _targetId = $"{gameObject.name}_{EntityId.ToULong(GetEntityId())}";
#else
                _targetId = $"{gameObject.name}_{GetInstanceID()}";
#endif
            }
        }

        public virtual Vector3 GetTargetCenter()
        {
            return transform.position + _centerOffset;
        }

        public virtual float GetTargetRadius()
        {
            return _targetRadius;
        }

        /// <summary>
        /// 设置是否可被选中
        /// </summary>
        public void SetTargetable(bool targetable)
        {
            _isTargetable = targetable;
        }

        /// <summary>
        /// 设置存活状态
        /// </summary>
        public void SetAlive(bool alive)
        {
            _isAlive = alive;
        }

        /// <summary>
        /// 设置队伍ID
        /// </summary>
        public void SetTeamId(int teamId)
        {
            _teamId = teamId;
        }
    }
}
