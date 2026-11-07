using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ritmo;

internal sealed class ArtworkLoader(string folder, Func<Uri, CancellationToken, Task<byte[]>>? fetch = null)
{
    internal const int LibraryConcurrency = 2;
    readonly SemaphoreSlim workers = new(LibraryConcurrency), coverWorkers = new(1);
    readonly ConcurrentDictionary<string, SemaphoreSlim> originals = new();
    readonly object sync = new();
    sealed class Job
    {
        public readonly CancellationTokenSource Stop = new();
        public Task<BitmapSource?> Task = null!;
        public int Readers;
        public bool Finished;
    }
    readonly Dictionary<string, Job> pending = [];
    readonly Dictionary<string, (BitmapSource Image, LinkedListNode<string> Node, long Bytes)> cache = [];
    readonly LinkedList<string> recent = [];
    long bytes;
    const long Budget = 32L * 1024 * 1024;
    int decoded;
    internal int DecodedCount => Volatile.Read(ref decoded);
    internal int PendingCount { get { lock (sync) return pending.Count; } }
    internal long CacheBytes { get { lock (sync) return bytes; } }
    // Reuse one thumbnail for rows, queue and footer instead of decoding three near-identical sizes.
    internal static int Resolution(int pixels) => pixels <= 64 ? 64 : pixels <= 128 ? 128 : pixels <= 256 ? 256 : pixels <= 512 ? 512 : 1024;

    public BitmapSource? Preview(Track track, int pixels)
    {
        var prefix = track.Id + "|" + track.Art + "|";
        lock (sync)
        {
            var images = cache.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(entry => entry.Value.Image).ToArray();
            return images.Where(image => image.PixelWidth >= pixels).MinBy(image => image.PixelWidth) ?? images.MaxBy(image => image.PixelWidth);
        }
    }
    public async Task<BitmapSource?> Get(Track track, int pixels, CancellationToken ct, bool currentCover = false)
    {
        ct.ThrowIfCancellationRequested();
        pixels = Resolution(pixels);
        var key = track.Id + "|" + track.Art + "|" + pixels;
        Job job;
        lock (sync)
        {
            if (cache.TryGetValue(key, out var hit))
            { recent.Remove(hit.Node); recent.AddLast(hit.Node); return hit.Image; }
            if (!pending.TryGetValue(key, out job!))
            {
                job = new(); pending[key] = job;
                var work = job;
                job.Task = Task.Run(() => Load(track, pixels, key, work, currentCover));
            }
            job.Readers++;
        }
        try { return await job.Task.WaitAsync(ct).ConfigureAwait(false); }
        finally
        {
            lock (sync)
            {
                if (--job.Readers == 0)
                {
                    if (job.Finished) job.Stop.Dispose();
                    else
                    {
                        // One disappearing view must not cancel a shared image still in use elsewhere.
                        if (pending.TryGetValue(key, out var active) && active == job) pending.Remove(key);
                        job.Stop.Cancel();
                    }
                }
            }
        }
    }
    async Task<BitmapSource?> Load(Track track, int pixels, string key, Job job, bool cover)
    {
        var ct = job.Stop.Token;
        var worker = cover ? coverWorkers : workers;
        try
        {
            await worker.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var data = await ReadOriginal(track, ct).ConfigureAwait(false);
                if (data == null) return null;
                using var stream = new MemoryStream(data);
                var compressed = new BitmapImage(); compressed.BeginInit();
                compressed.CacheOption = BitmapCacheOption.OnLoad; compressed.DecodePixelWidth = pixels;
                compressed.StreamSource = stream; compressed.EndInit();
                ct.ThrowIfCancellationRequested();
                // Force JPEG/PNG decoding and conversion here, not lazily on WPF's render thread.
                var converted = new FormatConvertedBitmap(compressed, PixelFormats.Pbgra32, null, 0);
                var stride = checked(converted.PixelWidth * 4);
                var size = checked((long)stride * converted.PixelHeight);
                if (size > Budget) return null;
                var buffer = new byte[(int)size]; converted.CopyPixels(buffer, stride, 0);
                ct.ThrowIfCancellationRequested();
                var image = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
                    PixelFormats.Pbgra32, null, buffer, stride);
                image.Freeze(); Interlocked.Increment(ref decoded);
                lock (sync)
                {
                    ct.ThrowIfCancellationRequested();
                    if (cache.TryGetValue(key, out var previous)) { bytes -= previous.Bytes; recent.Remove(previous.Node); }
                    while (bytes + size > Budget && recent.First is { } first)
                    { bytes -= cache[first.Value].Bytes; cache.Remove(first.Value); recent.RemoveFirst(); }
                    cache[key] = (image, recent.AddLast(key), size); bytes += size;
                }
                return image;
            }
            finally { worker.Release(); }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { return null; }
        finally
        {
            lock (sync)
            {
                if (pending.TryGetValue(key, out var active) && active == job) pending.Remove(key);
                job.Finished = true;
                if (job.Readers == 0) job.Stop.Dispose();
            }
        }
    }
    async Task<byte[]?> ReadOriginal(Track track, CancellationToken ct)
    {
        var path = Path.Combine(folder, "art", Store.Key(track.Id) + ".img");
        var gate = originals.GetOrAdd(path, _ => new(1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!Uri.TryCreate(track.Art, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
            var data = await (fetch?.Invoke(uri, ct) ?? Catalog.Http.GetByteArrayAsync(uri, ct)).ConfigureAwait(false);
            var temp = path + "." + Guid.NewGuid().ToString("N");
            try { await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return data;
        }
        finally { gate.Release(); }
    }
}
