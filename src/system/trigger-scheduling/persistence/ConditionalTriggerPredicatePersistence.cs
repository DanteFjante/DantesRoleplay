using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.TriggerScheduling;

internal static class ConditionalTriggerPredicatePersistence
{
    private const string BindingDomain = "dantes-roleplay/conditional-observer-predicate-binding/v1";
    private const string RoleDomain = BindingDomain + "/roles";
    private const string CaptureDomain = "dantes-roleplay/conditional-observer-predicate-capture/v1";

    internal static ConditionalTriggerPredicateBindingRecord Create(ConditionalTriggerDefinition definition)
    {
        var predicate = definition.Predicate ?? throw new InvalidDataException("A workflow observer is missing its predicate.");
        var selection = predicate.Selection;
        if (selection.Mechanic.Kind != "mechanic") throw Invalid("CONDITIONAL_PREDICATE_KIND");
        Upper(selection.Mechanic.ContentFingerprint);
        Upper(selection.CatalogOrigin.ActivationFingerprint);
        Upper(selection.CatalogOrigin.ApplicationFingerprint);
        Upper(selection.SourceRegistrationFingerprint);
        var requirements = InteractionCanonicalJson.CanonicalizeObject(selection.CanonicalRequirementsJson);
        if (PredicateProof.Requirements(requirements) != selection.RequirementsFingerprint)
            throw Invalid("CONDITIONAL_PREDICATE_REQUIREMENTS");
        var roles = CanonicalRoles(selection.RoleEntityIds);
        var roleHash = InteractionCanonicalJson.Fingerprint(RoleDomain, roles);
        return new ConditionalTriggerPredicateBindingRecord
        {
            ApplicationId = definition.ApplicationId.Value,
            TriggerId = definition.Id,
            TriggerVersion = definition.Version,
            MechanicId = selection.Mechanic.DefinitionId,
            MechanicVersion = selection.Mechanic.Revision,
            MechanicFingerprint = selection.Mechanic.ContentFingerprint,
            ActivationRevision = selection.CatalogOrigin.ActivationRevision,
            ActivationFingerprint = selection.CatalogOrigin.ActivationFingerprint,
            ActivationApplicationRevision = selection.CatalogOrigin.ApplicationRevision,
            ActivationApplicationFingerprint = selection.CatalogOrigin.ApplicationFingerprint,
            SourceRegistrationFingerprint = selection.SourceRegistrationFingerprint,
            RequirementsJson = requirements,
            RequirementsFingerprint = selection.RequirementsFingerprint,
            RoleEntityIdsJson = roles,
            RoleEntityIdsFingerprint = roleHash,
            Coalescing = "per-operation",
            MaximumOperationsPerFire = predicate.MaximumOperationsPerFire,
            BindingFingerprint = BindingFingerprint(selection, requirements, roles, roleHash,
                predicate.MaximumOperationsPerFire)
        };
    }

    internal static ConditionalTriggerObserverPredicate Materialize(ConditionalTriggerPredicateBindingRecord row)
    {
        Upper(row.MechanicFingerprint);
        Upper(row.ActivationFingerprint);
        Upper(row.ActivationApplicationFingerprint);
        Upper(row.SourceRegistrationFingerprint);
        Upper(row.RequirementsFingerprint);
        Upper(row.RoleEntityIdsFingerprint);
        Upper(row.BindingFingerprint);
        var requirements = InteractionCanonicalJson.CanonicalizeObject(row.RequirementsJson);
        var rolesJson = InteractionCanonicalJson.CanonicalizeObject(row.RoleEntityIdsJson);
        if (PredicateProof.Requirements(requirements) != row.RequirementsFingerprint ||
            InteractionCanonicalJson.Fingerprint(RoleDomain, rolesJson) != row.RoleEntityIdsFingerprint ||
            row.Coalescing != "per-operation" || row.MaximumOperationsPerFire is < 1 or > 16)
            throw Invalid("CONDITIONAL_PREDICATE_BINDING");
        var roles = JsonSerializer.Deserialize<Dictionary<string, string>>(rolesJson) ?? throw Invalid("CONDITIONAL_PREDICATE_ROLES");
        var selection = new ApplicationObserverPredicateSelection(
            ApplicationIdentifier.Parse(row.ApplicationId),
            new StandingGrantDefinitionReference(row.MechanicId, "mechanic", row.MechanicVersion, row.MechanicFingerprint),
            new StandingGrantActivationOrigin(row.ActivationRevision, row.ActivationFingerprint,
                row.ActivationApplicationRevision, row.ActivationApplicationFingerprint),
            row.SourceRegistrationFingerprint, requirements, row.RequirementsFingerprint, roles);
        if (BindingFingerprint(selection, requirements, rolesJson,
                row.RoleEntityIdsFingerprint, row.MaximumOperationsPerFire) != row.BindingFingerprint)
            throw Invalid("CONDITIONAL_PREDICATE_BINDING");
        return ConditionalTriggerObserverPredicate.Create(selection, row.MaximumOperationsPerFire);
    }

    internal static bool SameSelection(ConditionalTriggerPredicateBindingRecord row,
        ApplicationObserverPredicateSelection selection)
    {
        var requirements = InteractionCanonicalJson.CanonicalizeObject(selection.CanonicalRequirementsJson);
        var roles = CanonicalRoles(selection.RoleEntityIds);
        var roleHash = InteractionCanonicalJson.Fingerprint(RoleDomain, roles);
        return BindingFingerprint(selection, requirements, roles, roleHash,
            row.MaximumOperationsPerFire) == row.BindingFingerprint;
    }

