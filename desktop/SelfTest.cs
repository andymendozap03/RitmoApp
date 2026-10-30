using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Ritmo;
public static class SelfTest
{
    public static void Run()
    {
        var report = new List<string>();
        void Check(bool valid, string name) { if (!valid) throw new Exception(name); report.Add("PASS " + name); }
        var folder = Path.Combine(AppContext.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tracks = Enumerable.Range(0, 25).Select(i => new Track("yt:" + i.ToString("D11"), "Tema " + i, "Artista")).ToList();
            var shuffled = tracks.ToList(); Music.ShuffleFuture(shuffled, 4, new Random(42));
            Check(shuffled.Take(5).SequenceEqual(tracks.Take(5)) && shuffled.Select(t => t.Id).Distinct().Count() == 25 && !shuffled.SequenceEqual(tracks), "Aleatorio conserva actual e historial, sin repetir pendientes");
            var window = Music.Upcoming(tracks, 0).ToArray(); var advanced = Music.Upcoming(tracks, 1).ToArray();
            Check(window.Length == 20 && advanced.Length == 20 && window[0].Index == 1 && advanced[0].Index == 2 && advanced[^1].Index == 21 && advanced.All(row => row.Track.Id != tracks[0].Id), "Ventana móvil de 20 canciones conserva orden e índices");
            var special = new List<Track> { new("yt:abcdefghijk", "Canción, \"nueva\"\nsegunda línea", "Artista Ñ", "https://example.org/art.jpg", 123000), new("yt:12345678901", "=HYPERLINK(1)", "@artista") };
            Check(Store.ParseCsv(Store.Csv("Respaldo", special)).SequenceEqual(special), "CSV preserva IDs, Unicode, comillas, saltos y fórmulas protegidas");
            var store = new Store(folder); store.Data.Favorites.AddRange(special); store.Save();
            Check(new Store(folder).Data.Favorites.SequenceEqual(special), "Biblioteca persistente");
            store.SaveLyrics(special[0], new("[00:02.50]Una línea", "Una línea"));
            Check(new Store(folder).ReadLyrics(special[0])?.Lines()[0].Time == 2.5, "Letra offline y marcas de tiempo");
            var json = JsonNode.Parse("""{"items":[{"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"abcdefghijk"},"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Luna","navigationEndpoint":{"watchEndpoint":{"videoId":"abcdefghijk","watchEndpointMusicSupportedConfigs":{"watchEndpointMusicConfig":{"musicVideoType":"MUSIC_VIDEO_TYPE_ATV"}}}}}]}}},{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Feid","navigationEndpoint":{"browseEndpoint":{"browseId":"UCARTIST"}}},{"text":" • 4 M de reproducciones"}]}}}]}}]}""");
            var parsed = Catalog.Parse(json, "Feid Luna");
            Check(parsed.Tracks.Count == 1 && parsed.Tracks[0].Artist == "Feid" && parsed.Tracks[0].Official, "Catálogo identifica artista y grabación oficial");
            Check(Music.Rank(new[] { new Track("cover", "Luna cover", "Fan"), parsed.Tracks[0], new Track("wrong", "Otro tema", "Otro artista", Official: true) }, "Feid Luna")[0].Id == "yt:abcdefghijk", "Ranking prioriza coincidencia oficial");
            Task.Run(async () => await RangeTest(folder, Check)).GetAwaiter().GetResult();
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), report); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    static async Task RangeTest(string folder, Action<bool, string> check)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
        var bytes = new byte[2 * 1024 * 1024 + 317]; new Random(24).NextBytes(bytes);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serve = Task.Run(async () => {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context; try { context = await server.GetContextAsync().WaitAsync(stop.Token); } catch { break; }
                var fields = context.Request.Headers["Range"]![6..].Split('-'); int from = int.Parse(fields[0]), to = Math.Min(bytes.Length - 1, int.Parse(fields[1]));
                if (context.Request.RawUrl == "/invalid") from++;
                context.Response.StatusCode = 206; context.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{bytes.Length}"; context.Response.ContentLength64 = to - from + 1;
                try { await context.Response.OutputStream.WriteAsync(bytes.AsMemory(from, to - from + 1), stop.Token); } catch { } finally { context.Response.Close(); }
            }
        });
        try
        {
            var path = Path.Combine(folder, "audio.bin"); await AudioLibrary.Fetch($"http://127.0.0.1:{port}/audio", "RitmoTest", path, _ => { }, stop.Token);
            check((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes), "Descarga por rangos reconstruye más de 2 MB sin truncar");
            bool rejected = false; try { await AudioLibrary.Fetch($"http://127.0.0.1:{port}/invalid", "RitmoTest", path, _ => { }, stop.Token); } catch (IOException) { rejected = true; }
            check(rejected, "Rechaza rangos incorrectos en vez de marcar descarga completa");
        }
        finally { stop.Cancel(); server.Stop(); await serve; }
    }
}
