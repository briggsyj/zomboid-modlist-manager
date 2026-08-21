using System.Threading.Channels;

namespace ModlistManager.Services;

/// <summary>In-process queue of Mod IDs awaiting a Project Zomboid Mod ID lookup.</summary>
public class ModIdFetchQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public void Enqueue(int modId) => _channel.Writer.TryWrite(modId);

    public ChannelReader<int> Reader => _channel.Reader;
}
