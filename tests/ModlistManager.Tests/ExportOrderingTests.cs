using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class ExportOrderingTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public ExportOrderingTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    /// <summary>Approves a mod for the given workshop ID and attaches the given PZ Mod IDs.</summary>
    private async Task ApproveAsync(string workshopId, params string[] pzModIds)
    {
        var created = await _service.CreateRequestAsync(workshopId, "alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var modId = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId).ModId;
        foreach (var pzModId in pzModIds)
        {
            await _service.AddManualModIdAsync(modId, pzModId, null);
        }
    }

    [Fact]
    public async Task WorkshopExportIsSortedByWorkshopIdAscending()
    {
        // Deliberately created out of order, so insertion order can't be mistaken for sorted output.
        await ApproveAsync("3783094058", "Third");
        await ApproveAsync("108600", "First");
        await ApproveAsync("2872282653", "Second");

        Assert.Equal("108600;2872282653;3783094058", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task WorkshopExportSortsNumericallyNotAsText()
    {
        // Workshop IDs are stored as text. Sorted as strings, the 9-digit ID would come last;
        // numerically it belongs second.
        await ApproveAsync("2872282653", "Big");
        await ApproveAsync("498441420", "Older");

        Assert.Equal("498441420;2872282653", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task ZomboidExportFollowsTheSameWorkshopIdOrder()
    {
        await ApproveAsync("3783094058", "Third");
        await ApproveAsync("108600", "First");
        await ApproveAsync("2872282653", "Second");

        Assert.Equal(@"\First;\Second;\Third", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task TheTwoExportsStayPositionallyAligned()
    {
        await ApproveAsync("3783094058", "CMod");
        await ApproveAsync("498441420", "AMod");
        await ApproveAsync("2872282653", "BMod");

        var workshopIds = (await _service.BuildApprovedWorkshopIdExportAsync()).Split(';');
        var modIds = (await _service.BuildApprovedZomboidModIdExportAsync()).Split(';');

        Assert.Equal(["498441420", "2872282653", "3783094058"], workshopIds);
        Assert.Equal([@"\AMod", @"\BMod", @"\CMod"], modIds);
    }

    [Fact]
    public async Task AMultiModPackKeepsItsModIdsTogetherInDiscoveryOrder()
    {
        await ApproveAsync("222222222", "PackOne", "PackTwo");
        await ApproveAsync("111111111", "Single");

        Assert.Equal(@"\Single;\PackOne;\PackTwo", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task InactiveModsAreSkippedWithoutDisturbingTheOrder()
    {
        await ApproveAsync("333333333", "Third");
        await ApproveAsync("111111111", "First");
        await ApproveAsync("222222222", "Second");

        var middle = (await _service.GetModlistAsync()).Single(m => m.WorkshopId == "222222222");
        await _service.SetModActiveAsync(middle.Id, false);

        // Mods= drops it; WorkshopItems= keeps it, both still ascending.
        Assert.Equal(@"\First;\Third", await _service.BuildApprovedZomboidModIdExportAsync());
        Assert.Equal("111111111;222222222;333333333", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task TheModlistPageIsOrderedTheSameWayAsTheExports()
    {
        await ApproveAsync("3783094058", "Third");
        await ApproveAsync("108600", "First");
        await ApproveAsync("2872282653", "Second");

        var modlist = await _service.GetModlistAsync();

        Assert.Equal(
            ["108600", "2872282653", "3783094058"],
            modlist.Select(m => m.WorkshopId));
    }

    public void Dispose() => _dbFactory.Dispose();
}
