namespace ZeroEngine.Inventory
{
    /// <summary>A caller-owned state change joining the inventory's in-memory commit.</summary>
    public interface IInventoryExchangeParticipant
    {
        /// <summary>Validate/stage without changing live state, publishing events or performing I/O.</summary>
        bool TryPrepare();

        /// <summary>Commit only the prepared in-memory state, before inventory observers or saving.</summary>
        void Commit();

        /// <summary>Restore the pre-commit state if Commit throws. This must not publish or perform I/O.</summary>
        void Rollback();
    }
}
