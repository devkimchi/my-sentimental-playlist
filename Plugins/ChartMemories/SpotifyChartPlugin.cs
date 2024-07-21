using System.ComponentModel;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using MelonChart.Models;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;

using SpotifyAPI.Web;

// namespace MySentimentalPlaylist.Notebooks.Plugins.ChartMemories;

#pragma warning disable SKEXP0001

public class SpotifyChartPlugin
{
    private const string COLLECTION = "SpotifyChart";

    [KernelFunction, Description("Add Spotify Chart data to the memory")]
    public static async Task AddChart(
        [Description("The Semantic Memory instance")] ISemanticTextMemory memory,
        [Description("The HttpClient instance")] HttpClient http,
        [Description("The JsonSerializerOptions instance")] JsonSerializerOptions jso)
    {
        var today = DateTimeOffset.UtcNow
                                .ToOffset(TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul").BaseUtcOffset)
                                .ToString("yyyyMMdd");
        var requestUri = $"https://raw.githubusercontent.com/aliencube/MelonChart.NET/main/data/spotify100-{today}.json";

        http.DefaultRequestHeaders.Clear();
        var data = await http.GetStringAsync(requestUri);
        var chart = JsonSerializer.Deserialize<ChartItemCollection>(data, jso);

        foreach (var item in chart.Items)
        {
            var index = chart.Items.IndexOf(item) + 1;
            var serialised = JsonSerializer.Serialize(item, jso);
            await memory.SaveInformationAsync(collection: COLLECTION, id: $"{today}-{index.ToString("000")}", text: serialised);

            Console.WriteLine($"- Stored: {item.Artist} - {item.Title}");
        }
    }

    [KernelFunction, Description("Get Spotify Chart data")]
    public static async Task<string> GetChart(
        [Description("The HttpClient instance")] HttpClient http,
        [Description("The JsonSerializerOptions instance")] JsonSerializerOptions jso)
    {
        var today = DateTimeOffset.UtcNow
                                .ToOffset(TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul").BaseUtcOffset)
                                .ToString("yyyyMMdd");
        var requestUri = $"https://raw.githubusercontent.com/aliencube/MelonChart.NET/main/data/spotify100-{today}.json";

        http.DefaultRequestHeaders.Clear();
        var data = await http.GetStringAsync(requestUri);
        var chart = JsonSerializer.Deserialize<ChartItemCollection>(data, jso);
        var items = JsonSerializer.Serialize(chart.Items, jso);

        return items;
    }

    [KernelFunction, Description("Search songs from the memory")]
    public static async Task<List<ChartItem>> FindSongs(
        [Description("The Semantic Memory instance")] ISemanticTextMemory memory,
        [Description("The question")] string question,
        [Description("The JsonSerializerOptions instance")] JsonSerializerOptions jso)
    {
        var results = await memory.SearchAsync(COLLECTION, question, limit: 100, minRelevanceScore: 0.8d).ToListAsync();
        var output = results.Select(r => JsonSerializer.Deserialize<ChartItem>(r.Metadata.Text, jso)).ToList();

        return output;
    }

    [KernelFunction, Description("Create a playlist on Spotify")]
    public static async Task<string> CreatePlaylist(
        [Description("The Spotify instance")] SpotifyClient spotify,
        [Description("The tracks")] string tracks,
        [Description("The JsonSerializerOptions instance")] JsonSerializerOptions jso)
    {
        var profile = await GetMyProfileAsync(spotify).ConfigureAwait(false);
        var playlist = await GetPlaylistAsync(spotify, profile).ConfigureAwait(false);

        var items = JsonSerializer.Deserialize<List<ChartItem>>(tracks.Replace("```json", "").Replace("```", "").Trim(), jso);
        var trackUris = items.Select(p => p.TrackUri).ToList();

        var snapshot = await AddTracksToPlaylistAsync(spotify, playlist.Id!, trackUris).ConfigureAwait(false);

        return $"Playlist URL: https://open.spotify.com/playlist/{playlist.Id}";
    }

    internal static async Task<PrivateUser> GetMyProfileAsync(SpotifyClient spotify)
    {
        var profile = await spotify.UserProfile.Current().ConfigureAwait(false);

        return profile;
    }

    internal static async Task<FullPlaylist> GetPlaylistAsync(SpotifyClient spotify, PrivateUser profile)
    {
        var playlists = await spotify.Playlists.CurrentUsers().ConfigureAwait(false);
        var playlist = playlists.Items?.SingleOrDefault(p => p.Name!.Equals("My Sentimental Playlist", StringComparison.InvariantCultureIgnoreCase));
        if (playlist is not null)
        {
            var uris = await GetTrackItemUrisAsync(spotify, playlist.Id!).ConfigureAwait(false);
            if (uris.Count > 0)
            {
                await RemoveTrackItemsFromPlaylistAsync(spotify, playlist.Id!, playlist.SnapshotId!, uris).ConfigureAwait(false);
            }
        }
        else
        {
            playlist = await CreatePlaylistAsync(
                spotify,
                profile.Id,
                "My Sentimental Playlist",
                "A playlist generated by Semantic Kernel.",
                false).ConfigureAwait(false);
        }

        return playlist;
    }

    internal static async Task<List<string>> GetTrackItemUrisAsync(SpotifyClient spotify, string playlistId)
    {
        var tracks = await GetTrackItemsAsync(spotify, playlistId).ConfigureAwait(false);
        var uris = tracks?.Select(p => ((FullTrack)p.Track).Uri).ToList();

        return uris!;
    }

    internal static async Task<List<PlaylistTrack<IPlayableItem>>> GetTrackItemsAsync(SpotifyClient spotify, string playlistId)
    {
        var request = new PlaylistGetItemsRequest(PlaylistGetItemsRequest.AdditionalTypes.Track);
        var tracks = await spotify.Playlists.GetItems(playlistId, request).ConfigureAwait(false);
        var items = tracks.Items;

        return items!;
    }

    internal static async Task RemoveTrackItemsFromPlaylistAsync(SpotifyClient spotify, string playlistId, string snapshotId, List<string> trackUris)
    {
        var request = new PlaylistRemoveItemsRequest
        {
            SnapshotId = snapshotId,
            Tracks = trackUris.Select(p => new PlaylistRemoveItemsRequest.Item() { Uri = p }).ToList()
        };
        await spotify.Playlists.RemoveItems(playlistId, request).ConfigureAwait(false);
    }

    internal static async Task<FullPlaylist> CreatePlaylistAsync(SpotifyClient spotify, string userId, string name, string description, bool isPublic)
    {
        var request = new PlaylistCreateRequest(name)
        {
            Description = description,
            Public = isPublic
        };
        var playlist = await spotify.Playlists.Create(userId, request).ConfigureAwait(false);

        return playlist;
    }

    internal static async Task<SnapshotResponse> AddTracksToPlaylistAsync(SpotifyClient spotify, string playlistId, List<string> trackUris)
    {
        var request = new PlaylistAddItemsRequest(trackUris);
        var snapshot = await spotify.Playlists.AddItems(playlistId, request).ConfigureAwait(false);

        return snapshot;
    }
}