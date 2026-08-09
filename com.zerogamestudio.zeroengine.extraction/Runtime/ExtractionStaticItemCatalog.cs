using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionStaticItemCatalog : IExtractionItemCatalog
    {
        public List<ExtractionItemDefinition> ItemDefinitions = new();

        public bool TryGetItemDefinition(string definitionId, out ExtractionItemDefinition definition)
        {
            foreach (var candidate in ItemDefinitions)
            {
                if (candidate != null && candidate.DefinitionId == definitionId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }
    }
}
