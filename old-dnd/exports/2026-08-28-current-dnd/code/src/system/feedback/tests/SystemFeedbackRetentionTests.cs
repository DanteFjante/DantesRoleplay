using DantesRoleplay.DataAccess;
using DantesRoleplay.SystemFeedback;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SystemFeedbackRetentionTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public SystemFeedbackRetentionTests(SqliteFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Eligibility_uses_exact_180_and_90_day_closed_state_boundaries()
    {
        await using var db = _fixture.CreateContext();
        var closedAt = DateTime.UtcNow.AddDays(-180);
        var ordinary = await AddClosedAsync(db, SystemFeedbackCategory.Defect, closedAt);
        var positive = await AddClosedAsync(db, SystemFeedbackCategory.Positive, DateTime.UtcNow.AddDays(-90));
        var service = new SystemFeedbackRetentionService(db);

        var beforeOrdinary = await service.FindEligibleAsync(new SystemFeedbackRetentionQuery(closedAt.AddDays(180).AddTicks(-1)));
        var atOrdinary = await service.FindEligibleAsync(new SystemFeedbackRetentionQuery(closedAt.AddDays(180)));
        var atPositive = await service.FindEligibleAsync(new SystemFeedbackRetentionQuery(DateTime.UtcNow));

        Assert.DoesNotContain(beforeOrdinary.Reports, item => item.ReportId == ordinary.Id);
        Assert.Contains(atOrdinary.Reports, item => item.ReportId == ordinary.Id);
        Assert.Contains(atPositive.Reports, item => item.ReportId == positive.Id);
    }

    [Fact]
    public async Task Hold_excludes_archive_and_release_does_not_archive()
    {
        await using var db = _fixture.CreateContext();
        var report = await AddClosedAsync(db, SystemFeedbackCategory.Defect, DateTime.UtcNow.AddDays(-181));
        var service = new SystemFeedbackRetentionService(db);

        var hold = await service.TransitionAsync(new SystemFeedbackRetentionActionRequest(report.Id, "place-hold", 0, "ticket-123", "Preserve while this is investigated."));
        var archive = await service.TransitionAsync(new SystemFeedbackRetentionActionRequest(report.Id, "archive", 1, null, "Archive closed report.", DateTime.UtcNow));
        var release = await service.TransitionAsync(new SystemFeedbackRetentionActionRequest(report.Id, "release-hold", 1, "ticket-123", "Investigation is complete."));

        Assert.True(hold.Ok);
        Assert.False(archive.Ok);
        Assert.Equal("FEEDBACK_RETENTION_INELIGIBLE", archive.Problem!.Code);
        Assert.True(release.Ok);
        Assert.Null(release.Retention!.ArchivedAt);
        Assert.Equal("none", release.Retention.HoldState);
        Assert.Equal(2, release.Retention.RetentionRevision);
    }

    [Fact]
    public async Task Archive_restore_and_stale_revision_preserve_immutable_history()
    {
        await using var db = _fixture.CreateContext();
        var report = await AddClosedAsync(db, SystemFeedbackCategory.Defect, DateTime.UtcNow.AddDays(-181));
        var service = new SystemFeedbackRetentionService(db);

        var archived = await service.TransitionAsync(new SystemFeedbackRetentionActionRequest(report.Id, "archive", 0, null, "Ready for local archival.", DateTime.UtcNow));
        var stale = await service.TransitionAsync(new SystemFeedbackRetentionActionRequest(report.Id, "restore", 0, null, "Must not be accepted."));
        var restored = await service.TransitionAsync(new SystemFeedbackRetentionActionRequest(report.Id, "restore", 1, null, "Needed for a follow-up."));

        Assert.True(archived.Ok);
        Assert.NotNull(archived.Retention!.ArchivedAt);
        Assert.False(stale.Ok);
        Assert.Equal("FEEDBACK_RETENTION_CONFLICT", stale.Problem!.Code);
        Assert.True(restored.Ok);
        Assert.Null(restored.Retention!.ArchivedAt);
        Assert.Equal(["archive", "restore"], restored.Retention.Actions.Select(item => item.Action));
        Assert.Equal(2, await db.SystemFeedbackRetentionActions.CountAsync(item => item.ReportId == report.Id));
    }

    [Fact]
    public async Task Reopen_and_reclose_starts_a_new_eligibility_clock()
    {
        await using var db = _fixture.CreateContext();
        var report = await AddClosedAsync(db, SystemFeedbackCategory.Defect, DateTime.UtcNow.AddDays(-181));
        var triage = new SystemFeedbackAdministrationService(db);
        Assert.True((await triage.TransitionAsync(new SystemFeedbackDispositionRequest(report.Id, "open", 1, "The issue reproduced again."))).Ok);
        Assert.True((await triage.TransitionAsync(new SystemFeedbackDispositionRequest(report.Id, "resolved", 2, "The follow-up is resolved."))).Ok);

        var eligible = await new SystemFeedbackRetentionService(db).FindEligibleAsync(new SystemFeedbackRetentionQuery(DateTime.UtcNow));

        Assert.DoesNotContain(eligible.Reports, item => item.ReportId == report.Id);
    }

    private static async Task<SystemFeedbackReport> AddClosedAsync(DantesRoleplayDbContext db, SystemFeedbackCategory category, DateTime closedAt)
    {
        var id = "feedback." + Guid.NewGuid().ToString("n");
        var report = new SystemFeedbackReport
        {
            Id = id,
            RequestToken = "feedback-request." + Guid.NewGuid().ToString("n"),
            PayloadFingerprint = new string('b', 64),
            Category = category,
            Impact = SystemFeedbackImpact.Minor,
            State = SystemFeedbackState.Resolved,
            TriageRevision = 1,
            Summary = "Retention fixture report.",
            Observed = "Retention fixture detail.",
            CreatedAt = closedAt.AddDays(-1),
            SubmissionOperationId = Guid.NewGuid().ToString("n"),
            Dispositions = [new SystemFeedbackDisposition
            {
                Id = "feedback-disposition." + Guid.NewGuid().ToString("n"),
                ReportId = id,
                Revision = 1,
                FromState = SystemFeedbackState.Open,
                ToState = SystemFeedbackState.Resolved,
                Note = "Fixture closed for retention testing.",
                CreatedAt = closedAt
            }]
        };
        db.SystemFeedbackReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }
}
