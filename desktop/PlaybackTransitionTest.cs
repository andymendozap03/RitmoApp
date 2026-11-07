using System.IO;

namespace Ritmo;
public static class PlaybackTransitionTest
{
    public static async Task Run()
    {
        var report = Path.Combine(AppContext.BaseDirectory, "transition-test.txt");
        File.WriteAllText(report, "");
        void Check(bool condition, string message) {
            if (!condition) throw new Exception(message);
            File.AppendAllText(report, "PASS " + message + "\n");
        }
        async Task Until(Func<bool> condition, string message, int seconds = 15) {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition() && DateTime.UtcNow < end) await Task.Delay(50);
            Check(condition(), message);
        }
        var store = new Store(Path.Combine(AppContext.BaseDirectory, "transition-test-data"));
        using var playback = new Playback(new AudioLibrary(store, new Catalog()), store);
        playback.Player.Volume = 0;
        playback.Diagnostic += text => File.AppendAllText(report, "AUDIO " + text + "\n");
        string? error = null;
        playback.Error += message => error = message;
        try {
            var tracks = Enumerable.Range(0, 5).Select(i => new Track("transition-" + i, "Fixture " + i, "Local", Duration: 3000)).ToList();
            foreach (var track in tracks) Wave(store.Audio(track), 3);
            await playback.Play(tracks, tracks[0]);
            await Until(() => playback.Index == 0 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "primera canción inicia");
            await Until(() => playback.Index == 1 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "fin natural inicia la segunda canción");
            await Until(() => playback.Index == 2 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "segundo fin natural inicia la tercera canción");
            await playback.Toggle(); var paused = playback.Player.Position;
            await Task.Delay(500);
            Check(!playback.Playing && Math.Abs((playback.Player.Position - paused).TotalSeconds) < .1, "pausa no dispara recuperación ni avance automático");
            // Same methods bound to the taskbar thumbnail commands.
            await playback.Next();
            await Until(() => playback.Index == 3 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "siguiente inicia desde pausa");
            await playback.Previous();
            await Until(() => playback.Index == 2 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "anterior inicia la canción seleccionada");
            var next = playback.Next(); var previous = playback.Previous(); var last = playback.Next();
            await Task.WhenAll(next, previous, last);
            await Until(() => playback.Index == 3 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "pulsaciones rápidas conservan solo la última selección");
            await playback.Toggle();
            Check(error == null, "secuencia sin errores del decodificador");

            var sources = new[] { "audio", "cache" }.SelectMany(folder => {
                var path = Path.Combine(Store.Root, folder);
                return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.m4a") : [];
            }).Where(path => {
                using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[12];
                return stream.Read(header) == 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8);
            }).Take(3).ToList();
            Check(sources.Count >= 2, "hay al menos dos archivos M4A reales para probar cambios de decodificador");
            var songs = sources.Select((path, i) => new Track("compressed-" + i, "Audio real " + i, "Local")).ToList();
            for (var i = 0; i < songs.Count; i++) File.Copy(sources[i], store.Audio(songs[i]), true);
            await playback.Play(songs, songs[0]);
            await Until(() => !playback.Loading && playback.Playing && playback.Player.Position.TotalSeconds > .2, "primer M4A real inicia");
            playback.Seek(playback.Duration - .7);
            await Until(() => playback.Index == 1 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "fin real M4A inicia el siguiente M4A");
            await playback.Previous();
            await Until(() => playback.Index == 0 && playback.Playing && playback.Player.Position.TotalSeconds > .2, "volver al M4A anterior inicia reproducción");
            Check(error == null, "cambios entre archivos comprimidos sin errores");
            File.AppendAllText(report, "SUCCESS\n");
        }
        catch (Exception ex) { File.AppendAllText(report, "FAIL " + ex); Environment.ExitCode = 1; }
    }
    static void Wave(string path, int seconds)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var bytes = seconds * 8000 * 2;
        writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
    }
}
