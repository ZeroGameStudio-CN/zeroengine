namespace ZeroEngine.AutoBattle.Simulation
{
    public interface IBattleSimulationActionResolver
    {
        void ResolveAction(ISimulationUnit actor, ISimulationUnit target, BattleSimulationContext context);
    }
}
