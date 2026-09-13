using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Ecs;

/// <summary>
/// Restores one exact current component contract into a fresh installation without inventing
/// predecessor versions. Ordinary runtime registration remains append-only through
/// <see cref="IApplicationComponentTypeRegistry"/>.
/// </summary>
public interface IFreshComponentTypeBaselineInstaller
{
    RegisteredComponentTypeVersion Restore(
        ComponentTypeDefinition definition,
        int declaredVersion,
        string expectedSchemaHash);
}

public sealed class SqliteFreshComponentTypeBaselineInstaller(
    DantesRoleplayDbContext db,
    IBoundedJsonSchemaValidator validator) : IFreshComponentTypeBaselineInstaller
{
    public RegisteredComponentTypeVersion Restore(
        ComponentTypeDefinition definition,
        int declaredVersion,
        string expectedSchemaHash)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ComponentTypeIdentifier.Validate(definition.Owner, definition.QualifiedId);
        if (declaredVersion < 1) throw new ArgumentOutOfRangeException(nameof(declaredVersion));
        if (expectedSchemaHash is not { Length: 64 }
            || expectedSchemaHash.Any(value => !Uri.IsHexDigit(value))
            || expectedSchemaHash != expectedSchemaHash.ToUpperInvariant())
            throw new ArgumentException("The expected schema hash must be an uppercase SHA-256 fingerprint.",
                nameof(expectedSchemaHash));

        var compilation = validator.Compile(definition.SchemaJson);
        if (!compilation.IsAccepted)
            throw new ArgumentException(
                "The component schema is not accepted by the bounded schema profile: " +
                string.Join("; ", compilation.Diagnostics.Select(value =>
                    $"{value.Code} {value.Pointer}: {value.Message}")),
                nameof(definition));
        EcsComponentRolePolicyParser.Parse(compilation.NormalizedSchema);
        if (compilation.SchemaHash != expectedSchemaHash)
            throw new ArgumentException("The component schema does not match its expected canonical fingerprint.",
                nameof(expectedSchemaHash));

        var ownsTransaction = db.Database.CurrentTransaction is null;
        using var transaction = ownsTransaction ? db.Database.BeginTransaction() : null;
        if (!definition.Owner.IsSystem
            && !db.Set<ApplicationRegistryRecord>().Any(value => value.Id == definition.Owner.Value))
            throw new ArgumentException("A component type can only be restored for an existing application.",
                nameof(definition));

        var existingType = db.Set<ComponentTypeRecord>()
            .SingleOrDefault(value => value.QualifiedId == definition.QualifiedId);
        var existingVersions = db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Where(value => value.QualifiedId == definition.QualifiedId)
            .OrderBy(value => value.Version)
            .ToArray();
        if (existingType is not null || existingVersions.Length != 0)
        {
            var replay = existingType is { DisabledAtUtc: null }
                && existingType.ApplicationId == definition.Owner.Value
                && existingVersions is [var only]
                && only.Version == declaredVersion
                && only.ProfileId == compilation.ProfileId
                && only.SchemaJson == compilation.NormalizedSchema
                && only.SchemaHash == compilation.SchemaHash
                    ? only
                    : null;
            if (replay is null)
                throw new InvalidOperationException(
                    "A fresh component baseline cannot replace or extend an existing component type.");
            if (ownsTransaction) transaction!.Commit();
            return ToContract(replay, definition.Owner);
        }

        var now = DateTime.UtcNow;
        db.Add(new ComponentTypeRecord
        {
            QualifiedId = definition.QualifiedId,
            ApplicationId = definition.Owner.Value,
            CreatedAtUtc = now
        });
        var version = new ComponentTypeVersionRecord
        {
            QualifiedId = definition.QualifiedId,
            Version = declaredVersion,
            ProfileId = compilation.ProfileId,
            SchemaJson = compilation.NormalizedSchema,
            SchemaHash = compilation.SchemaHash,
            CreatedAtUtc = now
        };
        db.Add(version);
        db.SaveChanges();
        if (ownsTransaction) transaction!.Commit();
        return ToContract(version, definition.Owner);
    }

    private static RegisteredComponentTypeVersion ToContract(
        ComponentTypeVersionRecord row,
        ApplicationIdentifier owner) =>
        new(owner, row.QualifiedId, row.Version, row.ProfileId,
            row.SchemaJson, row.SchemaHash, row.CreatedAtUtc);
}
