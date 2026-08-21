using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class PzModIdExtractorTests
{
    [Fact]
    public void Extract_ReadsTheTrailingModIdLinePzUsesByConvention()
    {
        // Shape taken from a real workshop description (Vanilla Outfits Expanded, 3783094058).
        const string description = "Some description text.\r\n\r\nWorkshop ID: 3783094058\r\nMod ID: VanillaOutfitsExpanded";

        Assert.Equal(["VanillaOutfitsExpanded"], PzModIdExtractor.Extract(description));
    }

    [Fact]
    public void Extract_FindsEveryModIdInAMultiModPack()
    {
        const string description = "Workshop ID: 123\r\nMod ID: FirstMod\r\nMod ID: SecondMod\r\nMod ID: ThirdMod";

        Assert.Equal(["FirstMod", "SecondMod", "ThirdMod"], PzModIdExtractor.Extract(description));
    }

    [Fact]
    public void Extract_IgnoresRepeatsOfTheSameId()
    {
        const string description = "Mod ID: SameMod\r\nblah\r\nMod ID: SameMod";

        Assert.Equal(["SameMod"], PzModIdExtractor.Extract(description));
    }

    [Theory]
    [InlineData("Mod ID: Foo")]
    [InlineData("ModID: Foo")]
    [InlineData("Mod Id:Foo")]
    [InlineData("MOD ID:   Foo   ")]
    public void Extract_ToleratesSpacingAndCasingVariants(string description)
    {
        Assert.Equal(["Foo"], PzModIdExtractor.Extract(description));
    }

    [Fact]
    public void Extract_SeesThroughBbCodeMarkup()
    {
        const string description = "[b]Mod ID:[/b] StyledMod";

        Assert.Equal(["StyledMod"], PzModIdExtractor.Extract(description));
    }

    [Fact]
    public void Extract_KeepsModIdsThatContainSpaces()
    {
        // Real example: workshop item 3762319745 uses "The Long Dark Guns".
        const string description = "Workshop ID: 3762319745\r\nMod ID: The Long Dark Guns";

        Assert.Equal(["The Long Dark Guns"], PzModIdExtractor.Extract(description));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A description that simply never mentions the id.")]
    public void Extract_ReturnsEmptyWhenNoModIdIsDocumented(string? description)
    {
        Assert.Empty(PzModIdExtractor.Extract(description));
    }
}
