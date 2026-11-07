using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ritmo;
public partial class MainWindow
{
    // A real layout/render turn, not just the duration of the button callback.
    async Task PresentedFrame()
    {
        var completion = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) => { CompositionTarget.Rendering -= handler; completion.TrySetResult(); };
        CompositionTarget.Rendering += handler;
        InvalidateVisual();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { CompositionTarget.Rendering -= handler; }
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
    }
    async Task PanelTest()
    {
        var report = Path.Combine(AppContext.BaseDirectory, "panel-test.txt");
        File.WriteAllText(report, "");
        try
        {
            var realArt = Environment.GetCommandLineArgs().Contains("--real-art-test");
            var tracks = Enumerable.Range(0, 320).Select(i => new Track("yt:" + i.ToString("D11"),
                "Canción de prueba con un título largo " + i, "Artista de prueba", Duration: 120000)).ToList();
            var art = BitmapSource.Create(1000, 1000, 96, 96, PixelFormats.Bgr32, null, new byte[4000000], 4000);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(art));
            using var bytes = new MemoryStream(); encoder.Save(bytes);
            if (realArt)
            {
                var samples = new DirectoryInfo(Path.Combine(Store.Root, "art")).GetFiles("*.img").OrderByDescending(f => f.Length).Take(tracks.Count).ToArray();
                if (samples.Length < tracks.Count) throw new Exception("Faltan imágenes reales para esta prueba.");
                await Task.Run(() => {
                    for (var i = 0; i < tracks.Count; i++) File.Copy(samples[i].FullName, Path.Combine(store.Folder, "art", Store.Key(tracks[i].Id) + ".img"), true);
                });
                File.AppendAllText(report, $"REAL ART: {samples.Length} different cached files, {samples.Sum(f => f.Length)} compressed bytes\n");
            }
            else foreach (var track in tracks) await File.WriteAllBytesAsync(Path.Combine(store.Folder, "art", Store.Key(track.Id) + ".img"), bytes.ToArray());
            using (var writer = new BinaryWriter(File.Create(store.Audio(tracks[0]))))
            {
                const int length = 120 * 8000 * 2;
                writer.Write("RIFF"u8); writer.Write(36 + length); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
                writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(length); writer.Write(new byte[length]);
            }
            store.SaveLyrics(tracks[0], new(string.Join('\n', Enumerable.Range(0, 100).Select(i => $"[{i / 60:D2}:{i % 60:D2}.00]Línea de prueba {i}"))));
            playback.Player.Volume = 0;
            await playback.Play(tracks, tracks[0]);
            await RenderTracks(tracks);
            await PresentedFrame();
            await WaitForArtwork();
            File.AppendAllText(report, artworkPresenter.DebugState + "\n");
            var initialDecodes = artwork.DecodedCount;
            if (initialDecodes > 24 || initialDecodes < 4 || artworkPresenter.PresentedCount < 4) throw new Exception($"Portadas visibles sin dibujar o fuera de pantalla: decoded={initialDecodes}, presented={artworkPresenter.PresentedCount}.");
            File.AppendAllText(report, $"PASS initial artwork: {initialDecodes} decoded for {tracks.Count} tracks\n");
            if (realArt) Capture("real-art-library.png");
            var realized = Descendants<ListBoxItem>(content).Count();
            if (realized == 0 || realized > 80 || content.Items.Count != tracks.Count)
                throw new Exception($"La biblioteca debe conservar 320 canciones con pocas filas visibles; filas={realized}.");
            File.AppendAllText(report, $"PASS virtual library: {realized} containers for {tracks.Count} songs\n");
            content.ScrollIntoView(tracks[250]);
            await PresentedFrame();
            if (content.ItemContainerGenerator.ContainerFromItem(tracks[250]) is not ListBoxItem recycled ||
                (recycled.Content as FrameworkElement)?.Tag as string != tracks[250].Id)
                throw new Exception("La fila reciclada no corresponde a la canción solicitada.");
            var favorite = Descendants<Button>(recycled).First(b => AutomationProperties.GetName(b) == "Me gusta");
            var wasFavorite = store.Data.Favorites.Any(t => t.Id == tracks[250].Id);
            favorite.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (store.Data.Favorites.Any(t => t.Id == tracks[250].Id) == wasFavorite) throw new Exception("El botón de la fila reciclada modificó otra canción.");
            File.AppendAllText(report, "PASS recycled row actions address the correct song\n");
            content.ScrollIntoView(tracks[3]); await PresentedFrame();
            var backgroundLayout = playerLayout;
            if (backgroundLayout == null || queueList?.Children.Count != 23) throw new Exception("El reproductor no se preparó antes de abrirlo.");
            foreach (var mode in new[] { "cover", "lyrics", "queue" })
            {
                ClosePlayer();
                lyricsOpen = queueOpen = false;
                await PresentedFrame();
                var offset = scroller!.VerticalOffset;
                var watch = Stopwatch.StartNew();
                if (mode == "cover") OpenPlayer();
                else Descendants<Button>(root).First(b => AutomationProperties.GetName(b) ==
                    (mode == "lyrics" ? "Mostrar / ocultar letras" : "Mostrar / ocultar cola"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var callback = watch.Elapsed.TotalMilliseconds;
                await PresentedFrame();
                File.AppendAllText(report, $"{mode}: callback={callback:F1} ms, first frame={watch.Elapsed.TotalMilliseconds:F1} ms\n");
                await Task.Delay(300);
                await PresentedFrame();
                await WaitForArtwork();
                if (realArt) Capture("real-art-" + mode + ".png");
                if (!ReferenceEquals(backgroundLayout, playerLayout)) throw new Exception("La primera apertura reconstruyó el reproductor.");
                watch.Restart();
                Descendants<Button>(overlay).First(b => AutomationProperties.GetName(b) == "Volver a la biblioteca")
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await PresentedFrame();
                if (expanded || shell.Visibility != Visibility.Visible || Math.Abs(scroller.VerticalOffset - offset) > 1)
                    throw new Exception("Volver perdió la vista o el desplazamiento de la biblioteca.");
                File.AppendAllText(report, $"back from {mode}: {watch.Elapsed.TotalMilliseconds:F1} ms, scroll preserved\n");
            }
            // Recommendation cards share a large WrapPanel; they also need viewport-only images.
            content.Children.Clear(); Hero(tracks[0], tracks); Cards(tracks.Take(32).ToList());
            await PresentedFrame(); await WaitForArtwork();
            var snapshots = artwork.DecodedCount;
            for (var i = 0; i < 4; i++) { OpenPlayer(); await PresentedFrame(); ClosePlayer(); await PresentedFrame(); }
            await WaitForArtwork();
            if (artwork.DecodedCount > snapshots + 2) throw new Exception("Volver al inicio volvió a decodificar las mismas portadas.");
            File.AppendAllText(report, $"PASS home round trips reuse images; cache={artwork.CacheBytes} bytes\n");
            if (realArt) Capture("real-art-home.png");
            var stalledFolder = new Store(Path.Combine(store.Folder, "priority-test-" + Guid.NewGuid().ToString("N"))).Folder;
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource(); int active = 0;
            var loader = new ArtworkLoader(stalledFolder, async (uri, ct) => {
                if (uri.AbsolutePath.StartsWith("/library"))
                { if (Interlocked.Increment(ref active) == ArtworkLoader.LibraryConcurrency) entered.TrySetResult(); await release.Task.WaitAsync(ct); }
                return bytes.ToArray();
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var busy = Enumerable.Range(0, ArtworkLoader.LibraryConcurrency).Select(i => loader.Get(new("busy" + i, "", "", "https://art.test/library" + i), 48, timeout.Token)).ToArray();
            try
            {
                await entered.Task.WaitAsync(timeout.Token);
                var watch = Stopwatch.StartNew();
                var cover = await loader.Get(new("cover", "", "", "https://art.test/cover"), 450, timeout.Token, currentCover: true).WaitAsync(TimeSpan.FromSeconds(2));
                if (cover?.PixelWidth < 450 || busy.Any(t => t.IsCompleted)) throw new Exception("La portada esperó detrás de la biblioteca.");
                File.AppendAllText(report, $"PASS cover bypasses stalled thumbnail workers: {watch.Elapsed.TotalMilliseconds:F1} ms\n");
                var sharedEntered = new TaskCompletionSource(); var sharedRelease = new TaskCompletionSource(); int fetched = 0;
                var shared = new ArtworkLoader(stalledFolder, async (_, ct) => {
                    Interlocked.Increment(ref fetched); sharedEntered.TrySetResult();
                    await sharedRelease.Task.WaitAsync(ct); return bytes.ToArray();
                });
                using var firstView = new CancellationTokenSource();
                var sharedTrack = new Track("shared", "", "", "https://art.test/shared");
                var first = shared.Get(sharedTrack, 48, firstView.Token);
                var second = shared.Get(sharedTrack, 54, timeout.Token);
                await sharedEntered.Task.WaitAsync(timeout.Token); firstView.Cancel();
                try { await first; throw new Exception("La vista oculta no canceló su espera."); } catch (OperationCanceledException) { }
                sharedRelease.TrySetResult();
                if (await second == null || fetched != 1) throw new Exception("Cancelar una vista afectó la imagen compartida de otra vista.");
                File.AppendAllText(report, "PASS hidden view cancels its request without canceling a shared visible image\n");
                var stopEntered = new TaskCompletionSource(); var stopObserved = new TaskCompletionSource();
                var stoppable = new ArtworkLoader(stalledFolder, async (_, ct) => {
                    stopEntered.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, ct); return bytes.ToArray(); }
                    finally { stopObserved.TrySetResult(); }
                });
                using var hiddenView = new CancellationTokenSource();
                var stopped = stoppable.Get(new("cancelled", "", "", "https://art.test/cancelled"), 48, hiddenView.Token);
                await stopEntered.Task.WaitAsync(timeout.Token); hiddenView.Cancel();
                try { await stopped; throw new Exception("La descarga oculta siguió esperando."); } catch (OperationCanceledException) { }
                await stopObserved.Task.WaitAsync(timeout.Token);
                if (stoppable.PendingCount != 0) throw new Exception("Quedaron descargas de una vista oculta en la cola.");
                File.AppendAllText(report, "PASS unused artwork transfer is canceled and removed from the queue\n");
            }
            finally { release.TrySetResult(); await Task.WhenAll(busy); Directory.Delete(stalledFolder, true); }
            File.AppendAllText(report, "SUCCESS\n");
        }
        catch (Exception ex) { File.AppendAllText(report, "FAIL " + ex); Environment.ExitCode = 1; }
        finally { Close(); }
    }
    async Task WaitForArtwork()
    {
        artworkPresenter.Refresh(); await PresentedFrame();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(40);
            if (artwork.PendingCount == 0 && artworkPresenter.ActiveRequests == 0) return;
        }
        throw new Exception("Las portadas visibles no terminaron de cargar.");
    }
}
