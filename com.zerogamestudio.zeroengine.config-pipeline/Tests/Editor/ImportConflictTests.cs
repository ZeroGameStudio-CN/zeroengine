using NUnit.Framework;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class ImportConflictTests
    {
        private const string Base =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Json =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string Workbook =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        [TestCase(Base, Base, ConfigImportDecision.CandidateCurrentEqual, true)]
        [TestCase(Json, Json, ConfigImportDecision.CandidateCurrentEqual, true)]
        [TestCase(Json, Base, ConfigImportDecision.CandidateJsonOnly, true)]
        [TestCase(Base, Workbook, ConfigImportDecision.RejectStaleJson, false)]
        [TestCase(Json, Workbook, ConfigImportDecision.RejectConflict, false)]
        public void Resolve_ImplementsThreeWayMatrix(
            string jsonCurrent,
            string workbookCurrent,
            ConfigImportDecision expected,
            bool canCreateCandidate)
        {
            ConfigImportConflictResult result =
                ConfigImportConflictResolver.Resolve(Base, jsonCurrent, workbookCurrent);

            Assert.That(result.Decision, Is.EqualTo(expected));
            Assert.That(result.CanCreateCandidate, Is.EqualTo(canCreateCandidate));
        }

        [Test]
        public void Resolve_MissingBaseOnlyCreatesUnbasedCandidate()
        {
            ConfigImportConflictResult result =
                ConfigImportConflictResolver.Resolve(null, Json, Workbook);

            Assert.That(result.Decision, Is.EqualTo(ConfigImportDecision.CandidateUnbased));
            Assert.That(result.CanCreateCandidate, Is.True);
        }
    }
}
