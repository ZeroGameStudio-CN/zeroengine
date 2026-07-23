using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.World.Authoring
{
    public readonly struct AreaAuthoringRuleResult
    {
        private readonly IReadOnlyList<AreaAuthoringIssue> _issues;

        public AreaAuthoringRuleResult(IEnumerable<AreaAuthoringIssue> issues)
        {
            _issues = issues?.ToArray() ?? Array.Empty<AreaAuthoringIssue>();
        }

        public IReadOnlyList<AreaAuthoringIssue> Issues => _issues ?? Array.Empty<AreaAuthoringIssue>();
        public bool HasErrors => Issues.Any(issue => issue.IsError);

        public static AreaAuthoringRuleResult Empty { get; } = new AreaAuthoringRuleResult(Array.Empty<AreaAuthoringIssue>());
    }
}
