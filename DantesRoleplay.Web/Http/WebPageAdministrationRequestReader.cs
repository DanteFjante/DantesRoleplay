using System.Text.Json;
using DantesRoleplay.Web.Pages;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

internal static class WebPageAdministrationRequestReader
{
    public const int MaximumJsonBodyBytes = WebPageBundleLimits.MaximumHtmlBytes + 4096;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<T> ReadAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ContentLength > MaximumJsonBodyBytes)
            throw new WebPageAdministrationRequestException(
                "BODY_TOO_LARGE", "The page-administration request exceeds its size limit.",
                StatusCodes.Status413PayloadTooLarge);
        await using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumJsonBodyBytes)
                throw new WebPageAdministrationRequestException(
                    "BODY_TOO_LARGE", "The page-administration request exceeds its size limit.",
                    StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0)
            throw Invalid();
        try
        {
            return JsonSerializer.Deserialize<T>(buffer.ToArray(), RequestJson) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static WebPageAdministrationRequestException Invalid() =>
        new("INVALID_BODY", "The page-administration request body is invalid.",
            StatusCodes.Status400BadRequest);
}

internal sealed class WebPageAdministrationRequestException(
    string code,
    string message,
    int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
