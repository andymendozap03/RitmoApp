using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Ritmo;
public partial class MainWindow : Window
{
    readonly Store store = new(Environment.GetCommandLineArgs().Contains("--ui-test") ? Path.Combine(AppContext.BaseDirectory, "ui-test-data") : null); readonly Catalog catalog = new(); readonly AudioLibrary audio; readonly Playback playback;
    readonly ArtworkLoader artwork;
    readonly CancellationTokenSource lifetime = new(); CancellationTokenSource? pageRequest;
    readonly Grid root = new(), shell = new(), main = new(), overlay = new();
    readonly StackPanel sidebar = new(), content = new(), footerInfo = new();
    readonly TextBlock pageTitle = Label("Inicio", 28, true), notice = Label("", 13), currentTime = Label("0:00", 12), totalTime = Label("0:00", 12);
    readonly TextBox search = new(); readonly Slider seek = new() { Minimum = 0, Maximum = 1, IsMoveToPointEnabled = true };
    readonly Button playButton, shuffleButton, repeatButton, favoriteButton;
    readonly ThumbButtonInfo thumbPlay = new(), thumbFavorite = new(), thumbPrevious = new(), thumbNext = new();
    readonly ScrollViewer scroller; readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Dictionary<string, Button> navigation = new(); readonly HashSet<string> downloading = [];
    readonly Dictionary<string, List<Button>> downloadButtons = new();
    List<Track> displayed = []; string view = "home", searchQuery = ""; string? continuation, browseId; bool paging, dragging, expanded, lyricsOpen, queueOpen;
    Track? renderedTrack; int lyricActive = -1; List<(double Time, string Text)> lyricLines = []; List<TextBlock> lyricBlocks = [];
    ScrollViewer? lyricScroll; StackPanel? lyricsText, queueList; Grid? playerLayout;
    int renderedQueueRevision = -1, renderedQueueIndex = -1;
    public MainWindow()
    {
        Title = "Ritmo"; Width = 1280; Height = 830; MinWidth = 760; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) => WindowsIntegration.DarkTitlebar(this);
        Background = Brush("#060A09"); Foreground = Icons.Text; FontFamily = new FontFamily("Segoe UI");
        Icon = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/ritmo.ico"), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.MaxBy(frame => frame.PixelWidth);
        artwork = new(store.Folder);
        audio = new(store, catalog); _ = Task.Run(audio.TrimCache); playback = new(audio, store);
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = root;
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(205) }); shell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); root.Children.Add(shell);
        sidebar.Background = Brush("#0B1110"); sidebar.Margin = new Thickness(0, 0, 12, 0); shell.Children.Add(sidebar);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 28, 12, 30) };
        brand.Children.Add(new Image { Source = Icon, Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) }); brand.Children.Add(Label("RITMO", 22, true)); sidebar.Children.Add(brand);
        foreach (var (key, title, icon) in new[] { ("home", "Inicio", "home"), ("search", "Buscar", "search"), ("favorites", "Me gusta", "heart"), ("playlists", "Playlists", "folder"), ("downloads", "Descargas", "download") })
        {
            var button = Button(title, icon, () => _ = Route(key)); button.Margin = new Thickness(12, 3, 12, 3); button.HorizontalContentAlignment = HorizontalAlignment.Left;
            sidebar.Children.Add(button); navigation[key] = button;
        }
        sidebar.Children.Add(new TextBlock { Text = "Tu música, siempre a mano.", Foreground = Icons.Dim, Margin = new Thickness(24, 32, 18, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(main, 1); shell.Children.Add(main); main.Margin = new Thickness(14, 20, 24, 0);
        main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) }; top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        pageTitle.VerticalAlignment = VerticalAlignment.Center; pageTitle.Margin = new Thickness(0, 0, 28, 0); top.Children.Add(pageTitle);
        search.ToolTip = "Buscar canciones, artistas y playlists · Enter"; search.Tag = "Buscar música, artistas o playlists"; search.MaxWidth = 600; search.HorizontalAlignment = HorizontalAlignment.Stretch;
        search.KeyDown += async (_, e) => { if (e.Key == Key.Enter && search.Text.Trim().Length > 0) { searchQuery = search.Text.Trim(); await Route("search"); } };
        Grid.SetColumn(search, 1); top.Children.Add(search); main.Children.Add(top);
        notice.Foreground = Icons.Dim; notice.TextWrapping = TextWrapping.Wrap; notice.Margin = new Thickness(0, 2, 0, 10); Grid.SetRow(notice, 1); main.Children.Add(notice);
        scroller = new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 20) };
        scroller.ScrollChanged += async (_, e) => { if (e.VerticalChange > 0 && scroller.ScrollableHeight - scroller.VerticalOffset < 300) await More(); };
        Grid.SetRow(scroller, 2); main.Children.Add(scroller);
        overlay.Visibility = Visibility.Collapsed; overlay.Background = Brush("#0B1110"); root.Children.Add(overlay);
        var footer = new Grid { Background = Brush("#0B1110"), Margin = new Thickness(0), MinHeight = 108 };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(230) }); footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var open = new Button { Content = footerInfo, Background = Brushes.Transparent, Margin = new Thickness(12), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        open.Click += (_, _) => OpenPlayer(); footer.Children.Add(open);
        var center = new StackPanel { Margin = new Thickness(8, 8, 8, 10), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(center, 1); footer.Children.Add(center);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        shuffleButton = IconButton("shuffle", "Aleatorio", () => playback.ToggleShuffle());
        playButton = IconButton("play", "Reproducir / Pausar", () => _ = playback.Toggle()); playButton.Background = Icons.Accent;
        repeatButton = IconButton("repeat", "Repetir", () => playback.CycleRepeat());
        controls.Children.Add(shuffleButton); controls.Children.Add(IconButton("previous", "Anterior", () => _ = playback.Previous())); controls.Children.Add(playButton);
        controls.Children.Add(IconButton("next", "Siguiente", () => _ = playback.Next())); controls.Children.Add(repeatButton); center.Children.Add(controls);
        var timeline = new Grid(); timeline.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); timeline.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); timeline.ColumnDefinitions.Add(new() { Width = new GridLength(42) });
        currentTime.VerticalAlignment = totalTime.VerticalAlignment = VerticalAlignment.Center; totalTime.TextAlignment = TextAlignment.Right;
        timeline.Children.Add(currentTime); Grid.SetColumn(seek, 1); timeline.Children.Add(seek); Grid.SetColumn(totalTime, 2); timeline.Children.Add(totalTime); center.Children.Add(timeline);
        seek.PreviewMouseLeftButtonDown += (_, _) => dragging = true;
        seek.PreviewMouseLeftButtonUp += (_, _) => { playback.Seek(seek.Value); dragging = false; };
        seek.LostMouseCapture += (_, _) => { if (dragging) playback.Seek(seek.Value); dragging = false; };
        seek.PreviewKeyUp += (_, _) => playback.Seek(seek.Value);
        var utilities = new WrapPanel { Width = 150, Margin = new Thickness(10, 8, 8, 8), VerticalAlignment = VerticalAlignment.Center };
        favoriteButton = IconButton("heart", "Me gusta", () => { if (playback.Current is { } t) { store.Favorite(t); UpdatePlayback(); } }); utilities.Children.Add(favoriteButton);
        utilities.Children.Add(IconButton("lyrics", "Mostrar / ocultar letras", () => { lyricsOpen = !lyricsOpen; OpenPlayer(); }));
        utilities.Children.Add(IconButton("queue", "Mostrar / ocultar cola", () => { queueOpen = !queueOpen; OpenPlayer(); }));
        utilities.Children.Add(IconButton("plus", "Añadir a playlist", () => { if (playback.Current is { } t) PickPlaylist(t); }));
        utilities.Children.Add(IconButton("download", "Descargar canción", () => { if (playback.Current is { } t) _ = Download(t); }));
        Grid.SetColumn(utilities, 2); footer.Children.Add(utilities); Grid.SetRow(footer, 1); root.Children.Add(footer);
        TaskbarItemInfo = new TaskbarItemInfo();
        thumbFavorite.Description = "Me gusta"; thumbFavorite.ImageSource = Icons.Image("heart"); thumbFavorite.Click += (_, _) => { if (playback.Current is { } t) { store.Favorite(t); UpdatePlayback(); } };
        thumbPrevious.Description = "Anterior"; thumbPrevious.ImageSource = Icons.Image("previous"); thumbPrevious.Click += async (_, _) => await playback.Previous();
        thumbPlay.Description = "Reproducir"; thumbPlay.ImageSource = Icons.Image("play"); thumbPlay.Click += async (_, _) => await playback.Toggle();
        thumbNext.Description = "Siguiente"; thumbNext.ImageSource = Icons.Image("next"); thumbNext.Click += async (_, _) => await playback.Next();
        foreach (var button in new[] { thumbFavorite, thumbPrevious, thumbPlay, thumbNext }) TaskbarItemInfo.ThumbButtonInfos.Add(button);
        playback.Changed += UpdatePlayback; playback.Error += Notify; playback.OpenRequested += OpenPlayer;
        audio.Progress += (id, p) => Dispatcher.BeginInvoke(() => {
            if (playback.Loading && playback.Current?.Id == id) Notify($"Preparando audio · {p:P0}");
            if (downloadButtons.TryGetValue(id, out var buttons)) foreach (var button in buttons) button.ToolTip = $"Descargando · {p:P0}";
            if (downloading.Count > 0) { TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal; TaskbarItemInfo.ProgressValue = p; }
        });
        timer.Tick += (_, _) => Tick(); timer.Start();
        PreviewKeyDown += async (_, e) => {
            if (e.Key == Key.Escape && expanded) { expanded = false; overlay.Visibility = Visibility.Collapsed; e.Handled = true; }
            if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox) { await playback.Toggle(); e.Handled = true; }
            if (e.Key == Key.MediaNextTrack) { await playback.Next(); e.Handled = true; }
            if (e.Key == Key.MediaPreviousTrack) { await playback.Previous(); e.Handled = true; }
            if (e.Key == Key.MediaPlayPause) { await playback.Toggle(); e.Handled = true; }
        };
        SizeChanged += (_, _) => { shell.ColumnDefinitions[0].Width = new GridLength(ActualWidth < 950 ? 170 : 205); ArrangePlayer(); };
        Closed += (_, _) => { timer.Stop(); pageRequest?.Cancel(); lifetime.Cancel(); playback.Dispose(); };
        Loaded += async (_, _) => { UpdatePlayback(); if (!Environment.GetCommandLineArgs().Contains("--ui-test")) await Route("home"); else await PreviewTest(); };
    }
    static readonly Dictionary<string, Brush> palette = [];
    static Brush Brush(string hex)
    {
        if (palette.TryGetValue(hex, out var cached)) return cached;
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze();
        palette[hex] = brush; return brush;
    }
    static TextBlock Label(string text, double size = 14, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
    static Button Button(string title, string? icon, Action action)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) { var glyph = Icons.View(icon); glyph.Margin = new Thickness(0, 0, 10, 0); panel.Children.Add(glyph); }
        panel.Children.Add(Label(title)); var button = new Button { Content = panel }; button.Click += (_, _) => action(); return button;
    }
    static Button IconButton(string icon, string title, Action action)
    {
        var button = new Button { Content = Icons.View(icon), ToolTip = title, Width = 40, Height = 40, Padding = new Thickness(9), Margin = new Thickness(2), Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(button, title); button.Click += (_, _) => action(); return button;
    }
    void Notify(string text) { notice.Text = text; }
    static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    void Tick()
    {
        seek.IsEnabled = playback.Current != null && !playback.Loading;
        seek.Maximum = Math.Max(1, playback.Duration);
        if (!dragging) seek.Value = playback.Player.Position.TotalSeconds;
        currentTime.Text = Time(seek.Value); totalTime.Text = Time(playback.Duration);
        if (expanded && lyricsOpen && lyricLines.Count > 0)
        {
            var active = lyricLines.FindLastIndex(l => l.Time <= playback.Player.Position.TotalSeconds);
            if (active != lyricActive)
            {
                if (lyricActive >= 0 && lyricActive < lyricBlocks.Count) lyricBlocks[lyricActive].Foreground = Icons.Dim;
                lyricActive = active;
                if (active >= 0 && active < lyricBlocks.Count) { lyricBlocks[active].Foreground = Icons.Text; lyricBlocks[active].BringIntoView(); }
            }
        }
    }
    void UpdatePlayback()
    {
        var track = playback.Current;
        playButton.Content = Icons.View(playback.Playing ? "pause" : "play", Brush("#04140B"));
        shuffleButton.Content = Icons.View("shuffle", playback.Shuffle ? Icons.Accent : Icons.Dim);
        repeatButton.Content = playback.Repeat == 1 ? Label("1 ↻", 19) : Icons.View("repeat", playback.Repeat == 2 ? Icons.Accent : Icons.Dim);
        var liked = track != null && store.Data.Favorites.Any(t => t.Id == track.Id);
        favoriteButton.Content = Icons.View("heart", liked ? Icons.Accent : Icons.Dim); thumbFavorite.ImageSource = Icons.Image(liked ? "check" : "heart");
        thumbPlay.ImageSource = Icons.Image(playback.Playing ? "pause" : "play"); thumbPlay.Description = playback.Playing ? "Pausar" : "Reproducir";
        foreach (var button in new[] { thumbFavorite, thumbPrevious, thumbPlay, thumbNext }) button.IsEnabled = track != null;
        thumbNext.IsEnabled = track != null && (playback.Index + 1 < playback.Queue.Count || playback.Repeat == 2);
        TaskbarItemInfo.Description = track == null ? "Ritmo · Tu música" : track.Title + " · " + track.Artist;
        if (track?.Id != renderedTrack?.Id || footerInfo.Children.Count == 0)
        {
            renderedTrack = track; footerInfo.Children.Clear();
            var info = new DockPanel(); if (track != null) { var image = Artwork(track, 54); image.Margin = new Thickness(0, 0, 12, 0); info.Children.Add(image); }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(Label(track?.Title ?? "Elige una canción", 15, true));
            var artist = Label(track?.Artist ?? "Tu música, siempre a mano", 12); artist.Foreground = Icons.Dim; text.Children.Add(artist); info.Children.Add(text); footerInfo.Children.Add(info);
            Title = track == null ? "Ritmo" : track.Title + " · Ritmo";
            if (expanded) BuildPlayer();
        }
        if (expanded && queueOpen) RenderQueue();
    }
    FrameworkElement Artwork(Track track, double size)
    {
        var frame = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(12), Background = Brush("#1A2422"), ClipToBounds = true };
        var image = new Image { Stretch = Stretch.UniformToFill }; frame.Child = image;
        var pixels = (int)Math.Ceiling(size * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        image.Loaded += async (_, _) => {
            if (image.Source != null) return;
            var bitmap = await artwork.Get(track, pixels, lifetime.Token);
            if (!lifetime.IsCancellationRequested && image.IsLoaded) image.Source = bitmap;
        };
        return frame;
    }
}
