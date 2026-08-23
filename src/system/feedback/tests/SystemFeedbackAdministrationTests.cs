using DantesRoleplay.DataAccess;
using DantesRoleplay.SystemFeedback;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SystemFeedbackAdministrationTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public SystemFeedbackAdministrationTests(SqliteFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Transition_appends_an_immutable_disposition_and_advances_revision()
    {
        await using var db = _fixture.CreateContext();
        var report = await AddReportAsync(db);
        var result = await new SystemFeedbackAdministrationService(db).TransitionAsync(
            new SystemFeedbackDispositionRequest(report.Id, "acknowledged", 0, "Reproduced locally; investigating."));

        Assert.True(result.Ok);
        Assert.Equal("acknowledged", result.Report!.Report.State);
        Assert.Equal(1, result.Report.TriageRevision);
        var disposition = Assert.Single(result.Report.Dispositions);
        Assert.Equal(1, disposition.Revision);
        Assert.Equal("open", disposition.FromState);
        Assert.Equal("acknowledged", disposition.ToState);
        Assert.Equal(1, await db.SystemFeedbackDispositions.CountAsync(value => value.ReportId == report.Id));
    }

    [Fact]
    public async Task Stale_revision_is_rejected_without_another_disposition()
    {
        await using var db = _fixture.CreateContext();
        var report = await AddReportAsync(db);
        var service = new SystemFeedbackAdministrationService(db);
        Assert.True((await service.TransitionAsync(new SystemFeedbackDispositionRequest(report.Id, "acknowledged", 0, "Reviewed and queued."))).Ok);

        var stale = await service.TransitionAsync(new SystemFeedbackDispositionRequest(report.Id, "resolved", 0, "This must not be accepted."));

        Assert.False(stale.Ok);
        Assert.Equal("FEEDBACK_TRIAGE_CONFLICT", stale.Problem!.Code);
        Assert.Equal("acknowledged", stale.CurrentState);
        Assert.Equal(1, stale.CurrentRevision);
        Assert.Equal(1, await db.SystemFeedbackDispositions.CountAsync(value => value.ReportId == report.Id));
    }

    [Fact]
    public async Task Export_redacts_only_the_selected_report_prose()
    {
        await using var db = _fixture.CreateContext();
        var report = await AddReportAsync(db);
        var service = new SystemFeedbackAdministrationService(db);
        Assert.True((await service.TransitionAsync(new SystemFeedbackDispositionRequest(report.Id, "dismissed", 0, "Duplicate of a tracked defect."))).Ok);

        var exported = await service.BuildExportAsync(
            new SystemFeedbackAdministrationQuery([report.Id], Limit: 1),
            new HashSet<string>([report.Id], StringComparer.Ordinal));

        Assert.True(exported.Ok);
        var item = Assert.Single(exported.Document!.Reports);
        Assert.True(item.Redacted);
        Assert.Equal("[redacted from export]", item.Summary);
        Assert.Equal("[redacted from export]", item.Observed);
        Assert.Equal(["[redacted from export]"], item.ReproductionSteps);
        Assert.Equal("[redacted from export]", Assert.Single(item.Dispositions).Note);
        var stored = await db.SystemFeedbackReports.Include(value => value.Steps).SingleAsync(value => value.Id == report.Id);
        Assert.Equal("The internal detail should stay in the database.", stored.Observed);
        Assert.Equal("Open a campaign.", Assert.Single(stored.Steps).Text);
    }

    private static async Task<SystemFeedbackReport> AddReportAsync(DantesRoleplayDbContext db)
    {
        var report = new SystemFeedbackReport
        {
            Id = "feedback." + Guid.NewGuid().ToString("n"),
            RequestToken = "feedback-request." + Guid.NewGuid().ToString("n"),
            PayloadFingerprint = new string('a', 64),
            Category = SystemFeedbackCategory.Defect,
            Impact = SystemFeedbackImpact.Degraded,
            Summary = "A concise local report.",
            Observed = "The internal detail should stay in the database.",
            Expected = "The expected detail should stay in the database.",
            CreatedAt = DateTime.UtcNow,
            SubmissionOperationId = Guid.NewGuid().ToString("n"),
            Steps = [new SystemFeedbackStep { ReportId = string.Empty, Ordinal = 0, Text = "Open a campaign." }]
        };
        report.Steps.Single().ReportId = report.Id;
        db.SystemFeedbackReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }
}
