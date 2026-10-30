using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ritmo;
public sealed class Catalog
{
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    public const string Agent = "com.google.android.youtube/20.10.38 (Linux; U; Android 14) gzip";
    public static string S(JsonNode? node) => node is JsonValue ? node.ToString() : "";
    public static string Text(JsonNode? node) => node is JsonValue ? S(node) : S(node?["simpleText"]) is { Length: > 0 } simple ? simple :
        node?["runs"] is JsonArray runs ? string.Concat(runs.Select(r => S(r?["text"]))) : S(node?["content"]);
    public static IEnumerable<JsonObject> Walk(JsonNode? node)
    {
        if (node is JsonObject obj) { yield return obj; foreach (var p in obj) foreach (var child in Walk(p.Value)) yield return child; }
        else if (node is JsonArray arr) foreach (var item in arr) foreach (var child in Walk(item)) yield return child;
    }
    public async Task<JsonNode?> Request(string endpoint, JsonObject body, CancellationToken ct = default, string client = "WEB_REMIX", string version = "1.20250601.01.00", string agent = Agent)
    {
        var profile = new JsonObject { ["clientName"] = client, ["clientVersion"] = version, ["hl"] = "es", ["gl"] = "US" };
        if (client == "ANDROID") profile["androidSdkVersion"] = 35;
        body["context"] = new JsonObject { ["client"] = profile };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/" + endpoint + "?prettyPrint=false");
        req.Headers.TryAddWithoutValidation("User-Agent", agent); req.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(req, ct); response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public static SearchPage Parse(JsonNode? data, string query = "")
    {
        var tracks = new List<Track>(); var entities = new List<CatalogEntity>(); string? continuation = null;
        foreach (var obj in Walk(data))
        {
            if (obj["continuationCommand"] is JsonObject command) continuation ??= S(command["token"]);
            if (obj["nextContinuationData"] is JsonObject next) continuation ??= S(next["continuation"]);
            var item = obj["musicResponsiveListItemRenderer"] ?? obj["musicTwoRowItemRenderer"] ?? obj["videoRenderer"] ?? obj["playlistVideoRenderer"];
            if (item == null) continue;
            var titleNode = item["title"] ?? item["flexColumns"]?[0]?["musicResponsiveListItemFlexColumnRenderer"]?["text"];
            var title = Text(titleNode); if (title.Length == 0) continue;
            var watch = Walk(item).FirstOrDefault(x => x["watchEndpoint"] != null)?["watchEndpoint"];
            var id = S(item["videoId"]); if (id.Length == 0) id = S(item["playlistItemData"]?["videoId"]);
            if (id.Length == 0) id = S(watch?["videoId"]);
            var byline = item["ownerText"] ?? item["shortBylineText"] ?? item["subtitle"] ?? item["flexColumns"]?.AsArray().ElementAtOrDefault(1)?["musicResponsiveListItemFlexColumnRenderer"]?["text"];
            var raw = Text(byline);
            var artistRuns = (byline?["runs"] as JsonArray ?? []).Where(r => S(r?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]).StartsWith("UC")).Select(r => S(r?["text"])).Distinct();
            var artist = string.Join(", ", artistRuns);
            if (artist.Length == 0) artist = Regex.Replace(raw, @"^(Canción|Song|Vídeo|Video)\s*[•·]\s*", "", RegexOptions.IgnoreCase).Split(['•', '·'])[0].Replace(" - Topic", "").Trim();
            if (!Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$"))
            {
                var browse = item["navigationEndpoint"]?["browseEndpoint"] ?? Walk(titleNode).FirstOrDefault(x => x["browseEndpoint"] != null)?["browseEndpoint"];
                var browseId = S(browse?["browseId"]);
                if (browseId.StartsWith("UC") || browseId.StartsWith("VL") || browseId.StartsWith("MPRE")) entities.Add(new(browseId, title, browseId.StartsWith("UC") ? "Artista" : "Playlist"));
                continue;
            }
            var thumbs = Walk(item["thumbnail"] ?? item["thumbnailRenderer"]).FirstOrDefault(x => x["thumbnails"] != null)?["thumbnails"] as JsonArray;
            var art = S(thumbs?.LastOrDefault()?["url"]);
            if (art.Length == 0) art = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";
            art = Regex.Replace(art, @"=w\d+-h\d+[^?]*", "=w1000-h1000-l90-rj");
            var length = Text(item["lengthText"] ?? item["fixedColumns"]?.AsArray().FirstOrDefault()?["musicResponsiveListItemFixedColumnRenderer"]?["text"]);
            if (length.Length == 0) length = Regex.Match(raw, @"\b\d{1,2}:\d{2}(?::\d{2})?\b").Value;
            long seconds = 0; foreach (var part in length.Split(':')) seconds = seconds * 60 + (long.TryParse(part, out var n) ? n : 0);
            var official = S(watch?["watchEndpointMusicSupportedConfigs"]?["watchEndpointMusicConfig"]?["musicVideoType"]) == "MUSIC_VIDEO_TYPE_ATV" ||
                (item["badges"]?.ToJsonString() + item["ownerBadges"]?.ToJsonString()).Contains("VERIFIED") || raw.EndsWith(" - Topic");
            tracks.Add(new("yt:" + id, Regex.Replace(title, @"\s*\((Official )?(Music )?Video\)", "", RegexOptions.IgnoreCase), artist, art, seconds * 1000, official));
        }
        return new(Music.Rank(tracks, query), entities.DistinctBy(e => e.Id).ToList(), continuation);
    }
    public async Task<SearchPage> Search(string query, string? continuation = null, CancellationToken ct = default) =>
        Parse(await Request("search", continuation == null ? new() { ["query"] = query } : new() { ["continuation"] = continuation }, ct), query);
    public async Task<SearchPage> Browse(string id, string? continuation = null, CancellationToken ct = default) =>
        Parse(await Request("browse", continuation == null ? new() { ["browseId"] = id } : new() { ["continuation"] = continuation }, ct));
    public async Task<Track?> Match(Track track, CancellationToken ct = default)
    {
        if (Regex.IsMatch(track.VideoId, @"^[A-Za-z0-9_-]{11}$")) return track;
        var result = await Search(track.Artist + " " + track.Title, ct: ct);
        var title = Music.Normalize(track.Title); var artist = Music.Normalize(track.Artist);
        var match = result.Tracks.FirstOrDefault(t => (Music.Normalize(t.Title).Contains(title) || title.Contains(Music.Normalize(t.Title))) &&
            (Music.Normalize(t.Artist).Contains(artist) || artist.Contains(Music.Normalize(t.Artist))));
        return match == null ? null : match with { Title = track.Title, Artist = track.Artist };
    }
    public async Task<List<Track>> ImportLink(string url, CancellationToken ct = default)
    {
        var list = Regex.Match(url, @"[?&]list=([A-Za-z0-9_-]+)");
        if (list.Success)
        {
            var page = await Browse("VL" + list.Groups[1].Value, ct: ct); var all = page.Tracks; var seen = new HashSet<string>();
            while (!string.IsNullOrEmpty(page.Continuation) && seen.Add(page.Continuation)) { page = await Browse("", page.Continuation, ct); all.AddRange(page.Tracks); }
            return all.DistinctBy(t => t.Id).ToList();
        }
        var spotify = Regex.Match(url, @"(?:spotify\.com/(?:intl-[^/]+/)?(?:embed/)?playlist/|spotify:playlist:)([A-Za-z0-9]+)");
        if (!spotify.Success) throw new InvalidOperationException("Pega un enlace de playlist compartida o importa un CSV.");
        var html = await Http.GetStringAsync("https://open.spotify.com/embed/playlist/" + spotify.Groups[1].Value, ct);
        var script = Regex.Match(html, @"<script[^>]*id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>");
        if (!script.Success) throw new InvalidOperationException("No se pudo leer la lista compartida. Puedes importarla mediante CSV.");
        var json = JsonNode.Parse(script.Groups[1].Value);
        var items = Walk(json).FirstOrDefault(o => o["trackList"] is JsonArray)?["trackList"] as JsonArray ?? [];
        var matches = new List<Track>();
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var title = Text(item?["title"] ?? item?["name"]); var artist = Text(item?["subtitle"]);
            if (title.Length == 0) continue;
            var match = await Match(new("", title, artist), ct); if (match != null) matches.Add(match);
        }
        return matches;
    }
    public async Task<Lyrics?> FetchLyrics(Track track, CancellationToken ct = default)
    {
        var title = Uri.EscapeDataString(track.Title); var artist = Uri.EscapeDataString(track.Artist);
        try
        {
            var json = JsonNode.Parse(await Http.GetStringAsync($"https://lrclib.net/api/get?track_name={title}&artist_name={artist}&duration={track.Duration / 1000}", ct));
            var lyrics = new Lyrics(S(json?["syncedLyrics"]), S(json?["plainLyrics"])); if (lyrics.Available) return lyrics;
        } catch (Exception) when (!ct.IsCancellationRequested) { }
        try
        {
            var results = JsonNode.Parse(await Http.GetStringAsync("https://lrclib.net/api/search?q=" + Uri.EscapeDataString(track.Artist + " " + track.Title), ct)) as JsonArray;
            var match = results?.Where(x => Music.Normalize(S(x?["trackName"])) == Music.Normalize(track.Title) && Music.Normalize(S(x?["artistName"])) == Music.Normalize(track.Artist))
                .OrderByDescending(x => S(x?["syncedLyrics"]).Length > 0).FirstOrDefault();
            var lyrics = new Lyrics(S(match?["syncedLyrics"]), S(match?["plainLyrics"])); if (lyrics.Available) return lyrics;
        } catch (Exception) when (!ct.IsCancellationRequested) { }
        try
        {
            static string Slug(string value) => Music.Normalize(value).Replace(' ', '-');
            var html = await Http.GetStringAsync($"https://www.letras.com/{Slug(track.Artist)}/{Slug(track.Title)}/", ct);
            var block = Regex.Match(html, """<div[^>]*class=["'][^"']*\blyric-original\b[^"']*["'][^>]*>([\s\S]*?)</div>""");
            if (block.Success)
            {
                var plain = Regex.Replace(block.Groups[1].Value, @"<br\s*/?>|</p>", "\n", RegexOptions.IgnoreCase);
                plain = System.Net.WebUtility.HtmlDecode(Regex.Replace(plain, "<[^>]+>", "")).Trim();
                if (plain.Length > 0) return new("", plain);
            }
        } catch (Exception) when (!ct.IsCancellationRequested) { }
        try
        {
            var json = JsonNode.Parse(await Http.GetStringAsync($"https://api.lyrics.ovh/v1/{artist}/{title}", ct));
            var lyrics = new Lyrics("", S(json?["lyrics"])); return lyrics.Available ? lyrics : null;
        } catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }
    public async IAsyncEnumerable<(string Url, string Agent)> Sources(Track track, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var (name, version, agent) in new[] { ("ANDROID", "20.10.38", Agent), ("WEB_REMIX", "1.20241001.01.00", Agent), ("ANDROID_VR", "1.60.19", "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12; en_US; Quest 3 Build/SQ3A.220605.009.A1) gzip") })
        {
            JsonNode? json;
            try { json = await Request("player", new() { ["videoId"] = track.VideoId }, ct, name, version, agent); }
            catch (Exception) when (!ct.IsCancellationRequested) { continue; }
            var formats = new[] { json?["streamingData"]?["adaptiveFormats"], json?["streamingData"]?["formats"] }.OfType<JsonArray>().SelectMany(x => x).Where(x => x != null)
                .Where(x => S(x!["mimeType"]).Contains("mp4") && S(x["mimeType"]).Contains("mp4a"))
                .OrderByDescending(x => S(x!["mimeType"]).StartsWith("audio/"));
            foreach (var format in formats)
            {
                var url = S(format!["url"]);
                if (url.StartsWith("https://")) yield return (url, agent);
            }
        }
        foreach (var host in new[] { "https://yt.artemislena.eu", "https://inv.nadeko.net", "https://invidious.nerdvpn.de" })
        {
            JsonNode? json;
            try { json = JsonNode.Parse(await Http.GetStringAsync(host + "/api/v1/videos/" + track.VideoId, ct)); }
            catch (Exception) when (!ct.IsCancellationRequested) { continue; }
            foreach (var f in (json?["adaptiveFormats"] as JsonArray ?? []).Where(f => S(f?["type"]).Contains("audio/mp4")))
            { var url = S(f?["url"]); if (url.StartsWith("https://")) yield return (url, Agent); }
        }
    }
}
