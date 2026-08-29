using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ComponentTypeAdministration;

public sealed class ComponentTypeAdministrationService(DantesRoleplayDbContext db, IApplicationComponentTypeRegistry types, IBoundedJsonSchemaValidator schemas, IOperationLog operations) : IComponentTypeAdministrationService
{
    private const string Kind = "system.component-type.register";
    public async Task<ComponentTypeRegistrationPreview> PreviewAsync(ComponentTypeRegistrationRequest request, ComponentTypeAdministrationContext context, CancellationToken cancellationToken = default)
    {
        ValidateContext(context); var fingerprint = Fingerprint(request, context.ExpectedSchemaHash); var replay = Replay(request, context, fingerprint);
        if (replay is not null) return await Preview(context, fingerprint, replay.ComponentType, replay.Outcome, cancellationToken);
        var (type, outcome) = Validate(request, context.ExpectedSchemaHash);
        return await Preview(context, fingerprint, type, outcome == "registered" ? "would-register" : outcome, cancellationToken);
    }
    public async Task<ComponentTypeRegistrationReceipt> RegisterAsync(ComponentTypeRegistrationRequest request, ComponentTypeAdministrationContext context, CancellationToken cancellationToken = default)
    {
        ValidateContext(context); var fingerprint = Fingerprint(request, context.ExpectedSchemaHash); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try { var replay = Replay(request, context, fingerprint); if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }
            RequirePreview(context.RequestToken, fingerprint); var (derived, outcome) = Validate(request, context.ExpectedSchemaHash);
            var persisted = types.Define(new(request.ApplicationId, request.QualifiedTypeId, request.SchemaJson));
            if (persisted.SchemaHash != derived.SchemaHash || persisted.Version != derived.Version) throw Invalid("REGISTRATION_STALE", "Derived component-type evidence changed after dry run.");
            await operations.RecordAsync("commit", $"Registered component type '{persisted.QualifiedId}' version {persisted.Version}.", true,
                context.Intent, Subject(fingerprint), context.ProceduresUsed, consumesReadEvidence: true,
                cancellationToken: cancellationToken, guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence), id: context.RequestToken);
            await transaction.CommitAsync(cancellationToken); return new(persisted, outcome, context.RequestToken); }
        catch { await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
    private (RegisteredComponentTypeVersion Type, string Outcome) Validate(ComponentTypeRegistrationRequest request, string? expected)
    {
        ArgumentNullException.ThrowIfNull(request); try { ComponentTypeIdentifier.Validate(request.ApplicationId, request.QualifiedTypeId); } catch (ArgumentException e) { throw Invalid("INVALID_COMPONENT_TYPE", e.Message); }
        var current = types.GetLatest(request.QualifiedTypeId); RequireExpected(expected, current?.SchemaHash);
        var compiled = schemas.Compile(request.SchemaJson); if (!compiled.IsAccepted) throw Invalid("SCHEMA_REJECTED", "The component schema is not accepted by the bounded schema profile.");
        if (current is not null && current.SchemaHash == compiled.SchemaHash) return (current, "unchanged");
        if (types.GetBySchemaHash(request.QualifiedTypeId, compiled.ProfileId, compiled.SchemaHash) is not null)
            throw Invalid("SCHEMA_RETIRED", "A prior component-type schema cannot replace the latest schema.");
        return (new(request.ApplicationId, request.QualifiedTypeId, (current?.Version ?? 0) + 1, compiled.ProfileId, compiled.NormalizedSchema, compiled.SchemaHash, DateTime.UtcNow), "registered");
    }
    private ComponentTypeRegistrationReceipt? Replay(ComponentTypeRegistrationRequest request, ComponentTypeAdministrationContext context, string fingerprint)
    {
        var operation = db.Operations.AsNoTracking().SingleOrDefault(value => value.Id == context.RequestToken); if (operation is null) return null;
        if (!operation.Success || operation.Tool != "commit" || operation.Subject != Subject(fingerprint)) throw Invalid("REQUEST_TOKEN_CONFLICT", "That requestToken was already used by a different operation or canonical request.");
        var compiled = schemas.Compile(request.SchemaJson); if (!compiled.IsAccepted) throw Invalid("REGISTRY_INCONSISTENT", "The prior component-type receipt no longer has an accepted schema.");
        var type = types.GetBySchemaHash(request.QualifiedTypeId, compiled.ProfileId, compiled.SchemaHash) ?? throw Invalid("REGISTRY_INCONSISTENT", "The prior component-type receipt has no immutable type version.");
        var outcome = context.ExpectedSchemaHash is null || type.SchemaHash != context.ExpectedSchemaHash
            ? "registered"
            : "unchanged";
        return new(type, outcome, operation.Id);
    }
    private async Task<ComponentTypeRegistrationPreview> Preview(ComponentTypeAdministrationContext context, string fingerprint, RegisteredComponentTypeVersion type, string outcome, CancellationToken ct)
    { var op = await operations.RecordAsync("commit", "Validated component-type registration without changing registry state.", true,
        context.Intent, $"preview|{Kind}|{context.RequestToken}|{fingerprint}", context.ProceduresUsed,
        consumesReadEvidence: false, cancellationToken: ct, guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence)); return new(type, outcome, op.Id); }
    private void RequirePreview(string token, string fingerprint) { if (!db.Operations.AsNoTracking().Any(value => value.Tool == "commit" && value.Success && value.Subject == $"preview|{Kind}|{token}|{fingerprint}")) throw Invalid("DRY_RUN_REQUIRED", "Commit the exact payload with dryRun: true before applying it."); }
    private static void RequireExpected(string? given, string? current) { if (!string.Equals(given, current, StringComparison.Ordinal)) throw Invalid("REGISTRY_STALE", current is null ? "The component type is absent but expectedSchemaHash did not expect absence." : "expectedSchemaHash does not match the latest component type schema."); }
    private static void ValidateContext(ComponentTypeAdministrationContext c) { if (c.RequestToken.Length != 32 || c.RequestToken.Any(x => !(char.IsAsciiDigit(x) || x is >= 'a' and <= 'f'))) throw Invalid("INVALID_PAYLOAD", "requestToken must contain exactly 32 lowercase hexadecimal characters."); if (c.ExpectedSchemaHash is not null && !Hash(c.ExpectedSchemaHash)) throw Invalid("INVALID_PAYLOAD", "expectedSchemaHash must be null or an uppercase SHA-256 fingerprint."); if (!c.AuthorizationEvidence.Allowed) throw Invalid("PRIVATE_OPERATOR_DENIED", "A successful authorization decision is required."); }
    private static string Fingerprint(ComponentTypeRegistrationRequest r, string? expected) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind = Kind, applicationId = r.ApplicationId.Value, r.QualifiedTypeId, r.SchemaJson, expectedSchemaHash = expected })));
    private static string Subject(string fingerprint) => $"{Kind}|{fingerprint}";
    private static bool Hash(string value) => value.Length == 64 && value.All(x => char.IsAsciiDigit(x) || x is >= 'A' and <= 'F');
    private static ComponentTypeAdministrationException Invalid(string c, string m) => new(c, m);
}
