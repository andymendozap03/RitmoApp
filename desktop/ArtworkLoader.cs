using System.IO;
using System.Windows.Media.Imaging;

namespace Ritmo;

// Decoding and disk I/O never run on the dispatcher. Only frozen images cross threads.
internal sealed class ArtworkLoader(string folder)
{
    readonly SemaphoreSlim workers = new(4);
    readonly object sync = new();
    readonly Dictionary<string, Task<BitmapSource?>> pending = [];
    readonly Dictionary<string, (BitmapSource Image, LinkedListNode<string> Node, long Bytes)> cache = [];
    readonly LinkedList<string> recent = [];
    long bytes;
    const long Budget = 48L * 1024 * 1024;

    public Task<BitmapSource?> Get(Track track, int pixels, CancellationToken ct)
    {
        var key = track.Id + "|" + track.Art + "|" + pixels;
        lock (sync)
        {
            if (cache.TryGetValue(key, out var hit))
            {
                recent.Remove(hit.Node); recent.AddLast(hit.Node);
                return Task.FromResult<BitmapSource?>(hit.Image);
            }
            if (pending.TryGetValue(key, out var task)) return task;
            task = Task.Run(async () => {
                // Yielding through the lock also makes insertion into pending atomic.
                try
                {
                    await workers.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var path = Path.Combine(folder, "art", Store.Key(track.Id) + ".img");
                        byte[] data;
                        if (File.Exists(path)) data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                        else
                        {
                            if (!Uri.TryCreate(track.Art, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
                            data = await Catalog.Http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
                            var temp = path + "." + Guid.NewGuid().ToString("N");
                            try { await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false); File.Move(temp, path, true); }
                            finally { if (File.Exists(temp)) File.Delete(temp); }
                        }
                        using var stream = new MemoryStream(data);
                        var image = new BitmapImage(); image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = pixels;
                        image.StreamSource = stream; image.EndInit(); image.Freeze();
                        var size = (long)image.PixelWidth * image.PixelHeight * 4;
                        lock (sync)
                        {
                            if (size <= Budget)
                            {
                                while (bytes + size > Budget && recent.First is { } first)
                                { bytes -= cache[first.Value].Bytes; cache.Remove(first.Value); recent.RemoveFirst(); }
                                cache[key] = (image, recent.AddLast(key), size); bytes += size;
                            }
                        }
                        return (BitmapSource?)image;
                    }
                    finally { workers.Release(); }
                }
                catch (Exception) { return null; }
                finally { lock (sync) pending.Remove(key); }
            }, CancellationToken.None);
            pending[key] = task; return task;
        }
    }
}
