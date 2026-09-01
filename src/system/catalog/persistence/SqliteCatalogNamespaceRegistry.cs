using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Applications;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess.Catalog;

public sealed class SqliteCatalogNamespaceRegistry(DantesRoleplayDbContext db) :
    ICatalogNamespaceRegistry, ICatalogNamespaceOverlayRegistry
{
    public CatalogNamespaceDefinition Register(CatalogNamespaceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateRegistration(registration);
        var existing = db.Set<CatalogNamespaceRecord>().SingleOrDefault(value => value.Id == registration.Id);
        if (existing is not null)
        {
            var replay = View(existing);
            if (replay.Owner != registration.Owner || replay.Description != registration.Description
                || !replay.AllowedKinds.SequenceEqual(NormalizeKinds(registration.AllowedKinds), StringComparer.Ordinal)
                || !replay.Aliases.SequenceEqual(NormalizeAliases(registration.Aliases), StringComparer.Ordinal)
                || replay.ReviewStatus != registration.ReviewStatus
                || replay.ReviewNote != NormalizeReviewNote(registration.ReviewNote))
                throw Error("NAMESPACE_REGISTRATION_CONFLICT", "The namespace already has different registered metadata.");
            return replay;
        }

        var parent = Parent(registration.Id);
        if (parent is not null)
        {
            var parentRow = db.Set<CatalogNamespaceRecord>().AsNoTracking()
                .SingleOrDefault(value => value.Id == parent)
                ?? throw Error("NAMESPACE_PARENT_UNKNOWN", $"Register parent namespace '{parent}' first.");
            if (parentRow.DisabledAtUtc is not null)
                throw Error("NAMESPACE_PARENT_DISABLED", "A child cannot be registered below a disabled namespace.");
            if (parentRow.Owner != registration.Owner)
                throw Error("NAMESPACE_OWNER_MISMATCH", "A child namespace must have the same owner as its parent.");
            if (registration.ReviewStatus == CatalogNamespaceReviewStatuses.Reviewed
                && parentRow.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
                throw Error("NAMESPACE_PARENT_UNREVIEWED", "Review the parent namespace before reviewing a child namespace.");
        }

        var now = DateTime.UtcNow;
        var row = new CatalogNamespaceRecord
        {
            Id = registration.Id,
            ParentId = parent,
            Owner = registration.Owner,
            Description = registration.Description.Trim(),
            AllowedKindsJson = JsonSerializer.Serialize(NormalizeKinds(registration.AllowedKinds)),
            AliasesJson = JsonSerializer.Serialize(NormalizeAliases(registration.Aliases)),
            ReviewStatus = registration.ReviewStatus,
            ReviewNote = NormalizeReviewNote(registration.ReviewNote),
            ReviewedAtUtc = registration.ReviewStatus == CatalogNamespaceReviewStatuses.Reviewed ? now : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Add(row);
        db.SaveChanges();
        return View(row);
    }

    public CatalogNamespaceDefinition? Get(string namespaceId, bool includeDisabled = false)
    {
        ValidateNamespaceId(namespaceId);
        var row = db.Set<CatalogNamespaceRecord>().AsNoTracking().SingleOrDefault(value => value.Id == namespaceId);
        if (!includeDisabled && row is not null && (row.DisabledAtUtc is not null || AncestorDisabled(row.Id)))
            return null;
        return row is null ? null : View(row);
    }

    public IReadOnlyList<CatalogNamespaceDefinition> List(bool includeDisabled = false) =>
        db.Set<CatalogNamespaceRecord>().AsNoTracking()
            .Where(value => includeDisabled || value.DisabledAtUtc == null)
            .OrderBy(value => value.Id).ToArray()
            .Where(value => includeDisabled || !AncestorDisabled(value.Id))
            .Select(View).ToArray();

    public IReadOnlyList<CatalogNamespaceSearchHit> Search(
        string query, int limit = 20, bool includeDisabled = false)
    {
        if (string.IsNullOrWhiteSpace(query) || limit is < 1 or > 100)
            throw new ArgumentException("Namespace search requires a query and a limit from 1 through 100.");
        var normalized = query.Trim();
        return List(includeDisabled).Select(value => new
            {
                Value = value,
                Rank = value.Id == normalized ? 0
                    : value.Aliases.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? 1
                    : value.Id.StartsWith(normalized, StringComparison.Ordinal) ? 2
                    : Contains(value, normalized) ? 3 : int.MaxValue
            })
            .Where(value => value.Rank != int.MaxValue)
            .OrderBy(value => value.Rank).ThenBy(value => value.Value.Id, StringComparer.Ordinal)
            .Take(limit).Select(value => new CatalogNamespaceSearchHit(value.Value, value.Rank)).ToArray();
    }

    public CatalogNamespaceDefinition SetEnabled(string namespaceId, bool enabled)
    {
        ValidateNamespaceId(namespaceId);
        var row = db.Set<CatalogNamespaceRecord>().SingleOrDefault(value => value.Id == namespaceId)
            ?? throw Error("NAMESPACE_UNKNOWN", "The namespace is not registered.");
        if (enabled && row.ParentId is not null && db.Set<CatalogNamespaceRecord>().Any(value =>
                value.Id == row.ParentId && value.DisabledAtUtc != null))
            throw Error("NAMESPACE_PARENT_DISABLED", "Enable the parent namespace first.");
        row.DisabledAtUtc = enabled ? null : row.DisabledAtUtc ?? DateTime.UtcNow;
        row.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        return View(row);
    }

    public CatalogNamespaceDefinition SetReview(string namespaceId, string reviewStatus, string reviewNote)
    {
        ValidateNamespaceId(namespaceId);
        ValidateReview(reviewStatus, reviewNote);
        var row = db.Set<CatalogNamespaceRecord>().SingleOrDefault(value => value.Id == namespaceId)
            ?? throw Error("NAMESPACE_UNKNOWN", "The namespace is not registered.");
        if (reviewStatus == CatalogNamespaceReviewStatuses.Reviewed && row.ParentId is not null)
        {
            var parent = db.Set<CatalogNamespaceRecord>().AsNoTracking().Single(value => value.Id == row.ParentId);
            if (parent.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
                throw Error("NAMESPACE_PARENT_UNREVIEWED", "Review the parent namespace before reviewing a child namespace.");
        }
        if (reviewStatus == CatalogNamespaceReviewStatuses.NeedsReview
            && db.Set<CatalogNamespaceRecord>().Any(value => value.ParentId == row.Id
                && value.ReviewStatus == CatalogNamespaceReviewStatuses.Reviewed))
            throw Error("NAMESPACE_CHILD_REVIEWED", "Mark reviewed child namespaces as needing review first.");
        var now = DateTime.UtcNow;
        row.ReviewStatus = reviewStatus;
        row.ReviewNote = reviewNote.Trim();
        row.ReviewedAtUtc = reviewStatus == CatalogNamespaceReviewStatuses.Reviewed
            ? row.ReviewedAtUtc ?? now
            : null;
        row.UpdatedAtUtc = now;
        db.SaveChanges();
        return View(row);
    }

    public CatalogNamespaceDefinition RequireRecordNamespace(string qualifiedId, string recordKind)
    {
        CatalogNamespaceIdentity.ValidateRecordId(qualifiedId);
        if (!CatalogNamespaceKinds.All.Contains(recordKind))
            throw new ArgumentException("Unknown catalog record kind.", nameof(recordKind));
        var namespaceId = CatalogNamespaceIdentity.NamespaceOf(qualifiedId);
        var row = db.Set<CatalogNamespaceRecord>().AsNoTracking().SingleOrDefault(value => value.Id == namespaceId)
            ?? throw Error("NAMESPACE_UNKNOWN", $"Namespace '{namespaceId}' is not registered.");
        if (row.DisabledAtUtc is not null || AncestorDisabled(row.Id))
            throw Error("NAMESPACE_DISABLED", $"Namespace '{namespaceId}' is disabled.");
        if (row.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
            throw Error("NAMESPACE_UNREVIEWED", $"Namespace '{namespaceId}' has not been reviewed.");
        var result = View(row);
        if (!result.AllowedKinds.Contains(recordKind, StringComparer.Ordinal))
            throw Error("NAMESPACE_KIND_FORBIDDEN", $"Namespace '{namespaceId}' does not allow '{recordKind}' records.");
        return result;
    }

    public CatalogNamespaceOverlayProfile RegisterProfile(CatalogNamespaceOverlayProfileRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var application = ApplicationIdentifier.Parse(registration.ApplicationId);
        ValidateProfileId(registration.ProfileId);
        ValidateDescription(registration.Description, nameof(registration));
        if (!db.Set<ApplicationRegistryRecord>().AsNoTracking().Any(value => value.Id == application.Value))
            throw Error("NAMESPACE_OVERLAY_APPLICATION_UNKNOWN", "The overlay profile application is not registered.");
        var existing = db.Set<CatalogNamespaceOverlayProfileRecord>().SingleOrDefault(value =>
            value.ApplicationId == application.Value && value.ProfileId == registration.ProfileId);
        if (existing is not null)
        {
            if (existing.Description != registration.Description.Trim())
                throw Error("NAMESPACE_OVERLAY_PROFILE_CONFLICT", "The overlay profile already has different metadata.");
            return View(existing);
        }
        var row = new CatalogNamespaceOverlayProfileRecord
        {
            ApplicationId = application.Value,
            ProfileId = registration.ProfileId,
            Description = registration.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Add(row);
        db.SaveChanges();
        return View(row);
    }

    public CatalogNamespaceOverlayProfile? GetProfile(string applicationId, string profileId)
    {
        var application = ApplicationIdentifier.Parse(applicationId);
        ValidateProfileId(profileId);
        var row = db.Set<CatalogNamespaceOverlayProfileRecord>().AsNoTracking().SingleOrDefault(value =>
            value.ApplicationId == application.Value && value.ProfileId == profileId);
        return row is null ? null : View(row);
    }

    public IReadOnlyList<CatalogNamespaceOverlayProfile> ProfilesForApplication(string applicationId)
    {
        var application = ApplicationIdentifier.Parse(applicationId);
        return db.Set<CatalogNamespaceOverlayProfileRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == application.Value)
            .OrderBy(value => value.ProfileId)
            .Select(value => new CatalogNamespaceOverlayProfile(value.ApplicationId, value.ProfileId,
                value.Description, value.CreatedAtUtc)).ToArray();
    }

    public CatalogResolutionKeyDefinition RegisterResolutionKey(CatalogResolutionKeyRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var profile = GetProfile(registration.ApplicationId, registration.ProfileId)
            ?? throw Error("NAMESPACE_OVERLAY_PROFILE_UNKNOWN", "The overlay profile is not registered.");
        ValidateResolutionKey(registration.ResolutionKey);
        if (!CatalogNamespaceKinds.All.Contains(registration.RecordKind))
            throw new ArgumentException("The resolution key record kind is unknown.", nameof(registration));
        ValidateDescription(registration.Description, nameof(registration));
        var existing = db.Set<CatalogResolutionKeyRecord>().SingleOrDefault(value =>
            value.ApplicationId == profile.ApplicationId && value.ProfileId == profile.ProfileId
            && value.ResolutionKey == registration.ResolutionKey);
        if (existing is not null)
        {
            if (existing.RecordKind != registration.RecordKind || existing.Description != registration.Description.Trim())
                throw Error("NAMESPACE_RESOLUTION_KEY_CONFLICT", "The resolution key already has different metadata.");
            return View(existing);
        }
        var row = new CatalogResolutionKeyRecord
        {
            ApplicationId = profile.ApplicationId,
            ProfileId = profile.ProfileId,
            ResolutionKey = registration.ResolutionKey,
            RecordKind = registration.RecordKind,
            Description = registration.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Add(row);
        db.SaveChanges();
        return View(row);
    }

    public IReadOnlyList<CatalogResolutionKeyDefinition> ResolutionKeysForProfile(
        string applicationId, string profileId)
    {
        _ = GetProfile(applicationId, profileId)
            ?? throw Error("NAMESPACE_OVERLAY_PROFILE_UNKNOWN", "The overlay profile is not registered.");
        return db.Set<CatalogResolutionKeyRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId && value.ProfileId == profileId)
            .OrderBy(value => value.RecordKind).ThenBy(value => value.ResolutionKey)
            .Select(value => new CatalogResolutionKeyDefinition(value.ApplicationId, value.ProfileId,
                value.ResolutionKey, value.RecordKind, value.Description, value.CreatedAtUtc)).ToArray();
    }

    public CatalogNamespaceOverlayRule Register(CatalogNamespaceOverlayRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _ = GetProfile(rule.ApplicationId, rule.ProfileId)
            ?? throw Error("NAMESPACE_OVERLAY_PROFILE_UNKNOWN", "The overlay profile is not registered.");
        if (rule.HigherNamespaceId == rule.LowerNamespaceId
            || (rule.RecordKind is not null && !CatalogNamespaceKinds.All.Contains(rule.RecordKind)))
            throw new ArgumentException("A namespace overlay rule requires two distinct registered namespaces and an optional known kind.", nameof(rule));
        _ = Get(rule.HigherNamespaceId, includeDisabled: true) ?? throw Error("NAMESPACE_UNKNOWN", "The higher namespace is not registered.");
        _ = Get(rule.LowerNamespaceId, includeDisabled: true) ?? throw Error("NAMESPACE_UNKNOWN", "The lower namespace is not registered.");
        var storedKind = rule.RecordKind ?? string.Empty;
        if (db.Set<CatalogNamespaceOverlayRecord>().Any(value => value.ApplicationId == rule.ApplicationId
                && value.ProfileId == rule.ProfileId
                && value.HigherNamespaceId == rule.HigherNamespaceId && value.LowerNamespaceId == rule.LowerNamespaceId
                && value.RecordKind == storedKind))
            return rule;
        db.Add(new CatalogNamespaceOverlayRecord
        {
            ApplicationId = rule.ApplicationId,
            ProfileId = rule.ProfileId,
            HigherNamespaceId = rule.HigherNamespaceId,
            LowerNamespaceId = rule.LowerNamespaceId,
            RecordKind = storedKind,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        try { _ = Topological(rule.ApplicationId, rule.ProfileId, rule.RecordKind); }
        catch { db.Set<CatalogNamespaceOverlayRecord>().Remove(db.Set<CatalogNamespaceOverlayRecord>().Single(value =>
            value.ApplicationId == rule.ApplicationId && value.ProfileId == rule.ProfileId
            && value.HigherNamespaceId == rule.HigherNamespaceId
            && value.LowerNamespaceId == rule.LowerNamespaceId && value.RecordKind == storedKind)); db.SaveChanges(); throw; }
        return rule;
    }

    public IReadOnlyList<CatalogNamespaceOverlayRule> RulesForProfile(string applicationId, string profileId)
    {
        _ = GetProfile(applicationId, profileId)
            ?? throw Error("NAMESPACE_OVERLAY_PROFILE_UNKNOWN", "The overlay profile is not registered.");
        return db.Set<CatalogNamespaceOverlayRecord>().AsNoTracking().Where(value =>
                value.ApplicationId == applicationId && value.ProfileId == profileId)
            .OrderBy(value => value.RecordKind).ThenBy(value => value.HigherNamespaceId)
            .ThenBy(value => value.LowerNamespaceId)
            .Select(value => new CatalogNamespaceOverlayRule(value.ApplicationId, value.ProfileId, value.HigherNamespaceId,
                value.LowerNamespaceId, value.RecordKind.Length == 0 ? null : value.RecordKind)).ToArray();
    }

    public CatalogResolutionResult Resolve(
        string applicationId, string profileId, IReadOnlyList<CatalogResolutionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) throw new ArgumentException("At least one resolution candidate is required.", nameof(candidates));
        _ = GetProfile(applicationId, profileId)
            ?? throw Error("NAMESPACE_OVERLAY_PROFILE_UNKNOWN", "The overlay profile is not registered.");
        if (candidates.Select(value => (value.RecordKind, value.ResolutionKey)).Distinct().Count() != 1)
            throw new ArgumentException("Resolution candidates must share one record kind and resolution key.", nameof(candidates));
        if (candidates.Select(value => value.QualifiedId).Distinct(StringComparer.Ordinal).Count() != candidates.Count
            || candidates.Select(value => value.NamespaceId).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new ArgumentException("Resolution candidates must have distinct qualified IDs and namespaces.", nameof(candidates));
        foreach (var candidate in candidates)
        {
            CatalogNamespaceIdentity.ValidateRecordId(candidate.QualifiedId);
            if (CatalogNamespaceIdentity.NamespaceOf(candidate.QualifiedId) != candidate.NamespaceId)
                throw new ArgumentException("A resolution candidate namespace must match its qualified ID.", nameof(candidates));
            _ = Get(candidate.NamespaceId) ?? throw Error("NAMESPACE_UNKNOWN_OR_DISABLED",
                "A resolution candidate namespace is unknown or disabled.");
        }
        var kind = candidates[0].RecordKind;
        if (!CatalogNamespaceKinds.All.Contains(kind))
            throw new ArgumentException("Resolution candidates use an unknown record kind.", nameof(candidates));
        var resolutionKey = db.Set<CatalogResolutionKeyRecord>().AsNoTracking().SingleOrDefault(value =>
            value.ApplicationId == applicationId && value.ProfileId == profileId
            && value.ResolutionKey == candidates[0].ResolutionKey)
            ?? throw Error("NAMESPACE_RESOLUTION_KEY_UNKNOWN",
                "The resolution key is not registered in the selected overlay profile.");
        if (resolutionKey.RecordKind != kind)
            throw Error("NAMESPACE_RESOLUTION_KIND_MISMATCH",
                "The resolution key is registered for a different record kind.");
        if (candidates.Any(candidate => !CatalogExtensionSearch.Matches(
                candidate.QualifiedId, candidate.ResolutionKey)))
            throw new ArgumentException("A resolution candidate ID must end with its logical resolution key.", nameof(candidates));
        _ = Topological(applicationId, profileId, kind);
        var rules = RulesForProfile(applicationId, profileId)
            .Where(value => value.RecordKind is null || value.RecordKind == kind).ToArray();
        var lowerByHigher = rules.GroupBy(value => value.HigherNamespaceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(value => value.LowerNamespaceId).ToArray(),
                StringComparer.Ordinal);
        bool Dominates(string higher, string lower)
        {
            var pending = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Push(higher);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current)) continue;
                if (current == lower && current != higher) return true;
                if (lowerByHigher.TryGetValue(current, out var children))
                    foreach (var child in children) pending.Push(child);
            }
            return false;
        }
        var winner = candidates.Where(candidate => candidates.All(other =>
            ReferenceEquals(candidate, other) || candidate.NamespaceId == other.NamespaceId
            || Dominates(candidate.NamespaceId, other.NamespaceId))).ToArray();
        if (winner.Length != 1)
            throw Error("NAMESPACE_OVERLAY_AMBIGUOUS", "No unique namespace overlay winner exists for this logical record.");
        return new(profileId, View(resolutionKey), winner[0],
            candidates.Where(value => !ReferenceEquals(value, winner[0])).ToArray());
    }

    private IReadOnlyDictionary<string, int> Topological(string applicationId, string profileId, string? kind)
    {
        var rules = RulesForProfile(applicationId, profileId)
            .Where(value => value.RecordKind is null || value.RecordKind == kind).ToArray();
        var nodes = rules.SelectMany(value => new[] { value.HigherNamespaceId, value.LowerNamespaceId }).Distinct().ToArray();
        var edges = nodes.ToDictionary(value => value, _ => new List<string>(), StringComparer.Ordinal);
        var indegree = nodes.ToDictionary(value => value, _ => 0, StringComparer.Ordinal);
        foreach (var rule in rules) { edges[rule.HigherNamespaceId].Add(rule.LowerNamespaceId); indegree[rule.LowerNamespaceId]++; }
        var ready = new SortedSet<string>(nodes.Where(value => indegree[value] == 0), StringComparer.Ordinal);
        var rank = new Dictionary<string, int>(StringComparer.Ordinal); var index = 0;
        while (ready.Count != 0) { var node = ready.Min!; ready.Remove(node); rank[node] = index++;
            foreach (var lower in edges[node]) if (--indegree[lower] == 0) ready.Add(lower); }
        if (rank.Count != nodes.Length) throw Error("NAMESPACE_OVERLAY_CYCLE", "Namespace overlay rules must remain acyclic.");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(rank);
    }

    private bool AncestorDisabled(string namespaceId)
    {
        var current = Parent(namespaceId);
        while (current is not null)
        {
            var row = db.Set<CatalogNamespaceRecord>().AsNoTracking().SingleOrDefault(value => value.Id == current);
            if (row?.DisabledAtUtc is not null) return true;
            current = Parent(current);
        }
        return false;
    }

    private static bool Contains(CatalogNamespaceDefinition value, string query) =>
        value.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || value.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
        || value.Aliases.Any(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase));
    private static string? Parent(string id) => id == CatalogNamespaceIdentity.RootNamespaceId ? null
        : id.Contains('.') ? id[..id.LastIndexOf('.')] : null;
    private static string[] NormalizeKinds(IEnumerable<string> kinds) => kinds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static string[] NormalizeAliases(IEnumerable<string>? aliases) => (aliases ?? []).Select(value => value.Trim())
        .Where(value => value.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    private static void ValidateRegistration(CatalogNamespaceRegistration value)
    {
        ValidateNamespaceId(value.Id);
        if (string.IsNullOrWhiteSpace(value.Owner) || value.Owner.Length > 100
            || string.IsNullOrWhiteSpace(value.Description) || value.Description.Length > 2_000
            || value.AllowedKinds is null || value.AllowedKinds.Count == 0
            || value.AllowedKinds.Any(kind => !CatalogNamespaceKinds.All.Contains(kind)))
            throw new ArgumentException("A namespace requires a safe owner, description, and known allowed record kinds.", nameof(value));
        ValidateReview(value.ReviewStatus, value.ReviewNote);
    }
    private static void ValidateReview(string status, string? note)
    {
        if (!CatalogNamespaceReviewStatuses.All.Contains(status) || string.IsNullOrWhiteSpace(note)
            || note.Length > 2_000)
            throw new ArgumentException("A namespace requires a known review status and review note.");
    }
    private static string NormalizeReviewNote(string? note) => note?.Trim() ?? string.Empty;
    private static void ValidateDescription(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2_000)
            throw new ArgumentException("A bounded description is required.", parameter);
    }
    private static void ValidateProfileId(string value)
    {
        if (!CatalogOverlayIdentity.IsProfileId(value))
            throw new ArgumentException("Invalid catalog overlay profile ID.", nameof(value));
    }
    private static void ValidateResolutionKey(string value)
    {
        if (!CatalogOverlayIdentity.IsResolutionKey(value))
            throw new ArgumentException("Invalid catalog resolution key.", nameof(value));
    }
    private static void ValidateNamespaceId(string value)
    { if (!CatalogNamespaceIdentity.IsNamespaceId(value)) throw new ArgumentException("Invalid catalog namespace ID.", nameof(value)); }
    private static CatalogNamespaceDefinition View(CatalogNamespaceRecord row) => new(row.Id, row.ParentId, row.Owner,
        row.Description, JsonSerializer.Deserialize<string[]>(row.AllowedKindsJson) ?? [],
        JsonSerializer.Deserialize<string[]>(row.AliasesJson) ?? [], row.ReviewStatus, row.ReviewNote,
        row.ReviewedAtUtc, row.CreatedAtUtc, row.UpdatedAtUtc, row.DisabledAtUtc);
    private static CatalogNamespaceOverlayProfile View(CatalogNamespaceOverlayProfileRecord row) =>
        new(row.ApplicationId, row.ProfileId, row.Description, row.CreatedAtUtc);
    private static CatalogResolutionKeyDefinition View(CatalogResolutionKeyRecord row) =>
        new(row.ApplicationId, row.ProfileId, row.ResolutionKey, row.RecordKind,
            row.Description, row.CreatedAtUtc);
    private static CatalogNamespaceException Error(string code, string message) => new(code, message);
}

internal sealed class CatalogNamespaceRecord
{
    public required string Id { get; set; }
    public string? ParentId { get; set; }
    public required string Owner { get; set; }
    public required string Description { get; set; }
    public required string AllowedKindsJson { get; set; }
    public required string AliasesJson { get; set; }
    public required string ReviewStatus { get; set; }
    public required string ReviewNote { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
}

internal sealed class CatalogNamespaceOverlayRecord
{
    public required string ApplicationId { get; set; }
    public required string ProfileId { get; set; }
    public required string HigherNamespaceId { get; set; }
    public required string LowerNamespaceId { get; set; }
    public string RecordKind { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class CatalogNamespaceOverlayProfileRecord
{
    public required string ApplicationId { get; set; }
    public required string ProfileId { get; set; }
    public required string Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class CatalogResolutionKeyRecord
{
    public required string ApplicationId { get; set; }
    public required string ProfileId { get; set; }
    public required string ResolutionKey { get; set; }
    public required string RecordKind { get; set; }
    public required string Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
