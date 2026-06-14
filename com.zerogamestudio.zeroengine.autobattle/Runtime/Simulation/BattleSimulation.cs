using System.Collections.Generic;

namespace ZeroEngine.AutoBattle.Simulation
{
    public sealed class BattleSimulation
    {
        private readonly List<ISimulationUnit> _units = new List<ISimulationUnit>();

        public IReadOnlyList<ISimulationUnit> Units => _units;
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
