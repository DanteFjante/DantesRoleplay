using System.Text;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DantesRoleplay.Web.Hosting;

public static class WebInterfaceEndpoints
{
    public static IEndpointRouteBuilder MapDantesRoleplayWeb(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/pages/{id}", UploadPageAsync);
        endpoints.MapGet("/ui/{id}", GetPageAsync);
        endpoints.MapGet("/api/data/entity/{id}", GetEntityDataAsync);
        endpoints.MapGet("/api/data/{componentType}/{entityId}", GetComponentDataAsync);
        return endpoints;
    }

    private static async Task<IResult> UploadPageAsync(
        string id,
        HttpRequest request,
        IWebPageStore pages,
        CancellationToken cancellationToken)
    {
        if (!WebPageId.IsValid(id))
        {
            return Results.BadRequest(new
            {
                error = "INVALID_PAGE_ID",
                message = "Page IDs may contain letters, numbers, dots, underscores, and hyphens."
            });
        }

        if (request.ContentType is null ||
            !request.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new
                {
                    error = "HTML_REQUIRED",
                    message = "Upload a complete HTML document using the text/html content type."
                },
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var html = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            return Results.BadRequest(new
            {
                error = "EMPTY_HTML",
                message = "The HTML document cannot be empty."
            });
        }

        var saved = await pages.SaveAndActivateAsync(id, html, cancellationToken);
        return Results.Json(new
        {
            saved.Id,
            saved.Revision,
            Url = $"/ui/{saved.Id}"
        });
    }

    private static async Task<IResult> GetPageAsync(
        string id,
        IWebPageStore pages,
        CancellationToken cancellationToken)
    {
        var page = await pages.GetActiveAsync(id, cancellationToken);
        return page is null
            ? Results.NotFound()
            : Results.Text(page.Html, "text/html", Encoding.UTF8);
    }

    private static Task<IResult> GetEntityDataAsync(
        string id,
        DynamicDataReader data,
        CancellationToken cancellationToken) =>
        GetDataAsync("entity", id, data, cancellationToken);

    private static Task<IResult> GetComponentDataAsync(
        string componentType,
        string entityId,
        DynamicDataReader data,
        CancellationToken cancellationToken) =>
        GetDataAsync(componentType, entityId, data, cancellationToken);

    private static async Task<IResult> GetDataAsync(
        string type,
        string entityId,
        DynamicDataReader data,
        CancellationToken cancellationToken)
    {
        var document = await data.ReadAsync(type, entityId, cancellationToken);
        return document is null
            ? Results.NotFound()
            : Results.Text(document.Json.ToJsonString(), "application/json", Encoding.UTF8);
    }
}
