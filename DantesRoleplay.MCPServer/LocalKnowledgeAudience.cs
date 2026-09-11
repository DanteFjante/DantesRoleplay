using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Security;

namespace DantesRoleplay.MCPServer;

public sealed record LocalKnowledgeSeatSnapshot(
    bool Enabled,
    string PrincipalId,
    string ApplicationId,
    string CampaignId,
    string? ActorId,
    KnowledgeAudienceRole Role = KnowledgeAudienceRole.Actor,
    IReadOnlyList<string>? SourceIds = null,
    IReadOnlyDictionary<string, string>? AuthorizedRoleEntityIds = null);

public interface ILocalKnowledgeSeatProvider
{
    LocalKnowledgeSeatSnapshot Current();
}

internal sealed class LocalReadableRulesAudienceProvider(ILocalKnowledgeSeatProvider seats)
    : IWebReadableRulesAudienceProvider
{
    public ReadableRuleAudience Current()
    {
        var seat = seats.Current();
        return seat.Enabled && seat.Role == KnowledgeAudienceRole.GameMaster
            ? ReadableRuleAudience.Dm
            : ReadableRuleAudience.Public;
    }
}

internal sealed class LocalEntityMediaAudienceResolver(
    ILocalKnowledgeSeatProvider seats) : IEntityMediaAudienceResolver
{
    public EntityMediaAudienceContext? Resolve(ApplicationIdentifier applicationId)
    {
        var seat = seats.Current();
        if (!seat.Enabled || seat.ApplicationId != applicationId.Value ||
            seat.Role is not (KnowledgeAudienceRole.Actor or KnowledgeAudienceRole.GameMaster)) return null;
        if (seat.Role == KnowledgeAudienceRole.Actor && !Token(seat.ActorId)) return null;
        if (seat.Role == KnowledgeAudienceRole.GameMaster && seat.ActorId is not null) return null;
        return new(
            seat.Role == KnowledgeAudienceRole.GameMaster
                ? EntityMediaAudience.GameMaster
                : EntityMediaAudience.Player,
            seat.ActorId);
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= 200 && !value.Any(char.IsWhiteSpace);
}

internal static class SharedWebsiteContext
{
    // Only the endpoint access filter creates these principals. Never infer website authority
    // from a URL, forwarded identity header, browser perspective, or an MCP request.
    public static bool IsTrusted(HttpContext? context) =>
        context?.Connection.RemoteIpAddress is not null &&
        !context.Request.Path.StartsWithSegments(ServerConfiguration.McpEndpoint) &&
        context.User.Identity is { IsAuthenticated: true } identity &&
        identity.AuthenticationType is WebAccessPolicy.LocalAuthenticationType or
            WebAccessPolicy.TailscaleAuthenticationType or WebAccessPolicy.AnonymousPublicAuthenticationType;
}

internal sealed class ConfigurationLocalKnowledgeSeatProvider(
    IConfiguration? configuration, IHttpContextAccessor? http = null)
    : ILocalKnowledgeSeatProvider
{
    public LocalKnowledgeSeatSnapshot Current()
    {
        var section = configuration?.GetSection("Knowledge:LocalPlayer");
        var authorizedRoles = (section?.GetSection("RoleEntityIds").GetChildren()
            .ToDictionary(value => value.Key, value => value.Value ?? "", StringComparer.Ordinal))
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (SharedWebsiteContext.IsTrusted(http?.HttpContext))
            return new(true, "shared-website", section?["ApplicationId"] ?? "",
                section?["CampaignId"] ?? "", null, KnowledgeAudienceRole.GameMaster,
                AuthorizedRoleEntityIds: authorizedRoles);
        var role = section?["Role"] switch
        {
            null or "Actor" => KnowledgeAudienceRole.Actor,
            "GameMaster" => KnowledgeAudienceRole.GameMaster,
            _ => (KnowledgeAudienceRole)(-1)
        };
        // A higher-priority role override does not erase an ActorId supplied by appsettings.
        // Normalize the mutually exclusive seat shape here so selecting GameMaster cannot inherit
        // the configured local player's actor identity from a lower-priority provider.
        var actorId = role == KnowledgeAudienceRole.GameMaster ? null : section?["ActorId"];
        return new(
            section?.GetValue<bool>("Enabled") ?? false,
            section?["PrincipalId"] ?? "",
            section?["ApplicationId"] ?? "",
            section?["CampaignId"] ?? "",
            actorId,
            role,
            Array.AsReadOnly((section?.GetSection("SourceIds").GetChildren()
                .Select(value => value.Value ?? "").ToArray()) ?? []),
            authorizedRoles);
    }
}

/// <summary>
/// Adapts the host's trusted configuration into opaque application role bindings. This adapter
/// deliberately does not infer bindings from legacy seat fields or interpret application roles.
/// </summary>
internal sealed class LocalApplicationQueryAuthorizedContextProvider(
    ILocalKnowledgeSeatProvider seats,
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore entities) : IApplicationQueryAuthorizedContextProvider
{
    public async Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        cancellationToken.ThrowIfCancellationRequested();
        var seat = seats.Current();
        var values = seat.AuthorizedRoleEntityIds;
        var stateSpace = stateSpaces.Get(stateSpaceId);
        if (!seat.Enabled || seat.ApplicationId != applicationId.Value || stateSpace is null
            || stateSpace.ApplicationRevision.ApplicationId != applicationId
            || !Token(stateSpaceId) || values is null
            || values.Count > 32 || values.Any(value => !Key(value.Key) || !Token(value.Value)))
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (await entities.GetEntityAsync(stateSpaceId, value.Value, cancellationToken) is null)
                return null;
            result.Add(value.Key, value.Value);
        }
        return result;
    }

    private static bool Key(string value) => value.Split('.').All(segment =>
        segment is { Length: > 0 and <= 63 } && char.IsAsciiLetterLower(segment[0])
        && segment.All(character => char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character) || character == '-'));
    private static bool Token(string? value) => value is { Length: > 0 and <= 200 }
        && value == value.Trim() && !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace);
}

