using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SystemFeedback;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SystemFeedbackTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public SystemFeedbackTests(SqliteFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Submit_persists_an_append_only_report_and_audit_record()
    {
        await using var db = _fixture.CreateContext();
        var service = new SystemFeedbackService(db, new OperationLog(db));
        var token = "feedback-request." + Guid.NewGuid().ToString("n");

        var result = await service.SubmitAsync(Request(token), "Report a testing problem.", ["procedure.system.feedback"]);

        Assert.True(result.Ok);
        Assert.False(result.Duplicate);
        Assert.NotNull(result.Report);
        Assert.Matches("^feedback\\.[0-9a-f]{32}$", result.Report!.Id);
        Assert.Equal("defect", result.Report.Category);
        Assert.Equal("blocked", result.Report.Impact);
        Assert.Equal(1, await db.SystemFeedbackReports.CountAsync(r => r.RequestToken == token));
        var operation = await db.Operations.SingleAsync(o => o.Id == result.OperationId);
        Assert.True(operation.Success);
        Assert.Equal(result.Report.Id, operation.Subject);
        Assert.Equal(result.OperationId, result.Report.SubmissionOperationId);
    }

    [Fact]
    public async Task Same_token_and_payload_is_a_duplicate_without_a_second_report()
    {
        await using var db = _fixture.CreateContext();
        var service = new SystemFeedbackService(db, new OperationLog(db));
        var token = "feedback-request." + Guid.NewGuid().ToString("n");

        var first = await service.SubmitAsync(Request(token), "first", []);
        var second = await service.SubmitAsync(Request(token), "retry", []);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.True(second.Duplicate);
        Assert.Equal(first.Report!.Id, second.Report!.Id);
        Assert.Equal(1, await db.SystemFeedbackReports.CountAsync(r => r.RequestToken == token));
        Assert.Equal(2, await db.Operations.CountAsync(o => o.Subject == first.Report.Id));
    }

    [Fact]
    public async Task Same_token_with_different_payload_is_rejected_and_keeps_original()
    {
        await using var db = _fixture.CreateContext();
        var service = new SystemFeedbackService(db, new OperationLog(db));
        var token = "feedback-request." + Guid.NewGuid().ToString("n");
        var first = await service.SubmitAsync(Request(token), "first", []);
        var changed = Request(token) with { Summary = "A different observed problem" };

        var conflict = await service.SubmitAsync(changed, "retry", []);

        Assert.True(first.Ok);
        Assert.False(conflict.Ok);
        Assert.Equal("FEEDBACK_REQUEST_CONFLICT", conflict.Problem!.Code);
        Assert.Equal(1, await db.SystemFeedbackReports.CountAsync(r => r.RequestToken == token));
    }

    [Fact]
    public async Task Invalid_text_is_recorded_as_a_failure_without_a_report()
    {
        await using var db = _fixture.CreateContext();
        var service = new SystemFeedbackService(db, new OperationLog(db));
        var token = "feedback-request." + Guid.NewGuid().ToString("n");
        var result = await service.SubmitAsync(Request(token) with { Summary = " trailing " }, "test", []);

        Assert.False(result.Ok);
        Assert.Equal("INVALID_FEEDBACK", result.Problem!.Code);
        Assert.Equal(0, await db.SystemFeedbackReports.CountAsync(r => r.RequestToken == token));
        Assert.False((await db.Operations.SingleAsync(o => o.Id == result.OperationId)).Success);
    }

    [Fact]
    public async Task Procedure_references_are_resolved_to_the_current_version()
    {
        await using var db = _fixture.CreateContext();
        db.ProcedureContracts.Add(new ProcedureContract { Id = "procedure.test.feedback", Category = "system", CurrentVersion = 3, Status = ProcedureStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new SystemFeedbackService(db, new OperationLog(db));

        var result = await service.SubmitAsync(Request("feedback-request." + Guid.NewGuid().ToString("n")) with { RelatedProcedureIds = ["procedure.test.feedback"] }, "test", []);

        Assert.True(result.Ok);
        var reference = Assert.Single(result.Report!.RelatedProcedures);
        Assert.Equal("procedure.test.feedback", reference.Id);
        Assert.Equal(3, reference.Version);
    }

    private static SystemFeedbackSubmitRequest Request(string token) => new(
        token, "defect", "blocked", "Campaign resume is inconsistent", "The active session header was omitted.", "The active session header should be returned.", ["Start a session.", "Query campaign resume."], [], []);
}
