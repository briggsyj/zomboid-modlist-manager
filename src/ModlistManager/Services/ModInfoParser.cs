namespace ModlistManager.Services;

public static class ModInfoParser
{
    public readonly record struct ParsedMod(string ModId, string? ModName);

    /// <summary>Recursively finds all "mod.info" files under the given root directory.</summary>
    public static IReadOnlyList<string> FindModInfoFiles(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(rootDirectory, "mod.info", SearchOption.AllDirectories).ToList();
    }

    /// <summary>Parses "key=value" lines from mod.info content, returning the mod's ID and display name.</summary>
    public static ParsedMod? Parse(string modInfoContent)
    {
        string? id = null;
        string? name = null;

        foreach (var rawLine in modInfoContent.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (value.Length == 0)
            {
                continue;
            }

            if (id is null && key.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                id = value;
            }
            else if (name is null && key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
        }

        return id is null ? null : new ParsedMod(id, name);
    }
}
