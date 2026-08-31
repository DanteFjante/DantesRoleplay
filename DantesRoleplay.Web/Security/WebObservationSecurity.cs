using System.Data.Common;
using DantesRoleplay.Authorization;
using DantesRoleplay.Applications;
using DantesRoleplay.TriggerScheduling;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Security;

public sealed record WebObservationRequestDecision(
    bool Allowed,
    int StatusCode,
    TrustedPrincipalContext? Principal,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed class WebObservationRequestGuard
{
    private readonly WebPrivateOperatorGuard operators;
    private readonly WebAccessPolicy? access;
    private readonly IPhoneCompanionAuthenticator? devices;

    public WebObservationRequestGuard(WebPrivateOperatorGuard operators)
    {
        this.operators = operators;
    }

    public WebObservationRequestGuard(WebPrivateOperatorGuard operators, WebAccessPolicy access,
        IPhoneCompanionAuthenticator devices)
    {
        this.operators = operators;
        this.access = access;
        this.devices = devices;
    }

    public WebObservationRequestDecision Evaluate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = operators.Evaluate(context, PrivateOperatorCapability.TriggerObservationSubmit);
        if (!decision.Allowed)
            return Denied(StatusCodes.Status403Forbidden,
                decision.ErrorCode ?? "OBSERVATION_OPERATOR_DENIED",
                decision.ErrorMessage ?? "Private observation access is required.");
        if (!HttpMethods.IsPost(context.Request.Method))
            return Denied(StatusCodes.Status405MethodNotAllowed,
                "OBSERVATION_METHOD_DENIED", "Observations use POST.");
        if (!IsJson(context.Request.ContentType))
            return Denied(StatusCodes.Status415UnsupportedMediaType,
                "OBSERVATION_JSON_REQUIRED", "Observations require the application/json content type.");

        return new(true, StatusCodes.Status200OK,
            TrustedPrincipalContext.VerifiedPrincipal(
                decision.Evidence.PrincipalReference,
                decision.Evidence.AuthenticationMethod));
    }

    public async Task<WebObservationRequestDecision> EvaluateAsync(HttpContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Request.Headers.ContainsKey(PhoneCompanionIdentity.CredentialHeader))
            return Evaluate(context);
        if (access is null || devices is null)
            return Denied(StatusCodes.Status403Forbidden, "PHONE_CREDENTIAL_DENIED",
                "The phone credential was not accepted.");
        var accessDecision = access.Evaluate(context);
        if (!accessDecision.Allowed)
            return Denied(StatusCodes.Status403Forbidden,
                accessDecision.ErrorCode ?? "REMOTE_ACCESS_DENIED",
                accessDecision.ErrorMessage ?? "Private transport access is required.");
        if (!HttpMethods.IsPost(context.Request.Method))
            return Denied(StatusCodes.Status405MethodNotAllowed,
                "OBSERVATION_METHOD_DENIED", "Observations use POST.");
        if (!IsJson(context.Request.ContentType))
            return Denied(StatusCodes.Status415UnsupportedMediaType,
                "OBSERVATION_JSON_REQUIRED", "Observations require the application/json content type.");
        var values = context.Request.Headers[PhoneCompanionIdentity.CredentialHeader];
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 96)
            return Denied(StatusCodes.Status403Forbidden, "PHONE_CREDENTIAL_DENIED",
                "The phone credential was not accepted.");
        try
        {
            var route = context.Request.RouteValues.TryGetValue("applicationId", out var raw)
                ? Convert.ToString(raw) : null;
            var applicationId = ApplicationIdentifier.Parse(route ?? string.Empty);
            var decision = await devices.AuthenticateAsync(applicationId, values[0]!, cancellationToken);
            return decision.Allowed && decision.Principal is not null
                ? new(true, StatusCodes.Status200OK, decision.Principal)
                : Denied(StatusCodes.Status403Forbidden, "PHONE_CREDENTIAL_DENIED",
                    "The phone credential was not accepted.");
        }
        catch (ArgumentException)
        {
            return Denied(StatusCodes.Status403Forbidden, "PHONE_CREDENTIAL_DENIED",
                "The phone credential was not accepted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable, "PHONE_AUTHENTICATION_UNAVAILABLE",
                "Phone credential authentication is temporarily unavailable.");
        }
    }

    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static WebObservationRequestDecision Denied(int status, string code, string message) =>
        new(false, status, null, code, message);
}

public sealed class WebObservationRequestFilter(WebObservationRequestGuard guard) : IEndpointFilter
{
    private static readonly object PrincipalKey = new();

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        WebInterfaceSecurity.ApplyHeaders(context.HttpContext.Response);
        var decision = await guard.EvaluateAsync(context.HttpContext, context.HttpContext.RequestAborted);
        if (!decision.Allowed)
            return Results.Json(new { error = decision.ErrorCode, message = decision.ErrorMessage },
                statusCode: decision.StatusCode);

        context.HttpContext.Items[PrincipalKey] = decision.Principal!;
        return await next(context);
    }

    public static TrustedPrincipalContext GetPrincipal(HttpContext context) =>
        context.Items.TryGetValue(PrincipalKey, out var value) && value is TrustedPrincipalContext principal
            ? principal
            : throw new InvalidOperationException("The observation authorization filter did not supply a principal.");
}
