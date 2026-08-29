namespace DantesRoleplay.HostSettings;

public sealed class HostSettingOverride
{
    public required string Key { get; set; }
    public int CurrentVersion { get; set; }
    public int AppliedVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<HostSettingOverrideVersion> Versions { get; } = new List<HostSettingOverrideVersion>();
}

public sealed class HostSettingOverrideVersion
{
    public long Id { get; set; }
    public required string SettingKey { get; set; }
    public int Version { get; set; }
    public string? ValueJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public required string CreatedBy { get; set; }
    public required string OperationId { get; set; }
    public HostSettingOverride? Setting { get; set; }
}

public sealed record HostSettingOverrideHead(
    string Key,
    int CurrentVersion,
    int AppliedVersion,
    string? ValueJson,
    DateTime UpdatedAtUtc);

public sealed record HostSettingOverrideRevision(
    string Key,
    int Version,
    string? ValueJson,
    DateTime CreatedAtUtc,
    string CreatedBy,
    string OperationId);

public sealed record HostSettingOverrideWrite(
    string Key,
    int ExpectedRevision,
    string? ValueJson,
    string Actor,
    string Tool,
    int? RollbackTargetRevision = null);

public sealed record HostSettingOverrideWriteResult(
    HostSettingOverrideRevision Revision,
    int AppliedVersion);

public sealed class HostSettingOverrideStoreException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public interface IHostSettingOverrideStore
{
    Task<IReadOnlyDictionary<string, HostSettingOverrideHead>> GetHeadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostSettingOverrideRevision>> ListVersionsAsync(string key, int? beforeVersion, int limit, CancellationToken cancellationToken = default);
    Task<HostSettingOverrideRevision?> GetVersionAsync(string key, int version, CancellationToken cancellationToken = default);
    Task<HostSettingOverrideWriteResult> AppendAsync(HostSettingOverrideWrite write, CancellationToken cancellationToken = default);
    Task<int> MarkPendingAppliedAsync(CancellationToken cancellationToken = default);
}
