using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Ritmo;
public partial class MainWindow
{
    int listGeneration;
    async Task Route(string target)
    {
        pageRequest?.Cancel(); pageRequest?.Dispose(); pageRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var ct = pageRequest.Token;
        view = target; expanded = false; overlay.Visibility = Visibility.Collapsed; continuation = browseId = null; paging = false;
        listGeneration++; content.Children.Clear(); displayed = []; downloadButtons.Clear(); scroller.ScrollToTop();
        foreach (var pair in navigation) pair.Value.Background = Brush(pair.Key == view ? "#24312E" : "#0B1110");
        pageTitle.Text = target switch { "home" => "Inicio", "search" => "Buscar", "favorites" => "Me gusta", "playlists" => "Playlists", _ => "Descargas" };
        Notify("");
        if (target == "favorites") { LibraryToolbar("Me gusta", store.Data.Favorites, true); await RenderTracks(store.Data.Favorites); return; }
        if (target == "downloads") { var tracks = store.Data.Downloads.Where(store.Downloaded).ToList(); LibraryToolbar("Descargas", tracks); await RenderTracks(tracks); return; }
        if (target == "playlists") { PlaylistFolders(); return; }
        if (target == "search" && searchQuery.Length == 0) { Notify("Escribe una canción o artista y pulsa Enter."); search.Focus(); return; }
        try
        {
            Notify("Buscando música…");
            if (target == "home")
            {
                if (store.Data.Recent.Count > 0) { content.Children.Add(Heading("Escuchado recientemente")); Cards(store.Data.Recent.Take(8).ToList()); }
                var queries = new[] { "éxitos latinos", "pop en español", "rock en español", "música para conducir", "reggaeton éxitos", "pop internacional" };
                var query = store.Data.Favorites.Count > 0 && Random.Shared.Next(2) == 0 ? store.Data.Favorites[Random.Shared.Next(store.Data.Favorites.Count)].Artist : queries[Random.Shared.Next(queries.Length)];
                var page = await Task.Run(() => catalog.Search(query, ct: ct), ct); ct.ThrowIfCancellationRequested();
                if (page.Tracks.Count > 0) { Hero(page.Tracks[Random.Shared.Next(Math.Min(8, page.Tracks.Count))], page.Tracks); content.Children.Add(Heading("Hecho para ti")); Cards(page.Tracks.Take(24).ToList()); }
                Notify("Una selección nueva cada vez que vuelves a Inicio.");
            }
            else
            {
                var page = await Task.Run(() => catalog.Search(searchQuery, ct: ct), ct); ct.ThrowIfCancellationRequested(); continuation = page.Continuation;
                await RenderTracks(page.Tracks); ct.ThrowIfCancellationRequested(); Entities(page.Entities); Notify($"{page.Tracks.Count} canciones · primero las coincidencias oficiales disponibles");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { Notify("No se pudo cargar el catálogo. " + ex.Message); content.Children.Add(Button("Reintentar", "repeat", () => _ = Route(target))); } }
    }
    static TextBlock Heading(string text) { var label = Label(text, 24, true); label.Margin = new Thickness(0, 20, 0, 16); return label; }
    void Hero(Track track, List<Track> tracks)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26) };
        var eyebrow = Label("RITMO · PARA TI", 12, true); eyebrow.Foreground = Icons.Accent; copy.Children.Add(eyebrow);
        var greeting = Label("¿Qué quieres escuchar?", 30, true); greeting.TextWrapping = TextWrapping.Wrap; greeting.Margin = new Thickness(0, 10, 0, 16); copy.Children.Add(greeting);
        copy.Children.Add(Label(track.Title, 23, true)); var artist = Label(track.Artist, 16); artist.Foreground = Icons.Dim; artist.Margin = new Thickness(0, 6, 0, 18); copy.Children.Add(artist);
        var actions = new WrapPanel(); var play = Button("Escuchar ahora", "play", () => _ = playback.Play(tracks, track)); play.Background = Brush("#167C48"); actions.Children.Add(play); actions.Children.Add(DownloadButton(track, true)); copy.Children.Add(actions); grid.Children.Add(copy);
        var art = Artwork(track, 200); art.Margin = new Thickness(0, 24, 24, 24); Grid.SetColumn(art, 1); grid.Children.Add(art);
        content.Children.Add(new Border { Child = grid, Background = new LinearGradientBrush(Color.FromRgb(28, 62, 47), Color.FromRgb(17, 25, 23), 30), CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 12, 0, 8) });
    }
    void Cards(List<Track> tracks)
    {
        var wrap = new WrapPanel();
        foreach (var track in tracks)
        {
            var panel = new StackPanel(); panel.Children.Add(Artwork(track, 160));
            var title = Label(track.Title, 15, true); title.Margin = new Thickness(0, 12, 0, 5); panel.Children.Add(title);
            var artist = Label(track.Artist, 12); artist.Foreground = Icons.Dim; panel.Children.Add(artist);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var play = IconButton("play", "Reproducir " + track.Title, () => _ = playback.Play(tracks, track)); play.Background = Brush("#167C48"); actions.Children.Add(play);
            actions.Children.Add(DownloadButton(track)); actions.Children.Add(IconButton("plus", "Añadir a playlist", () => PickPlaylist(track))); panel.Children.Add(actions);
            wrap.Children.Add(new Border { Child = panel, Width = 188, Padding = new Thickness(14), Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(16), Background = Brush("#111917") });
        }
        content.Children.Add(wrap);
    }
    async Task RenderTracks(IEnumerable<Track> tracks, bool append = false)
    {
        var incoming = tracks.ToList(); if (!append) displayed = incoming; else displayed.AddRange(incoming.Where(t => displayed.All(x => x.Id != t.Id)));
        if (displayed.Count == 0) { content.Children.Add(Label("Todavía no hay canciones aquí. Busca música o importa una lista.", 16)); return; }
        var generation = listGeneration; var count = 0;
        foreach (var track in incoming)
        {
            if (generation != listGeneration || lifetime.IsCancellationRequested) return;
            content.Children.Add(TrackRow(track, () => _ = playback.Play(displayed.ToList(), track)));
            if (++count % 16 == 0) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
    }
    FrameworkElement TrackRow(Track track, Action play, bool compact = false)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Tag = track.Id };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var textGrid = new Grid(); textGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); textGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var art = Artwork(track, compact ? 42 : 48); art.Margin = new Thickness(0, 0, 14, 0); textGrid.Children.Add(art);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; copy.Children.Add(Label(track.Title, compact ? 13 : 15, true)); var artist = Label(track.Artist, 12); artist.Foreground = Icons.Dim; copy.Children.Add(artist); Grid.SetColumn(copy, 1); textGrid.Children.Add(copy);
        var button = new Button { Content = textGrid, Padding = new Thickness(8), Margin = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brush(playback.Current?.Id == track.Id ? "#24312E" : "#111917"), ToolTip = track.Title + " · " + track.Artist };
        // ContentPresenter stretches for track text so long titles cannot push actions out.
        button.Click += (_, _) => { if (playback.Current?.Id == track.Id) OpenPlayer(); else play(); }; row.Children.Add(button);
        if (!compact)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            Button? favorite = null;
            favorite = IconButton("heart", "Me gusta", () => { store.Favorite(track); favorite!.Content = Icons.View("heart", store.Data.Favorites.Any(t => t.Id == track.Id) ? Icons.Accent : Icons.Dim); UpdatePlayback(); });
            favorite.Content = Icons.View("heart", store.Data.Favorites.Any(t => t.Id == track.Id) ? Icons.Accent : Icons.Dim); actions.Children.Add(favorite);
            actions.Children.Add(DownloadButton(track)); actions.Children.Add(IconButton("plus", "Añadir a playlist", () => PickPlaylist(track)));
            Grid.SetColumn(actions, 1); row.Children.Add(actions);
        }
        return row;
    }
    Button DownloadButton(Track track, bool labeled = false)
    {
        var button = labeled ? Button("Descargar", "download", () => _ = Download(track)) : IconButton("download", "Descargar", () => _ = Download(track));
        if (store.Downloaded(track)) { button.Content = labeled ? Label("✓ Disponible offline") : Icons.View("check", Icons.Accent); button.ToolTip = "Disponible sin conexión"; }
        if (!downloadButtons.TryGetValue(track.Id, out var buttons)) downloadButtons[track.Id] = buttons = [];
        buttons.Add(button); return button;
    }
    async Task Download(Track track)
    {
        if (store.Downloaded(track)) { Notify("Esta canción ya está disponible sin conexión."); return; }
        if (!downloading.Add(track.Id)) return;
        Notify("Descargando " + track.Title + "…");
        try
        {
            await audio.Download(track, lifetime.Token);
            if (downloadButtons.TryGetValue(track.Id, out var buttons)) foreach (var button in buttons) { button.Content = Icons.View("check", Icons.Accent); button.ToolTip = "Disponible sin conexión"; }
            Notify("Guardada sin conexión: " + track.Title);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notify("No se pudo descargar " + track.Title + ": " + ex.Message); }
        finally { downloading.Remove(track.Id); if (downloading.Count == 0) TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None; }
    }
    void LibraryToolbar(string title, List<Track> tracks, bool import = false)
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var play = Button("Reproducir", "play", () => { if (tracks.Count > 0) _ = playback.Play(tracks, tracks[0]); }); play.IsEnabled = tracks.Count > 0; bar.Children.Add(play);
        bar.Children.Add(Button("Descargar todo", "download", async () => { foreach (var track in tracks.ToList()) { if (lifetime.IsCancellationRequested) break; await Download(track); } }));
        bar.Children.Add(Button("Respaldar CSV", "folder", () => Export(title, tracks)));
        if (import) bar.Children.Add(Button("Importar CSV", "plus", () => _ = ImportCsv(true)));
        content.Children.Add(bar);
    }
    void Export(string title, List<Track> tracks)
    {
        var dialog = new SaveFileDialog { Filter = "Respaldo CSV|*.csv", FileName = string.Concat(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) + ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, Store.Csv(title, tracks), new UTF8Encoding(true)); Notify("Respaldo guardado."); } catch (Exception ex) { Notify(ex.Message); }
    }
    async Task ImportCsv(bool favorites)
    {
        var dialog = new OpenFileDialog { Filter = "Listas CSV|*.csv" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            var source = Store.ParseCsv(await File.ReadAllTextAsync(dialog.FileName)); var tracks = new List<Track>();
            for (var i = 0; i < source.Count; i++)
            {
                Notify($"Importando {i + 1} de {source.Count}…");
                try { var track = await catalog.Match(source[i], lifetime.Token); if (track != null) tracks.Add(track); } catch (Exception) when (!lifetime.IsCancellationRequested) { }
            }
            if (tracks.Count == 0) { Notify("No se encontraron canciones para importar."); return; }
            if (favorites) store.Data.Favorites = store.Data.Favorites.Concat(tracks).DistinctBy(t => t.Id).ToList();
            else store.Data.Playlists.Insert(0, new() { Title = Path.GetFileNameWithoutExtension(dialog.FileName), Tracks = tracks.DistinctBy(t => t.Id).ToList() });
            store.Save(); await Route(favorites ? "favorites" : "playlists"); Notify($"Importadas {tracks.Count} de {source.Count} canciones.");
        } catch (Exception ex) { Notify("No se pudo importar: " + ex.Message); }
    }
    void PlaylistFolders()
    {
        var bar = new WrapPanel(); bar.Children.Add(Button("Nueva playlist", "plus", () => PickPlaylist(null))); bar.Children.Add(Button("Importar CSV", "folder", () => _ = ImportCsv(false)));
        bar.Children.Add(Button("Importar enlace", "plus", ImportLinkDialog)); content.Children.Add(bar);
        var folders = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var list in store.Data.Playlists)
        {
            var panel = new StackPanel { Width = 170 }; panel.Children.Add(list.Tracks.Count > 0 ? Artwork(list.Tracks[0], 150) : Icons.View("folder", Icons.Accent, 100));
            var title = Label(list.Title, 17, true); title.Margin = new Thickness(0, 12, 0, 6); panel.Children.Add(title); panel.Children.Add(Label(list.Tracks.Count + " canciones", 12));
            var button = new Button { Content = panel, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(18) }; button.Click += (_, _) => _ = PlaylistDetail(list); folders.Children.Add(button);
        }
        content.Children.Add(folders);
        if (store.Data.Playlists.Count == 0) content.Children.Add(Label("Crea tu primera playlist y añade canciones con el botón +.", 16));
    }
    async Task PlaylistDetail(Playlist list)
    {
        downloadButtons.Clear(); listGeneration++; content.Children.Clear(); content.Children.Add(Button("Mis playlists", "back", () => _ = Route("playlists"))); content.Children.Add(Heading(list.Title));
        var generation = listGeneration;
        LibraryToolbar(list.Title, list.Tracks); await RenderTracks(list.Tracks);
        if (generation != listGeneration) return;
        content.Children.Add(Button("Quitar playlist", "close", () => { if (MessageBox.Show(this, "¿Quitar «" + list.Title + "»? Las descargas se conservarán.", "Ritmo", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { store.Data.Playlists.Remove(list); store.Save(); _ = Route("playlists"); } }));
    }
    void PickPlaylist(Track? track)
    {
        var (dialog, panel) = Dialog(track == null ? "Nueva playlist" : "Añadir a playlist");
        if (track != null) panel.Children.Add(Label(track.Title + " · " + track.Artist, 15));
        var name = new TextBox { Margin = new Thickness(0, 16, 0, 12), MaxLength = 80 }; panel.Children.Add(name);
        panel.Children.Add(Button(track == null ? "Crear" : "Crear y añadir", "plus", () => {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            var list = new Playlist { Title = name.Text.Trim() }; if (track != null) list.Tracks.Add(track); store.Data.Playlists.Insert(0, list); store.Save(); dialog.Close(); if (view == "playlists") { listGeneration++; content.Children.Clear(); PlaylistFolders(); }
        }));
        if (track != null) foreach (var list in store.Data.Playlists)
        {
            var button = Button(list.Title, "folder", () => { list.Tracks.Add(track); store.Save(); dialog.Close(); Notify("Añadida a " + list.Title); }); button.IsEnabled = list.Tracks.All(t => t.Id != track.Id); panel.Children.Add(button);
        }
        dialog.ShowDialog();
    }
    (Window, StackPanel) Dialog(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(Heading(title));
        var dialog = new Window { Owner = this, Title = title, Width = 440, MaxHeight = Math.Max(400, ActualHeight - 40), SizeToContent = SizeToContent.Height, Background = Brush("#111917"), Foreground = Icons.Text, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Icon = Icon, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        dialog.SourceInitialized += (_, _) => WindowsIntegration.DarkTitlebar(dialog);
        return (dialog, panel);
    }
    void ImportLinkDialog()
    {
        var (dialog, panel) = Dialog("Importar playlist compartida"); var input = new TextBox { Margin = new Thickness(0, 10, 0, 16) }; panel.Children.Add(input);
        panel.Children.Add(Button("Importar", "plus", async () => {
            var url = input.Text.Trim(); dialog.Close(); Notify("Leyendo playlist…");
            try { var tracks = await catalog.ImportLink(url, lifetime.Token); if (tracks.Count == 0) throw new InvalidOperationException("No se encontraron canciones."); store.Data.Playlists.Insert(0, new() { Title = "Playlist importada", Tracks = tracks }); store.Save(); await Route("playlists"); }
            catch (Exception ex) { Notify(ex.Message); }
        })); dialog.ShowDialog();
    }
    void Entities(List<CatalogEntity> entities)
    {
        if (entities.Count == 0) return; content.Children.Add(Heading("Artistas y playlists")); var bar = new WrapPanel();
        foreach (var entity in entities) bar.Children.Add(Button(entity.Title, "folder", () => _ = OpenEntity(entity))); content.Children.Add(bar);
    }
    async Task OpenEntity(CatalogEntity entity)
    {
        pageRequest?.Cancel(); pageRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var ct = pageRequest.Token;
        view = "catalog"; browseId = entity.Id; continuation = null; listGeneration++; content.Children.Clear(); pageTitle.Text = entity.Title; Notify("Cargando…");
        try { var page = await Task.Run(() => catalog.Browse(entity.Id, ct: ct), ct); ct.ThrowIfCancellationRequested(); continuation = page.Continuation; await RenderTracks(page.Tracks); ct.ThrowIfCancellationRequested(); Entities(page.Entities); Notify(""); }
        catch (OperationCanceledException) { } catch (Exception ex) { Notify(ex.Message); }
    }
    async Task More()
    {
        if (paging || string.IsNullOrEmpty(continuation) || view is not ("search" or "catalog")) return;
        var ct = pageRequest?.Token ?? lifetime.Token; var token = continuation; paging = true;
        try
        {
            var page = browseId == null ? await Task.Run(() => catalog.Search(searchQuery, token, ct), ct) : await Task.Run(() => catalog.Browse(browseId, token, ct), ct); ct.ThrowIfCancellationRequested();
            continuation = page.Continuation == token ? null : page.Continuation; await RenderTracks(page.Tracks.Where(t => displayed.All(x => x.Id != t.Id)).ToList(), true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notify("No se pudieron cargar más resultados: " + ex.Message); }
        finally { if (!ct.IsCancellationRequested) paging = false; }
    }
}
