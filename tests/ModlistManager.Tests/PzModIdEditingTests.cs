using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class PzModIdEditingTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public PzModIdEditingTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    /// <summary>Approves a mod with the given Mod IDs and returns its Mod id.</summary>
    private async Task<int> ApproveAsync(string workshopId, params string[] pzModIds)
    {
        var created = await _service.CreateRequestAsync(workshopId, "alice");
        await _service.SetStatusAsync(created.RequestId!.Value, RequestStatus.Approved);

        var modId = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId).ModId;
        foreach (var value in pzModIds)
        {
            await _service.AddManualModIdAsync(modId, value, null);
        }

        return modId;
    }

    private async Task<List<PzModId>> ModIdsOfAsync(int modId) =>
        [.. (await _service.GetModlistAsync()).Single(m => m.Id == modId).PzModIds.OrderBy(p => p.Id)];

    [Fact]
    public async Task ModIdsStartEnabled()
    {
        var modId = await ApproveAsync("111", "AMod", "BMod");

        Assert.All(await ModIdsOfAsync(modId), p => Assert.True(p.IsEnabled));
    }

    [Fact]
    public async Task DisablingAModIdRemovesItFromTheZomboidExport()
    {
        var modId = await ApproveAsync("111", "Keep", "Drop");
        var toDisable = (await ModIdsOfAsync(modId)).Single(p => p.Value == "Drop");

        await _service.SetPzModIdEnabledAsync(toDisable.Id, false);

        Assert.Equal(@"\Keep", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task DisablingAModIdLeavesTheWorkshopExportAlone()
    {
        var modId = await ApproveAsync("111", "Only");
        var only = Assert.Single(await ModIdsOfAsync(modId));

        await _service.SetPzModIdEnabledAsync(only.Id, false);

        // The item still needs downloading; it's the Mods= line that changes.
        Assert.Equal("111", await _service.BuildApprovedWorkshopIdExportAsync());
        Assert.Equal(string.Empty, await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task ReEnablingPutsTheModIdBack()
    {
        var modId = await ApproveAsync("111", "Toggle");
        var entry = Assert.Single(await ModIdsOfAsync(modId));

        await _service.SetPzModIdEnabledAsync(entry.Id, false);
        Assert.Equal(string.Empty, await _service.BuildApprovedZomboidModIdExportAsync());

        await _service.SetPzModIdEnabledAsync(entry.Id, true);

        Assert.Equal(@"\Toggle", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task DisablingIsPerModIdNotPerMod()
    {
        // A pack where only one of the bundled mods is wanted.
        var modId = await ApproveAsync("111", "PackOne", "PackTwo", "PackThree");
        var second = (await ModIdsOfAsync(modId)).Single(p => p.Value == "PackTwo");

        await _service.SetPzModIdEnabledAsync(second.Id, false);

        Assert.Equal(@"\PackOne;\PackThree", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task EditingAModIdChangesTheExportedValue()
    {
        var modId = await ApproveAsync("111", "TypoMod");
        var entry = Assert.Single(await ModIdsOfAsync(modId));

        var changed = await _service.UpdatePzModIdValueAsync(entry.Id, "CorrectedMod");

        Assert.True(changed);
        Assert.Equal(@"\CorrectedMod", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task EditingTrimsWhitespace()
    {
        var modId = await ApproveAsync("111", "Original");
        var entry = Assert.Single(await ModIdsOfAsync(modId));

        await _service.UpdatePzModIdValueAsync(entry.Id, "   Spaced   ");

        Assert.Equal("Spaced", Assert.Single(await ModIdsOfAsync(modId)).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EditingToBlankIsIgnoredRatherThanWipingTheValue(string? blank)
    {
        var modId = await ApproveAsync("111", "KeepMe");
        var entry = Assert.Single(await ModIdsOfAsync(modId));

        var changed = await _service.UpdatePzModIdValueAsync(entry.Id, blank);

        Assert.False(changed);
        Assert.Equal("KeepMe", Assert.Single(await ModIdsOfAsync(modId)).Value);
    }

    [Fact]
    public async Task EditingToTheSameValueReportsNoChange()
    {
        var modId = await ApproveAsync("111", "Same");
        var entry = Assert.Single(await ModIdsOfAsync(modId));

        Assert.False(await _service.UpdatePzModIdValueAsync(entry.Id, "Same"));
    }

    [Fact]
    public async Task AnEditedModIdIsMarkedManualSoARefetchWontOverwriteIt()
    {
        var modId = await ApproveAsync("111");

        // Simulate an automatically discovered entry.
        var db = _dbFactory.CreateDbContext();
        db.PzModIds.Add(new PzModId { ModId = modId, Value = "AutoFound", IsManual = false });
        await db.SaveChangesAsync();
        db.Dispose();

        var entry = Assert.Single(await ModIdsOfAsync(modId));
        Assert.False(entry.IsManual);

        await _service.UpdatePzModIdValueAsync(entry.Id, "HandCorrected");

        var updated = Assert.Single(await ModIdsOfAsync(modId));
        Assert.Equal("HandCorrected", updated.Value);
        Assert.True(updated.IsManual);
    }

    [Fact]
    public async Task DeletingRemovesTheModIdEntirely()
    {
        var modId = await ApproveAsync("111", "Doomed", "Survivor");
        var doomed = (await ModIdsOfAsync(modId)).Single(p => p.Value == "Doomed");

        await _service.DeletePzModIdAsync(doomed.Id);

        Assert.Equal(["Survivor"], (await ModIdsOfAsync(modId)).Select(p => p.Value));
        Assert.Equal(@"\Survivor", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task AddingTheSameModIdTwiceIsIgnored()
    {
        // A duplicate would be emitted twice in the Mods= line.
        var modId = await ApproveAsync("111", "OnlyOnce");

        await _service.AddManualModIdAsync(modId, "OnlyOnce", null);
        await _service.AddManualModIdAsync(modId, "  OnlyOnce  ", null);

        Assert.Single(await ModIdsOfAsync(modId));
        Assert.Equal(@"\OnlyOnce", await _service.BuildApprovedZomboidModIdExportAsync());
    }

    [Fact]
    public async Task TheSameModIdCanExistOnDifferentMods()
    {
        var first = await ApproveAsync("111", "Shared");
        var second = await ApproveAsync("222", "Shared");

        Assert.Single(await ModIdsOfAsync(first));
        Assert.Single(await ModIdsOfAsync(second));
    }

    [Fact]
    public async Task EditingOntoAValueTheModAlreadyHasIsRejected()
    {
        var modId = await ApproveAsync("111", "First", "Second");
        var second = (await ModIdsOfAsync(modId)).Single(p => p.Value == "Second");

        var changed = await _service.UpdatePzModIdValueAsync(second.Id, "First");

        Assert.False(changed);
        Assert.Equal(["First", "Second"], (await ModIdsOfAsync(modId)).Select(p => p.Value));
    }

    [Fact]
    public async Task UnknownIdsAreIgnoredRatherThanThrowing()
    {
        await _service.SetPzModIdEnabledAsync(9999, false);
        await _service.DeletePzModIdAsync(9999);

        Assert.False(await _service.UpdatePzModIdValueAsync(9999, "Nope"));
    }

    public void Dispose() => _dbFactory.Dispose();
}
