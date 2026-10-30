using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Ritmo;
public class LibraryData
{
    public List<Track> Favorites { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [];
    public List<Track> Downloads { get; set; } = [];
    public List<Track> Recent { get; set; } = [];
}
public class Store
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RitmoDesktop");
    public string Folder { get; }
    public LibraryData Data { get; }
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public Store(string? folder = null)
    {
        Folder = folder ?? Root;
        foreach (var child in new[] { "audio", "cache", "lyrics", "art" }) Directory.CreateDirectory(Path.Combine(Folder, child));
        Data = Read<LibraryData>("library.json") ?? Read<LibraryData>("library.json.bak") ?? new();
    }
    public static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    public string Audio(Track t) => Path.Combine(Folder, "audio", Key(t.Id) + ".m4a");
    public string Cache(Track t) => Path.Combine(Folder, "cache", Key(t.Id) + ".m4a");
    public bool Downloaded(Track t) => File.Exists(Audio(t)) && new FileInfo(Audio(t)).Length > 0;
    T? Read<T>(string file) { try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Folder, file)), Json); } catch { return default; } }
    public void Save()
    {
        var file = Path.Combine(Folder, "library.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Data, Json));
        if (File.Exists(file)) File.Copy(file, file + ".bak", true);
        File.Move(file + ".tmp", file, true);
    }
    public Lyrics? ReadLyrics(Track t) => Read<Lyrics>(Path.Combine("lyrics", Key(t.Id) + ".json"));
    public void SaveLyrics(Track t, Lyrics lyrics)
    {
        if (!lyrics.Available) return;
        var path = Path.Combine(Folder, "lyrics", Key(t.Id) + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(lyrics, Json)); File.Move(path + ".tmp", path, true);
    }
    public void Favorite(Track t)
    {
        if (Data.Favorites.Any(x => x.Id == t.Id)) Data.Favorites.RemoveAll(x => x.Id == t.Id); else Data.Favorites.Insert(0, t);
        Save();
    }
    public static string Csv(string title, IEnumerable<Track> tracks)
    {
        static string Cell(string value) { if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
        var rows = new List<string[]> { new[] { "Track Name", "Artist Name", "Track ID", "Artwork", "Duration (ms)", "Playlist" } };
        rows.AddRange(tracks.Select(t => new[] { t.Title, t.Artist, t.Id, t.Art, t.Duration.ToString(), title }));
        return string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(Cell)))) + "\r\n";
    }
    public static List<Track> ParseCsv(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF'))) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(","); var headers = parser.ReadFields()?.Select(Music.Normalize).ToArray() ?? [];
        int Find(params string[] keys) => Array.FindIndex(headers, keys.Contains);
        int title = Find("track name", "title", "song", "song name"), artist = Find("artist name", "artist", "artists"), id = Find("track id"), art = Find("artwork"), duration = Find("duration ms");
        if (title < 0 || artist < 0) throw new InvalidDataException("El CSV necesita columnas de canción y artista.");
        var tracks = new List<Track>(); bool own = id >= 0 && art >= 0 && duration >= 0;
        while (!parser.EndOfData)
        {
            var cells = parser.ReadFields() ?? [];
            string Cell(int index) { var value = index >= 0 && index < cells.Length ? cells[index] : ""; return own && value.StartsWith('\'') && value[1..].TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' ? value[1..] : value; }
            if (string.IsNullOrWhiteSpace(Cell(title))) continue;
            var validId = System.Text.RegularExpressions.Regex.IsMatch(Cell(id), @"^yt:[A-Za-z0-9_-]{11}$") ? Cell(id) : "";
            tracks.Add(new(validId, Cell(title), Cell(artist), Cell(art), long.TryParse(Cell(duration), out var ms) ? Math.Max(ms, 0) : 0));
        }
        return tracks;
    }
}