    internal static string SerializeCapture(ApplicationObserverPredicateCapturedInput captured)
    {
        Upper(captured.MappingFingerprint);
        Upper(captured.InputFingerprint);
        Upper(captured.ProjectionFingerprint);
        Upper(captured.CaptureFingerprint);
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new CaptureWire(
            SelectionWire.From(captured.Selection), captured.Projection, captured.MappingFingerprint,
            captured.InputFingerprint, captured.ProjectionFingerprint, captured.CaptureFingerprint)));
    }

    internal static ApplicationObserverPredicateCapturedInput DeserializeCapture(
        string json, string expectedFingerprint)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        if (EnvelopeFingerprint(canonical) != expectedFingerprint)
            throw Invalid("CONDITIONAL_PREDICATE_CAPTURE");
        var wire = JsonSerializer.Deserialize<CaptureWire>(canonical) ?? throw Invalid("CONDITIONAL_PREDICATE_CAPTURE");
        Upper(wire.MappingFingerprint);
        Upper(wire.InputFingerprint);
        Upper(wire.ProjectionFingerprint);
        Upper(wire.CaptureFingerprint);
        var selection = wire.Selection.ToSelection();
        var requirements = InteractionCanonicalJson.CanonicalizeObject(selection.CanonicalRequirementsJson);
        if (PredicateProof.Requirements(requirements) != selection.RequirementsFingerprint)
            throw Invalid("CONDITIONAL_PREDICATE_CAPTURE");
        _ = CanonicalRoles(selection.RoleEntityIds);
        return new(selection, wire.Projection, wire.MappingFingerprint,
            wire.InputFingerprint, wire.ProjectionFingerprint, wire.CaptureFingerprint);
    }

    internal static string EnvelopeFingerprint(string canonicalCapture) =>
        InteractionCanonicalJson.Fingerprint(CaptureDomain + "/envelope",
            InteractionCanonicalJson.CanonicalizeObject(canonicalCapture));

    private static string CanonicalRoles(IReadOnlyDictionary<string, string> roles)
    {
        if (roles.Count > 32 || roles.Any(value => string.IsNullOrWhiteSpace(value.Key) ||
            value.Key.Length > 100 || string.IsNullOrWhiteSpace(value.Value) || value.Value.Length > 200))
            throw Invalid("CONDITIONAL_PREDICATE_ROLES");
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(
            roles.OrderBy(value => value.Key, StringComparer.Ordinal).ToDictionary(value => value.Key,
                value => value.Value, StringComparer.Ordinal)));
    }

    private static string BindingFingerprint(ApplicationObserverPredicateSelection selection,
        string requirements, string roles, string roleHash, int maximumOperationsPerFire)
    {
        var binding = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            applicationId = selection.ApplicationId.Value,
            selection.Mechanic.DefinitionId,
            selection.Mechanic.Kind,
            selection.Mechanic.Revision,
            selection.Mechanic.ContentFingerprint,
            selection.CatalogOrigin.ActivationRevision,
            selection.CatalogOrigin.ActivationFingerprint,
            selection.CatalogOrigin.ApplicationRevision,
            selection.CatalogOrigin.ApplicationFingerprint,
            selection.SourceRegistrationFingerprint,
            requirements,
            selection.RequirementsFingerprint,
            roles,
            roleHash,
            coalescing = "per-operation",
            maximumOperationsPerFire
        }));
        return InteractionCanonicalJson.Fingerprint(BindingDomain, binding);
    }

    private static void Upper(string value)
    {
        if (value is not { Length: 64 } || value.Any(c => !(char.IsAsciiDigit(c) || c is >= 'A' and <= 'F')))
            throw Invalid("CONDITIONAL_PREDICATE_FINGERPRINT");
    }

    private static TriggerSchedulingContractException Invalid(string code) =>
        new(code, "The retained observer predicate evidence is invalid.");

    private sealed record CaptureWire(SelectionWire Selection, MechanicProjection Projection,
        string MappingFingerprint, string InputFingerprint, string ProjectionFingerprint,
        string CaptureFingerprint);

    private sealed record SelectionWire(string ApplicationId, string MechanicId, int MechanicVersion,
        string MechanicFingerprint, int ActivationRevision, string ActivationFingerprint,
        int ActivationApplicationRevision, string ActivationApplicationFingerprint,
        string SourceRegistrationFingerprint, string RequirementsJson, string RequirementsFingerprint,
        Dictionary<string, string> RoleEntityIds)
    {
        internal static SelectionWire From(ApplicationObserverPredicateSelection value) => new(
            value.ApplicationId.Value, value.Mechanic.DefinitionId, value.Mechanic.Revision,
            value.Mechanic.ContentFingerprint, value.CatalogOrigin.ActivationRevision,
            value.CatalogOrigin.ActivationFingerprint, value.CatalogOrigin.ApplicationRevision,
            value.CatalogOrigin.ApplicationFingerprint, value.SourceRegistrationFingerprint,
            value.CanonicalRequirementsJson, value.RequirementsFingerprint,
            value.RoleEntityIds.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        internal ApplicationObserverPredicateSelection ToSelection() => new(
            DantesRoleplay.Applications.ApplicationIdentifier.Parse(ApplicationId),
            new(MechanicId, "mechanic", MechanicVersion, MechanicFingerprint),
            new(ActivationRevision, ActivationFingerprint, ActivationApplicationRevision,
                ActivationApplicationFingerprint), SourceRegistrationFingerprint,
            RequirementsJson, RequirementsFingerprint, RoleEntityIds);
    }
}
