namespace ZeroEngine.AutoBattle.Simulation
{
    public enum SimulationTeam
    {
        Player,
        Enemy
    }

    public enum SimulationUnitRole
    {
        Damage,
        Tank,
        Healer,
        Assassin
    }

    public enum SimulationBattleResult
    {
        InProgress,
        PlayerWin,
        EnemyWin,
        Timeout
    }
}
