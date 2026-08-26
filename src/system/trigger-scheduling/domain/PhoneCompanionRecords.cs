namespace DantesRoleplay.TriggerScheduling;

public sealed class PhoneCompanionDeviceRecord
{
    public required string ApplicationId { get; set; }
    public required string DeviceId { get; set; }
    public required string PrincipalId { get; set; }
    public required string SourceId { get; set; }
    public int SourceVersion { get; set; }
    public required string CredentialVerifier { get; set; }
    public required string PermissionProfile { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<PhoneCompanionDeviceStructureRecord> Structures { get; } =
        new List<PhoneCompanionDeviceStructureRecord>();
    public ICollection<PhoneCompanionDeviceStatusRecord> StatusRevisions { get; } =
        new List<PhoneCompanionDeviceStatusRecord>();
}

public sealed class PhoneCompanionDeviceStructureRecord
{
    public required string ApplicationId { get; set; }
    public required string DeviceId { get; set; }
    public int Ordinal { get; set; }
    public required string StructureId { get; set; }
    public int StructureVersion { get; set; }
    public required string StructureHash { get; set; }
    public PhoneCompanionDeviceRecord? Device { get; set; }
}

public sealed class PhoneCompanionDeviceStatusRecord
{
    public required string ApplicationId { get; set; }
    public required string DeviceId { get; set; }
    public int Revision { get; set; }
    public required string Status { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public PhoneCompanionDeviceRecord? Device { get; set; }
}

public sealed class PhoneCompanionDeviceCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string DeviceId { get; set; }
    public int CurrentRevision { get; set; }
}
