using System.IO;
using System.Windows.Media;

namespace Ritmo;
public static class NetworkTest
{
    public static async Task Run()
    {
        var log = Path.Combine(AppContext.BaseDirectory, "network-test.txt"); File.WriteAllText(log, "Inicio de prueba de integración\n");
        void Report(string text) => File.AppendAllText(log, text + "\n");
        var folder = Path.Combine(AppContext.BaseDirectory, "network-test-data");
        var store = new Store(folder); var catalog = new Catalog(); var audio = new AudioLibrary(store, catalog);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var player = new MediaPlayer { Volume = 0 };
        try
        {
            foreach (var query in new[] { "Don Omar Virtual Diva", "Feid Luna", "Karol G Provenza" })
            {
            player.Close();
            var page = await catalog.Search(query, ct: cancel.Token); Report($"Búsqueda ({query}): {page.Tracks.Count} canciones");
            var track = page.Tracks.First(); Report("Preparando " + track.Title + " · " + track.Artist);
            var file = await audio.Prepare(track, cancel.Token); Report("Audio completo: " + new FileInfo(file).Length + " bytes");
            var opened = new TaskCompletionSource(); player.MediaOpened += (_, _) => opened.TrySetResult(); player.MediaFailed += (_, e) => opened.TrySetException(e.ErrorException);
            player.Open(new Uri(file)); await opened.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Report("Decodificación: " + player.NaturalDuration.TimeSpan);
            var target = Math.Min(90, player.NaturalDuration.TimeSpan.TotalSeconds / 2); player.Position = TimeSpan.FromSeconds(target); player.Play();
            await Task.Delay(2000, cancel.Token); player.Pause();
            if (player.Position.TotalSeconds < target + .5) throw new Exception("El avance de reproducción no se confirmó.");
            Report("Avance confirmado: " + player.Position.TotalSeconds.ToString("F1") + " segundos");
            await audio.Download(track, cancel.Token); if (!store.Downloaded(track)) throw new Exception("Descarga no persistida.");
            var reloaded = new Store(folder); if (!reloaded.Downloaded(track)) throw new Exception("Descarga perdida al reabrir biblioteca.");
            Report("Descarga persistida; preparar de nuevo usa archivo local: " + (await audio.Prepare(track, cancel.Token) == store.Audio(track)));
            }
            Report("SUCCESS");
        }
        catch (Exception ex) { Report("FAIL " + ex); Environment.ExitCode = 1; }
        finally { player.Close(); }
    }
}
