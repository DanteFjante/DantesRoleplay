namespace DantesRoleplay.MCPServer;

/// <summary>
/// Reports the independently versioned pieces required to serve one application. A listener is not
/// considered ready merely because the process accepts HTTP connections.
/// </summary>
public static class ApplicationReadinessWebEndpoint
{
    public static async Task<IResult> ReadAsync(
        string applicationId,
        HttpContext context,
        ApplicationReadinessService readiness,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            var report = await readiness.ReadAsync(applicationId, cancellationToken);
            return Results.Json(report, statusCode: report.Status == "ready"
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
        }
        catch (ApplicationReadinessException exception)
        {
            return Results.BadRequest(new
            {
                status = "failed",
                code = exception.Code,
                message = exception.Message
            });
        }
    }
}
