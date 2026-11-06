namespace ZeroEngine.Interaction
{
    /// <summary>Relationship facts for the actor that owns this interaction request.</summary>
    public interface IInteractionRelationshipQuery
    {
        bool TryGetLevel(string npcId, out int level);
    }
}
