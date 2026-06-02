namespace ZeroEngine.Gameplay.Rewards
{
    public interface IRewardHandler
    {
        bool CanHandle(RewardRequest request);
        RewardResult Grant(RewardRequest request);
    }
}
