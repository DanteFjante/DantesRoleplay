using System.Threading.Channels;

namespace DantesRoleplay.DataAccess;

/// <summary>Best-effort worker wake-up. Durable scanning remains the correctness path.</summary>
public sealed class StoryPlanWakeQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Wake(string storyPlanId) => _channel.Writer.TryWrite(storyPlanId);
    public ValueTask<string> ReadAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);
}
