using NUnit.Framework;
using ZeroEngine.PvP.Matching;
using ZeroEngine.PvP.Ranking;
using ZeroEngine.PvP.Snapshot;

namespace ZeroEngine.PvP.Editor.Tests
{
    public sealed class MatchingAndRankingTests
    {
        [Test]
        public void FindOpponentReturnsNullWhenPoolIsEmpty()
        {
            var service = new MatchingService();

            Assert.IsNull(service.FindOpponent(playerPower: 1000));
        }

        [Test]
        public void FindOpponentPrefersCandidateInsideConfiguredTolerance()
        {
            var service = new MatchingService(new MatchingConfig
            {
                PowerTolerance = 0.1f,
                FloorToleranceBonus = 0f
            });
            var opponent = CreateTeam(1080);
            service.AddCandidate(CreateTeam(1500));
            service.AddCandidate(opponent);

            Assert.AreSame(opponent, service.FindOpponent(playerPower: 1000));
        }

        [Test]
        public void FindOpponentsCapsReturnedCountToAvailableCandidates()
        {
            var service = new MatchingService();
            service.AddCandidate(CreateTeam(900));
            service.AddCandidate(CreateTeam(1100));

            var opponents = service.FindOpponents(playerPower: 1000, count: 5);

            Assert.AreEqual(2, opponents.Count);
        }

        [Test]
        public void RankingRecordsWinStreakRatesAndTier()
        {
            var ranking = new RankingData { Score = 990 };

            ranking.RecordAttack(won: true, scoreChange: 30);
            ranking.RecordAttack(won: false, scoreChange: -10);
            ranking.RecordDefense(won: true, scoreChange: 5);

            Assert.AreEqual(1, ranking.AttackWins);
            Assert.AreEqual(1, ranking.AttackLosses);
            Assert.AreEqual(1, ranking.DefenseWins);
            Assert.AreEqual(0, ranking.WinStreak);
            Assert.AreEqual(1, ranking.MaxWinStreak);
            Assert.AreEqual(0.5f, ranking.AttackWinRate);
            Assert.AreEqual(1f, ranking.DefenseWinRate);
            Assert.AreEqual(RankTier.Silver, ranking.Tier);
        }

        private static TeamSnapshot CreateTeam(int totalPower)
        {
            return new TeamSnapshot
            {
                TotalPower = totalPower
            };
        }
    }
}
