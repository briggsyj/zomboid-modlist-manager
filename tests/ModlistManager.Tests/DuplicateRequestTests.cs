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
        await _service.CreateRequestAsync("Better Sorting", "3783094058", "Alice");

        var existing = await _service.FindExistingRequestsAsync("3783094058");

        var match = Assert.Single(existing);
        Assert.Equal("Better Sorting", match.Title);
        Assert.Equal("alice", match.RequesterName);
        Assert.Equal(RequestStatus.Pending, match.Status);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_MatchesAFullUrlAgainstARequestMadeWithABareId()
    {
        await _service.CreateRequestAsync("Better Sorting", "3783094058", "Alice");

        var existing = await _service.FindExistingRequestsAsync(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058");

        Assert.Single(existing);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_MatchesABareIdAgainstARequestMadeWithAUrl()
    {
        await _service.CreateRequestAsync(
            "Better Sorting", "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "Alice");

        var existing = await _service.FindExistingRequestsAsync("3783094058");

        Assert.Single(existing);
    }

    [Fact]
    public async Task FindExistingRequestsAsync_ReturnsEveryRequestForTheSameItem()
    {
        await _service.CreateRequestAsync("First ask", "111", "Alice");
        await _service.CreateRequestAsync("Second ask", "111", "Bob");

        var existing = await _service.FindExistingRequestsAsync("111");

        Assert.Equal(2, existing.Count);
        Assert.Equal(["alice", "bob"], existing.Select(e => e.RequesterName));
    }

    [Fact]
    public async Task FindExistingRequestsAsync_IgnoresDifferentWorkshopItems()
    {
        await _service.CreateRequestAsync("Something else", "999", "Alice");

        Assert.Empty(await _service.FindExistingRequestsAsync("111"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a workshop link")]
    public async Task FindExistingRequestsAsync_ReturnsEmptyForInputThatIsNotAWorkshopReferenceYet(string? input)
    {
        await _service.CreateRequestAsync("Something", "111", "Alice");

        Assert.Empty(await _service.FindExistingRequestsAsync(input));
    }

    [Fact]
    public async Task CreateRequestAsync_StoresTheReason()
    {
        var created = await _service.CreateRequestAsync("A", "111", "Alice", reason: "  We need more loot variety.  ");

        var request = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId);
        Assert.Equal("We need more loot variety.", request.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateRequestAsync_LeavesReasonNullWhenBlank(string? reason)
    {
        var created = await _service.CreateRequestAsync("A", "111", "Alice", reason: reason);

        var request = (await _service.GetRequestsAsync()).Single(r => r.Id == created.RequestId);
        Assert.Null(request.Reason);
    }

    public void Dispose() => _dbFactory.Dispose();
}
