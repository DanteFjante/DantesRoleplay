using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Fail-closed host mode used while an updater probes a candidate runtime on loopback.
/// The updater owns every migration or other mutation before this host starts.
/// </summary>
internal static class RuntimeVerification
{
    internal const string ConfigurationKey = "Runtime:VerificationOnly";
    private const string ApplicationConfigurationKey = "Knowledge:LocalPlayer:ApplicationId";
    private const string RejectionCode = "RUNTIME_VERIFICATION_ONLY";

    internal sealed record State(bool Enabled, string ApplicationId)
    {
        internal static readonly State Disabled = new(false, string.Empty);
    }

    /// <summary>
    /// Call after application service composition and before <c>builder.Build()</c>.
    /// Application hosted services are removed without resolving their factories, so neither
    /// their constructors nor their start methods can claim queued work during a verification.
    /// </summary>
    internal static State PrepareServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue(ConfigurationKey, false)) return State.Disabled;

        var applicationId = configuration[ApplicationConfigurationKey]?.Trim();
        if (string.IsNullOrWhiteSpace(applicationId)
            || !Regex.IsMatch(applicationId, "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                $"{ConfigurationKey} requires {ApplicationConfigurationKey} for the exact readiness route.");

        RequireExplicitLoopbackListener(configuration);
        foreach (var descriptor in services
                     .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                         && !IsFrameworkHostedService(descriptor))
                     .ToArray())
            services.Remove(descriptor);

        return new State(true, applicationId);
    }

    /// <summary>Install before authorization, rate limiting, and endpoint dispatch.</summary>
    internal static IApplicationBuilder UseRuntimeVerification(
        this IApplicationBuilder application,
        State state)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Enabled) return application;

        return application.Use(next => context => InvokeAsync(context, next, state));
    }

    internal static async Task InvokeAsync(HttpContext context, RequestDelegate next, State state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Enabled)
        {
            await next(context);
            return;
        }

        if (IsLoopback(context.Connection.RemoteIpAddress)
            && IsLoopbackHost(context.Request.Host.Host)
            && IsReadMethod(context.Request.Method)
            && IsProbePath(context.Request.Path, state.ApplicationId))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            code = RejectionCode,
            message = "This candidate runtime accepts only the configured loopback verification probes."
        }, cancellationToken: context.RequestAborted);
    }

    private static bool IsReadMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

    private static bool IsProbePath(PathString path, string applicationId)
    {
        if (path == "/" || path == "/api/audience-context") return true;
        return path == $"/api/readiness/applications/{applicationId}";
    }

    private static bool IsLoopback(IPAddress? address) => address is not null && IPAddress.IsLoopback(address);

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    private static bool IsFrameworkHostedService(ServiceDescriptor descriptor)
    {
        var assembly = descriptor.ImplementationType?.Assembly
            ?? descriptor.ImplementationInstance?.GetType().Assembly
            ?? descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly;
        var assemblyName = assembly?.GetName().Name;
        return assemblyName is not null
            && (assemblyName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
    }

    private static void RequireExplicitLoopbackListener(IConfiguration configuration)
    {
        var configured = configuration["urls"] ?? configuration["ASPNETCORE_URLS"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                $"{ConfigurationKey} requires an explicit loopback-only URL.");

        var urls = configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (urls.Length == 0 || urls.Any(url => !IsLoopbackListener(url)))
            throw new InvalidOperationException(
                $"{ConfigurationKey} requires every configured URL to use a loopback host.");
    }

    private static bool IsLoopbackListener(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || !(parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0
            || parsed.AbsolutePath != "/")
            return false;
        if (parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var host = parsed.Host.Trim('[', ']');
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
