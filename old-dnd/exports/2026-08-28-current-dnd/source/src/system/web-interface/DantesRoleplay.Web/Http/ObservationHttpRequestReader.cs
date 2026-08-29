using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.TriggerScheduling;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

public sealed class ObservationHttpRequestException(string code, string message, int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed partial class ObservationHttpRequestReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] EnvelopeProperties = ["requestId", "source", "structure", "observedAt", "data"];
    private static readonly string[] SourceProperties = ["id", "instanceId", "occurrenceId"];
    private static readonly string[] StructureProperties = ["id", "version"];

    public async Task<ObservationSubmission> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = await ReadBodyAsync(request.Body, request.ContentLength, cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = TriggerSchedulingLimits.MaximumJsonDepth
            });
        }
        catch (JsonException exception) when (
            exception.Message.Contains("depth", StringComparison.OrdinalIgnoreCase))
        {
            throw TooLarge("OBSERVATION_REQUEST_BOUNDS", "The observation request exceeds its JSON resource bounds.");
        }
        catch (JsonException)
        {
            throw Bad("OBSERVATION_JSON_INVALID", "The observation request must be valid bounded JSON.");
        }

        using (document)
        {
            EnforceResourceBounds(document.RootElement);
            RequireExactObject(document.RootElement, EnvelopeProperties, "OBSERVATION_ENVELOPE_INVALID");
            var source = document.RootElement.GetProperty("source");
            var structure = document.RootElement.GetProperty("structure");
            RequireExactObject(source, SourceProperties, "OBSERVATION_SOURCE_INVALID");
            RequireExactObject(structure, StructureProperties, "OBSERVATION_STRUCTURE_INVALID");

            var observedAtText = RequiredString(document.RootElement, "observedAt");
            if (!Rfc3339Utc().IsMatch(observedAtText) ||
                !DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var observedAt) ||
                observedAt.Offset != TimeSpan.Zero)
                throw Bad("OBSERVATION_TIME_NOT_UTC", "The observation time must be RFC 3339 UTC ending in Z.");
            var version = RequiredInt32(structure, "version");
            if (version < 1)
                throw Bad("INVALID_STRUCTURE_VERSION", "The structure version must be positive.");
            var data = document.RootElement.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Object)
                throw Bad("OBSERVATION_DATA_ROOT", "Observation data must be a JSON object.");

            try
            {
                return ObservationSubmission.Create(
                    RequiredString(document.RootElement, "requestId"),
                    ObservationSourceReference.Create(
                        RequiredString(source, "id"),
                        RequiredString(source, "instanceId"),
                        RequiredString(source, "occurrenceId")),
                    ObservationStructureReference.Create(RequiredString(structure, "id"), version),
                    observedAt,
                    data.GetRawText());
            }
            catch (TriggerSchedulingContractException exception)
            {
                var bounded = exception.Code is "OBSERVATION_DATA_TOO_LARGE" or "OBSERVATION_DATA_DEPTH" or
                    "OBSERVATION_DATA_NODES" or "OBSERVATION_DATA_PROPERTIES" or
                    "OBSERVATION_DATA_ARRAY_ITEMS" or "OBSERVATION_DATA_STRING";
                throw new ObservationHttpRequestException(exception.Code, exception.Message,
                    bounded ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest);
            }
        }
    }

    private static async Task<string> ReadBodyAsync(
        Stream body,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        if (contentLength > TriggerSchedulingLimits.MaximumRequestBytes)
            throw TooLarge("OBSERVATION_REQUEST_TOO_LARGE", "The observation request exceeds 65,536 bytes.");
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                var read = await body.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0) break;
                if (output.Length + read > TriggerSchedulingLimits.MaximumRequestBytes)
                    throw TooLarge("OBSERVATION_REQUEST_TOO_LARGE", "The observation request exceeds 65,536 bytes.");
                output.Write(rented, 0, read);
            }
            try { return StrictUtf8.GetString(output.GetBuffer(), 0, checked((int)output.Length)); }
            catch (DecoderFallbackException)
            {
                throw Bad("OBSERVATION_UTF8_INVALID", "The observation request must use valid UTF-8.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void EnforceResourceBounds(JsonElement root)
    {
        var state = new ResourceState();
        state.Visit(root, 1);
    }

    private static void RequireExactObject(JsonElement value, string[] required, string code)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Bad(code, "The observation request shape is invalid.");
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != required.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            names.Except(required, StringComparer.Ordinal).Any() || required.Except(names, StringComparer.Ordinal).Any())
            throw Bad(code, "The observation request contains missing, duplicate, or unknown fields.");
    }

    private static string RequiredString(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
            throw Bad("OBSERVATION_FIELD_INVALID", "A required observation field has the wrong type.");
        return value.GetString()!;
    }

    private static int RequiredInt32(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw Bad("OBSERVATION_FIELD_INVALID", "A required observation field has the wrong type.");
        return result;
    }

    private static ObservationHttpRequestException Bad(string code, string message) =>
        new(code, message, StatusCodes.Status400BadRequest);

    private static ObservationHttpRequestException TooLarge(string code, string message) =>
        new(code, message, StatusCodes.Status413PayloadTooLarge);

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?Z$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Rfc3339Utc();

    private sealed class ResourceState
    {
        private int nodes;
        private int properties;
        private int arrayItems;

        public void Visit(JsonElement value, int depth)
        {
            if (depth > TriggerSchedulingLimits.MaximumJsonDepth || ++nodes > TriggerSchedulingLimits.MaximumJsonNodes)
                throw TooLarge("OBSERVATION_REQUEST_BOUNDS", "The observation request exceeds its JSON resource bounds.");
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject())
                    {
                        if (++properties > TriggerSchedulingLimits.MaximumObjectProperties ||
                            Encoding.UTF8.GetByteCount(property.Name) > TriggerSchedulingLimits.MaximumStringBytes)
                            throw TooLarge("OBSERVATION_REQUEST_BOUNDS", "The observation request exceeds its JSON resource bounds.");
                        Visit(property.Value, depth + 1);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray())
                    {
                        if (++arrayItems > TriggerSchedulingLimits.MaximumArrayItems)
                            throw TooLarge("OBSERVATION_REQUEST_BOUNDS", "The observation request exceeds its JSON resource bounds.");
                        Visit(item, depth + 1);
                    }
                    break;
                case JsonValueKind.String:
                    if (Encoding.UTF8.GetByteCount(value.GetString() ?? string.Empty) > TriggerSchedulingLimits.MaximumStringBytes)
                        throw TooLarge("OBSERVATION_REQUEST_BOUNDS", "The observation request exceeds its JSON resource bounds.");
                    break;
            }
        }
    }
}
