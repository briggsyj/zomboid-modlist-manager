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

    /// <summary>
    /// Adds a second request against an existing mod straight through the DbContext. The service now
    /// refuses to create one, but databases predating that rule still contain them, so the logic
    /// that copes with several requests per mod still needs covering.
    /// </summary>
    private async Task<int> AddLegacyDuplicateRequestAsync(string workshopId, string requesterName)
    {
        await using var db = _dbFactory.CreateDbContext();
        var mod = db.Mods.Single(m => m.WorkshopId == workshopId);
        var request = new ModRequest { ModId = mod.Id, RequesterName = requesterName, Status = RequestStatus.Pending };
        db.ModRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task CreateRequestAsync_NormalizesNameAndParsesWorkshopId()
    {
        var result = await _service.CreateRequestAsync(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "  Jane DOE  ");

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
        var result = await _service.CreateRequestAsync("not a link", "Someone");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Empty(await _service.GetRequestsAsync());
    }

    [Theory]
    [InlineData("3783094058", "")]
    [InlineData("3783094058", "   ")]
    [InlineData("", "Someone")]
    public async Task CreateRequestAsync_FailsWhenRequiredFieldsMissing(string workshop, string name)
    {
        var result = await _service.CreateRequestAsync(workshop, name);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateRequestAsync_DoesNotRequireATitle()
    {
        // The name comes from the workshop item, so a request only needs a link and a requester.
        var result = await _service.CreateRequestAsync("3783094058", "Alice");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ModDisplayNameFallsBackToTheWorkshopIdUntilTheTitleResolves()
    {
        await _service.CreateRequestAsync("3783094058", "Alice");

        var request = Assert.Single(await _service.GetRequestsAsync());
        Assert.Null(request.Mod!.Title);
        Assert.Equal("Workshop item 3783094058", request.Mod!.DisplayName);

        request.Mod.Title = "Vanilla Outfits Expanded";
        Assert.Equal("Vanilla Outfits Expanded", request.Mod.DisplayName);
    }

    [Fact]
    public async Task CreateRequestAsync_RejectsASecondRequestForTheSameWorkshopItem()
    {
        await _service.CreateRequestAsync("3783094058", "Alice");

        var second = await _service.CreateRequestAsync("3783094058", "Bob");

        Assert.False(second.Success);
        Assert.Contains("already been requested", second.Error);
        Assert.Single(await _service.GetRequestsAsync());
    }

    [Fact]
    public async Task CreateRequestAsync_RejectsADuplicateGivenAsAUrlRatherThanAnId()
    {
        await _service.CreateRequestAsync("3783094058", "Alice");

        var second = await _service.CreateRequestAsync(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "Bob");

        Assert.False(second.Success);
    }

    [Fact]
    public async Task CreateRequestAsync_AllowsTheSameWorkshopItemForADifferentGame()
    {
        await _service.CreateRequestAsync("111", "Alice");

        var other = await _service.CreateRequestAsync("111", "Bob", game: "Some Other Game");

        Assert.True(other.Success);
    }

    [Fact]
    public async Task SetStatusAsync_UpdatesStatusAndDecidedAt()
    {
        var created = await _service.CreateRequestAsync("12345", "Someone");

        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var request = Assert.Single(await _service.GetRequestsAsync(RequestStatus.Approved));
        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.NotNull(request.DecidedAtUtc);
    }

    [Fact]
    public async Task SetStatusAsync_Approved_AddsModToModlist()
    {
        var created = await _service.CreateRequestAsync("111", "Alice");

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
        var a = await _service.CreateRequestAsync("111", "Alice");
        var b = await AddLegacyDuplicateRequestAsync("111", "bob");

        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(b, RequestStatus.Approved);

        var modlist = await _service.GetModlistAsync();
        Assert.Single(modlist);
    }

    [Fact]
    public async Task SetStatusAsync_UnapprovingLastApprovedRequest_RemovesModFromModlist()
    {
        var created = await _service.CreateRequestAsync("111", "Alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Declined);

        Assert.Empty(await _service.GetModlistAsync());
    }

    [Fact]
    public async Task SetStatusAsync_UnapprovingOneOfTwoRequests_KeepsModOnModlist()
    {
        var a = await _service.CreateRequestAsync("111", "Alice");
        var b = await AddLegacyDuplicateRequestAsync("111", "bob");
        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(b, RequestStatus.Approved);

        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Declined);

        var modlist = await _service.GetModlistAsync();
        var entry = Assert.Single(modlist);
        Assert.True(entry.IsInModlist);
    }

    [Fact]
    public async Task BuildApprovedWorkshopIdExportAsync_JoinsOnlyApprovedIdsWithSemicolons()
    {
        var a = await _service.CreateRequestAsync("111", "Alice");
        var b = await _service.CreateRequestAsync("222", "Bob");
        await _service.CreateRequestAsync("333", "Carl"); // left pending

        await _service.SetStatusAsync(a.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(b.RequestId!.Value, RequestStatus.Approved);

        var export = await _service.BuildApprovedWorkshopIdExportAsync();

        Assert.Equal("111;222", export);
    }

    [Fact]
    public async Task BuildApprovedZomboidModIdExportAsync_PrefixesEachIdWithBackslash()
    {
        var created = await _service.CreateRequestAsync("111", "Alice");
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
        var created = await _service.CreateRequestAsync("111", "Alice", game: "Some Other Game");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);
        var modId = (await GetRequestAsync(created.RequestId!.Value)).ModId;
        await _service.AddManualModIdAsync(modId, "ShouldNotAppear", null);

        var export = await _service.BuildApprovedZomboidModIdExportAsync();

        Assert.Equal(string.Empty, export);
    }

    [Fact]
    public async Task DeletePzModIdAsync_RemovesEntry()
    {
        var created = await _service.CreateRequestAsync("111", "Alice");
        var modId = (await GetRequestAsync(created.RequestId!.Value)).ModId;
        await _service.AddManualModIdAsync(modId, "SomeMod", null);
        var pzModId = (await GetRequestAsync(created.RequestId!.Value)).Mod!.PzModIds.Single().Id;

        await _service.DeletePzModIdAsync(pzModId);

        Assert.Empty((await GetRequestAsync(created.RequestId!.Value)).Mod!.PzModIds);
    }

    [Fact]
    public async Task GetRequestsByStatusAsync_ReturnsOnlyTheRequestedStatuses()
    {
        var pending = await _service.CreateRequestAsync("111", "Alice");
        var backlogged = await _service.CreateRequestAsync("222", "Bob");
        var approved = await _service.CreateRequestAsync("333", "Carl");
        var declined = await _service.CreateRequestAsync("444", "Dave");

        await _service.SetStatusAsync(backlogged.RequestId!.Value, RequestStatus.Backlogged);
        await _service.SetStatusAsync(approved.RequestId!.Value, RequestStatus.Approved);
        await _service.SetStatusAsync(declined.RequestId!.Value, RequestStatus.Declined);

        // What the public request page asks for: everything still open.
        var open = await _service.GetRequestsByStatusAsync(RequestStatus.Pending, RequestStatus.Backlogged);

        Assert.Equal(
            [pending.RequestId, backlogged.RequestId],
            open.Select(r => (int?)r.Id).Order());
    }

    [Fact]
    public async Task GetRequestsByStatusAsync_ReturnsEmptyWhenNoStatusesGiven()
    {
        await _service.CreateRequestAsync("111", "Alice");

        Assert.Empty(await _service.GetRequestsByStatusAsync());
    }

    [Fact]
    public async Task GetRequestsByStatusAsync_IncludesTheModSoTitlesAndModIdsRender()
    {
        var created = await _service.CreateRequestAsync("111", "Alice");
        var modId = (await GetRequestAsync(created.RequestId!.Value)).ModId;
        await _service.AddManualModIdAsync(modId, "SomeMod", null);

        var request = Assert.Single(await _service.GetRequestsByStatusAsync(RequestStatus.Pending));

        Assert.NotNull(request.Mod);
        Assert.Equal("SomeMod", Assert.Single(request.Mod!.PzModIds).Value);
    }

    [Fact]
    public async Task GetDistinctRequesterNamesAsync_ReturnsNormalizedDistinctSortedNames()
    {
        await _service.CreateRequestAsync("111", "Bob");
        await _service.CreateRequestAsync("222", "  BOB  ");
        await _service.CreateRequestAsync("333", "Alice");

        var names = await _service.GetDistinctRequesterNamesAsync();

        Assert.Equal(["alice", "bob"], names);
    }

    public void Dispose() => _dbFactory.Dispose();
}
