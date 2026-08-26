using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.TriggerScheduling;

public enum ObservationDataClassification
{
    General,
    PrivacyMinimizedSignal,
    RawLocation,
    ThirdPartyNotificationContent
}

public enum PhoneCompanionPermissionProfile { PrivacyMinimizedSignals }
public enum PhoneCompanionStatus { Active, Revoked, StaleSource, StaleStructure }

public sealed record PhoneCompanionStructurePermission
{
    private PhoneCompanionStructurePermission(string id, int version)
    {
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw new TriggerSchedulingContractException("PHONE_STRUCTURE_VERSION",
            "The phone structure version must be positive.");
        Version = version;
    }
    public string Id { get; }
    public int Version { get; }
    public static PhoneCompanionStructurePermission Create(string id, int version) => new(id, version);
}

public sealed record PhoneCompanionRegistrationRequest
{
    private PhoneCompanionRegistrationRequest(ApplicationIdentifier applicationId, string deviceId,
        string sourceId, int sourceVersion, PhoneCompanionPermissionProfile permissionProfile,
        IReadOnlyList<PhoneCompanionStructurePermission> structures)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        DeviceId = PhoneCompanionIdentity.ValidateDeviceId(deviceId);
        SourceId = TriggerSchedulingIdentifier.Qualified(sourceId, nameof(sourceId));
        if (sourceVersion < 1) throw new TriggerSchedulingContractException("PHONE_SOURCE_VERSION",
            "The phone source version must be positive.");
        if (!Enum.IsDefined(permissionProfile)) throw new TriggerSchedulingContractException(
            "PHONE_PERMISSION_PROFILE", "The phone permission profile is invalid.");
        if (structures is null || structures.Count is < 1 or > 8 || structures.Any(value => value is null) ||
            structures.Select(value => (value.Id, value.Version)).Distinct().Count() != structures.Count)
            throw new TriggerSchedulingContractException("PHONE_STRUCTURE_PERMISSIONS",
                "A phone registration requires one to eight distinct exact structures.");
        SourceVersion = sourceVersion;
        PermissionProfile = permissionProfile;
        Structures = Array.AsReadOnly(structures.OrderBy(value => value.Id, StringComparer.Ordinal)
            .ThenBy(value => value.Version).ToArray());
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string DeviceId { get; }
    public string SourceId { get; }
    public int SourceVersion { get; }
    public PhoneCompanionPermissionProfile PermissionProfile { get; }
    public IReadOnlyList<PhoneCompanionStructurePermission> Structures { get; }

    public static PhoneCompanionRegistrationRequest Create(ApplicationIdentifier applicationId,
        string deviceId, string sourceId, int sourceVersion,
        IReadOnlyList<PhoneCompanionStructurePermission> structures) =>
        new(applicationId, deviceId, sourceId, sourceVersion,
            PhoneCompanionPermissionProfile.PrivacyMinimizedSignals, structures);
}

public sealed record PhoneCompanionRegistrationResult(
    PhoneCompanionDeviceView Device,
    string Credential);

public sealed record PhoneCompanionDeviceView(
    ApplicationIdentifier ApplicationId,
    string DeviceId,
    string PrincipalId,
    string SourceId,
    int SourceVersion,
    PhoneCompanionPermissionProfile PermissionProfile,
    IReadOnlyList<PhoneCompanionStructurePermission> Structures,
    PhoneCompanionStatus Status,
    int StatusRevision,
    DateTimeOffset CreatedAt,
    DateTimeOffset StatusRecordedAt);

public static partial class PhoneCompanionIdentity
{
    public const string AuthenticationMethod = "phone-companion.v1";
    public const string CredentialHeader = "DantesRoleplay-Device-Credential";
    private const string PrincipalDomain = "dantes-roleplay/phone-companion-principal/v1\0";
    private const string CredentialDomain = "dantes-roleplay/phone-companion-credential/v1\0";

    public static string ValidateDeviceId(string value)
    {
        if (value is null || !DeviceIdPattern().IsMatch(value))
            throw new TriggerSchedulingContractException("PHONE_DEVICE_ID",
                "The phone device ID is invalid.");
        return value;
    }

    public static string PrincipalId(ApplicationIdentifier applicationId, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ValidateDeviceId(deviceId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            PrincipalDomain + applicationId.Value + "\0" + deviceId));
        return "principal." + Convert.ToHexStringLower(hash);
    }

    public static string ValidateCredential(string value)
    {
        if (value is null || !CredentialPattern().IsMatch(value))
            throw new TriggerSchedulingContractException("PHONE_CREDENTIAL_INVALID",
                "The phone credential is invalid.");
        return value;
    }

    public static string CredentialVerifier(string credential)
    {
        ValidateCredential(credential);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CredentialDomain + credential)));
    }

    [GeneratedRegex("^phone-device\\.[0-9a-f]{32}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DeviceIdPattern();
    [GeneratedRegex("^phone-credential\\.[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CredentialPattern();
}

public interface IPhoneCompanionCredentialGenerator
{
    string Generate();
}

public sealed class RandomPhoneCompanionCredentialGenerator : IPhoneCompanionCredentialGenerator
{
    public string Generate() => "phone-credential." +
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}

public interface IPhoneCompanionRegistry
{
    Task<PhoneCompanionRegistrationResult> RegisterAsync(PhoneCompanionRegistrationRequest request,
        CancellationToken cancellationToken = default);
    Task<PhoneCompanionDeviceView?> RevokeAsync(ApplicationIdentifier applicationId, string deviceId,
        CancellationToken cancellationToken = default);
    Task<PhoneCompanionDeviceView?> GetAsync(ApplicationIdentifier applicationId, string deviceId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhoneCompanionDeviceView>> ListAsync(ApplicationIdentifier applicationId,
        int limit = 50, CancellationToken cancellationToken = default);
}

public sealed record PhoneCompanionAuthenticationResult(
    bool Allowed,
    TrustedPrincipalContext? Principal = null,
    string ErrorCode = "PHONE_CREDENTIAL_DENIED");

public interface IPhoneCompanionAuthenticator
{
    Task<PhoneCompanionAuthenticationResult> AuthenticateAsync(ApplicationIdentifier applicationId,
        string credential, CancellationToken cancellationToken = default);
}

public interface IObservationIngestionPolicy
{
    Task ValidateAsync(TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
        ObservationSubmission submission, CancellationToken cancellationToken = default);
}
