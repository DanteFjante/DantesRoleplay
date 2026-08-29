using DantesRoleplay.DataAccess;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.Tools;
using DantesRoleplay.Tools.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class FeedbackToolTests : IDisposable
{
    private readonly string _database = Path.GetTempFileName();
    private readonly string _export;

    public FeedbackToolTests() => _export = _database + ".json";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _database, _database + "-wal", _database + "-shm", _export })
            if (File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public async Task Triage_and_redacted_export_use_the_local_developer_surface()
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite($"Data Source={_database}").Options;
        await using (var db = new DantesRoleplayDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.SystemFeedbackReports.Add(new SystemFeedbackReport
            {
                Id = "feedback." + Guid.NewGuid().ToString("n"),
                RequestToken = "feedback-request." + Guid.NewGuid().ToString("n"),
                PayloadFingerprint = new string('f', 64),
                Category = SystemFeedbackCategory.Defect,
                Impact = SystemFeedbackImpact.Degraded,
                Summary = "Local report summary.",
                Observed = "Sensitive test prose.",
                CreatedAt = DateTime.UtcNow,
                SubmissionOperationId = Guid.NewGuid().ToString("n")
            });
            await db.SaveChangesAsync();
        }

        SystemFeedbackReport report;
        await using (var reader = new DantesRoleplayDbContext(options))
            report = await reader.SystemFeedbackReports.SingleAsync();
        var tool = new FeedbackTool();
        var triageOut = new StringWriter();
        var triage = await tool.RunAsync(new ToolContext(
            ["triage", report.Id],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["database"] = _database,
                ["to"] = "acknowledged",
                ["expected-revision"] = "0",
                ["note"] = "Reproduced locally."
            }, _database, triageOut, new StringWriter()), CancellationToken.None);

        Assert.Equal(0, triage);
        Assert.Contains("acknowledged", triageOut.ToString(), StringComparison.Ordinal);
        var export = await tool.RunAsync(new ToolContext(
            ["export", _export],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["database"] = _database,
                ["format"] = "json",
                ["ids"] = report.Id,
                ["redact-ids"] = report.Id
            }, _database, new StringWriter(), new StringWriter()), CancellationToken.None);

        Assert.Equal(0, export);
        var content = await File.ReadAllTextAsync(_export);
        Assert.Contains("dantes-system-feedback-export-v1", content, StringComparison.Ordinal);
        Assert.Contains("[redacted from export]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive test prose.", content, StringComparison.Ordinal);
        Assert.DoesNotContain(report.RequestToken, content, StringComparison.Ordinal);
        Assert.DoesNotContain(report.PayloadFingerprint, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retention_commands_list_eligible_reports_and_archive_reversibly()
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite($"Data Source={_database}").Options;
        var closedAt = DateTime.UtcNow.AddDays(-181);
        var report = new SystemFeedbackReport
        {
            Id = "feedback." + Guid.NewGuid().ToString("n"),
            RequestToken = "feedback-request." + Guid.NewGuid().ToString("n"),
            PayloadFingerprint = new string('a', 64),
            Category = SystemFeedbackCategory.Defect,
            Impact = SystemFeedbackImpact.Minor,
            State = SystemFeedbackState.Resolved,
            TriageRevision = 1,
            Summary = "Eligible local report.",
            Observed = "Local-only retention test.",
            CreatedAt = closedAt.AddDays(-1),
            SubmissionOperationId = Guid.NewGuid().ToString("n"),
            Dispositions =
            [
                new SystemFeedbackDisposition
                {
                    Id = "feedback-disposition." + Guid.NewGuid().ToString("n"),
                    ReportId = "",
                    Revision = 1,
                    FromState = SystemFeedbackState.Acknowledged,
                    ToState = SystemFeedbackState.Resolved,
                    Note = "Resolved locally.",
                    CreatedAt = closedAt
                }
            ]
        };
        report.Dispositions.Single().ReportId = report.Id;
        await using (var db = new DantesRoleplayDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.SystemFeedbackReports.Add(report);
            await db.SaveChangesAsync();
        }

        var tool = new FeedbackTool();
        var asOf = DateTime.UtcNow.ToString("O");
        var eligibleOut = new StringWriter();
        var eligible = await tool.RunAsync(new ToolContext(
            ["retention", "eligible"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["database"] = _database,
                ["as-of"] = asOf
            }, _database, eligibleOut, new StringWriter()), CancellationToken.None);

        Assert.Equal(0, eligible);
        Assert.Contains(report.Id, eligibleOut.ToString(), StringComparison.Ordinal);

        var archiveOut = new StringWriter();
        var archive = await tool.RunAsync(new ToolContext(
            ["retention", "archive", report.Id],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["database"] = _database,
                ["as-of"] = asOf,
                ["expected-retention-revision"] = "0",
                ["note"] = "Archived after the local retention window."
            }, _database, archiveOut, new StringWriter()), CancellationToken.None);

        Assert.Equal(0, archive);
        Assert.Contains("archived true", archiveOut.ToString(), StringComparison.Ordinal);
        await using var reader = new DantesRoleplayDbContext(options);
        var persisted = await reader.SystemFeedbackReports.Include(item => item.RetentionActions).SingleAsync();
        Assert.Equal(SystemFeedbackState.Resolved, persisted.State);
        Assert.NotNull(persisted.ArchivedAt);
        Assert.Equal(1, persisted.RetentionRevision);
        Assert.Single(persisted.RetentionActions);
    }
}
