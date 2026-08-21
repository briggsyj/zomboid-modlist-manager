using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class ModInfoParserTests
{
    [Fact]
    public void Parse_ReadsIdAndName()
    {
        const string content = "name=My Cool Mod\nid=MyCoolMod\ndescription=Does cool things\n";

        var result = ModInfoParser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal("MyCoolMod", result!.Value.ModId);
        Assert.Equal("My Cool Mod", result.Value.ModName);
    }

    [Fact]
    public void Parse_WorksWithoutName()
    {
        const string content = "id=OnlyId\ndescription=No name field\n";

        var result = ModInfoParser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal("OnlyId", result!.Value.ModId);
        Assert.Null(result.Value.ModName);
    }

    [Fact]
    public void Parse_ReturnsNullWhenNoIdPresent()
    {
        const string content = "name=Missing Id Mod\ndescription=oops\n";

        Assert.Null(ModInfoParser.Parse(content));
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnKeysAndTrimsWhitespace()
    {
        const string content = "  ID = SpacedId  \r\n  Name = Spaced Name \r\n";

        var result = ModInfoParser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal("SpacedId", result!.Value.ModId);
        Assert.Equal("Spaced Name", result.Value.ModName);
    }

    [Fact]
    public void Parse_IgnoresBlankLinesAndLinesWithoutEquals()
    {
        const string content = "\n\nid=Foo\nthis is not a key value line\nname=Bar\n";

        var result = ModInfoParser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal("Foo", result!.Value.ModId);
        Assert.Equal("Bar", result.Value.ModName);
    }

    [Fact]
    public void FindModInfoFiles_ReturnsEmptyForMissingDirectory()
    {
        var files = ModInfoParser.FindModInfoFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Empty(files);
    }

    [Fact]
    public void FindModInfoFiles_FindsNestedModInfoFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "modinfo-test-" + Guid.NewGuid());
        var modADir = Path.Combine(root, "ModA");
        var modBDir = Path.Combine(root, "Contents", "ModB");
        Directory.CreateDirectory(modADir);
        Directory.CreateDirectory(modBDir);
        File.WriteAllText(Path.Combine(modADir, "mod.info"), "id=ModA\n");
        File.WriteAllText(Path.Combine(modBDir, "mod.info"), "id=ModB\n");

        try
        {
            var files = ModInfoParser.FindModInfoFiles(root);

            Assert.Equal(2, files.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
