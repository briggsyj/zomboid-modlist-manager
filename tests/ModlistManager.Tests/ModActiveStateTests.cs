using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class ModActiveStateTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public ModActiveStateTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    /// <summary>Creates an approved mod with the given PZ Mod ID and returns its Mod id.</summary>
    private async Task<int> ApproveWithModIdAsync(string workshopId, string pzModId)
    {
        var created = await _service.CreateRequestAsync(workshopId, "alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var modId = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId).ModId;
        await _service.AddManualModIdAsync(modId, pzModId, null);
        return modId;
    }

    [Fact]
    public async Task NewModsStartActive()
    {
        await ApproveWithModIdAsync("111", "AMod");

        var mod = Assert.Single(await _service.GetModlistAsync());
        Assert.True(mod.IsActive);
    }

    [Fact]
    public async Task DeactivatingRemovesTheModIdFromTheZomboidExport()
    {
        var active = await ApproveWithModIdAsync("111", "KeepMod");
        var inactive = await ApproveWithModIdAsync("222", "DropMod");

        await _service.SetModActiveAsync(inactive, false);

        Assert.Equal(@"\KeepMod", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task DeactivatingKeepsTheWorkshopIdInTheWorkshopExport()
    {
        await ApproveWithModIdAsync("111", "KeepMod");
        var inactive = await ApproveWithModIdAsync("222", "DropMod");

        await _service.SetModActiveAsync(inactive, false);

        // The server should still download an inactive mod, so it can be re-enabled without a re-download.
        Assert.Equal("111;222", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task ReactivatingPutsTheModIdBack()
    {
        var modId = await ApproveWithModIdAsync("111", "ToggleMod");

        await _service.SetModActiveAsync(modId, false);
        Assert.Equal(string.Empty, await _service.BuildApprovedZomboidModIdExportAsync());

        await _service.SetModActiveAsync(modId, true);
        Assert.Equal(@"\ToggleMod", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task DeactivatingDropsEveryModIdOfAMultiModPack()
    {
        var modId = await ApproveWithModIdAsync("111", "PackPartOne");
        await _service.AddManualModIdAsync(modId, "PackPartTwo", null);
        Assert.Equal(@"\PackPartOne;\PackPartTwo", await _service.BuildApprovedZomboidModIdExportAsync());

        await _service.SetModActiveAsync(modId, false);

        Assert.Equal(string.Empty, await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task InactiveModsStayVisibleOnTheModlist()
    {
        var modId = await ApproveWithModIdAsync("111", "StillListed");

        await _service.SetModActiveAsync(modId, false);

        var mod = Assert.Single(await _service.GetModlistAsync());
        Assert.False(mod.IsActive);
        Assert.True(mod.IsInModlist);
    }

    [Fact]
    public async Task SetModActiveAsync_IgnoresAnUnknownMod()
    {
        await _service.SetModActiveAsync(9999, false);

        Assert.Empty(await _service.GetModlistAsync());
    }

    public void Dispose() => _dbFactory.Dispose();
}
