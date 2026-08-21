using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class WorkshopIdParserTests
{
    [Theory]
    [InlineData("3783094058", "3783094058")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "3783094058")]
    [InlineData("http://steamcommunity.com/sharedfiles/filedetails/?id=3783094058", "3783094058")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058&searchtext=", "3783094058")]
    [InlineData("  3783094058  ", "3783094058")]
    [InlineData("steamcommunity.com/sharedfiles/filedetails/?id=42", "42")]
    public void TryParse_ExtractsIdFromValidInput(string input, string expected)
    {
        Assert.Equal(expected, WorkshopIdParser.TryParse(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a workshop link")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?appid=108600")]
    [InlineData("12abc34")]
    public void TryParse_ReturnsNullForInvalidInput(string? input)
    {
        Assert.Null(WorkshopIdParser.TryParse(input));
    }
}
