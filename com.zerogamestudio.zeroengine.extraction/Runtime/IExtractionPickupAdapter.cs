namespace POB.Extraction
{
    public interface IExtractionPickupAdapter
    {
        bool TryCreatePickup(
            ExtractionPickupAdapterRequest request,
            IExtractionItemCatalog catalog,
            out ExtractionLootPickup pickup);
    }

    public class ExtractionCatalogPickupAdapter : IExtractionPickupAdapter
    {
        public bool TryCreatePickup(
            ExtractionPickupAdapterRequest request,
            IExtractionItemCatalog catalog,
            out ExtractionLootPickup pickup)
        {
            pickup = null;
            if (request == null || catalog == null || !request.IsValid) return false;
            if (!catalog.TryGetItemDefinition(request.DefinitionId, out var definition)) return false;
            if (definition == null || request.Quantity > definition.MaxStack) return false;

            var item = new ExtractionItemInstance(
                request.InstanceId,
                request.DefinitionId,
                request.Quantity,
                request.SourceKind,
                request.SourceId);
            ExtractionItemActionPolicyService.ApplyDefinitionPolicyToInstance(definition, item);
            pickup = new ExtractionLootPickup(item, definition, request.CanEnterSecureContainer);
            return pickup.IsValid;
        }
    }
}
