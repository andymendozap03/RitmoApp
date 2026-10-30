using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Ritmo;
public sealed class AudioLibrary(Store store, Catalog catalog)
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> lyricLocks = new();
    readonly SemaphoreSlim workers = new(3);
    public event Action<string, double>? Progress;
    public async Task<string> Prepare(Track track, CancellationToken ct)
    {
        var gate = locks.GetOrAdd(track.Id, _ => new(1)); await gate.WaitAsync(ct);
        try
        {
            if (store.Downloaded(track)) return store.Audio(track);
            var path = store.Cache(track); if (File.Exists(path)) return path;
            await workers.WaitAsync(ct);
            try
            {
                Exception? last = null;
                await foreach (var source in catalog.Sources(track, ct))
                {
                    try
                    {
                        await Task.Run(() => Fetch(source.Url, source.Agent, path + ".part", p => Progress?.Invoke(track.Id, p), ct), ct);
                        File.Move(path + ".part", path, true); return path;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { last = ex; }
                    finally { if (File.Exists(path + ".part")) File.Delete(path + ".part"); }
                }
                throw new IOException("No hay un audio disponible para esta canción. Intenta otra versión.", last);
            }
            finally { workers.Release(); }
        }
        finally { gate.Release(); }
    }
    // Complete, validated files only. A partial CDN response must never be shown as downloaded.
    internal static async Task Fetch(string url, string agent, string target, Action<double> progress, CancellationToken ct)
    {
        const int chunk = 1024 * 1024;
        long received = 0, total = -1;
        var notifications = System.Diagnostics.Stopwatch.StartNew();
        var lengthMatch = System.Text.RegularExpressions.Regex.Match(url, @"[?&]clen=(\d+)");
        long declared = lengthMatch.Success && long.TryParse(lengthMatch.Groups[1].Value, out var length) ? length : -1;
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        do
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(received, total > 0 ? Math.Min(total - 1, received + chunk - 1) : received + chunk - 1);
            req.Headers.TryAddWithoutValidation("User-Agent", agent); req.Headers.Referrer = new Uri("https://music.youtube.com/");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(40));
            using var response = await Catalog.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var mime = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || mime.Contains("json", StringComparison.OrdinalIgnoreCase)) throw new IOException("El origen devolvió un error en lugar de audio.");
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                if (range?.From != received || !range.HasLength || range.To == null) throw new IOException("El servidor devolvió un rango de audio incorrecto.");
                if (total > 0 && range.Length != total) throw new IOException("El tamaño del audio cambió durante la descarga.");
                total = range.Length!.Value;
            }
            else
            {
                if (received != 0) throw new IOException("El servidor ignoró el rango de audio.");
                total = response.Content.Headers.ContentLength ?? -1;
                if (total <= 0) throw new IOException("No se pudo validar el tamaño del audio.");
            }
            if (declared > 0 && total != declared) throw new IOException("La respuesta no contiene el archivo de audio completo.");
            if (total > 512L * 1024 * 1024) throw new IOException("El archivo supera el límite de 512 MB por canción.");
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!).AvailableFreeSpace;
            if (free < total - received + 32 * 1024 * 1024) throw new IOException("No hay suficiente espacio para guardar la canción.");
            var expected = range?.To is long end ? end - received + 1 : total;
            long count = 0; var buffer = new byte[65536];
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            int n;
            while ((n = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                count += n; if (count > expected) throw new IOException("Respuesta de audio inconsistente.");
                await output.WriteAsync(buffer.AsMemory(0, n), timeout.Token); received += n;
                if (notifications.ElapsedMilliseconds >= 100 || received == total)
                { progress((double)received / total); notifications.Restart(); }
            }
            if (count != expected || count == 0) throw new IOException("El archivo de audio llegó incompleto.");
        } while (received < total);
        if (received != total) throw new IOException("La descarga no está completa.");
        await output.FlushAsync(ct);
    }
    public async Task Download(Track track, CancellationToken ct)
    {
        var source = await Task.Run(() => Prepare(track, ct), ct);
        if (!store.Downloaded(track))
        {
            var target = store.Audio(track);
            await Task.Run(() => { File.Copy(source, target + ".part", true); File.Move(target + ".part", target, true); }, ct);
        }
        if (store.Data.Downloads.All(t => t.Id != track.Id)) store.Data.Downloads.Add(track);
        store.Save();
        _ = CacheLyrics(track, ct);
    }
    async Task CacheLyrics(Track track, CancellationToken ct) { try { await Lyrics(track, ct); } catch (Exception) { /* Audio stays available even when the lyrics provider is offline. */ } }
    public async Task<Lyrics?> Lyrics(Track track, CancellationToken ct)
    {
        var gate = lyricLocks.GetOrAdd(track.Id, _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            var cached = await Task.Run(() => store.ReadLyrics(track), ct); if (cached?.Available == true) return cached;
            var lyrics = await Task.Run(() => catalog.FetchLyrics(track, ct), ct);
            if (lyrics?.Available == true) await Task.Run(() => store.SaveLyrics(track, lyrics), ct);
            return lyrics;
        }
        finally { gate.Release(); }
    }
    public void TrimCache()
    {
        var files = new DirectoryInfo(Path.Combine(store.Folder, "cache")).GetFiles("*.m4a").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long size = 0;
        foreach (var file in files) { size += file.Length; if (size > 512L * 1024 * 1024) try { file.Delete(); } catch (IOException) { } }
    }
}
