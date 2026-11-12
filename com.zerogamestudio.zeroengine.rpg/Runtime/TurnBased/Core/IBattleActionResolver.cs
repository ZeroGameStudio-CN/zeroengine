using System.Collections.Generic;

namespace ZeroEngine.RPG.TurnBased
{
    public interface IBattleActionResolver<in TAction>
    {
        IReadOnlyList<BattleActionResult> Resolve(TAction action);
    }
}
