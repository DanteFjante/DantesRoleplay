using System.Text;
using DantesRoleplay.TriggerScheduling;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

internal static class TriggerAdministrationHttpRequestReader
{
    internal static async Task<TriggerSchedulingAdministrationCommand> ReadAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > TriggerSchedulingLimits.MaximumRequestBytes)
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD_TOO_LARGE",
                "The trigger administration command exceeds the configured size limit.");
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true);
        var buffer = new char[4096];
        var builder = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) break;
            builder.Append(buffer, 0, count);
            if (builder.Length > TriggerSchedulingLimits.MaximumRequestBytes)
                throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD_TOO_LARGE",
                    "The trigger administration command exceeds the configured size limit.");
        }
        var json = builder.ToString();
        if (Encoding.UTF8.GetByteCount(json) > TriggerSchedulingLimits.MaximumRequestBytes)
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD_TOO_LARGE",
                "The trigger administration command exceeds the configured size limit.");
        return TriggerSchedulingAdministrationCommand.Parse(json);
    }
}
