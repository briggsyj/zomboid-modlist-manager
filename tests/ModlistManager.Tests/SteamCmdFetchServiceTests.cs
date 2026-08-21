using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

/// <summary>
/// Exercises the real background fetch loop (BackgroundService.StartAsync + the shared queue) end
/// to end, independent of any UI/circuit concerns - this is what "click Retry fetch" ultimately
/// triggers. SteamCMD is never actually installed in CI/this sandbox, so these assert the specific
/// "executable not found" failure path, and that Retry genuinely re-queues and re-attempts rather
/// than being a no-op.
/// </summary>
public class SteamCmdFetchServiceTests : IAsyncDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly SteamCmdFetchQueue _queue = new();
    private readonly ModRequestService _requestService;
    private readonly SteamCmdFetchService _fetchService;

    public SteamCmdFetchServiceTests()
    {
        _requestService = new ModRequestService(_dbFactory, _queue);
        var options = Options.Create(new SteamCmdOptions
        {
            ExecutablePath = "definitely-not-a-real-steamcmd-binary",
            WorkshopContentRoot = Path.GetTempPath(),
            TimeoutSeconds = 5
        });
        _fetchService = new SteamCmdFetchService(_queue, _dbFactory, options, NullLogger<SteamCmdFetchService>.Instance);
    }

    private async Task<ModRequest> WaitForTerminalStatusAsync(int requestId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var request = (await _requestService.GetRequestsAsync()).Single(r => r.Id == requestId);
            if (request.Mod!.FetchStatus is ModIdFetchStatus.Completed or ModIdFetchStatus.Failed)
            {
                return request;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Fetch never reached a terminal status.");
    }

    [Fact]
    public async Task MissingSteamCmdExecutable_FailsWithClearMessage()
    {
        await _fetchService.StartAsync(CancellationToken.None);

        var created = await _requestService.CreateRequestAsync("Title", "111", "Alice");
        var request = await WaitForTerminalStatusAsync(created.RequestId!.Value, TimeSpan.FromSeconds(10));

        Assert.Equal(ModIdFetchStatus.Failed, request.Mod!.FetchStatus);
        Assert.Contains("executable not found", request.Mod.FetchLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryFetchAsync_ReQueuesAndReAttemptsAfterFailure()
    {
        await _fetchService.StartAsync(CancellationToken.None);

        var created = await _requestService.CreateRequestAsync("Title", "222", "Bob");
        var afterFirstAttempt = await WaitForTerminalStatusAsync(created.RequestId!.Value, TimeSpan.FromSeconds(10));
        Assert.Equal(ModIdFetchStatus.Failed, afterFirstAttempt.Mod!.FetchStatus);
        var modId = afterFirstAttempt.ModId;

        await _requestService.RetryFetchAsync(modId);

        // Immediately after RetryFetchAsync, status should already be back to Queued/Processing,
        // proving the click path itself (service call + queue write) is not a no-op.
        var immediatelyAfterRetry = (await _requestService.GetRequestsAsync()).Single(r => r.Id == created.RequestId!.Value);
        Assert.NotEqual(ModIdFetchStatus.Failed, immediatelyAfterRetry.Mod!.FetchStatus);

        var afterSecondAttempt = await WaitForTerminalStatusAsync(created.RequestId!.Value, TimeSpan.FromSeconds(10));
        Assert.Equal(ModIdFetchStatus.Failed, afterSecondAttempt.Mod!.FetchStatus);
    }

    public async ValueTask DisposeAsync()
    {
        await _fetchService.StopAsync(CancellationToken.None);
        _fetchService.Dispose();
        _dbFactory.Dispose();
    }
}
