using System.Collections.Generic;

namespace ZeroEngine.AutoBattle.Simulation
{
    public sealed class BattleSimulation
    {
        private readonly List<ISimulationUnit> _units = new List<ISimulationUnit>();
        private readonly BattleSimulationContext _context = new BattleSimulationContext();
        private readonly IBattleSimulationLayout _layout;
        private readonly IBattleSimulationActionResolver _actionResolver;

        public BattleSimulation()
        {
        }

        public BattleSimulation(IBattleSimulationLayout layout, IBattleSimulationActionResolver actionResolver)
        {
            _layout = layout;
            _actionResolver = actionResolver;
        }

        public IReadOnlyList<ISimulationUnit> Units => _units;
        public IReadOnlyList<BattleSimulationEvent> LastTickEvents => _context.Events;
        public float ElapsedTime { get; private set; }
        public float MaxDuration { get; set; } = 60f;

        public void AddUnit(ISimulationUnit unit)
        {
            if (unit != null && !_units.Contains(unit))
            {
                _units.Add(unit);
            }
        }

        public SimulationBattleResult Advance(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                ElapsedTime += deltaTime;
            }

            var result = CheckResult();
            if (result != SimulationBattleResult.InProgress)
            {
                return result;
            }

            return ElapsedTime >= MaxDuration
                ? SimulationBattleResult.Timeout
                : SimulationBattleResult.InProgress;
        }

        public SimulationBattleResult AdvanceTick()
        {
            _context.Clear();
            _context.Units = _units;
            _context.Layout = _layout;

            if (_layout == null || _actionResolver == null)
            {
                return CheckResult();
            }

            for (int i = 0; i < _units.Count; i++)
            {
                var actor = _units[i];
                if (actor == null || !actor.IsAlive)
                {
                    continue;
                }

                var target = BattleSimulationTargeting.FindTarget(actor, _units);
                if (target == null)
                {
                    continue;
                }

                if (!_layout.CanAttack(actor, target, _units))
                {
                    if (_layout.MoveTowards(actor, target, _units))
                    {
                        _context.AddEvent(BattleSimulationEventType.Moved, actor, target);
                    }

                    continue;
                }

                _actionResolver.ResolveAction(actor, target, _context);
                _context.AddEvent(BattleSimulationEventType.ActionResolved, actor, target);

                if (!target.IsAlive)
                {
                    _context.AddEvent(BattleSimulationEventType.UnitDefeated, actor, target);
                }
            }

            return CheckResult();
        }

        public SimulationBattleResult CheckResult()
        {
            bool hasPlayer = false;
            bool hasEnemy = false;

            for (int i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                if (unit.Team == SimulationTeam.Player)
                {
                    hasPlayer = true;
                }
                else
                {
                    hasEnemy = true;
                }
            }

            if (!hasPlayer)
            {
                return SimulationBattleResult.EnemyWin;
            }

            if (!hasEnemy)
            {
                return SimulationBattleResult.PlayerWin;
            }

            return SimulationBattleResult.InProgress;
        }
    }
}
