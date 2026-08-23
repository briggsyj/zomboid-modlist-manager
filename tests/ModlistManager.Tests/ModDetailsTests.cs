using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class ModDetailsTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public ModDetailsTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    private async Task<int> CreateModAsync(string workshopId = "111")
    {
        var created = await _service.CreateRequestAsync(workshopId, "alice");
        return (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId).ModId;
    }

    [Fact]
    public async Task AModStartsWithAnUnknownModIdSource()
    {
        await CreateModAsync();

        var request = Assert.Single(await _service.GetRequestsAsync());
        Assert.Equal(ModIdSource.Unknown, request.Mod!.ModIdSource);
    }

    [Fact]
    public async Task AdminNotesRoundTrip()
    {
        var modId = await CreateModAsync();

        await _service.SetModAdminNotesAsync(modId, "  Conflicts with Brita's.  ");

        var request = Assert.Single(await _service.GetRequestsAsync());
        Assert.Equal("Conflicts with Brita's.", request.Mod!.AdminNotes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAdminNotesClearTheField(string? notes)
    {
        var modId = await CreateModAsync();
        await _service.SetModAdminNotesAsync(modId, "something");

        await _service.SetModAdminNotesAsync(modId, notes);

        var request = Assert.Single(await _service.GetRequestsAsync());
        Assert.Null(request.Mod!.AdminNotes);
    }

    [Fact]
    public async Task SetModAdminNotesAsync_IgnoresAnUnknownMod()
    {
        await _service.SetModAdminNotesAsync(9999, "nothing to attach this to");

        Assert.Empty(await _service.GetRequestsAsync());
    }

    /// <summary>
    /// With SteamCMD disabled the workshop API is the only source, so a completed fetch must record
    /// that - this is the value the details panel shows.
    /// </summary>
    [Fact]
    public async Task AFetchViaTheWorkshopApiIsRecordedAsSuch()
    {
        var queue = new ModIdFetchQueue();
        var service = new ModRequestService(_dbFactory, queue);
        var api = StubApi("A Mod", "blurb\r\nWorkshop ID: 111\r\nMod ID: StubbedMod");
        var options = Options.Create(new SteamCmdOptions { Enabled = false });
        var fetch = new ModIdFetchService(
            queue, _dbFactory, api, new SteamCmdModInfoReader(options), options,
            NullLogger<ModIdFetchService>.Instance);

        await fetch.StartAsync(CancellationToken.None);
        await service.CreateRequestAsync("111", "alice");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        ModRequest? request = null;
        while (DateTime.UtcNow < deadline)
        {
            request = (await service.GetRequestsAsync()).SingleOrDefault();
            if (request?.Mod!.FetchStatus is ModIdFetchStatus.Completed or ModIdFetchStatus.Failed)
            {
                break;
            }

            await Task.Delay(50);
        }

        await fetch.StopAsync(CancellationToken.None);
        fetch.Dispose();

        Assert.NotNull(request);
        Assert.Equal(ModIdFetchStatus.Completed, request!.Mod!.FetchStatus);
        Assert.Equal(ModIdSource.SteamWorkshopApi, request.Mod.ModIdSource);
        Assert.Equal("StubbedMod", Assert.Single(request.Mod.PzModIds).Value);
    }

    public void Dispose() => _dbFactory.Dispose();

    /// <summary>
    /// A real SteamWorkshopApiClient wired to a canned HTTP response, so the JSON shape and the
    /// description parsing are still exercised rather than mocked away.
    /// </summary>
    private static SteamWorkshopApiClient StubApi(string title, string description)
    {
        var payload = JsonSerializer.Serialize(new
        {
            response = new
            {
                publishedfiledetails = new[] { new { result = 1, title, description } }
            }
        });

        return new SteamWorkshopApiClient(
            new StubHttpClientFactory(new StubHandler(payload)),
            NullLogger<SteamWorkshopApiClient>.Instance);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // The client disposes what it creates, so keep the handler alive across calls.
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
