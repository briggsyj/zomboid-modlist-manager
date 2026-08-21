using System.Text.RegularExpressions;

namespace ModlistManager.Services;

/// <summary>
/// Pulls Project Zomboid Mod IDs out of a Steam Workshop description.
///
/// PZ's workshop upload convention is for the description to end with lines like
/// "Workshop ID: 3783094058" / "Mod ID: VanillaOutfitsExpanded", and packs that bundle several
/// mods list one "Mod ID:" line each. This is the same value a server's Mods= line needs, so it
/// avoids downloading the mod just to read mod.info.
/// </summary>
public static partial class PzModIdExtractor
{
    /// <summary>Returns the Mod IDs named in the description, in order, without duplicates.</summary>
    public static IReadOnlyList<string> Extract(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        // Strip BBCode tags first so "[b]Mod ID:[/b] Foo" is read the same as "Mod ID: Foo".
        // Replaced with a space rather than removed so surrounding words don't run together.
        var plainText = BbCodeRegex().Replace(description, " ");

        var results = new List<string>();
        foreach (Match match in ModIdRegex().Matches(plainText))
        {
            var value = match.Groups["id"].Value.Trim();
            if (value.Length > 0 && !results.Contains(value, StringComparer.Ordinal))
            {
                results.Add(value);
            }
        }

        return results;
    }

    [GeneratedRegex(@"\[[^\]\r\n]*\]")]
    private static partial Regex BbCodeRegex();

    // "Mod ID", "ModID" and "Mod Id" all appear in the wild; the value runs to the end of the line
    // because some IDs legitimately contain spaces (e.g. "The Long Dark Guns").
    [GeneratedRegex(@"Mod\s*ID\s*:\s*(?<id>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModIdRegex();
}
