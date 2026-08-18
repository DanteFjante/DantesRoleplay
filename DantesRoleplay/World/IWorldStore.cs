namespace DantesRoleplay.World;

/// <summary>
/// Everything the kernel can do to world state.
///
/// This interface lives in the core project so that orchestration never depends on Entity
/// Framework. It is also, deliberately, the complete list of structural changes possible —
/// when the effect vocabulary lands (§P9) it maps one-to-one onto these methods, and nothing
/// game-specific will be added to either.
/// </summary>
public interface IWorldStore
{
    // ---- entities -------------------------------------------------------------------

    Task<EntitySnapshot> CreateEntityAsync(
        string name,
        string? id = null,
        CancellationToken cancellationToken = default);

    Task<EntitySnapshot?> GetEntityAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Materialise several entities at once — the call a mechanic's declared requirements turn into.</summary>
    Task<IReadOnlyList<EntitySnapshot>> GetEntitiesAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materialise several entities while restricting component payloads to the requested
    /// definitions. The projection resolver uses this overload so undeclared data never crosses
    /// the storage boundary unnecessarily.
    /// </summary>
    Task<IReadOnlyList<EntitySnapshot>> GetEntitiesAsync(
        IEnumerable<string> ids,
        IReadOnlyCollection<string> componentDefinitionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntitySummary>> FindEntitiesAsync(
        string? nameQuery = null,
        string? withDefinitionId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Soft delete. Returns false when the entity did not exist or was already deleted.</summary>
    Task<bool> DeleteEntityAsync(string id, CancellationToken cancellationToken = default);

    // ---- component definitions ------------------------------------------------------

    Task<ComponentDefinitionView> DefineComponentAsync(
        string id,
        string name,
        string description,
        string schema = "",
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComponentDefinitionView>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default);

    // ---- components -----------------------------------------------------------------

    /// <summary>Replace a component's data wholesale, creating it if absent.</summary>
    Task<ComponentView> SetComponentAsync(
        string entityId,
        string definitionId,
        string json,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shallow-merge top-level keys into existing data, creating the component if absent.
    ///
    /// Set and Merge are separate operations with names that say which they are. TravelRoleplay
    /// had one operation that read like a merge and behaved like a replace, so partial patches
    /// silently wiped adjacent keys — see ARCHITECTURE.md §P9.
    /// </summary>
    Task<ComponentView> MergeComponentAsync(
        string entityId,
        string definitionId,
        string json,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveComponentAsync(
        string entityId,
        string definitionId,
        CancellationToken cancellationToken = default);

    // ---- containment ----------------------------------------------------------------

    /// <summary>
    /// Put an entity inside another, or nowhere when <paramref name="containerId"/> is null.
    /// Moving is idempotent and always leaves exactly zero or one container.
    /// </summary>
    Task MoveAsync(
        string containedId,
        string? containerId,
        string slot = "",
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainmentView>> GetContentsAsync(
        string containerId,
        CancellationToken cancellationToken = default);

    // ---- relationships --------------------------------------------------------------

    Task<RelationshipView> RelateAsync(
        string fromEntityId,
        string toEntityId,
        string kind,
        string json = "{}",
        CancellationToken cancellationToken = default);

    Task<bool> UnrelateAsync(
        string fromEntityId,
        string toEntityId,
        string kind,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RelationshipView>> GetRelationshipsAsync(
        string entityId,
        bool includeIncoming = true,
        CancellationToken cancellationToken = default);
}
