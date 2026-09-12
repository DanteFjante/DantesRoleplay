namespace DantesRoleplay.Web.Pages;

/// <summary>Immutable mapping from retained web content to a canonical target. It grants no authority.</summary>
public sealed class WebPageResourceIdentity
{
    public required string ContentPageId { get; init; }
    public required string QualifiedTargetId { get; init; }
    public required string OwnerApplicationId { get; init; }
    public required string SourceOperationId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
