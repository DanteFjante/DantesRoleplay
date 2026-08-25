using DantesRoleplay.Authorization;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.ComponentTypeAdministration;

public sealed record ComponentTypeRegistrationRequest(ApplicationIdentifier ApplicationId, string QualifiedTypeId, string SchemaJson);
public sealed record ComponentTypeAdministrationContext(string RequestToken, string? ExpectedSchemaHash, string Intent, IReadOnlyList<string> ProceduresUsed, AuthorizationAuditEvidence AuthorizationEvidence);
public sealed record ComponentTypeRegistrationPreview(RegisteredComponentTypeVersion ComponentType, string Outcome, string OperationId);
public sealed record ComponentTypeRegistrationReceipt(RegisteredComponentTypeVersion ComponentType, string Outcome, string OperationId);
public sealed class ComponentTypeAdministrationException(string code, string message) : Exception(message) { public string Code { get; } = code; }
public interface IComponentTypeAdministrationService
{
    Task<ComponentTypeRegistrationPreview> PreviewAsync(ComponentTypeRegistrationRequest request, ComponentTypeAdministrationContext context, CancellationToken cancellationToken = default);
    Task<ComponentTypeRegistrationReceipt> RegisterAsync(ComponentTypeRegistrationRequest request, ComponentTypeAdministrationContext context, CancellationToken cancellationToken = default);
}
