using System.Runtime.CompilerServices;
using ZeroEngine.Achievement;

// Keep the old assembly-qualified names resolvable by existing saves and consumers.
[assembly: TypeForwardedTo(typeof(AchievementManager))]
[assembly: TypeForwardedTo(typeof(AchievementSaveData))]
[assembly: TypeForwardedTo(typeof(AchievementCondition))]
[assembly: TypeForwardedTo(typeof(CounterCondition))]
[assembly: TypeForwardedTo(typeof(EventCondition))]
[assembly: TypeForwardedTo(typeof(CompositeAchievementCondition))]
[assembly: TypeForwardedTo(typeof(AchievementReward))]
[assembly: TypeForwardedTo(typeof(AchievementPointReward))]
[assembly: TypeForwardedTo(typeof(CustomAchievementReward))]
[assembly: TypeForwardedTo(typeof(CompositeReward))]
[assembly: TypeForwardedTo(typeof(AchievementSO))]
[assembly: TypeForwardedTo(typeof(AchievementGroupSO))]
[assembly: TypeForwardedTo(typeof(AchievementState))]
[assembly: TypeForwardedTo(typeof(AchievementCategory))]
[assembly: TypeForwardedTo(typeof(AchievementEventType))]
[assembly: TypeForwardedTo(typeof(AchievementEventArgs))]
[assembly: TypeForwardedTo(typeof(AchievementProgress))]
[assembly: TypeForwardedTo(typeof(IAchievementProvider))]
[assembly: TypeForwardedTo(typeof(UnlockReward))]
[assembly: TypeForwardedTo(typeof(UnlockType))]
