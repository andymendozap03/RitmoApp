using System.Windows.Media;

namespace Ritmo;
public sealed class Playback : IDisposable
{
    public MediaPlayer Player { get; } = new();
    public List<Track> Queue { get; private set; } = [];
    List<string> original = [];
    public int Index { get; private set; } = -1;
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public bool Playing { get; private set; }
    public bool Loading { get; private set; }
    public bool Shuffle { get; private set; }
    public int Repeat { get; private set; }
    public int QueueRevision { get; private set; }
    public double Duration => Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan.TotalSeconds : (Current?.Duration ?? 0) / 1000.0;
    readonly AudioLibrary audio; readonly Store store;
    CancellationTokenSource? request;
    bool wantPlay;
    bool failed;
    public event Action? Changed;
    public event Action? OpenRequested;
    public event Action<string>? Error;
    public Playback(AudioLibrary audio, Store store)
    {
        this.audio = audio; this.store = store;
        Player.MediaOpened += (_, _) => { Loading = false; if (wantPlay) { Player.Play(); Playing = true; } Changed?.Invoke(); };
        Player.MediaFailed += (_, e) => { failed = true; Loading = false; Playing = wantPlay = false; Error?.Invoke("No se pudo reproducir: " + e.ErrorException.Message); Changed?.Invoke(); };
        Player.MediaEnded += async (_, _) => {
            if (Repeat == 1) { Player.Position = TimeSpan.Zero; Player.Play(); return; }
            if (Index + 1 < Queue.Count || Repeat == 2) await Next(); else { Playing = wantPlay = false; Changed?.Invoke(); }
        };
    }
    public async Task Play(IEnumerable<Track> tracks, Track selected)
    {
        if (Current?.Id == selected.Id && !failed && (Loading || Player.Source != null)) { OpenRequested?.Invoke(); return; }
        Queue = tracks.DistinctBy(t => t.Id).ToList(); original = Queue.Select(t => t.Id).ToList(); Index = Queue.FindIndex(t => t.Id == selected.Id);
        QueueRevision++;
        if (Index < 0) return;
        if (Shuffle) Music.ShuffleFuture(Queue, Index);
        await Load();
    }
    public async Task Jump(int index) { if (index < 0 || index >= Queue.Count) return; Index = index; await Load(); }
    async Task Load()
    {
        request?.Cancel(); request?.Dispose(); request = new(); var token = request.Token;
        Player.Close(); failed = false; Playing = false; Loading = wantPlay = true; Changed?.Invoke();
        var track = Current; if (track == null) return;
        try
        {
            var file = await Task.Run(() => audio.Prepare(track, token), token); token.ThrowIfCancellationRequested();
            Player.Open(new Uri(file));
            store.Data.Recent.RemoveAll(t => t.Id == track.Id); store.Data.Recent.Insert(0, track);
            store.Data.Recent = store.Data.Recent.Take(80).ToList(); store.Save();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) { failed = true; Loading = Playing = wantPlay = false; Error?.Invoke(ex.Message); Changed?.Invoke(); } }
    }
    public async Task Toggle()
    {
        if (Current == null) return;
        if (Loading) { wantPlay = !wantPlay; Changed?.Invoke(); return; }
        if (Player.Source == null) { await Load(); return; }
        Playing = !Playing; wantPlay = Playing;
        if (Playing) Player.Play(); else Player.Pause(); Changed?.Invoke();
    }
    public Task Next() => Index + 1 < Queue.Count ? Jump(Index + 1) : Repeat == 2 && Queue.Count > 0 ? Jump(0) : Task.CompletedTask;
    public Task Previous() { if (Player.Position.TotalSeconds > 4) { Seek(0); return Task.CompletedTask; } return Jump(Math.Max(0, Index - 1)); }
    public void Seek(double seconds) { if (!Loading && Player.Source != null) Player.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, Duration - .1))); }
    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        if (Shuffle) Music.ShuffleFuture(Queue, Index);
        else Queue = Queue.Take(Index + 1).Concat(Queue.Skip(Index + 1).OrderBy(t => original.IndexOf(t.Id))).ToList();
        QueueRevision++;
        Changed?.Invoke();
    }
    public void CycleRepeat() { Repeat = (Repeat + 1) % 3; Changed?.Invoke(); }
    public void Dispose() { request?.Cancel(); Player.Close(); }
}
