using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph.Traversal;

namespace ZeroEngine.World.Tests.Editor
{
    [TestFixture]
    public sealed class WorldCellTraversalValidatorTests
    {
        [Test]
        public void Validate_CompleteProfile_ReturnsNoBlockingIssues()
        {
            var definition = CreateValidDefinition();

            var issues = WorldCellTraversalValidator.Validate(definition);

            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void Validate_MissingRecoveryAnchor_ReturnsBlockingIssue()
        {
            var definition = CreateValidDefinition();
            definition.SetAnchors(new[]
            {
                new WorldTraversalAnchorDefinition(
                    "spawn.main",
                    WorldTraversalAnchorKind.Spawn,
                    Vector3.zero,
                    Vector3.forward)
            });

            var issues = WorldCellTraversalValidator.Validate(definition);

            Assert.That(issues, Has.Some.Matches<WorldCellTraversalValidationIssue>(
                issue => issue.Code == WorldCellTraversalValidationCode.MissingRecoveryAnchor
                         && issue.IsBlocking));
        }

        [Test]
        public void Validate_PortalOverlapsSolidBoundary_ReturnsBlockingIssue()
        {
            var definition = CreateValidDefinition();
            definition.SetBoundaries(new[]
            {
                new WorldTraversalBoundaryDefinition(
                    "solid.north",
                    WorldTraversalBoundaryKind.Solid,
                    new Bounds(new Vector3(0f, 1f, 5f), new Vector3(10f, 2f, 0.5f))),
                new WorldTraversalBoundaryDefinition(
                    "portal.north",
                    WorldTraversalBoundaryKind.Portal,
                    new Bounds(new Vector3(0f, 1f, 5f), new Vector3(2f, 2f, 0.5f)),
                    "cell.next")
            });

            var issues = WorldCellTraversalValidator.Validate(definition);

            Assert.That(issues, Has.Some.Matches<WorldCellTraversalValidationIssue>(
                issue => issue.Code == WorldCellTraversalValidationCode.ExitOverlapsSolid
                         && issue.IsBlocking));
        }

        private static WorldCellTraversalDefinition CreateValidDefinition()
        {
            return new WorldCellTraversalDefinition(
                "cell.test",
                new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)),
                -8f,
                new List<WorldTraversalBoundaryDefinition>
                {
                    new(
                        "solid.north",
                        WorldTraversalBoundaryKind.Solid,
                        new Bounds(new Vector3(0f, 1f, 5f), new Vector3(10f, 2f, 0.5f)))
                },
                new List<WorldTraversalAnchorDefinition>
                {
                    new("spawn.main", WorldTraversalAnchorKind.Spawn, Vector3.zero, Vector3.forward),
                    new("recovery.main", WorldTraversalAnchorKind.Recovery, Vector3.zero, Vector3.forward)
                });
        }
    }
}
