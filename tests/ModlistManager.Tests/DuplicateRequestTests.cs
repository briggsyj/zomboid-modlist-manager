using ModlistManager.Data.Entities;
using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class DuplicateRequestTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly ModRequestService _service;

    public DuplicateRequestTests()
    {
        _service = new ModRequestService(_dbFactory, new ModIdFetchQueue());
    }

    [Fact]
    public async Task FindExistingRequestsAsync_MatchesABareIdAgainstAnExistingRequest()
    {
        await _service.CreateRequestAsync("3783094058", "Alice");

        var existing = await _service.FindExistingRequestsAsync("3783094058");

        var match = Assert.Single(existing);
        Assert.Equal("alice", match.RequesterName);
        Assert.Equal(RequestStatus.Pending, match.Status);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_MatchesAFullUrlAgainstARequestMadeWithABareId()
    {
        await _service.CreateRequestAsync("3783094058", "Alice");

        var existing = await _service.FindExistingRequestsAsync(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058");

        Assert.Single(existing);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_MatchesABareIdAgainstARequestMadeWithAUrl()
    {
        await _service.CreateRequestAsync(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "Alice");

        var existing = await _service.FindExistingRequestsAsync("3783094058");

        Assert.Single(existing);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_ReturnsEveryRequestForTheSameItem()
    {
        // The service now refuses a second request for the same item, but older databases contain
        // them, so the lookup still has to report all of them.
        await _service.CreateRequestAsync("111", "Alice");
        await using (var db = _dbFactory.CreateDbContext())
        {
            var mod = db.Mods.Single(m => m.WorkshopId == "111");
            db.ModRequests.Add(new ModRequest { ModId = mod.Id, RequesterName = "bob", Status = RequestStatus.Pending });
            await db.SaveChangesAsync();
        }

        var existing = await _service.FindExistingRequestsAsync("111");

        Assert.Equal(2, existing.Count);
        Assert.Equal(["alice", "bob"], existing.Select(e => e.RequesterName));
    }

    [Fact]
    public async Task ADuplicateSubmissionIsRejected()
    {
        await _service.CreateRequestAsync("111", "Alice");

        var second = await _service.CreateRequestAsync("111", "Bob");

        Assert.False(second.Success);
        Assert.Single(await _service.GetRequestsAsync());
    }

    [Fact]
    public async Task ADeclinedModStillCountsAsAlreadyRequested()
    {
        var first = await _service.CreateRequestAsync("111", "Alice");
        await _service.SetStatusAsync(first.RequestId!.Value, RequestStatus.Declined);

        // Deliberate: a declined mod shouldn't be quietly re-requested to get a different answer.
        Assert.False((await _service.CreateRequestAsync("111", "Bob")).Success);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_IgnoresDifferentWorkshopItems()
    {
        await _service.CreateRequestAsync("999", "Alice");

        Assert.Empty(await _service.FindExistingRequestsAsync("111"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a workshop link")]
    public async Task FindExistingRequestsAsync_ReturnsEmptyForInputThatIsNotAWorkshopReferenceYet(string? input)
    {
        await _service.CreateRequestAsync("111", "Alice");

        Assert.Empty(await _service.FindExistingRequestsAsync(input));
    }

    [Fact]
    public async Task CreateRequestAsync_StoresTheReason()
    {
        var created = await _service.CreateRequestAsync("111", "Alice", reason: "  We need more loot variety.  ");

        var request = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId);
        Assert.Equal("We need more loot variety.", request.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateRequestAsync_LeavesReasonNullWhenBlank(string? reason)
    {
        var created = await _service.CreateRequestAsync("111", "Alice", reason: reason);

        var request = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId);
        Assert.Null(request.Reason);
    }

    public void Dispose() => _dbFactory.Dispose();
}
