using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ritmo;
public partial class MainWindow
{
    string? playerTrackId;
    Grid? lyricCardView, queueCardView;
    bool lyricsRequested;
    TextBlock? playerSongTitle;
    void OpenPlayer()
    {
        if (playback.Current == null) { Notify("Selecciona una canción para abrir el reproductor."); return; }
        expanded = true; overlay.Visibility = Visibility.Visible; BuildPlayer();
    }
    void BuildPlayer()
    {
        var track = playback.Current; if (track == null) return;
        if (playerTrackId == track.Id && playerLayout != null) { ArrangePlayer(); if (queueOpen) RenderQueue(); return; }
        playerTrackId = track.Id; lyricsRequested = false;
        lyricsText = null; queueList = null; renderedQueueRevision = renderedQueueIndex = -1;
        overlay.Children.Clear(); overlay.RowDefinitions.Clear(); overlay.RowDefinitions.Add(new() { Height = GridLength.Auto }); overlay.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var top = new DockPanel { Margin = new Thickness(18, 10, 18, 10) };
        var close = IconButton("back", "Volver a la biblioteca", () => { expanded = false; overlay.Visibility = Visibility.Collapsed; }); DockPanel.SetDock(close, Dock.Left); top.Children.Add(close);
        var title = Label("REPRODUCIENDO AHORA", 12, true); title.HorizontalAlignment = HorizontalAlignment.Center; title.VerticalAlignment = VerticalAlignment.Center; title.Foreground = Icons.Dim; top.Children.Add(title); overlay.Children.Add(top);
        playerLayout = new Grid { Margin = new Thickness(26, 0, 26, 22) }; Grid.SetRow(playerLayout, 1); overlay.Children.Add(playerLayout);
        for (var i = 0; i < 3; i++) playerLayout.ColumnDefinitions.Add(new());
        var song = new Grid { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
        song.RowDefinitions.Add(new() { Height = GridLength.Auto }); song.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var cover = Artwork(track, 450); var box = new Viewbox { Child = cover, Stretch = Stretch.Uniform, MaxHeight = Math.Max(120, ActualHeight - 300), MaxWidth = 480, Margin = new Thickness(0, 4, 0, 20) }; song.Children.Add(box);
        var copy = new StackPanel { Margin = new Thickness(8, 0, 8, 12), MaxWidth = 500 };
        var name = Label(track.Title, lyricsOpen || queueOpen ? 25 : 32, true); playerSongTitle = name; name.TextWrapping = TextWrapping.Wrap; name.MaxHeight = 94; copy.Children.Add(name);
        var artist = Label(track.Artist, 17); artist.Foreground = Icons.Dim; artist.TextWrapping = TextWrapping.Wrap; artist.Margin = new Thickness(0, 8, 0, 0); copy.Children.Add(artist); Grid.SetRow(copy, 1); song.Children.Add(copy); playerLayout.Children.Add(song);
        lyricLines = []; lyricBlocks = []; lyricActive = -1;
        {
            var lyricCard = new Grid { Margin = new Thickness(14, 0, 8, 0), Background = new LinearGradientBrush(Color.FromRgb(30, 53, 43), Color.FromRgb(18, 25, 23), 90) };
            lyricCard.RowDefinitions.Add(new() { Height = GridLength.Auto }); lyricCard.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new DockPanel { Margin = new Thickness(20, 12, 12, 8) };
            var x = IconButton("close", "Cerrar letras", () => { lyricsOpen = false; OpenPlayer(); }); DockPanel.SetDock(x, Dock.Right); heading.Children.Add(x);
            var caption = Label("Letra", 16, true); caption.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(caption); lyricCard.Children.Add(heading);
            lyricsText = new StackPanel { Margin = new Thickness(24, 12, 24, 70) }; lyricsText.Children.Add(Label("Buscando letra…", 20));
            lyricScroll = new ScrollViewer { Content = lyricsText, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(lyricScroll, 1); lyricCard.Children.Add(lyricScroll);
            Grid.SetColumn(lyricCard, 1); playerLayout.Children.Add(lyricCard); lyricCardView = lyricCard;
        }
        {
            var queue = new Grid { Background = Brush("#111917"), Margin = new Thickness(12, 0, 0, 0) }; queue.RowDefinitions.Add(new() { Height = GridLength.Auto }); queue.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            var heading = new DockPanel { Margin = new Thickness(14, 12, 10, 8) }; var x = IconButton("close", "Cerrar cola", () => { queueOpen = false; OpenPlayer(); }); DockPanel.SetDock(x, Dock.Right); heading.Children.Add(x);
            var caption = Label("Cola de reproducción", 15, true); caption.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(caption); queue.Children.Add(heading);
            queueList = new StackPanel { Margin = new Thickness(12, 0, 12, 12) }; var scroll = new ScrollViewer { Content = queueList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); queue.Children.Add(scroll);
            Grid.SetColumn(queue, 2); playerLayout.Children.Add(queue); queueCardView = queue;
        }
        if (queueOpen) RenderQueue();
        ArrangePlayer();
    }
    void ArrangePlayer()
    {
        if (playerLayout == null) return;
        if (lyricCardView != null) lyricCardView.Visibility = lyricsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (queueCardView != null) queueCardView.Visibility = queueOpen ? Visibility.Visible : Visibility.Collapsed;
        if (playerSongTitle != null) playerSongTitle.FontSize = lyricsOpen || queueOpen ? 25 : 32;
        if (lyricsOpen && !lyricsRequested && lyricsText != null && playback.Current is { } track)
        { lyricsRequested = true; _ = ShowLyrics(track, lyricsText); }
        if (playerLayout.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<Viewbox>().FirstOrDefault() is { } cover)
            cover.MaxHeight = Math.Max(120, ActualHeight - 300);
        var columns = playerLayout.ColumnDefinitions;
        columns[0].Width = new GridLength(lyricsOpen ? 1 : queueOpen ? 1 : 1, GridUnitType.Star);
        columns[1].Width = lyricsOpen ? new GridLength(queueOpen ? 1.6 : 2, GridUnitType.Star) : new GridLength(0);
        columns[2].Width = queueOpen ? new GridLength(lyricsOpen ? 1.1 : 1, GridUnitType.Star) : new GridLength(0);
    }
    async Task ShowLyrics(Track track, StackPanel target)
    {
        try
        {
            var lyrics = await audio.Lyrics(track, lifetime.Token);
            if (target != lyricsText || playback.Current?.Id != track.Id) return;
            target.Children.Clear(); lyricLines = lyrics?.Lines() ?? []; lyricBlocks = []; lyricActive = -1;
            var lines = lyricLines.Count > 0 ? lyricLines.Select(l => l.Text).ToList() : (lyrics?.PlainLyrics ?? "No se encontró una letra disponible.").Split('\n').ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var text = Label(lines[i], 28, true); text.TextWrapping = TextWrapping.Wrap; text.Margin = new Thickness(0, 0, 0, 24); text.Foreground = lyricLines.Count > 0 ? Icons.Dim : Icons.Text;
                if (lyricLines.Count > 0) { var time = lyricLines[i].Time; text.Cursor = System.Windows.Input.Cursors.Hand; text.ToolTip = "Ir a " + Time(time); text.MouseLeftButtonUp += (_, _) => playback.Seek(time); }
                lyricBlocks.Add(text); target.Children.Add(text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (target == lyricsText) { target.Children.Clear(); target.Children.Add(Label("Letra no disponible sin conexión.", 20)); } }
    }
    void RenderQueue()
    {
        if (queueList == null || !queueOpen) return;
        if (renderedQueueRevision == playback.QueueRevision && renderedQueueIndex == playback.Index) return;
        renderedQueueRevision = playback.QueueRevision; renderedQueueIndex = playback.Index;
        queueList.Children.Clear();
        var current = playback.Current; if (current == null) return;
        var now = Label("Estás escuchando", 12, true); now.Foreground = Icons.Accent; now.Margin = new Thickness(4, 6, 0, 12); queueList.Children.Add(now);
        queueList.Children.Add(TrackRow(current, OpenPlayer, true));
        var next = Label("Próximas canciones · hasta 20", 12, true); next.Margin = new Thickness(4, 20, 0, 12); queueList.Children.Add(next);
        foreach (var (index, track) in Music.Upcoming(playback.Queue, playback.Index)) queueList.Children.Add(TrackRow(track, () => _ = playback.Jump(index), true));
        if (playback.Index + 1 >= playback.Queue.Count) queueList.Children.Add(Label("Fin de la cola", 13));
    }
    async Task PreviewTest()
    {
        try { await RunPreviewTests(); }
        catch (Exception ex) { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-test.txt"), "FAIL " + ex); Environment.ExitCode = 1; }
        finally { Close(); }
    }
    async Task RunPreviewTests()
    {
        var report = Path.Combine(AppContext.BaseDirectory, "ui-test.txt"); File.WriteAllText(report, "");
        void Check(bool valid, string text) { if (!valid) throw new Exception(text); File.AppendAllText(report, "PASS " + text + "\n"); }
        var tracks = Enumerable.Range(0, 45).Select(i => new Track("yt:" + i.ToString("D11"), "Canción de prueba " + i, "Artista", Duration: 120000)).ToList();
        // Local PCM fixtures keep this regression test independent of providers and user data.
        foreach (var track in tracks.Take(2))
        {
            using var writer = new BinaryWriter(File.Create(store.Audio(track)));
            const int bytes = 120 * 8000 * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
            store.SaveLyrics(track, new("[00:00.00]Primera línea\n[00:30.00]Segunda línea", "Primera línea\nSegunda línea"));
        }
        playback.Player.Volume = 0; await playback.Play(tracks, tracks[0]);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (playback.Loading && DateTime.UtcNow < deadline) await Task.Delay(50);
        Check(!playback.Loading && playback.Playing, "Reproducción local disponible");
        Hero(tracks[0], tracks); content.Children.Add(Heading("Hecho para ti")); Cards(tracks.Take(3).ToList());
        Capture("preview.png");
        var current = playback.Current!; var source = playback.Player.Source; var queue = playback.Queue;
        playback.Seek(65); await Task.Delay(100); var position = playback.Player.Position;
        await playback.Play([current], current with { Title = current.Title + " (otra lista)" });
        Check(expanded && ReferenceEquals(playback.Queue, queue) && playback.Player.Source == source && playback.Playing && playback.Player.Position >= position,
            "Pulsar canción actual conserva audio, cola y posición");
        await playback.Toggle(); var pausedAt = playback.Player.Position;
        Descendants<Button>(TrackRow(current, () => throw new Exception("La fila reinició el audio"))).First()
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(!playback.Playing && playback.Player.Position == pausedAt, "Abrir portada conserva el estado de pausa");
        lyricsOpen = queueOpen = true; OpenPlayer(); await Task.Delay(100); Capture("preview-player.png");
        Check(queueList!.Children.Count == 23, "Cola muestra canción actual y próximas 20");
        Check(lyricsText!.Children.Count == 2 && lyricLines.Count == 2, "Letras guardadas se muestran con marcas de tiempo");
        var retainedLayout = playerLayout; var retainedLyrics = lyricsText; var retainedQueue = queueList;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) { lyricsOpen = !lyricsOpen; queueOpen = !queueOpen; OpenPlayer(); }
        elapsed.Stop();
        Check(ReferenceEquals(retainedLayout, playerLayout) && ReferenceEquals(retainedLyrics, lyricsText)
            && ReferenceEquals(retainedQueue, queueList) && playback.Player.Source == source && !playback.Playing,
            "Alternar paneles conserva controles, letra, audio y pausa");
        File.AppendAllText(report, $"METRIC 100 cambios de panel: {elapsed.Elapsed.TotalMilliseconds:F1} ms\n");
        var artPath = Path.Combine(store.Folder, "art", Store.Key(current.Id) + ".img");
        var generated = BitmapSource.Create(1024, 1024, 96, 96, PixelFormats.Bgr32, null, new byte[1024 * 1024 * 4], 4096);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(generated));
        using (var file = File.Create(artPath)) encoder.Save(file);
        var images = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => artwork.Get(current, 96, lifetime.Token)));
        Check(images[0] is { IsFrozen: true, PixelWidth: 96 } && images.All(img => ReferenceEquals(img, images[0])),
            "Miniaturas comparten imagen reducida y congelada sin decodificaciones duplicadas");
        var large = await artwork.Get(current, 900, lifetime.Token);
        Check(large?.PixelWidth == 900, "Portada grande conserva resolución independiente de miniaturas");
        await playback.Next();
        Check(queueList!.Children.Count == 23 && queueList.Children.OfType<FrameworkElement>().All(row => row.Tag as string != current.Id)
            && (queueList.Children[^1] as FrameworkElement)?.Tag as string == tracks[21].Id,
            "La cola retira la anterior e incorpora una canción al avanzar");
        Width = 800; Height = 580; await Task.Delay(100); Capture("preview-compact.png");
        expanded = false; overlay.Visibility = Visibility.Collapsed; content.Children.Clear(); await RenderTracks(tracks.Take(3));
        Check(content.Children.Count == 3 && content.Children.OfType<Grid>().Count() == 3, "Listas restauradas a controles convencionales sin virtualización");
        var interrupted = RenderTracks(tracks);
        listGeneration++; content.Children.Clear();
        await interrupted;
        Check(content.Children.Count == 0, "Cambiar de página cancela las filas pendientes sin mezclar bibliotecas");
        File.AppendAllText(report, "SUCCESS\n");
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    void Capture(string name)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); png.Save(file);
    }
}
