using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.World.Authoring
{
    public delegate IEnumerable<AreaAuthoringIssue> AreaAuthoringValidationRule();

    public static class AreaAuthoringValidationRunner
    {
        public static IReadOnlyList<AreaAuthoringIssue> Run(params AreaAuthoringValidationRule[] rules)
        {
            if (rules == null || rules.Length == 0)
            {
                return Array.Empty<AreaAuthoringIssue>();
            }

            var issues = new List<AreaAuthoringIssue>();
            foreach (var rule in rules.Where(rule => rule != null))
            {
                var result = rule();
                if (result != null)
                {
                    issues.AddRange(result);
                }
            }

            return issues;
        }
    }
}
