package com.ritmo.auto.data

import org.json.JSONObject
import java.io.IOException

object PlaylistImporter {

    sealed class ImportResult {
        data class Success(val playlist: Playlist) : ImportResult()
        data class Error(val message: String) : ImportResult()
    }

    fun importFromUrl(url: String, progressCallback: ((current: Int, total: Int) -> Unit)? = null): ImportResult {
        val trimmed = url.trim()
        return when {
            trimmed.contains("spotify.com/playlist/") -> importSpotify(trimmed, progressCallback)
            trimmed.contains("youtube.com/playlist") || trimmed.contains("music.youtube.com/playlist") ||
                    trimmed.startsWith("PL") || trimmed.startsWith("VLPL") -> importYoutube(trimmed)
            else -> ImportResult.Error("Enlace de playlist no reconocido.")
        }
    }

    private fun importSpotify(url: String, progressCallback: ((Int, Int) -> Unit)?): ImportResult {
        val playlistId = Regex("playlist/([A-Za-z0-9]+)").find(url)?.groupValues?.getOrNull(1)
            ?: return ImportResult.Error("ID de playlist de Spotify no válido")

        val html = try {
            Net.get("https://open.spotify.com/embed/playlist/$playlistId", 12000)
        } catch (t: Throwable) {
            return ImportResult.Error("No se pudo conectar con Spotify: ${t.message}")
        }

        val idx = html.indexOf("__NEXT_DATA__")
        if (idx == -1) return ImportResult.Error("No se pudo leer la información de la playlist de Spotify")

        val start = html.indexOf('>', idx) + 1
        val end = html.indexOf("</script>", start)
        if (start <= 0 || end <= start) return ImportResult.Error("Formato de Spotify incompatible")

        val jsonStr = html.substring(start, end)
        val json = try {
            JSONObject(jsonStr)
        } catch (t: Throwable) {
            return ImportResult.Error("Error al procesar datos de Spotify")
        }

        val entity = json.optJSONObject("props")
            ?.optJSONObject("pageProps")
            ?.optJSONObject("state")
            ?.optJSONObject("data")
            ?.optJSONObject("entity") ?: return ImportResult.Error("Playlist vacía o privada")

        val playlistTitle = entity.optString("title").ifBlank { "Playlist de Spotify" }
        val trackList = entity.optJSONArray("trackList") ?: return ImportResult.Error("No se encontraron canciones en la playlist")

        val tracks = mutableListOf<Track>()
        val total = trackList.length()

        for (i in 0 until total) {
            progressCallback?.invoke(i + 1, total)
            val item = trackList.optJSONObject(i) ?: continue
            val title = item.optString("title")
            val artist = item.optString("subtitle")
            if (title.isBlank()) continue

            // Match each imported song against the music catalog.
            val query = "$artist - $title"
            val results = try {
                Innertube.search(query, 1)
            } catch (_: Throwable) {
                emptyList()
            }

            if (results.isNotEmpty()) {
                val found = results[0]
                tracks.add(found.copy(title = title, artist = artist))
            }
        }

        if (tracks.isEmpty()) {
            return ImportResult.Error("No se encontraron canciones disponibles para esta playlist")
        }

        val playlist = Playlist(
            id = "spotify_$playlistId",
            title = playlistTitle,
            source = "spotify",
            tracks = tracks
        )
        return ImportResult.Success(playlist)
    }

    private fun importYoutube(url: String): ImportResult {
        val playlistId = Innertube.extractPlaylistId(url)
            ?: return ImportResult.Error("Enlace de playlist no válido")

        return try {
            val (title, tracks) = Innertube.playlist(playlistId)
            if (tracks.isEmpty()) {
                ImportResult.Error("No se encontraron canciones en esa playlist")
            } else {
                val playlist = Playlist(
                    id = "online_$playlistId",
                    title = title,
                    source = "online",
                    tracks = tracks.toMutableList()
                )
                ImportResult.Success(playlist)
            }
        } catch (t: Throwable) {
            ImportResult.Error("No se pudo cargar la playlist: ${t.message}")
        }
    }
}
