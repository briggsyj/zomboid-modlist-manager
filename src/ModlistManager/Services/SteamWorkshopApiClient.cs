using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ModlistManager.Services;

/// <summary>
/// Minimal client for Steam's public GetPublishedFileDetails endpoint. No API key required,
/// and unlike SteamCMD it needs no external binary and no download of the mod's actual content.
/// </summary>
public class SteamWorkshopApiClient(IHttpClientFactory httpClientFactory, ILogger<SteamWorkshopApiClient> logger)
{
    public const string HttpClientName = "steam-workshop";

    private const string Endpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    public record WorkshopItem(string WorkshopId, string? Title, string? Description);

    /// <summary>
    /// Returns the workshop item's metadata, or null if Steam doesn't know the ID or the call failed.
    /// </summary>
    public async Task<WorkshopItem?> GetItemAsync(string workshopId, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("itemcount", "1"),
            new KeyValuePair<string, string>("publishedfileids[0]", workshopId)
        ]);

        try
        {
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.PostAsync(Endpoint, form, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<GetPublishedFileDetailsResponse>(cancellationToken);
            var detail = payload?.Response?.PublishedFileDetails?.FirstOrDefault();

            // Steam uses result == 1 for "ok"; anything else (commonly 9) means the ID doesn't exist.
            if (detail is null || detail.Result != 1)
            {
                return null;
            }

            return new WorkshopItem(workshopId, detail.Title, detail.Description);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            logger.LogWarning(ex, "Steam Workshop API lookup failed for {WorkshopId}", workshopId);
            return null;
        }
    }

    private sealed class GetPublishedFileDetailsResponse
    {
        [JsonPropertyName("response")]
        public ResponseBody? Response { get; set; }
    }

    private sealed class ResponseBody
    {
        [JsonPropertyName("publishedfiledetails")]
        public List<PublishedFileDetail>? PublishedFileDetails { get; set; }
    }

    private sealed class PublishedFileDetail
    {
        [JsonPropertyName("result")]
        public int Result { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
