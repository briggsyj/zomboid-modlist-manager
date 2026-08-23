using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

/// <summary>
/// Load order is significant in Project Zomboid, so the modlist carries an explicit admin-defined
/// order and both clipboard exports follow it.
/// </summary>
public class ExportOrderingTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public ExportOrderingTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    /// <summary>Approves a mod for the given workshop ID, attaches Mod IDs, and returns its Mod id.</summary>
    private async Task<int> ApproveAsync(string workshopId, params string[] pzModIds)
    {
        var created = await _service.CreateRequestAsync(workshopId, "alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var modId = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId).ModId;
        foreach (var pzModId in pzModIds)
        {
            await _service.AddManualModIdAsync(modId, pzModId, null);
        }

        return modId;
    }

    [Fact]
    public async Task ApprovalOrderIsTheStartingOrder()
    {
        // Deliberately not ascending by workshop ID - order now follows approval, not the ID.
        await ApproveAsync("3783094058", "Third");
        await ApproveAsync("108600", "First");
        await ApproveAsync("2872282653", "Second");

        Assert.Equal("3783094058;108600;2872282653", await _service.BuildApprovedWorkshopIdExportAsync());
        Assert.Equal(@"\Third;\First;\Second", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task ApprovingAModAppendsItToTheEnd()
    {
        var first = await ApproveAsync("111", "A");
        var second = await ApproveAsync("222", "B");
        var third = await ApproveAsync("333", "C");

        var modlist = await _service.GetModlistAsync();

        Assert.Equal([first, second, third], modlist.Select(m => m.Id));
        Assert.Equal([1, 2, 3], modlist.Select(m => m.SortOrder));
    }

    [Fact]
    public async Task ReorderingChangesBothExports()
    {
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");
        var c = await ApproveAsync("333", "CMod");

        await _service.ReorderModlistAsync([c, a, b]);

        Assert.Equal("333;111;222", await _service.BuildApprovedWorkshopIdExportAsync());
        Assert.Equal(@"\CMod;\AMod;\BMod", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task TheTwoExportsStayPositionallyAligned()
    {
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");
        var c = await ApproveAsync("333", "CMod");

        await _service.ReorderModlistAsync([b, c, a]);

        Assert.Equal(["222", "333", "111"], (await _service.BuildApprovedWorkshopIdExportAsync()).Split(';'));
        Assert.Equal([@"\BMod", @"\CMod", @"\AMod"], (await _service.BuildApprovedZomboidModIdExportAsync()).Split(';'));
    }

    [Fact]
    public async Task TheModlistPageIsOrderedTheSameWayAsTheExports()
    {
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");
        var c = await ApproveAsync("333", "CMod");

        await _service.ReorderModlistAsync([c, b, a]);

        var modlist = await _service.GetModlistAsync();

        Assert.Equal(["333", "222", "111"], modlist.Select(m => m.WorkshopId));
    }

    [Fact]
    public async Task AMultiModPackKeepsItsModIdsTogetherInDiscoveryOrder()
    {
        var pack = await ApproveAsync("111", "PackOne", "PackTwo");
        var single = await ApproveAsync("222", "Single");

        await _service.ReorderModlistAsync([single, pack]);

        Assert.Equal(@"\Single;\PackOne;\PackTwo", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task InactiveModsAreSkippedWithoutDisturbingTheOrder()
    {
        await ApproveAsync("111", "First");
        var middle = await ApproveAsync("222", "Second");
        await ApproveAsync("333", "Third");

        await _service.SetModActiveAsync(middle, false);

        // Mods= drops it; WorkshopItems= keeps it, both still in modlist order.
        Assert.Equal(@"\First;\Third", await _service.BuildApprovedZomboidModIdExportAsync());
        Assert.Equal("111;222;333", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task ReorderingIgnoresIdsThatArentOnTheModlist()
    {
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");

        await _service.ReorderModlistAsync([b, 9999, a]);

        Assert.Equal("222;111", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task ModsMissingFromAReorderKeepTheirRelativeOrderAtTheEnd()
    {
        // A stale page could post a list that predates a newly approved mod; it must not vanish
        // from the order or collide on SortOrder.
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");
        var c = await ApproveAsync("333", "CMod");

        await _service.ReorderModlistAsync([c]);

        var modlist = await _service.GetModlistAsync();
        Assert.Equal([c, a, b], modlist.Select(m => m.Id));
        Assert.Equal([1, 2, 3], modlist.Select(m => m.SortOrder));
    }

    [Fact]
    public async Task ReApprovingAModDoesNotMoveIt()
    {
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");
        await _service.ReorderModlistAsync([b, a]);

        // A second request for the same mod, also approved, must not shunt it to the end. The
        // service refuses to create duplicates now, so seed one the way an older database would.
        int secondId;
        await using (var db = _dbFactory.CreateDbContext())
        {
            var mod = db.Mods.Single(m => m.WorkshopId == "111");
            var extra = new ModRequest { ModId = mod.Id, RequesterName = "bob", Status = RequestStatus.Pending };
            db.ModRequests.Add(extra);
            await db.SaveChangesAsync();
            secondId = extra.Id;
        }

        await _service.SetStatusAsync(secondId, RequestStatus.Approved);

        Assert.Equal("222;111", await _service.BuildApprovedWorkshopIdExportAsync());
    }

    [Fact]
    public async Task AModRejoiningTheModlistIsAppendedRatherThanKeepingAStalePosition()
    {
        var a = await ApproveAsync("111", "AMod");
        var b = await ApproveAsync("222", "BMod");
        var requestForA = (await _service.GetRequestsAsync()).First(r => r.ModId == a);

        await _service.SetStatusAsync(requestForA.Id, RequestStatus.Declined);
        Assert.Equal("222", await _service.BuildApprovedWorkshopIdExportAsync());

        await _service.SetStatusAsync(requestForA.Id, RequestStatus.Approved);

        Assert.Equal("222;111", await _service.BuildApprovedWorkshopIdExportAsync());
        Assert.Equal(2, (await _service.GetModlistAsync()).Count);
        _ = b;
    }

    public void Dispose() => _dbFactory.Dispose();
}
