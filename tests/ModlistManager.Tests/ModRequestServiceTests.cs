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
        _service = new ModRequestService(_dbFactory, new SteamCmdFetchQueue());
    }

    [Fact]
    public async Task CreateRequestAsync_NormalizesNameAndParsesWorkshopId()
    {
        var result = await _service.CreateRequestAsync(
            "Better Sorting", "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "  Jane DOE  ");

        Assert.True(result.Success);
        var requests = await _service.GetRequestsAsync();
        var created = Assert.Single(requests);
        Assert.Equal("3783094058", created.WorkshopId);
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
    public async Task SetStatusAsync_UpdatesStatusAndDecidedAt()
    {
        var created = await _service.CreateRequestAsync("Title", "12345", "Someone");

        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var request = Assert.Single(await _service.GetRequestsAsync(RequestStatus.Approved));
        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.NotNull(request.DecidedAtUtc);
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
        var request = await _service.CreateRequestAsync("A", "111", "Alice");
        await _service.SetStatusAsync(request.RequestId!.Value, RequestStatus.Approved);
        await _service.AddManualModIdAsync(request.RequestId!.Value, "FirstMod", null);
        await _service.AddManualModIdAsync(request.RequestId!.Value, "SecondMod", null);

        var export = await _service.BuildApprovedZomboidModIdExportAsync();

        Assert.Equal(@"\FirstMod;\SecondMod", export);
    }

    [Fact]
    public async Task BuildApprovedZomboidModIdExportAsync_ExcludesOtherGames()
    {
        var request = await _service.CreateRequestAsync("A", "111", "Alice", game: "Some Other Game");
        await _service.SetStatusAsync(request.RequestId!.Value, RequestStatus.Approved);
        await _service.AddManualModIdAsync(request.RequestId!.Value, "ShouldNotAppear", null);

        var export = await _service.BuildApprovedZomboidModIdExportAsync();

        Assert.Equal(string.Empty, export);
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
