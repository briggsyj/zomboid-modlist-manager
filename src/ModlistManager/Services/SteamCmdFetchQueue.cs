using System.Threading.Channels;

namespace ModlistManager.Services;

/// <summary>In-process queue of ModRequest IDs awaiting a SteamCMD mod.info fetch.</summary>
public class SteamCmdFetchQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public void Enqueue(int modRequestId) => _channel.Writer.TryWrite(modRequestId);

    public ChannelReader<int> Reader => _channel.Reader;
}
