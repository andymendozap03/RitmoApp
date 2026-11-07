using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ritmo;

// Bounded local diagnostics for delays that only occur on a user's display/driver.
// No song titles, library contents or network requests are recorded.
internal sealed class UiDiagnostics(Window window, string folder)
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Channel<string> messages = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    long lastTick;
    public void Start()
    {
        Write($"start version={Assembly.GetExecutingAssembly().GetName().Version} renderTier={RenderCapability.Tier >> 16}");
        window.Closed += (_, _) => messages.Writer.TryComplete();
        _ = Task.Run(async () => {
            while (await messages.Reader.WaitToReadAsync())
            {
                var lines = new List<string>();
                while (messages.Reader.TryRead(out var line)) lines.Add(line);
                try
                {
                    var path = Path.Combine(folder, "performance.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path, "");
                    await File.AppendAllLinesAsync(path, lines);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        });
    }
    public void Tick(string state)
    {
        var now = clock.ElapsedMilliseconds;
        if (lastTick != 0 && now - lastTick > 750 && window.IsVisible && window.WindowState != WindowState.Minimized)
            Write($"dispatcher-gap={now - lastTick}ms state={state}");
        lastTick = now;
    }
    public void Transition(string action)
    {
        var started = clock.ElapsedMilliseconds;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            Write($"{action} dispatcher-ready={clock.ElapsedMilliseconds - started}ms")));
    }
    public void PlaybackState(string message) => Write("audio " + message);
    void Write(string line)
    {
        messages.Writer.TryWrite($"{DateTimeOffset.Now:O} {line}");
    }
}
