using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Sources;

namespace DantesRoleplay.CatalogNavigation;

public sealed class ApplicationCatalogMaterializationException : Exception
{
    public ApplicationCatalogMaterializationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

/// <summary>
/// Converts exact active procedure/mechanic/query/entity source winners into the generic immutable navigation
/// model. It contains no application IDs and never executes application content.
/// </summary>
public sealed class ActivatedApplicationCatalogMaterializer(
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    ISourceRegistry sources,
    IAllowedSourceRootResolver allowedRoots,
    IApplicationExtensionRegistry? extensions = null)
{
    private const string MaterializerVersion = "activated-application-catalog-v2";
    private const string SearchSortVersion = "catalog-lexical-v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public CatalogNavigationManifest Build(ApplicationIdentifier applicationId) =>
        BuildFeatureSnapshot(applicationId).Manifest;

    public ActiveCatalogFeatureSnapshot BuildFeatureSnapshot(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var application = applications.Describe(applicationId)
            ?? throw Failure("APPLICATION_UNKNOWN", "The application registration is unavailable.");
        if (string.IsNullOrWhiteSpace(application.DisplayName) || string.IsNullOrWhiteSpace(application.Description))
            throw Failure("APPLICATION_METADATA_INCOMPLETE", "Published catalogs require authored application metadata.");
        var activation = activations.Current(applicationId)
            ?? throw Failure("APPLICATION_INACTIVE", "The application has no active source manifest.");
        if (activation.Winners.Count > CatalogNavigationLimits.MaximumRecords * 4)
            throw Failure("CATALOG_DOCUMENT_LIMIT", "The active source manifest is too large to materialize.");

        var registrations = sources.For(applicationId).ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        var extensionRegistrations = (extensions ?? new EmptyApplicationExtensionRegistry()).For(applicationId)
            .ToDictionary(value => value.ExtensionId, StringComparer.Ordinal);
        foreach (var retained in activation.Sources)
        {
            if (!registrations.TryGetValue(retained.SourceId, out var current)
                || SourceRegistrationFingerprint.Compute(current) != retained.RegistrationFingerprint)
                throw Failure("SOURCE_REGISTRATION_DRIFT", "An active source registration no longer matches its retained evidence.");
        }

        var winners = activation.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var records = new List<CatalogRecordDefinition>();
        foreach (var winner in activation.Winners.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            if (!TryRecordKind(winner.RelativePath, out var kind)) continue;
            var sourceText = ReadText(winner, registrations);
            try
            {
                records.Add(kind switch
                {
                    "procedure" => ProcedureRecord(applicationId, applicationId.Value, winner,
                        ProcedureFile.Parse(sourceText, winner.RelativePath)),
                    "query" => QueryRecord(applicationId, applicationId.Value, winner,
                        ApplicationQueryContract.Parse(sourceText, applicationId)),
                    "entity" => EntityRecord(applicationId, applicationId.Value, winner, sourceText),
                    _ => MechanicRecord(applicationId, applicationId.Value, winner, sourceText, winners, registrations)
                });
            }
            catch (ApplicationCatalogMaterializationException) { throw; }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
            {
                throw Failure("CATALOG_RECORD_INVALID", "An active catalog record could not be parsed or adapted.", exception);
            }
        }
        if (records.Count == 0)
            throw Failure("CATALOG_RECORDS_UNAVAILABLE", "The active application contains no supported public catalog records.");

        var collection = applicationId.Value;
        var nodes = Nodes(collection, application.DisplayName, application.Description, records);
        var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            activation.ActivationFingerprint,
            materializer = MaterializerVersion
        }));
        try
        {
            var manifest = CatalogNavigationManifest.Create(applicationId, fingerprint, SearchSortVersion,
                [new(collection, application.DisplayName, application.Description)], nodes, records);
            var trust = activation.Winners.ToDictionary(
                value => (value.SourceId, value.RelativePath),
                value => value.Trust);
            var documents = manifest.Records.Select(record =>
            {
                if (!trust.TryGetValue((record.SourceId, record.SourceLogicalPath), out var winnerTrust))
                    throw Failure("CATALOG_PROVENANCE_MISSING", "An active catalog record has no exact source-winner provenance.");
                return new ActiveCatalogFeatureDocument(record, winnerTrust);
            }).ToArray();
            return new ActiveCatalogFeatureSnapshot(manifest, documents)
            {
                EffectiveSetFingerprint = activation.ActivationFingerprint,
                Resolution = CatalogExtensionResolutionContext.Create(applicationId,
                    activation.ResolutionFingerprint,
                    activation.Extensions.Select(value =>
                    {
                        if (!extensionRegistrations.TryGetValue(value.ExtensionId, out var registration))
                            throw Failure("EXTENSION_REGISTRATION_DRIFT",
                                "An active extension registration is no longer available.");
                        return new CatalogExtensionContribution(value.ExtensionId,
                            registration.DisplayName, registration.Description, registration.Classification,
                            registration.SourceIds, value.NamespaceIds, value.HigherPriorityThan,
                            value.OverridesBase);
                    }).ToArray())
            };
        }
        catch (ArgumentException exception)
        {
            throw Failure("CATALOG_MANIFEST_INVALID", "The active catalog could not form one bounded immutable manifest.", exception);
        }
    }

    private CatalogRecordDefinition MechanicRecord(
        ApplicationIdentifier applicationId,
        string collection,
        ActivatedApplicationDocument markdownWinner,
        string markdown,
        IReadOnlyDictionary<string, ActivatedApplicationDocument> winners,
        IReadOnlyDictionary<string, SourceRegistration> registrations)
    {
        var sourcePath = Path.ChangeExtension(markdownWinner.RelativePath, ".js").Replace('\\', '/');
        if (!winners.TryGetValue(sourcePath, out var sourceWinner))
            throw Failure("MECHANIC_SOURCE_MISSING", "An active mechanic Markdown record has no active JavaScript sidecar.");
        if (sourceWinner.SourceId != markdownWinner.SourceId)
            throw Failure("MECHANIC_SOURCE_SPLIT", "A mechanic contract and JavaScript sidecar must come from one effective source.");
        var source = ReadText(sourceWinner, registrations);
        var file = MechanicFile.Parse(markdown, markdownWinner.RelativePath, source);
        var content = ApplicationCatalogRecordContent.MechanicJson(file);
        return Record(applicationId, collection, "mechanic", file.Id, file.Category, file.Name,
            file.Description, SplitTerms(file.Matches), file.Status.ToString(), content, markdownWinner);
    }

    private static CatalogRecordDefinition ProcedureRecord(
        ApplicationIdentifier applicationId,
        string collection,
        ActivatedApplicationDocument winner,
        ProcedureFile file)
    {
        var content = JsonSerializer.Serialize(new
        {
            id = file.Id,
            category = file.Category,
            name = file.Name,
            description = file.Description,
            matches = file.Matches,
            governs = file.Governs,
            instructions = file.Instructions,
            constraints = file.Constraints,
            status = file.Status.ToString().ToLowerInvariant()
        });
        // A procedure's match phrases reach retrieval the same way a mechanic's do. This passed an
        // empty list, so a contract could only ever be found by the words it happened to use --
        // and an exact phrase is the top-ranked hit, which is what pins the right document when a
        // semantically similar neighbour would otherwise win.
        return Record(applicationId, collection, "procedure", file.Id, file.Category, file.Name,
            file.Description, SplitTerms(file.Matches), file.Status.ToString(), content, winner);
    }

    private static CatalogRecordDefinition QueryRecord(
        ApplicationIdentifier applicationId,
        string collection,
        ActivatedApplicationDocument winner,
        ApplicationQueryContract file)
    {
        var content = ApplicationCatalogRecordContent.QueryJson(file);
        return Record(applicationId, collection, ApplicationQueryContract.CatalogKind, file.Id,
            file.Category, file.Name, file.Description, file.Matches, file.Status, content, winner);
    }

    private static CatalogRecordDefinition EntityRecord(
        ApplicationIdentifier applicationId,
        string collection,
        ActivatedApplicationDocument winner,
        string content)
    {
        var file = EntityFile.Parse(content, winner.RelativePath);
        var qualifiedId = file.Id.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? file.Id : applicationId.Value + "." + file.Id;
        var segments = winner.RelativePath.Split('/');
        var entityIndex = ContentEntityIndex(segments);
        if (entityIndex < 0)
            throw Failure("CATALOG_ENTITY_PATH_INVALID", "An active entity is outside the authored content/entities boundary.");
        var pathSegments = new[] { "entities" }
            .Concat(segments.Skip(entityIndex + 2).Take(Math.Max(0, segments.Length - entityIndex - 3)));
        return new(collection, "entity", qualifiedId, file.Name, file.Name, [file.Id], [],
            string.Join('/', pathSegments), "active", 1, content,
            Hash(Encoding.UTF8.GetBytes(content)), winner.SourceId, winner.RelativePath);
    }

    private static CatalogRecordDefinition Record(
        ApplicationIdentifier applicationId,
        string collection,
        string kind,
        string localId,
        string category,
        string name,
        string description,
        IReadOnlyList<string> matchPhrases,
        string status,
        string content,
        ActivatedApplicationDocument winner)
    {
        var qualifiedId = localId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? localId : applicationId.Value + "." + localId;
        var categoryPath = CatalogLayout.CategoryDirectory(category);
        return new(collection, kind, qualifiedId, name, Summary(description), [], matchPhrases,
            (kind == ApplicationQueryContract.CatalogKind ? "queries" : kind + "s") + "/" + categoryPath,
            status.ToLowerInvariant(), 1, content,
            Hash(Encoding.UTF8.GetBytes(content)), winner.SourceId, winner.RelativePath);
    }

    private string ReadText(
        ActivatedApplicationDocument winner,
        IReadOnlyDictionary<string, SourceRegistration> registrations)
    {
        if (!winner.IsText || !registrations.TryGetValue(winner.SourceId, out var registration)
            || !allowedRoots.TryResolve(registration.AllowedRootId, out var configuredRoot)
            || string.IsNullOrWhiteSpace(configuredRoot))
            throw Failure("SOURCE_ROOT_UNAVAILABLE", "An active text document cannot be resolved through its allowed root.");
        try
        {
            var root = Path.GetFullPath(configuredRoot);
            var path = Path.GetFullPath(Path.Combine(root,
                winner.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(root, path))
                throw Failure("SOURCE_PATH_OUTSIDE_ROOT", "An active document path escapes its allowed root.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != winner.Length || Hash(bytes) != winner.ContentFingerprint)
                // Name the file. One drifted document takes the whole application catalog down --
                // feature search, catalog search and every mechanic at once -- and an unnamed
                // failure leaves the operator diffing hundreds of files against an activation.
                throw Failure("SOURCE_FILE_DRIFT",
                    $"'{winner.RelativePath}' no longer matches the length and hash retained at "
                    + "activation. Re-activate the application, or restore the file.");
            var text = StrictUtf8.GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (ApplicationCatalogMaterializationException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or DecoderFallbackException)
        {
            throw Failure("SOURCE_FILE_UNAVAILABLE", "An active document could not be read safely.", exception);
        }
    }

    private static IReadOnlyList<CatalogNodeDefinition> Nodes(
        string collection,
        string rootTitle,
        string rootDescription,
        IReadOnlyList<CatalogRecordDefinition> records)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal) { "" };
        foreach (var record in records)
        {
            var segments = record.Path.Split('/');
            for (var length = 1; length <= segments.Length; length++)
                paths.Add(string.Join('/', segments.Take(length)));
        }
        return paths.Order(StringComparer.Ordinal).Select(path => path.Length == 0
            ? new CatalogNodeDefinition(collection, path, rootTitle, rootDescription, CatalogDescriptionStatus.Authored)
            : new CatalogNodeDefinition(collection, path, path.Split('/')[^1], "", CatalogDescriptionStatus.Missing))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitTerms(string value)
    {
        var terms = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length > CatalogNavigationLimits.MaximumAliasesPerRecord || terms.Any(term => term.Length > 200))
            throw Failure("CATALOG_MATCH_TERMS_INVALID", "A catalog record has unbounded match phrases.");
        return terms;
    }

    private static string Summary(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool TryRecordKind(string path, out string kind)
    {
        kind = "";
        var segments = path.Split('/');
        if (path.EndsWith(".json", StringComparison.Ordinal) && ContentEntityIndex(segments) >= 0)
        { kind = "entity"; return true; }
        if (path.EndsWith(".json", StringComparison.Ordinal)
            && segments.Contains("queries", StringComparer.Ordinal))
        { kind = "query"; return true; }
        if (!path.EndsWith(".md", StringComparison.Ordinal)) return false;
        if (segments.Contains("procedures", StringComparer.Ordinal)) { kind = "procedure"; return true; }
        if (segments.Contains("mechanics", StringComparer.Ordinal)) { kind = "mechanic"; return true; }
        return false;
    }

    private static int ContentEntityIndex(IReadOnlyList<string> segments)
    {
        for (var index = 0; index + 1 < segments.Count; index++)
        {
            if (segments[index] == "content" && segments[index + 1] == "entities") return index;
        }
        return -1;
    }

    private static bool Inside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ApplicationCatalogMaterializationException Failure(string code, string message, Exception? inner = null) =>
        new(code, message, inner);
}

/// <summary>Fail-closed bridge from explicit host publication policy to one active navigator.</summary>
public sealed class ActivatedApplicationCatalogProvider(
    IPublicApplicationCatalogPolicy policy,
    ActivatedApplicationCatalogMaterializer materializer,
    CatalogCursorCodec cursors)
    : IPublicApplicationCatalogProvider, IActiveCatalogFeatureSnapshotProvider,
      IPublicApplicationCatalogDiagnostics
{
    private readonly Dictionary<ApplicationIdentifier, ICatalogNavigator> _cache = [];
    private readonly Dictionary<ApplicationIdentifier, ActiveCatalogFeatureSnapshot> _snapshots = [];
    private readonly Dictionary<ApplicationIdentifier, PublicApplicationCatalogFailure> _failures = [];

    public PublicApplicationCatalogFailure? LastFailure(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return _failures.TryGetValue(applicationId, out var failure) ? failure : null;
    }

    public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (!policy.IsPublished(applicationId))
        {
            navigator = null!;
            return false;
        }
        if (_cache.TryGetValue(applicationId, out navigator!)) return true;
        try
        {
            var snapshot = materializer.BuildFeatureSnapshot(applicationId);
            navigator = new InMemoryCatalogNavigator(snapshot.Manifest, cursors, snapshot.Resolution);
            _cache.Add(applicationId, navigator);
            _snapshots.Add(applicationId, snapshot);
            _failures.Remove(applicationId);
            return true;
        }
        catch (ApplicationCatalogMaterializationException exception)
        {
            _failures[applicationId] = new(exception.Code, exception.Message);
            navigator = null!;
            return false;
        }
    }

    public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot)
    {
        if (!TryGet(applicationId, out _))
        {
            snapshot = null!;
            return false;
        }
        return _snapshots.TryGetValue(applicationId, out snapshot!);
    }
}
