namespace ZeroEngine.RPG.TurnBased
{
    public enum BattleActionResultType
    {
        None = 0,
        Damage = 1,
        Heal = 2,
        ShieldDamage = 3,
        DefendStarted = 4,
        ItemConsumed = 5,
        UnitDefeated = 6,
        EscapeSucceeded = 7,
        EscapeFailed = 8,
        BattleEnded = 9
    }

    public sealed class BattleActionResult
    {
        public BattleActionResultType Type { get; private set; }
        public ITurnBasedCombatant Actor { get; private set; }
        public ITurnBasedCombatant Target { get; private set; }
        public int Amount { get; private set; }
        public int TargetHpAfter { get; private set; }
        public bool IsCritical { get; private set; }
        public string Id { get; private set; }
        public object Payload { get; private set; }

        public static BattleActionResult Damage(ITurnBasedCombatant actor, ITurnBasedCombatant target, int amount, int targetHpAfter, bool isCritical)
        {
            return new BattleActionResult
            {
                Type = BattleActionResultType.Damage,
                Actor = actor,
                Target = target,
                Amount = amount,
                TargetHpAfter = targetHpAfter,
                IsCritical = isCritical
            };
        }

        public static BattleActionResult Heal(ITurnBasedCombatant actor, ITurnBasedCombatant target, int amount, int targetHpAfter)
        {
            return new BattleActionResult
            {
                Type = BattleActionResultType.Heal,
                Actor = actor,
                Target = target,
                Amount = amount,
                TargetHpAfter = targetHpAfter
            };
        }

        public static BattleActionResult ShieldDamage(ITurnBasedCombatant actor, ITurnBasedCombatant target, int amount)
        {
            return new BattleActionResult
            {
                Type = BattleActionResultType.ShieldDamage,
                Actor = actor,
                Target = target,
                Amount = amount
            };
        }

        public static BattleActionResult Simple(
            BattleActionResultType type,
            ITurnBasedCombatant actor,
            ITurnBasedCombatant target = null,
            string id = null,
            object payload = null)
        {
            return new BattleActionResult
            {
                Type = type,
                Actor = actor,
                Target = target,
                Id = id,
                Payload = payload
            };
        }
    }
}
