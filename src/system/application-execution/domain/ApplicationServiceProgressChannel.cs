using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Transient, single-invocation progress outlet for trusted C# consumers. The runtime alone calls
/// TryBind, TryWrite and Complete; only Reader is given to a consumer. No writer or channel enters
/// JavaScript. Attempts and serialized bytes are charged even when full or closed.
/// </summary>
[JsonConverter(typeof(RejectApplicationServiceProgressChannelJsonConverter))]
public sealed class ApplicationServiceProgressChannel
{
    private readonly object _gate = new();
    private readonly Channel<ApplicationServiceProgressFrame> _channel =
        Channel.CreateBounded<ApplicationServiceProgressFrame>(new BoundedChannelOptions(ApplicationReadOnlyServiceLimits.ProgressChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false
        });
    private bool _bound;
    private bool _closed;
    private int _attempts;
    private int _bytes;
    private int _sequence;

    public ChannelReader<ApplicationServiceProgressFrame> Reader => _channel.Reader;

    public bool TryBind()
    {
        lock (_gate)
        {
            if (_bound) return false;
            _bound = true;
            return true;
        }
    }

    public ApplicationServiceProgressDisposition TryWrite(string dataJson)
    {
        lock (_gate)
        {
            if (!_bound) throw new InvalidOperationException("The progress outlet is not bound to an invocation.");
            if (_attempts >= ApplicationReadOnlyServiceLimits.MaximumProgressFrames)
                throw new InteractionContractException("SERVICE_PROGRESS_LIMIT", "The progress attempt allowance is exhausted.");
            _attempts++;
            var frame = new ApplicationServiceProgressFrame(_sequence + 1, dataJson);
            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(frame));
            if (bytes > ApplicationReadOnlyServiceLimits.MaximumProgressBytesPerRoot - _bytes)
                throw new InteractionContractException("SERVICE_PROGRESS_LIMIT", "The progress byte allowance is exhausted.");
            _bytes += bytes;
            if (_closed) return ApplicationServiceProgressDisposition.Closed;
            if (!_channel.Writer.TryWrite(frame)) return ApplicationServiceProgressDisposition.Backpressured;
            _sequence++;
            return ApplicationServiceProgressDisposition.Accepted;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _closed = true;
            _channel.Writer.TryComplete();
        }
    }
}

public sealed class RejectApplicationServiceProgressChannelJsonConverter : JsonConverter<ApplicationServiceProgressChannel>
{
    public override ApplicationServiceProgressChannel Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => throw new JsonException("Progress channels are host-only.");

    public override void Write(Utf8JsonWriter writer, ApplicationServiceProgressChannel value,
        JsonSerializerOptions options) => throw new JsonException("Progress channels are host-only.");
}
