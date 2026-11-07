using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ritmo;

// One coordinator per window, not one polling timer per cover.
internal sealed class ArtworkPresenter
{
    sealed class Entry(Image image, Track track, int pixels, bool cover)
    {
        public readonly Image Image = image;
        public readonly Track Track = track;
        public readonly int Pixels = pixels;
        public readonly bool Cover = cover;
        public CancellationTokenSource? Request;
        public bool Complete;
    }
    readonly Window window;
    readonly ArtworkLoader loader;
    readonly CancellationToken lifetime;
    readonly HashSet<Entry> entries = [];
    readonly Queue<(Entry Entry, BitmapSource Bitmap, CancellationTokenSource Request)> ready = [];
    readonly DispatcherTimer present;
    bool refreshPending;
    internal int ActiveRequests => entries.Count(entry => entry.Request != null);
    internal int PresentedCount { get; private set; }
    internal string DebugState
    {
        get
        {
            var image = entries.FirstOrDefault(e => e.Image.IsVisible)?.Image;
            string detail;
            try { detail = image == null ? "none" : $"image={image.ActualWidth}x{image.ActualHeight} bounds={image.TransformToAncestor(window).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight))} window={window.ActualWidth}x{window.ActualHeight}"; }
            catch (Exception ex) { detail = ex.Message; }
            return $"entries={entries.Count} visible={entries.Count(e => e.Image.IsVisible)} inViewport={entries.Count(e => Visible(e.Image))} loaded={entries.Count(e => e.Image.IsLoaded)} windowVisible={window.IsVisible} {detail}";
        }
    }
    public ArtworkPresenter(Window window, ArtworkLoader loader, CancellationToken lifetime)
    {
        this.window = window; this.loader = loader; this.lifetime = lifetime;
        present = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        present.Tick += (_, _) => Present();
        window.SizeChanged += (_, _) => Refresh();
        window.Closed += (_, _) => {
            present.Stop(); foreach (var entry in entries) Cancel(entry); entries.Clear();
            while (ready.TryDequeue(out var item)) item.Request.Dispose();
        };
    }
    public void Attach(Image image, Track track, int pixels, bool cover)
    {
        var entry = new Entry(image, track, pixels, cover);
        image.Loaded += (_, _) => { entries.Add(entry); Refresh(); };
        image.Unloaded += (_, _) => { entries.Remove(entry); Cancel(entry); };
        image.IsVisibleChanged += (_, _) => {
            if (!image.IsVisible) Cancel(entry); else Refresh();
        };
    }
    public void Refresh()
    {
        if (refreshPending || lifetime.IsCancellationRequested) return;
        refreshPending = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            refreshPending = false;
            foreach (var entry in entries)
            {
                if (!Visible(entry.Image)) { Cancel(entry); continue; }
                if (entry.Complete || entry.Request != null) continue;
                entry.Image.Source ??= loader.Preview(entry.Track, entry.Pixels);
                entry.Request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                _ = Load(entry, entry.Request);
            }
        }));
    }
    async Task Load(Entry entry, CancellationTokenSource request)
    {
        bool queued = false;
        try
        {
            var bitmap = await loader.Get(entry.Track, entry.Pixels, request.Token, entry.Cover);
            if (bitmap == null || request.IsCancellationRequested || entry.Request != request || !Visible(entry.Image)) return;
            ready.Enqueue((entry, bitmap, request)); queued = true; present.Start();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!queued) { if (entry.Request == request) entry.Request = null; request.Dispose(); }
        }
    }
    void Present()
    {
        int applied = 0;
        while (applied < 2 && ready.TryDequeue(out var item))
        {
            var (entry, bitmap, request) = item;
            try
            {
                if (request.IsCancellationRequested || entry.Request != request || !Visible(entry.Image)) continue;
                entry.Image.Source = bitmap; entry.Complete = true; applied++; PresentedCount++;
            }
            finally { if (entry.Request == request) entry.Request = null; request.Dispose(); }
        }
        if (ready.Count == 0) present.Stop();
    }
    static void Cancel(Entry entry)
    {
        var request = entry.Request; entry.Request = null; request?.Cancel();
    }
    bool Visible(Image image)
    {
        // An Image with no Source can have a zero render size. Its fixed-size cover frame
        // defines visibility before the first bitmap is assigned.
        var anchor = image.Parent as FrameworkElement ?? image;
        if (!image.IsLoaded || !image.IsVisible || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0) return false;
        try
        {
            var bounds = new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight);
            if (!anchor.TransformToAncestor(window).TransformBounds(bounds).IntersectsWith(new Rect(0, 0, window.ActualWidth, window.ActualHeight))) return false;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(image); parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll && !anchor.TransformToAncestor(scroll).TransformBounds(bounds)
                    .IntersectsWith(new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight))) return false;
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }
}
