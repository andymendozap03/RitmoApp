using System.Windows.Media;
using System.Windows.Threading;

namespace Ritmo;
public sealed class Playback : IDisposable
{
    public MediaPlayer Player { get; private set; } = new();
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
    bool ended, disposed;
    int generation, startAttempt;
    public event Action? Changed;
    public event Action? OpenRequested;
    public event Action<string>? Error;
    public event Action<string>? Diagnostic;
    public Playback(AudioLibrary audio, Store store)
    {
        this.audio = audio; this.store = store;
    }
    bool Active(MediaPlayer player, int version) => !disposed && version == generation && ReferenceEquals(Player, player);
    void OpenFile(Uri file, int version, double position = 0, bool recovery = false)
    {
        var previous = Player;
        var player = new MediaPlayer { Volume = previous.Volume, IsMuted = previous.IsMuted, Balance = previous.Balance, SpeedRatio = previous.SpeedRatio };
        Player = player; previous.Close();
        player.MediaOpened += (_, _) => {
            if (!Active(player, version)) return;
            Loading = false; ended = false;
            if (position > 0) player.Position = TimeSpan.FromSeconds(position);
            Playing = wantPlay;
            if (wantPlay) { player.Play(); _ = ConfirmStarted(player, version, ++startAttempt, recovery); }
            else player.Pause();
            Diagnostic?.Invoke($"opened generation={version} index={Index} play={wantPlay} recovery={recovery}");
            Changed?.Invoke();
        };
        player.MediaFailed += (_, e) => {
            if (!Active(player, version)) return;
            Fail("No se pudo reproducir: " + e.ErrorException.Message);
        };
        player.MediaEnded += (_, _) => {
            if (!Active(player, version) || ended) return;
            ended = true; Playing = false;
            Diagnostic?.Invoke($"ended generation={version} index={Index}");
            // Leave the old decoder's callback before opening the next file.
            // A manual Next/Previous in the meantime invalidates this callback.
            player.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () => {
                if (!Active(player, version)) return;
                if (Repeat == 1) await Load();
                else if (Index + 1 < Queue.Count || Repeat == 2) await Next();
                else { wantPlay = false; Changed?.Invoke(); }
            }));
        };
        player.Open(file);
        // Set the desired media state immediately, including while opening.
        if (wantPlay) player.Play();
    }
    async Task ConfirmStarted(MediaPlayer player, int version, int attempt, bool recovery)
    {
        var initial = player.Position.TotalSeconds;
        var token = request?.Token ?? CancellationToken.None;
        try {
            for (var i = 0; i < 12; i++) {
                await Task.Delay(250, token);
                if (!Active(player, version) || attempt != startAttempt || !wantPlay || ended) return;
                if (player.Position.TotalSeconds > initial + .1) { Diagnostic?.Invoke($"started generation={version} index={Index}"); return; }
            }
            if (!recovery && player.Source is { } file) {
                Diagnostic?.Invoke($"restart-stalled generation={version} index={Index}");
                Loading = true; Playing = false;
                OpenFile(file, version, player.Position.TotalSeconds, true); Changed?.Invoke();
            } else Fail("El audio no pudo iniciar. Pulsa reproducir para reintentar.");
        }
        catch (OperationCanceledException) { }
    }
    void Fail(string message)
    {
        failed = true; Loading = Playing = wantPlay = false;
        Player.Pause(); Diagnostic?.Invoke($"failed generation={generation} index={Index}");
        Error?.Invoke(message); Changed?.Invoke();
    }
    public async Task Play(IEnumerable<Track> tracks, Track selected)
    {
        if (Current?.Id == selected.Id && !failed && !ended && (Loading || Player.Source != null)) { OpenRequested?.Invoke(); return; }
        Queue = tracks.DistinctBy(t => t.Id).ToList(); original = Queue.Select(t => t.Id).ToList(); Index = Queue.FindIndex(t => t.Id == selected.Id);
        QueueRevision++;
        if (Index < 0) return;
        if (Shuffle) Music.ShuffleFuture(Queue, Index);
        await Load();
    }
    public async Task Jump(int index) { if (index < 0 || index >= Queue.Count) return; Index = index; await Load(); }
    async Task Load()
    {
        if (disposed) return;
        var version = ++generation;
        request?.Cancel(); request?.Dispose(); request = new(); var token = request.Token;
        Player.Close(); failed = ended = false; Playing = false; Loading = wantPlay = true; Changed?.Invoke();
        var track = Current; if (track == null) return;
        Diagnostic?.Invoke($"prepare generation={version} index={Index}");
        try
        {
            var file = await Task.Run(() => audio.Prepare(track, token), token); token.ThrowIfCancellationRequested();
            if (disposed || version != generation) return;
            OpenFile(new Uri(file), version);
            store.Data.Recent.RemoveAll(t => t.Id == track.Id); store.Data.Recent.Insert(0, track);
            store.Data.Recent = store.Data.Recent.Take(80).ToList(); store.Save();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) Fail(ex.Message); }
    }
    public async Task Toggle()
    {
        if (Current == null) return;
        if (Loading) { wantPlay = !wantPlay; Changed?.Invoke(); return; }
        if (Player.Source == null || failed || ended) { await Load(); return; }
        Playing = !Playing; wantPlay = Playing;
        if (Playing) { Player.Play(); _ = ConfirmStarted(Player, generation, ++startAttempt, false); }
        else { startAttempt++; Player.Pause(); }
        Changed?.Invoke();
    }
    public Task Next() => Index + 1 < Queue.Count ? Jump(Index + 1) : Repeat == 2 && Queue.Count > 0 ? Jump(0) : Task.CompletedTask;
    public Task Previous() { if (Player.Position.TotalSeconds > 4) { Seek(0); return Task.CompletedTask; } return Jump(Math.Max(0, Index - 1)); }
    public void Seek(double seconds) { if (!Loading && Player.Source != null) { startAttempt++; Player.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, Duration - .1))); } }
    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        if (Shuffle) Music.ShuffleFuture(Queue, Index);
        else Queue = Queue.Take(Index + 1).Concat(Queue.Skip(Index + 1).OrderBy(t => original.IndexOf(t.Id))).ToList();
        QueueRevision++;
        Changed?.Invoke();
    }
    public void CycleRepeat() { Repeat = (Repeat + 1) % 3; Changed?.Invoke(); }
    public void Dispose() { disposed = true; generation++; request?.Cancel(); request?.Dispose(); Player.Close(); }
}
