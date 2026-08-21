using System.Text.RegularExpressions;

namespace ModlistManager.Services;

public static partial class WorkshopIdParser
{
    /// <summary>
    /// Accepts either a full Steam Workshop URL
    /// (e.g. "https://steamcommunity.com/sharedfiles/filedetails/?id=3783094058")
    /// or a bare numeric published file ID (e.g. "3783094058").
    /// Returns the canonical numeric ID, or null if the input doesn't contain one.
    /// </summary>
    public static string? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();

        // Bare numeric ID.
        if (BareIdRegex().IsMatch(trimmed))
        {
            return trimmed;
        }

        // Full or partial URL with an "id" query parameter.
        var match = UrlIdRegex().Match(trimmed);
        return match.Success ? match.Groups["id"].Value : null;
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex BareIdRegex();

    [GeneratedRegex(@"[?&]id=(?<id>\d+)")]
    private static partial Regex UrlIdRegex();
}
