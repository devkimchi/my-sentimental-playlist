﻿using SpotifyAPI.Web;

var http = new HttpClient();
http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", "qwertyuiop");

var accessToken = await http.GetStringAsync("https://asdfghjkl.azure-api.net/spotify/access-token").ConfigureAwait(false);
var spotify = new SpotifyClient(accessToken);
var profile = await spotify.UserProfile.Current().ConfigureAwait(false);
var playlists = await spotify.Playlists.CurrentUsers().ConfigureAwait(false);
var playlist = playlists.Items?.SingleOrDefault(p => p.Name!.Equals("My Sentimental Playlist", StringComparison.InvariantCultureIgnoreCase));
if (playlist is not null)
{
    var uris = await GetTrackItemUrisAsync(playlist.Id!).ConfigureAwait(false);
    if (uris.Count > 0)
    {
        await RemoveTrackItemsFromPlaylistAsync(playlist.Id!, playlist.SnapshotId!, uris).ConfigureAwait(false);
    }
}
else
{
    playlist = await CreatePlaylistAsync(
        profile.Id,
        "My Sentimental Playlist",
        "A playlist generated by Semantic Kernel.",
        false).ConfigureAwait(false);
}

var tracks = new List<string>()
{
    "spotify:track:track-id-1",
    "spotify:track:track-id-2",
    "spotify:track:track-id-3",
    "spotify:track:track-id-4",
    "spotify:track:track-id-5",
    "spotify:track:track-id-6",
    "spotify:track:track-id-7",
};

var snapshot = await AddTracksToPlaylistAsync(playlist.Id!, tracks).ConfigureAwait(false);

Console.WriteLine($"Playlist URL: https://open.spotify.com/playlist/{playlist.Id}");

async Task<SnapshotResponse> AddTracksToPlaylistAsync(string playlistId, List<string> trackUris)
{
    var request = new PlaylistAddItemsRequest(trackUris);
    var snapshot = await spotify.Playlists.AddItems(playlistId, request).ConfigureAwait(false);

    return snapshot;
}

async Task<FullPlaylist> CreatePlaylistAsync(string userId, string name, string description, bool isPublic)
{
    var request = new PlaylistCreateRequest(name)
    {
        Description = description,
        Public = isPublic
    };
    var playlist = await spotify.Playlists.Create(userId, request).ConfigureAwait(false);

    return playlist;
}

async Task RemoveTrackItemsFromPlaylistAsync(string playlistId, string snapshotId, List<string> trackUris)
{
    var request = new PlaylistRemoveItemsRequest
    {
        SnapshotId = snapshotId,
        Tracks = trackUris.Select(p => new PlaylistRemoveItemsRequest.Item() { Uri = p }).ToList()
    };
    await spotify!.Playlists.RemoveItems(playlistId, request).ConfigureAwait(false);
}

async Task<List<string>> GetTrackItemUrisAsync(string playlistId)
{
    var tracks = await GetTrackItemsAsync(playlistId).ConfigureAwait(false);
    var uris = tracks?.Select(p => ((FullTrack)p.Track).Uri).ToList();

    return uris!;
}

async Task<List<PlaylistTrack<IPlayableItem>>> GetTrackItemsAsync(string playlistId)
{
    var request = new PlaylistGetItemsRequest(PlaylistGetItemsRequest.AdditionalTypes.Track);
    var tracks = await spotify!.Playlists.GetItems(playlistId, request).ConfigureAwait(false);
    var items = tracks.Items;

    return items!;
}