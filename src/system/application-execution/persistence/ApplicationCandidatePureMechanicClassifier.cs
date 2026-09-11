using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationExecution;

internal enum ApplicationCandidatePureMechanicOutcome { Supported, Invalid, Unavailable }

internal sealed class ApplicationCandidatePureMechanicClassification
{
    private ApplicationCandidatePureMechanicClassification(
        ApplicationCandidatePureMechanicOutcome outcome,
        ApplicationCandidatePureMechanicPlan? plan,
        ApplicationCandidateDiagnostic? diagnostic)
    {
        Outcome = outcome;
        Plan = plan;
        Diagnostic = diagnostic;
    }

    internal ApplicationCandidatePureMechanicOutcome Outcome { get; }
    internal ApplicationCandidatePureMechanicPlan? Plan { get; }
    internal ApplicationCandidateDiagnostic? Diagnostic { get; }

    internal static ApplicationCandidatePureMechanicClassification Supported(
        ApplicationCandidatePureMechanicPlan plan) => new(
        ApplicationCandidatePureMechanicOutcome.Supported, plan, null);

    internal static ApplicationCandidatePureMechanicClassification Rejected(
        ApplicationCandidatePureMechanicOutcome outcome,
        ApplicationCandidateDiagnostic diagnostic) => new(outcome, null, diagnostic);
}

internal sealed class ApplicationCandidatePureMechanicPlan
{
    private ApplicationCandidatePureMechanicPlan(
        StandingGrantDefinitionReference definition,
        string source,
        string? normalizedInputSchema,
        string? inputSchemaHash,
        string classificationFingerprint)
    {
        Definition = definition;
        Source = source;
        NormalizedInputSchema = normalizedInputSchema;
        InputSchemaHash = inputSchemaHash;
        ClassificationFingerprint = classificationFingerprint;
    }

    internal StandingGrantDefinitionReference Definition { get; }
    internal string Source { get; }
    internal string? NormalizedInputSchema { get; }
    internal string? InputSchemaHash { get; }
    internal string ClassificationFingerprint { get; }

    internal static ApplicationCandidatePureMechanicPlan Create(
        StandingGrantDefinitionReference definition,
        string source,
        string? normalizedInputSchema,
        string? inputSchemaHash)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            definition = new
            {
                id = definition.DefinitionId,
                kind = definition.Kind,
                version = definition.Revision,
                fingerprint = definition.ContentFingerprint
            },
            sourceFingerprint = ApplicationCatalogRecordContent.Fingerprint(source),
            inputSchemaHash
        }));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-pure-mechanic-classification/v1", canonical);
        return new(definition, source, normalizedInputSchema, inputSchemaHash, fingerprint);
    }
}

