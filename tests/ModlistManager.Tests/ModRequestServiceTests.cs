using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class ModRequestServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public ModRequestServiceTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    private async Task<ModRequest> GetRequestAsync(int requestId) =>
        (await _service.GetRequestsAsync()).Single(r => r.Id == requestId);

    [Fact]
    public async Task CreateRequestAsync_NormalizesNameAndParsesWorkshopId()
    {
        var result = await _service.CreateRequestAsync(
            "Better Sorting", "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "  Jane DOE  ");

        Assert.True(result.Success);
        var requests = await _service.GetRequestsAsync();
        var created = Assert.Single(requests);
        Assert.Equal("3783094058", created.Mod!.WorkshopId);
        Assert.Equal("jane doe", created.RequesterName);
        Assert.Equal(RequestStatus.Pending, created.Status);
    }

    [Fact]
    public async Task CreateRequestAsync_FailsWhenWorkshopInputIsInvalid()
    {
        var result = await _service.CreateRequestAsync("Title", "not a link", "Someone");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Empty(await _service.GetRequestsAsync());
    }

    [Theory]
    [InlineData("", "3783094058", "Someone")]
    [InlineData("Title", "3783094058", "")]
    public async Task CreateRequestAsync_FailsWhenRequiredFieldsMissing(string title, string workshop, string name)
    {
        var result = await _service.CreateRequestAsync(title, workshop, name);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateRequestAsync_ReusesExistingModForSameWorkshopIdAndGame()
    {
        await _service.CreateRequestAsync("A", "111", "Alice");
        await _service.CreateRequestAsync("B", "111", "Bob");

        var requests = await _service.GetRequestsAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].ModId, requests[1].ModId);
    }

    [Fact]
    public async Task SetStatusAsync_UpdatesStatusAndDecidedAt()
    {
        var created = await _service.CreateRequestAsync("Title", "12345", "Someone");

        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var request = Assert.Single(await _service.GetRequestsAsync(RequestStatus.Approved));
        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.NotNull(request.DecidedAtUtc);
    }

    [Fact]
    public async Task SetStatusAsync_Approved_AddsModToModlist()
    {
        var created = await _service.CreateRequestAsync("Title", "111", "Alice");

        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var modlist = await _service.GetModlistAsync();
        var entry = Assert.Single(modlist);
        Assert.Equal("111", entry.WorkshopId);
        Assert.True(entry.IsInModlist);
        Assert.NotNull(entry.AddedToModlistAtUtc);
    }

    [Fact]
    public async Task SetStatusAsync_TwoRequestsForSameMod_ApprovingEitherKeepsOneModlistEntry()
    {
        var a = await _service.CreateRequestAsync("A", "111", "Alice");
        var b = await _service.CreateRequestAsync("B", "111", "Bob");

        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(b.RequestId!.Value, RequestStatus.Approved);

        var modlist = await _service.GetModlistAsync();
        Assert.Single(modlist);
    }

    [Fact]
    public async Task SetStatusAsync_UnapprovingLastApprovedRequest_RemovesModFromModlist()
    {
        var created = await _service.CreateRequestAsync("Title", "111", "Alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Declined);

        Assert.Empty(await _service.GetModlistAsync());
    }

    [Fact]
    public async Task SetStatusAsync_UnapprovingOneOfTwoRequests_KeepsModOnModlist()
    {
        var a = await _service.CreateRequestAsync("A", "111", "Alice");
        var b = await _service.CreateRequestAsync("B", "111", "Bob");
        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(b.RequestId!.Value, RequestStatus.Approved);

        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Declined);

        var modlist = await _service.GetModlistAsync();
        var entry = Assert.Single(modlist);
        Assert.True(entry.IsInModlist);
    }

    [Fact]
    public async Task BuildApprovedWorkshopIdExportAsync_JoinsOnlyApprovedIdsWithSemicolons()
    {
        var a = await _service.CreateRequestAsync("A", "111", "Alice");
        var b = await _service.CreateRequestAsync("B", "222", "Bob");
        await _service.CreateRequestAsync("C", "333", "Carl"); // left pending

        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(b.RequestId!.Value, RequestStatus.Approved);

        var export = await _service.BuildApprovedWorkshopIdExportAsync();

        Assert.Equal("111;222", export);
    }

    [Fact]
    public async Task BuildApprovedZomboidModIdExportAsync_PrefixesEachIdWithBackslash()
    {
        var created = await _service.CreateRequestAsync("A", "111", "Alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);
        var modId = (await GetRequestAsync(created.RequestId!.Value)).ModId;
        await _service.AddManualModIdAsync(modId, "FirstMod", null);
        await _service.AddManualModIdAsync(modId, "SecondMod", null);

        var export = await _service.BuildApprovedZomboidModIdExportAsync();

        Assert.Equal(@"\FirstMod;\SecondMod", export);
    }

    [Fact]
    public async Task BuildApprovedZomboidModIdExportAsync_ExcludesOtherGames()
    {
        var created = await _service.CreateRequestAsync("A", "111", "Alice", game: "Some Other Game");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);
        var modId = (await GetRequestAsync(created.RequestId!.Value)).ModId;
        await _service.AddManualModIdAsync(modId, "ShouldNotAppear", null);

        var export = await _service.BuildApprovedZomboidModIdExportAsync();

        Assert.Equal(string.Empty, export);
    }

    [Fact]
    public async Task DeletePzModIdAsync_RemovesEntry()
    {
        var created = await _service.CreateRequestAsync("A", "111", "Alice");
        var modId = (await GetRequestAsync(created.RequestId!.Value)).ModId;
        await _service.AddManualModIdAsync(modId, "SomeMod", null);
        var pzModId = (await GetRequestAsync(created.RequestId!.Value)).Mod!.PzModIds.Single().Id;

        await _service.DeletePzModIdAsync(pzModId);

        Assert.Empty((await GetRequestAsync(created.RequestId!.Value)).Mod!.PzModIds);
    }

    [Fact]
    public async Task GetDistinctRequesterNamesAsync_ReturnsNormalizedDistinctSortedNames()
    {
        await _service.CreateRequestAsync("A", "111", "Bob");
        await _service.CreateRequestAsync("B", "222", "  BOB  ");
        await _service.CreateRequestAsync("C", "333", "Alice");

        var names = await _service.GetDistinctRequesterNamesAsync();

        Assert.Equal(["alice", "bob"], names);
    }

    public void Dispose() => _dbFactory.Dispose();
}
