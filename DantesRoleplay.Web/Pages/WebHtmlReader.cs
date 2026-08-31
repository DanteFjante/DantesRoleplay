using System.Text;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Pages;

public sealed class WebHtmlUploadException(
    string code,
    string message,
    int statusCode,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}

public sealed class WebHtmlReader
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<string> ReadAsync(
        Stream input,
        long? declaredLength = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (declaredLength > WebPageBundleLimits.MaximumHtmlBytes)
        {
            throw TooLarge();
        }

        await using var buffered = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffered.Length + read > WebPageBundleLimits.MaximumHtmlBytes)
            {
                throw TooLarge();
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        string html;
        try
        {
            html = StrictUtf8.GetString(buffered.GetBuffer(), 0, checked((int)buffered.Length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new WebHtmlUploadException(
                "INVALID_HTML_ENCODING",
                "The HTML document must be valid UTF-8.",
                StatusCodes.Status400BadRequest,
                exception);
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new WebHtmlUploadException(
                "EMPTY_HTML",
                "The HTML document cannot be empty.",
                StatusCodes.Status400BadRequest);
        }

        return html;
    }

    private static WebHtmlUploadException TooLarge() =>
        new(
            "HTML_TOO_LARGE",
            "The HTML document exceeds the 1 MiB limit.",
            StatusCodes.Status413PayloadTooLarge);
}