/// <summary>
/// Website admission grants one shared application context. Other callers retain the configured
/// loopback seat. Campaign selection is still resolved through the application binding.
/// </summary>
internal sealed class LocalKnowledgeAudiencePolicy(
    IHttpContextAccessor http,
    ILocalKnowledgeSeatProvider seats) : IAuthorizedKnowledgeAudiencePolicy
{
    public Task<KnowledgeAudienceResolution> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seat = seats.Current();
        if (!Valid(seat) || !Token(campaignId) ||
            (seat.Role != KnowledgeAudienceRole.GameMaster && campaignId != seat.CampaignId) ||
            !(Loopback(http.HttpContext?.Connection.RemoteIpAddress) || SharedWebsiteContext.IsTrusted(http.HttpContext)))
            return Task.FromResult(KnowledgeAudienceResolution.Denied());

        var effectiveCampaignId = seat.Role == KnowledgeAudienceRole.GameMaster
            ? campaignId
            : seat.CampaignId;

        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy = SharedWebsiteContext.IsTrusted(http.HttpContext)
                ? "shared-website-v1" : "local-loopback-seat-v1",
            seat.Enabled,
            seat.PrincipalId,
            seat.ApplicationId,
            seat.CampaignId,
            effectiveCampaignId,
            seat.ActorId,
            seat.Role,
            SourceIds = seat.SourceIds?.Order(StringComparer.Ordinal).ToArray() ?? []
        })));
        return Task.FromResult(new KnowledgeAudienceResolution(new(
            seat.PrincipalId,
            effectiveCampaignId,
            seat.Role,
            seat.ActorId,
            revision)));
    }

    private static bool Valid(LocalKnowledgeSeatSnapshot seat)
    {
        if (!seat.Enabled || !Token(seat.PrincipalId) || !Token(seat.CampaignId) ||
            !Enum.IsDefined(seat.Role)) return false;
        if (seat.SourceIds is { Count: > 32 } ||
            seat.SourceIds is not null && (seat.SourceIds.Any(value => !Token(value)) ||
                seat.SourceIds.Distinct(StringComparer.Ordinal).Count() != seat.SourceIds.Count))
            return false;
        if (seat.Role == KnowledgeAudienceRole.GameMaster && seat.ActorId is not null) return false;
        if (seat.Role == KnowledgeAudienceRole.Actor && !Token(seat.ActorId)) return false;
        try { return ApplicationIdentifier.Parse(seat.ApplicationId).Value == seat.ApplicationId; }
        catch (ArgumentException) { return false; }
    }

    private static bool Loopback(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= 200 && !value.Any(char.IsWhiteSpace);
}
