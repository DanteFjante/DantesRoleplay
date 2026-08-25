using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
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
/// Converts exact active procedure/mechanic source winners into the generic immutable navigation
/// model. It contains no application IDs and never executes application content.
/// </summary>
public sealed class ActivatedApplicationCatalogMaterializer(
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    ISourceRegistry sources,
    IAllowedSourceRootResolver allowedRoots)
{
    private const string MaterializerVersion = "activated-action-catalog-v1";
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
            var markdown = ReadText(winner, registrations);
            try
            {
                records.Add(kind == "procedure"
                    ? ProcedureRecord(applicationId, applicationId.Value, winner, ProcedureFile.Parse(markdown, winner.RelativePath))
                    : MechanicRecord(applicationId, applicationId.Value, winner, markdown, winners, registrations));
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
            return new ActiveCatalogFeatureSnapshot(manifest, documents);
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
        var content = JsonSerializer.Serialize(new
        {
            id = file.Id,
            category = file.Category,
            name = file.Name,
            description = file.Description,
            matches = file.Matches,
            requirements = file.Requirements,
            source = file.Source,
            scope = file.Scope,
            status = file.Status.ToString().ToLowerInvariant()
        });
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
            governs = file.Governs,
            instructions = file.Instructions,
            constraints = file.Constraints,
            status = file.Status.ToString().ToLowerInvariant()
        });
        return Record(applicationId, collection, "procedure", file.Id, file.Category, file.Name,
            file.Description, [], file.Status.ToString(), content, winner);
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
            kind + "s/" + categoryPath, status.ToLowerInvariant(), 1, content,
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
                throw Failure("SOURCE_FILE_DRIFT", "An active document no longer matches its retained length and hash.");
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
            throw Failure("CATALOG_MATCH_TERMS_INVALID", "A mechanic has unbounded match phrases.");
        return terms;
    }

    private static string Summary(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool TryRecordKind(string path, out string kind)
    {
        kind = "";
        if (!path.EndsWith(".md", StringComparison.Ordinal)) return false;
        var segments = path.Split('/');
        if (segments.Contains("procedures", StringComparer.Ordinal)) { kind = "procedure"; return true; }
        if (segments.Contains("mechanics", StringComparer.Ordinal)) { kind = "mechanic"; return true; }
        return false;
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
    CatalogCursorCodec cursors) : IPublicApplicationCatalogProvider, IActiveCatalogFeatureSnapshotProvider
{
    private readonly Dictionary<ApplicationIdentifier, ICatalogNavigator> _cache = [];
    private readonly Dictionary<ApplicationIdentifier, ActiveCatalogFeatureSnapshot> _snapshots = [];

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
            navigator = new InMemoryCatalogNavigator(snapshot.Manifest, cursors);
            _cache.Add(applicationId, navigator);
            _snapshots.Add(applicationId, snapshot);
            return true;
        }
        catch (ApplicationCatalogMaterializationException)
        {
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