/// <summary>
/// Classifies only the initial state-free mechanic subset from an exact normalized catalog record.
/// This does not parse JavaScript, prove dependency closure, validate a complete candidate, run an
/// engine, grant authority, or create durable evidence.
/// </summary>
internal sealed class ApplicationCandidatePureMechanicClassifier(IBoundedJsonSchemaValidator schemas)
{
    internal ApplicationCandidatePureMechanicClassification Classify(CatalogRecordView exactNormalizedRecord)
    {
        ArgumentNullException.ThrowIfNull(exactNormalizedRecord);
        var summary = exactNormalizedRecord.Summary;
        var target = summary?.QualifiedId ?? "mechanic";
        if (summary is null)
            return Invalid(target, "PURE_MECHANIC_RECORD_INVALID", "The normalized mechanic record is absent.");
        if (summary.Kind != "mechanic")
            return Unavailable(target, "PURE_MECHANIC_KIND_UNAVAILABLE",
                "Only normalized mechanic records can use pure mechanic classification.");

        try
        {
            var contentJson = exactNormalizedRecord.ContentJson;
            if (summary.Version < 1 || string.IsNullOrWhiteSpace(contentJson)
                || contentJson.Length > CatalogNavigationLimits.MaximumContentLength
                || !string.Equals(ApplicationCatalogRecordContent.Fingerprint(contentJson),
                    summary.ContentFingerprint, StringComparison.Ordinal))
                return Invalid(target, "PURE_MECHANIC_RECORD_INVALID",
                    "The exact mechanic identity or content fingerprint is invalid.");
            var application = ApplicationIdentifier.Parse(summary.Collection);
            CatalogNamespaceIdentity.ValidateRecordId(summary.QualifiedId);
            if (!summary.QualifiedId.StartsWith(application.Value + ".", StringComparison.Ordinal))
                return Invalid(target, "PURE_MECHANIC_RECORD_INVALID",
                    "The exact mechanic identity is outside its application namespace.");

            using var content = JsonDocument.Parse(contentJson,
                new JsonDocumentOptions { MaxDepth = InteractionContractLimits.JsonDepth });
            RejectDuplicateProperties(content.RootElement, caseInsensitive: false);
            var file = ReadNormalizedMechanic(content.RootElement);
            var qualifiedId = file.Id.StartsWith(application.Value + ".", StringComparison.Ordinal)
                ? file.Id
                : application.Value + "." + file.Id;
            if (qualifiedId != summary.QualifiedId || file.Status.ToString().ToLowerInvariant() != summary.Status
                || ApplicationCatalogRecordContent.MechanicJson(file) != contentJson)
                return Invalid(target, "PURE_MECHANIC_RECORD_INVALID",
                    "The mechanic record is not the exact normalized owner representation.");

            using var requirementsDocument = JsonDocument.Parse(file.Requirements,
                new JsonDocumentOptions { MaxDepth = InteractionContractLimits.JsonDepth });
            RejectDuplicateProperties(requirementsDocument.RootElement, caseInsensitive: true);
            if (requirementsDocument.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid(target, "PURE_MECHANIC_REQUIREMENTS_INVALID",
                    "Mechanic requirements must be a JSON object.");
            var fields = requirementsDocument.RootElement.EnumerateObject().ToArray();
            if (fields.Length > 1 || fields.Length == 1
                && !fields[0].Name.Equals("inputSchema", StringComparison.OrdinalIgnoreCase))
                return Unavailable(target, "PURE_MECHANIC_REQUIREMENTS_UNAVAILABLE",
                    "The mechanic declares requirements outside the initial pure subset.");
            MechanicRequirements requirements;
            try { requirements = MechanicRequirements.Parse(file.Requirements); }
            catch (JsonException)
            {
                return Invalid(target, "PURE_MECHANIC_REQUIREMENTS_INVALID",
                    "Mechanic requirements are malformed.");
            }
            string? normalizedSchema = null;
            string? schemaHash = null;
            if (fields.Length == 1)
            {
                if (requirements.InputSchema is not { } inputSchema)
                    return Invalid(target, "PURE_MECHANIC_INPUT_SCHEMA_INVALID",
                        "The mechanic input schema is malformed.");
                var compiled = schemas.Compile(inputSchema.GetRawText());
                if (!compiled.IsAccepted)
                    return Invalid(target, "PURE_MECHANIC_INPUT_SCHEMA_INVALID",
                        "The mechanic input schema is not accepted by the bounded schema owner.");
                normalizedSchema = compiled.NormalizedSchema;
                schemaHash = compiled.SchemaHash;
            }

            var definition = new StandingGrantDefinitionReference(
                summary.QualifiedId, "mechanic", summary.Version, summary.ContentFingerprint);
            return ApplicationCandidatePureMechanicClassification.Supported(
                ApplicationCandidatePureMechanicPlan.Create(
                    definition, file.Source, normalizedSchema, schemaHash));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or ArgumentException or CryptographicException)
        {
            return Invalid(target, "PURE_MECHANIC_RECORD_INVALID", "The normalized mechanic record is malformed.");
        }
    }

    private static MechanicFile ReadNormalizedMechanic(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        var fields = root.EnumerateObject().ToArray();
        var expected = new[]
        {
            "id", "category", "name", "description", "matches", "requirements", "source", "scope", "status"
        };
        if (fields.Length != expected.Length || fields.Select(field => field.Name)
                .Except(expected, StringComparer.Ordinal).Any()) throw new JsonException();
        var statusText = Text(root, "status");
        if (!Enum.TryParse<MechanicStatus>(statusText, true, out var status)
            || !Enum.IsDefined(status)
            || status.ToString().ToLowerInvariant() != statusText) throw new JsonException();
        var source = Text(root, "source");
        if (string.IsNullOrWhiteSpace(source)) throw new JsonException();
        return new(Text(root, "id"), Text(root, "category"), Text(root, "name"),
            Text(root, "description"), Text(root, "matches"), Text(root, "requirements"),
            source, Text(root, "scope"), status);
    }

    private static string Text(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new JsonException();
    }

    private static void RejectDuplicateProperties(JsonElement value, bool caseInsensitive)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(caseInsensitive
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateProperties(property.Value, caseInsensitive: false);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                RejectDuplicateProperties(item, caseInsensitive: false);
    }

    private static ApplicationCandidatePureMechanicClassification Invalid(
        string target, string code, string message) =>
        ApplicationCandidatePureMechanicClassification.Rejected(
            ApplicationCandidatePureMechanicOutcome.Invalid, new(code, target, message));

    private static ApplicationCandidatePureMechanicClassification Unavailable(
        string target, string code, string message) =>
        ApplicationCandidatePureMechanicClassification.Rejected(
            ApplicationCandidatePureMechanicOutcome.Unavailable, new(code, target, message));
}
