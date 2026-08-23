using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

/// <summary>
/// The modlist page is public, so what a visitor may see is decided by the query, not by the UI.
/// </summary>
public class ModlistVisibilityTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public ModlistVisibilityTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    private async Task<int> ApproveAsync(string workshopId, string game = Mod.DefaultGame)
    {
        var created = await _service.CreateRequestAsync(workshopId, "alice", game: game);
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);
        return (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId).ModId;
    }

    [Fact]
    public async Task VisitorsDoNotSeeParkedMods()
    {
        await ApproveAsync("111");
        var parked = await ApproveAsync("222");
        await _service.SetModActiveAsync(parked, false);

        var visitorView = await _service.GetModlistAsync(activeOnly: true);

        var visible = Assert.Single(visitorView);
        Assert.Equal("111", visible.WorkshopId);
    }

    [Fact]
    public async Task AdminsSeeParkedModsAsWellAsActiveOnes()
    {
        await ApproveAsync("111");
        var parked = await ApproveAsync("222");
        await _service.SetModActiveAsync(parked, false);

        var adminView = await _service.GetModlistAsync();

        Assert.Equal(["111", "222"], adminView.Select(m => m.WorkshopId).Order());
        Assert.Contains(adminView, m => !m.IsActive);
    }

    [Fact]
    public async Task DefaultingToTheAdminViewMeansTheVisitorFilterMustBeAskedForExplicitly()
    {
        var parked = await ApproveAsync("111");
        await _service.SetModActiveAsync(parked, false);

        Assert.Single(await _service.GetModlistAsync());
        Assert.Empty(await _service.GetModlistAsync(activeOnly: true));
    }

    [Fact]
    public async Task ReactivatingBringsAModBackIntoTheVisitorView()
    {
        var mod = await ApproveAsync("111");
        await _service.SetModActiveAsync(mod, false);
        Assert.Empty(await _service.GetModlistAsync(activeOnly: true));

        await _service.SetModActiveAsync(mod, true);

        Assert.Single(await _service.GetModlistAsync(activeOnly: true));
    }

    [Fact]
    public async Task TheGameFilterAndTheActiveFilterApplyTogether()
    {
        await ApproveAsync("111");
        var otherGameParked = await ApproveAsync("222", game: "Some Other Game");
        await _service.SetModActiveAsync(otherGameParked, false);
        await ApproveAsync("333", game: "Some Other Game");

        var visitorOtherGame = await _service.GetModlistAsync("Some Other Game", activeOnly: true);

        var visible = Assert.Single(visitorOtherGame);
        Assert.Equal("333", visible.WorkshopId);
    }

    [Fact]
    public async Task GetParkedModsAsync_ReturnsExactlyWhatTheVisitorModlistLeavesOut()
    {
        await ApproveAsync("111");
        var parked = await ApproveAsync("222");
        await _service.SetModActiveAsync(parked, false);

        var running = await _service.GetModlistAsync(activeOnly: true);
        var switchedOff = await _service.GetParkedModsAsync();

        Assert.Equal(["111"], running.Select(m => m.WorkshopId));
        Assert.Equal(["222"], switchedOff.Select(m => m.WorkshopId));

        // Between them the two pages account for every approved mod, with no overlap.
        var everything = await _service.GetModlistAsync();
        Assert.Equal(
            everything.Select(m => m.WorkshopId).Order(),
            running.Concat(switchedOff).Select(m => m.WorkshopId).Order());
    }

    [Fact]
    public async Task GetParkedModsAsync_IgnoresModsThatWereNeverApproved()
    {
        // Pending and declined requests never reach the modlist, so they're not "switched off".
        await _service.CreateRequestAsync("111", "alice");
        var declined = await _service.CreateRequestAsync("222", "bob");
        await _service.SetStatusAsync(declined.RequestId!.Value, RequestStatus.Declined);

        Assert.Empty(await _service.GetParkedModsAsync());
    }

    [Fact]
    public async Task GetParkedModsAsync_RespectsTheGameFilter()
    {
        var pz = await ApproveAsync("111");
        var other = await ApproveAsync("222", game: "Some Other Game");
        await _service.SetModActiveAsync(pz, false);
        await _service.SetModActiveAsync(other, false);

        var parked = Assert.Single(await _service.GetParkedModsAsync(Mod.DefaultGame));

        Assert.Equal("111", parked.WorkshopId);
    }

    [Fact]
    public async Task ParkingAModDoesNotRemoveItFromTheModlist()
    {
        var mod = await ApproveAsync("111");

        await _service.SetModActiveAsync(mod, false);

        // Still approved and still exported to WorkshopItems=; just not shown to visitors.
        var adminView = Assert.Single(await _service.GetModlistAsync());
        Assert.True(adminView.IsInModlist);
        Assert.Equal("111", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    public void Dispose() => _dbFactory.Dispose();
}
