using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ritmo;
public record Track(string Id, string Title, string Artist, string Art = "", long Duration = 0, bool Official = false)
{
    public string VideoId => Id.StartsWith("yt:") ? Id[3..] : Id;
    public string DurationText => TimeSpan.FromMilliseconds(Duration).ToString(@"m\:ss");
}
public class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Playlist";
    public List<Track> Tracks { get; set; } = [];
}
public record Lyrics(string SyncedLyrics = "", string PlainLyrics = "")
{
    public bool Available => !string.IsNullOrWhiteSpace(SyncedLyrics + PlainLyrics);
    public List<(double Time, string Text)> Lines()
    {
        var lines = new List<(double Time, string Text)>();
        foreach (var line in SyncedLyrics.Split('\n'))
        {
            var stamps = Regex.Matches(line, @"\[(\d+):(\d{2}(?:\.\d+)?)\]");
            var text = Regex.Replace(line, @"\[[^\]]*\]", "").Trim();
            foreach (Match stamp in stamps)
                if (text.Length > 0) lines.Add((int.Parse(stamp.Groups[1].Value) * 60 + double.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture), text));
        }
        return lines.OrderBy(x => x.Time).ToList();
    }
}
public record CatalogEntity(string Id, string Title, string Kind);
public record SearchPage(List<Track> Tracks, List<CatalogEntity> Entities, string? Continuation);
public static class Music
{
    public static IEnumerable<(int Index, Track Track)> Upcoming(IReadOnlyList<Track> queue, int currentIndex) =>
        queue.Skip(currentIndex + 1).Take(20).Select((track, offset) => (currentIndex + 1 + offset, track));
    public static string Normalize(string text) => Regex.Replace(new string(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    public static List<Track> Rank(IEnumerable<Track> tracks, string query)
    {
        var tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tracks.DistinctBy(t => t.Id).OrderByDescending(t => {
            var title = Normalize(t.Title); var words = Normalize(t.Title + " " + t.Artist).Split(' ');
            var relevance = tokens.Length == 0 ? 1 : (double)tokens.Count(words.Contains) / tokens.Length;
            var version = new[] { "cover", "karaoke", "remix", "slowed", "live", "instrumental" }.Any(v => title.Split(' ').Contains(v) && !tokens.Contains(v));
            return relevance * 120 + (relevance >= .6 && t.Official ? 50 : 0) - (version ? 35 : 0);
        }).ToList();
    }
    public static void ShuffleFuture(List<Track> queue, int index, Random? random = null)
    {
        random ??= Random.Shared;
        for (var i = queue.Count - 1; i > index + 1; i--) { var j = random.Next(index + 1, i + 1); (queue[i], queue[j]) = (queue[j], queue[i]); }
    }
}
