using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Procedures;
using DantesRoleplay.Snapshots;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// The first typed snapshot producer. It captures only one already-ended S3 factual recap plus
/// durable session/campaign/world identities; it never serializes current world or campaign state.
/// </summary>
public sealed class CampaignSessionEvidenceProducer(
    DantesRoleplayDbContext db,
    IWorldStore world,
    ICampaignSessionRecapReader recaps,
    IProcedureStore procedures) : ICampaignSessionEvidenceProducer
{
    internal const string ScopeContract = "procedure.campaign.session";
    internal const string Producer = "snapshot.producer.campaign-session-evidence";
    internal const string EncodingName = "dantes-canonical-json-v1";
    internal const string Format = "dantes.snapshot.campaign-session-evidence";
    private const string Session = "game.core.campaign.session";
    private const string Recap = "game.core.campaign.session-recap";
    private const string HasSession = "game.core.campaign.has-session";
    private const string InWorld = "game.core.campaign.in-world";
    private const string WorldRoot = "game.core.world.root";
    private const int MaximumBytes = 65_536;
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly ICampaignSessionRecapReader _recaps = recaps;
    private readonly IProcedureStore _procedures = procedures;

    public async Task<CampaignSessionEvidenceProductionResult> ProduceAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
            return Failure(sessionId, null, null, "SNAPSHOT_TRANSACTION_REQUIRED", "sessionId", "Session evidence production must join its owning root transaction.", "Begin the approved checkpoint root before producing evidence.");
        if (!Id(sessionId, "session."))
            return Failure(sessionId, null, null, "INVALID_SESSION_ID", "sessionId", "sessionId must be a canonical session.* id.", "Provide one ended canonical session id.");

        var historical = await _recaps.GetAsync(sessionId, cancellationToken);
        if (!historical.Found)
        {
            var problem = historical.Problems.FirstOrDefault() ?? new CampaignSessionProblem("SESSION_EVIDENCE_UNAVAILABLE", "sessionId", "The ended session recap was unavailable.", "Read one valid ended session recap.");
            return new("unavailable", sessionId, historical.CampaignId, null, null, [problem]);
        }

        var session = await _world.GetEntityAsync(sessionId, cancellationToken);
        var lifecycle = Single(session, Session);
        var recap = Single(session, Recap);
        if (session is null || lifecycle is null || recap is null || !Ended(lifecycle.Data, out var ordinal))
            return Failure(sessionId, historical.CampaignId, null, "SESSION_GRAPH_INVALID", "sessionId", "The ended session must retain exactly one complete lifecycle and recap component.", "Read one valid ended session recap.");

        var scopes = (await _world.GetRelationshipsAsync(sessionId, true, cancellationToken))
            .Where(link => link.Kind == HasSession && link.ToEntityId == sessionId)
            .ToArray();
        if (scopes.Length != 1 || scopes[0].FromEntityId != historical.CampaignId || scopes[0].Data != "{}")
            return Failure(sessionId, historical.CampaignId, null, "SESSION_GRAPH_INVALID", "sessionId", "The ended session must retain one empty-data campaign scope link.", "Read one valid ended session recap.");

        var campaignId = scopes[0].FromEntityId;
        var worldScopes = (await _world.GetRelationshipsAsync(campaignId, false, cancellationToken))
            .Where(link => link.Kind == InWorld && link.FromEntityId == campaignId)
            .ToArray();
        if (worldScopes.Length != 1 || worldScopes[0].Data != "{}" || !Id(worldScopes[0].ToEntityId, "world."))
            return Failure(sessionId, campaignId, null, "CAMPAIGN_WORLD_SCOPE_INVALID", "campaignId", "Campaign must have one empty-data link to an active world root.", "Repair the campaign world scope before capturing evidence.");

        var worldId = worldScopes[0].ToEntityId;
        var worldRoot = await _world.GetEntityAsync(worldId, cancellationToken);
        if (worldRoot is null || !ActiveWorld(worldRoot))
            return Failure(sessionId, campaignId, worldId, "CAMPAIGN_WORLD_SCOPE_INVALID", "campaignId", "Campaign world scope must name one active world root.", "Repair the campaign world scope before capturing evidence.");

        var contract = await _procedures.GetAsync(ScopeContract, cancellationToken: cancellationToken);
        if (contract is null || contract.Status != ProcedureStatus.Active || contract.Version < 1)
            return Failure(sessionId, campaignId, worldId, "SNAPSHOT_SCOPE_CONTRACT_UNAVAILABLE", "scopeContract", "The active campaign-session contract version was unavailable.", "Import or restore the active campaign-session contract before capturing evidence.");

        var fingerprint = BoundaryFingerprint(sessionId, lifecycle.Data, scopes[0], worldScopes[0], recap.Data, contract.Version);
        var content = Write(sessionId, ordinal, campaignId, worldId, historical.Recap!);
        if (content.Length > MaximumBytes)
            return Failure(sessionId, campaignId, worldId, "SNAPSHOT_PRODUCER_CONTENT_TOO_LARGE", "content", "The closed session evidence package exceeds its 64 KiB limit.", "Reduce the approved source data; capture never truncates evidence.");

        var proposal = new SnapshotCaptureProposal(ScopeContract, contract.Version, Producer, 1, EncodingName, fingerprint, content);
        return new("produced", sessionId, campaignId, worldId, proposal, []);
    }

    private static byte[] Write(string sessionId, int ordinal, string campaignId, string worldId, CampaignSessionRecap recap)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false });
        writer.WriteStartObject();
        writer.WriteString("format", Format);
        writer.WriteNumber("formatVersion", 1);
        writer.WritePropertyName("session"); writer.WriteStartObject();
        writer.WriteString("id", sessionId); writer.WriteString("status", "ended"); writer.WriteNumber("ordinal", ordinal);
        writer.WriteEndObject();
        writer.WritePropertyName("scope"); writer.WriteStartObject();
        writer.WriteString("campaignId", campaignId); writer.WriteString("worldId", worldId);
        writer.WriteEndObject();
        writer.WritePropertyName("recap"); writer.WriteStartObject();
        writer.WriteString("protocolVersion", recap.ProtocolVersion);
        writer.WritePropertyName("chapter"); writer.WriteStartObject();
        writer.WriteString("id", recap.Chapter.Id); writer.WriteString("status", recap.Chapter.Status); writer.WriteString("title", recap.Chapter.Title); writer.WriteString("partyQuestion", recap.Chapter.PartyQuestion);
        writer.WriteEndObject();
        writer.WritePropertyName("arc"); writer.WriteStartObject();
        writer.WriteString("id", recap.Arc.Id); writer.WriteString("status", recap.Arc.Status); writer.WriteString("title", recap.Arc.Title); writer.WriteString("partyStake", recap.Arc.PartyStake);
        writer.WriteEndObject();
        writer.WritePropertyName("milestones"); writer.WriteStartArray();
        foreach (var milestone in recap.Milestones)
        {
            writer.WriteStartObject();
            writer.WriteString("chapterId", milestone.ChapterId); writer.WriteString("title", milestone.Title); writer.WriteString("closingSummary", milestone.ClosingSummary);
            writer.WriteString("timestamp", milestone.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); writer.WriteNumber("sequence", milestone.Sequence);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string BoundaryFingerprint(string sessionId, string lifecycle, RelationshipView campaignScope, RelationshipView worldScope, string recap, int contractVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dantes.snapshot.boundary.campaign-session-evidence.v1");
        Append(hash, sessionId); Append(hash, lifecycle);
        Append(hash, campaignScope.FromEntityId); Append(hash, campaignScope.ToEntityId); Append(hash, campaignScope.Kind); Append(hash, campaignScope.Data);
        Append(hash, worldScope.FromEntityId); Append(hash, worldScope.ToEntityId); Append(hash, worldScope.Kind); Append(hash, worldScope.Data);
        Append(hash, recap); Append(hash, contractVersion.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }

    private static ComponentView? Single(EntitySnapshot? entity, string definitionId)
    {
        var components = entity?.Components.Where(component => component.DefinitionId == definitionId).ToArray();
        return components is { Length: 1 } ? components[0] : null;
    }

    private static bool Ended(string json, out int ordinal)
    {
        ordinal = 0;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(["status", "ordinal"])
                && root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "ended"
                && root.TryGetProperty("ordinal", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out ordinal) && ordinal > 0;
        }
        catch { return false; }
    }

    private static bool ActiveWorld(EntitySnapshot entity)
    {
        var root = Single(entity, WorldRoot);
        if (root is null) return false;
        try
        {
            using var document = JsonDocument.Parse(root.Data); var value = document.RootElement;
            return value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && status.GetString() == "active";
        }
        catch { return false; }
    }

    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static CampaignSessionEvidenceProductionResult Failure(string? sessionId, string? campaignId, string? worldId, string code, string path, string reason, string recovery) =>
        new("unavailable", sessionId ?? string.Empty, campaignId, worldId, null, [new CampaignSessionProblem(code, path, reason, recovery)]);
}
