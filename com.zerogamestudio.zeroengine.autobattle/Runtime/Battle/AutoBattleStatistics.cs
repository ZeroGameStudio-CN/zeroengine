using System;
using ZeroEngine.AutoBattle.Grid;

namespace ZeroEngine.AutoBattle.Battle
{
    /// <summary>
    /// Runtime statistics collected from actual unit health and death events.
    /// </summary>
    [Serializable]
    public sealed class AutoBattleStatistics
    {
        public float PlayerDamageDealt { get; private set; }
        public float PlayerDamageTaken { get; private set; }
        public float PlayerHealing { get; private set; }
        public float EnemyDamageDealt { get; private set; }
        public float EnemyDamageTaken { get; private set; }
        public float EnemyHealing { get; private set; }
        public int PlayerDeaths { get; private set; }
        public int EnemyDeaths { get; private set; }
        public float Duration { get; private set; }
        public BattleResult Result { get; private set; }

        internal void Reset()
        {
            PlayerDamageDealt = 0f;
            PlayerDamageTaken = 0f;
            PlayerHealing = 0f;
            EnemyDamageDealt = 0f;
            EnemyDamageTaken = 0f;
            EnemyHealing = 0f;
            PlayerDeaths = 0;
            EnemyDeaths = 0;
            Duration = 0f;
            Result = BattleResult.None;
        }

        internal void RecordHealthChange(BattleTeam targetTeam, float oldHealth, float newHealth)
        {
            float delta = oldHealth - newHealth;
            if (delta > 0f)
            {
                if (targetTeam == BattleTeam.Player)
                {
                    PlayerDamageTaken += delta;
                    EnemyDamageDealt += delta;
                }
                else
                {
                    EnemyDamageTaken += delta;
                    PlayerDamageDealt += delta;
                }
                return;
            }

            float healing = -delta;
            if (healing <= 0f)
            {
                return;
            }

            if (targetTeam == BattleTeam.Player)
            {
                PlayerHealing += healing;
            }
            else
            {
                EnemyHealing += healing;
            }
        }

        internal void RecordDeath(BattleTeam team)
        {
            if (team == BattleTeam.Player)
            {
                PlayerDeaths++;
            }
            else
            {
                EnemyDeaths++;
            }
        }

        internal void Complete(BattleResult result, float duration)
        {
            Result = result;
            Duration = duration;
        }
    }
}
