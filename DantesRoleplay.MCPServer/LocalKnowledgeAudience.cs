using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DantesRoleplay.MCPServer;

public sealed record LocalKnowledgeSeatSnapshot(
    bool Enabled,
    string PrincipalId,
    string ApplicationId,
    string CampaignId,
    string? ActorId,
    KnowledgeAudienceRole Role = KnowledgeAudienceRole.Actor);

public interface ILocalKnowledgeSeatProvider
{
    LocalKnowledgeSeatSnapshot Current();
}

internal sealed class ConfigurationLocalKnowledgeSeatProvider(IConfiguration? configuration)
    : ILocalKnowledgeSeatProvider
{
    public LocalKnowledgeSeatSnapshot Current()
    {
        var section = configuration?.GetSection("Knowledge:LocalPlayer");
        var role = section?["Role"] switch
        {
            null or "Actor" => KnowledgeAudienceRole.Actor,
            "GameMaster" => KnowledgeAudienceRole.GameMaster,
            _ => (KnowledgeAudienceRole)(-1)
        };
        return new(
            section?.GetValue<bool>("Enabled") ?? false,
            section?["PrincipalId"] ?? "",
            section?["ApplicationId"] ?? "",
            section?["CampaignId"] ?? "",
            section?["ActorId"],
            role);
    }
}

/// <summary>
/// Temporary private-table policy. It trusts only current host configuration and the server-side
/// loopback peer; request bodies, browser selections, forwarded headers, and remote access never
/// select a principal, role, application, campaign, or actor.
/// </summary>
internal sealed class LocalKnowledgeAudiencePolicy(
    IHttpContextAccessor http,
    ILocalKnowledgeSeatProvider seats,
    KnowledgeApplicationSelection application) : IAuthorizedKnowledgeAudiencePolicy
{
    public Task<KnowledgeAudienceResolution> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seat = seats.Current();
        if (!Valid(seat) || seat.ApplicationId != application.ApplicationId ||
            campaignId != seat.CampaignId || !Loopback(http.HttpContext?.Connection.RemoteIpAddress))
            return Task.FromResult(KnowledgeAudienceResolution.Denied());

        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy = "local-loopback-seat-v1",
            seat.Enabled,
            seat.PrincipalId,
            seat.ApplicationId,
            seat.CampaignId,
            seat.ActorId,
            seat.Role
        })));
        return Task.FromResult(new KnowledgeAudienceResolution(new(
            seat.PrincipalId,
            seat.CampaignId,
            seat.Role,
            seat.ActorId,
            revision)));
    }

    private static bool Valid(LocalKnowledgeSeatSnapshot seat)
    {
        if (!seat.Enabled || !Token(seat.PrincipalId) || !Token(seat.CampaignId) ||
            !Enum.IsDefined(seat.Role)) return false;
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
