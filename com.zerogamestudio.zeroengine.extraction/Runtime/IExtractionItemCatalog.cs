namespace POB.Extraction
{
    public interface IExtractionItemCatalog
    {
        bool TryGetItemDefinition(string definitionId, out ExtractionItemDefinition definition);
    }
}
