using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Dlc
{
    [CreateAssetMenu(menuName = "ZeroEngine/DLC/Content Pack Catalog", fileName = "ContentPackCatalog")]
    public sealed class ContentPackCatalog : ScriptableObject
    {
        [SerializeField] private List<ContentPackDefinition> _contentPacks = new();

        public IReadOnlyList<ContentPackDefinition> ContentPacks => _contentPacks;

        public bool TryGetContentPack(string contentPackId, out ContentPackDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(contentPackId))
            {
                return false;
            }

            foreach (var contentPack in _contentPacks)
            {
                if (contentPack == null)
                {
                    continue;
                }

                if (string.Equals(contentPack.ContentPackId, contentPackId, StringComparison.Ordinal))
                {
                    definition = contentPack;
                    return true;
                }
            }

            return false;
        }

        public static ContentPackCatalog CreateInMemory(IEnumerable<ContentPackDefinition> definitions)
        {
            var catalog = CreateInstance<ContentPackCatalog>();
            catalog._contentPacks = definitions == null
                ? new List<ContentPackDefinition>()
                : new List<ContentPackDefinition>(definitions);
            return catalog;
        }
    }
}
