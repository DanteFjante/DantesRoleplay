using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Capabilities;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.Projections;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Validates repository-authored application capability contracts without claiming ownership of
/// their mechanics or queries. Persisted migration-only records remain readable through the
/// compatibility adapter; new catalog source must be fully self-describing.
/// </summary>
public static class ApplicationCapabilityCatalogValidator
{
    public static IReadOnlyList<CatalogValidationIssue> Validate(string catalogRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        var applicationsRoot = Path.Combine(catalogRoot, "applications");
        if (!Directory.Exists(applicationsRoot)) return [];

        var issues = new List<CatalogValidationIssue>();
        foreach (var applicationDirectory in Directory.EnumerateDirectories(applicationsRoot)
                     .Order(StringComparer.Ordinal))
        {
            var applicationIdText = Path.GetFileName(applicationDirectory);
            ApplicationIdentifier applicationId;
            try { applicationId = ApplicationIdentifier.Parse(applicationIdText); }
            catch (ArgumentException exception)
            {
                issues.Add(Issue("application", applicationIdText, "capability-application-id", exception.Message));
                continue;
            }
            ValidateApplication(applicationId, applicationDirectory, issues);
        }
        return issues;
    }

    private static void ValidateApplication(
        ApplicationIdentifier applicationId,
        string applicationDirectory,
        List<CatalogValidationIssue> issues)
    {
        var schemas = new BoundedJsonSchemaValidator();
        var mechanics = new Dictionary<string, MechanicContract>(StringComparer.Ordinal);
        var mechanicsRoot = Path.Combine(applicationDirectory, "mechanics");
        if (Directory.Exists(mechanicsRoot))
        {
            foreach (var path in Directory.EnumerateFiles(mechanicsRoot, "*.md", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                try
                {
                    var sourcePath = Path.ChangeExtension(path, ".js");
                    if (!File.Exists(sourcePath))
                    {
                        issues.Add(Issue("mechanic", Path.GetFileNameWithoutExtension(path),
                            "capability-source", "The mechanic has no same-path JavaScript sidecar."));
                        continue;
                    }
                    var file = MechanicFile.Parse(File.ReadAllText(path), path, File.ReadAllText(sourcePath));
                    var content = ApplicationCatalogRecordContent.MechanicJson(file);
                    var qualifiedId = Qualify(applicationId, file.Id);
                    if (!mechanics.TryAdd(qualifiedId, new(file, content,
                            ApplicationCatalogRecordContent.Fingerprint(content))))
                    {
                        issues.Add(Issue("mechanic", qualifiedId, "capability-duplicate",
                            "The application contains more than one mechanic with this identity."));
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                    or JsonException or IOException or UnauthorizedAccessException)
                {
                    issues.Add(Issue("mechanic", Path.GetFileNameWithoutExtension(path),
                        "capability-parse", exception.Message));
                }
            }
        }

        var authoredBoundary = mechanics.Values.Any(value =>
            MechanicRequirements.Parse(value.File.Requirements).InputSchema is not null);
        foreach (var (qualifiedId, mechanic) in mechanics.OrderBy(value => value.Key, StringComparer.Ordinal))
            ValidateMechanic(applicationId, qualifiedId, mechanic, mechanics, schemas, issues, authoredBoundary);

        var objects = new Dictionary<(string QualifiedId, int Version), ProjectionDefinitionRequest>();
        var objectsRoot = Path.Combine(applicationDirectory, "objects");
        if (Directory.Exists(objectsRoot))
        {
            foreach (var path in Directory.EnumerateFiles(objectsRoot, "*.json", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                try
                {
                    var definition = ApplicationObjectDocument.Parse(File.ReadAllText(path), applicationId);
                    var version = definition.DeclaredVersion ?? 1;
                    if (!objects.TryAdd((definition.QualifiedId, version), definition))
                        issues.Add(Issue("object", definition.QualifiedId, "capability-duplicate",
                            "The application contains more than one object with this identity and version."));
                }
                catch (Exception exception) when (exception is ArgumentException or JsonException
                    or IOException or UnauthorizedAccessException)
                {
                    issues.Add(Issue("object", Path.GetFileNameWithoutExtension(path),
                        "capability-contract", exception.Message));
                }
            }
        }

        foreach (var history in objects.GroupBy(value => value.Key.QualifiedId, StringComparer.Ordinal))
        {
            var versions = history.Select(value => value.Key.Version).Order().ToArray();
            if (!versions.SequenceEqual(Enumerable.Range(1, versions.Length)))
                issues.Add(Issue("object", history.Key, "capability-version-history",
                    "Application object versions must form one contiguous history beginning at version one."));
        }

        var queriesRoot = Path.Combine(applicationDirectory, "queries");
        if (!Directory.Exists(queriesRoot)) return;
        foreach (var path in Directory.EnumerateFiles(queriesRoot, "*.json", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            try
            {
                var query = ApplicationQueryContract.Parse(File.ReadAllText(path), applicationId);
                var content = ApplicationCatalogRecordContent.QueryJson(query);
                var qualifiedId = Qualify(applicationId, query.Id);
                var projectionId = Qualify(applicationId, query.ProjectionQualifiedId);
                if (query.Executor == ApplicationQueryContract.ObjectProjectionExecutor)
                {
                    if (!objects.TryGetValue((projectionId, query.ProjectionVersion), out var definition))
                        issues.Add(Issue("query", qualifiedId, "capability-object-missing",
                            $"The query references unavailable object '{projectionId}' version {query.ProjectionVersion}."));
                    else if (definition.ObjectContract?.Collections.All(value =>
                                 value.CollectionId != query.ObjectCollectionId) != false)
                        issues.Add(Issue("query", qualifiedId, "capability-object-collection",
                            $"The query references unavailable object collection '{query.ObjectCollectionId}'."));
                }
                else if (query.Executor == ApplicationQueryContract.MechanicProjectionExecutor)
                {
                    if (!mechanics.TryGetValue(projectionId, out var projection))
                        issues.Add(Issue("query", qualifiedId, "capability-projection-missing",
                            $"The query projects unavailable mechanic '{projectionId}'."));
                    else if (query.ProjectionVersion != 1 || !string.Equals(query.ProjectionContentHash,
                                 projection.Fingerprint, StringComparison.Ordinal))
                        issues.Add(Issue("query", qualifiedId, "capability-projection-stale",
                            $"The query must pin projection version 1 and fingerprint {projection.Fingerprint}."));
                }
                var record = Record(applicationId, ApplicationQueryContract.CatalogKind, qualifiedId,
                    query.Name, query.Description, query.Status, content, path);
                ValidateDescriptor(ApplicationCapabilityContractAdapter.Create(applicationId, record), schemas,
                    "query", qualifiedId, issues);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                or JsonException or IOException or UnauthorizedAccessException)
            {
                issues.Add(Issue("query", Path.GetFileNameWithoutExtension(path),
                    "capability-contract", exception.Message));
            }
        }
    }

    private static void ValidateMechanic(
        ApplicationIdentifier applicationId,
        string qualifiedId,
        MechanicContract mechanic,
        IReadOnlyDictionary<string, MechanicContract> mechanics,
        BoundedJsonSchemaValidator schemas,
        List<CatalogValidationIssue> issues,
        bool authoredBoundary)
    {
        var requirements = MechanicRequirements.Parse(mechanic.File.Requirements);
        if (requirements.InputSchema is not JsonElement inputSchema)
        {
            issues.Add(new CatalogValidationIssue("mechanic", qualifiedId, "capability-input-schema",
                authoredBoundary
                    ? "An authored application mechanic must declare inputSchema."
                    : "This unchanged legacy application has not crossed the authored capability-contract boundary.",
                Warning: !authoredBoundary));
            return;
        }
        if (!ClosedObject(inputSchema))
            issues.Add(Issue("mechanic", qualifiedId, "capability-input-closed",
                "The authored input schema must reject unknown top-level properties."));

        foreach (var (resultKey, child) in requirements.Children.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var childId = Qualify(applicationId, child.MechanicId);
            if (!mechanics.TryGetValue(childId, out var target))
            {
                issues.Add(Issue("mechanic", qualifiedId, "capability-child-missing",
                    $"Child '{resultKey}' targets unavailable mechanic '{childId}'."));
                continue;
            }
            if (child.MechanicVersion != 1 || !string.Equals(child.ContentFingerprint,
                    target.Fingerprint, StringComparison.Ordinal))
                issues.Add(Issue("mechanic", qualifiedId, "capability-child-stale",
                    $"Child '{resultKey}' must pin version 1 and fingerprint {target.Fingerprint}."));
        }

        try
        {
            var record = Record(applicationId, "mechanic", qualifiedId, mechanic.File.Name,
                mechanic.File.Description, mechanic.File.Status.ToString().ToLowerInvariant(),
                mechanic.ContentJson, mechanic.File.Id + ".md");
            ValidateDescriptor(ApplicationCapabilityContractAdapter.Create(applicationId, record), schemas,
                "mechanic", qualifiedId, issues);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            issues.Add(Issue("mechanic", qualifiedId, "capability-contract", exception.Message));
        }
    }

    private static void ValidateDescriptor(
        CapabilityContractDescriptor descriptor,
        BoundedJsonSchemaValidator schemas,
        string kind,
        string id,
        List<CatalogValidationIssue> issues)
    {
        foreach (var problem in CapabilityContractConformanceValidator.FindProblems(descriptor, schemas))
            issues.Add(Issue(kind, id, "capability-contract-conformance", problem));
    }

    private static bool ClosedObject(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return false;
        if (schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False) return true;
        if (!schema.TryGetProperty("anyOf", out var alternatives)
            || alternatives.ValueKind != JsonValueKind.Array || alternatives.GetArrayLength() == 0) return false;
        return alternatives.EnumerateArray().All(ClosedObject);
    }

    private static CatalogRecordDefinition Record(
        ApplicationIdentifier applicationId,
        string kind,
        string qualifiedId,
        string name,
        string description,
        string status,
        string content,
        string path) => new(applicationId.Value, kind, qualifiedId, name, Summary(description), [], [], kind + "s",
            status, 1, content, ApplicationCatalogRecordContent.Fingerprint(content),
            applicationId.Value + "-catalog", path);

    private static string Qualify(ApplicationIdentifier applicationId, string id) =>
        id.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? id : applicationId.Value + "." + id;

    private static string Summary(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static CatalogValidationIssue Issue(string kind, string id, string check, string detail) =>
        new(kind, id, check, detail, Warning: false);

    private sealed record MechanicContract(MechanicFile File, string ContentJson, string Fingerprint);
}
