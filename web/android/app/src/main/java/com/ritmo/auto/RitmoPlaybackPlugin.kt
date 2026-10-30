package com.ritmo.auto

import android.app.Activity
import android.content.Intent
import androidx.activity.result.ActivityResult
import com.getcapacitor.annotation.ActivityCallback
import com.ritmo.auto.data.LyricsStore

import com.getcapacitor.JSArray
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import com.ritmo.auto.data.Downloader
import com.ritmo.auto.data.Library
import com.ritmo.auto.data.Net
import com.ritmo.auto.data.Playlist
import com.ritmo.auto.data.Track
import com.ritmo.auto.play.PlayerHub
import org.json.JSONArray
import org.json.JSONObject

@CapacitorPlugin(name = "RitmoPlayback")
class RitmoPlaybackPlugin : Plugin() {
    private val playbackListener: () -> Unit = {
        com.ritmo.auto.core.main.post { notifyListeners("playbackState", snapshot()) }
    }
    private val downloadsListener: () -> Unit = {
        notifyListeners("downloadsState", downloadsSnapshot())
    }

    override fun load() {
        Library.init(context)
        PlayerHub.init(context)
        PlayerHub.addListener(playbackListener)
        Downloader.addListener(downloadsListener)
    }

    @PluginMethod
    fun play(call: PluginCall) {
        val items = call.getArray("tracks") ?: JSArray()
        val tracks = ArrayList<Track>(items.length())
        for (index in 0 until items.length()) {
            val item = items.optJSONObject(index) ?: continue
            tracks.add(readTrack(item))
        }
        if (tracks.isEmpty()) {
            call.reject("La cola no contiene canciones")
            return
        }
        val index = (call.getInt("index", 0) ?: 0).coerceIn(0, tracks.lastIndex)
        val shuffle = call.getBoolean("shuffle", false) ?: false
        onMain {
            PlayerHub.play(tracks, index, shuffle)
            call.resolve()
        }
    }

    @PluginMethod
    fun getState(call: PluginCall) {
        onMain { call.resolve(snapshot()) }
    }

    @PluginMethod
    fun getDownloads(call: PluginCall) {
        onMain { call.resolve(downloadsSnapshot()) }
    }

    @PluginMethod
    fun exitApp(call: PluginCall) {
        onMain {
            activity.finish()
            call.resolve()
        }
    }

    @PluginMethod
    fun innerTube(call: PluginCall) {
        val endpoint = call.getString("endpoint")?.takeIf { it.matches(Regex("[A-Za-z]+")) }
        val body = call.getObject("body")
        if (endpoint == null || body == null) {
            call.reject("Solicitud de música no válida")
            return
        }
        com.ritmo.auto.core.io.execute {
            try {
                val response = Net.postJson(
                    "https://music.youtube.com/youtubei/v1/$endpoint?prettyPrint=false",
                    body.toString(),
                    mapOf("Origin" to "https://music.youtube.com", "Referer" to "https://music.youtube.com/"),
                    18000
                )
                call.resolve(JSObject(response))
            } catch (error: Throwable) {
                call.reject("No se pudo consultar el catálogo")
            }
        }
    }

    @PluginMethod
    fun toggle(call: PluginCall) {
        onMain {
            PlayerHub.toggle()
            call.resolve()
        }
    }

    @PluginMethod
    fun next(call: PluginCall) {
        onMain {
            PlayerHub.next()
            call.resolve()
        }
    }

    @PluginMethod
    fun previous(call: PluginCall) {
        onMain {
            PlayerHub.previous()
            call.resolve()
        }
    }

    @PluginMethod
    fun seek(call: PluginCall) {
        // The JS bridge may decode a number as Long or Double. Avoid turning a
        // valid slider value into zero when the bridge representation differs.
        val position = call.getLong("position")
            ?: call.getDouble("position")?.toLong()
            ?: call.getInt("position")?.toLong()
            ?: 0L
        onMain {
            PlayerHub.seekTo(position)
            call.resolve()
        }
    }

    @PluginMethod
    fun setShuffle(call: PluginCall) {
        val enabled = call.getBoolean("enabled", false) ?: false
        onMain {
            PlayerHub.setShuffle(enabled)
            call.resolve(snapshot())
        }
    }

    @PluginMethod
    fun playAtIndex(call: PluginCall) {
        val index = call.getInt("index") ?: -1
        onMain { PlayerHub.seekToQueueIndex(index); call.resolve() }
    }

    @PluginMethod
    fun getLyrics(call: PluginCall) {
        val track = call.getObject("track")?.let { readTrack(it) }
        if (track == null) { call.reject("Falta la canción"); return }
        com.ritmo.auto.core.io.execute {
            val lyrics = runCatching { LyricsStore.getOrFetch(context, track) }.getOrNull()
            call.resolve(JSObject(lyrics?.toString() ?: "{}"))
        }
    }

    @PluginMethod
    fun saveLyrics(call: PluginCall) {
        val id = call.getString("id")
        val lyrics = call.getObject("lyrics")
        if (id.isNullOrBlank() || lyrics == null) { call.reject("Falta la letra"); return }
        com.ritmo.auto.core.io.execute {
            try { LyricsStore.save(context, id, lyrics); call.resolve() }
            catch (_: Exception) { call.reject("No se pudo guardar la letra") }
        }
    }

    @PluginMethod
    fun exportCsv(call: PluginCall) {
        val content = call.getString("content")
        if (content == null) { call.reject("Falta el contenido del respaldo"); return }
        val intent = Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "text/csv"
            putExtra(Intent.EXTRA_TITLE, call.getString("filename") ?: "Ritmo.csv")
        }
        onMain {
            try { startActivityForResult(call, intent, "csvCreated") }
            catch (_: Exception) { call.reject("No se pudo abrir el selector de archivos") }
        }
    }

    @ActivityCallback
    private fun csvCreated(call: PluginCall?, result: ActivityResult) {
        if (call == null) return
        val uri = result.data?.data
        if (result.resultCode != Activity.RESULT_OK || uri == null) {
            call.resolve(JSObject().put("cancelled", true)); return
        }
        com.ritmo.auto.core.io.execute {
            try {
                val stream = context.contentResolver.openOutputStream(uri, "wt")
                    ?: throw java.io.IOException("No se pudo abrir el archivo")
                stream.use { it.write((call.getString("content") ?: "").toByteArray(Charsets.UTF_8)) }
                call.resolve(JSObject().put("saved", true))
            } catch (_: Exception) { call.reject("No se pudo escribir el respaldo") }
        }
    }

    @PluginMethod
    fun cycleRepeat(call: PluginCall) {
        onMain { call.resolve(JSObject().put("mode", PlayerHub.cycleRepeat())) }
    }

    @PluginMethod
    fun setFavorite(call: PluginCall) {
        val track = call.getObject("track")?.let { readTrack(it) }
        if (track == null) {
            call.reject("Falta la canción")
            return
        }
        val favorite = call.getBoolean("favorite", true) ?: true
        if (Library.isFavorite(track.id) != favorite) Library.toggleFavorite(track)
        call.resolve()
    }

    @PluginMethod
    fun syncLibrary(call: PluginCall) {
        val favorites = readTracks(call.getArray("favorites"))
        val ids = favorites.mapTo(HashSet()) { it.id }
        Library.favorites().filter { it.id !in ids }.forEach { Library.toggleFavorite(it) }
        Library.addFavorites(favorites)

        val playlists = call.getArray("playlists")
        if (playlists != null) {
            val imported = ArrayList<Playlist>(playlists.length())
            for (index in 0 until playlists.length()) {
                val item = playlists.optJSONObject(index) ?: continue
                runCatching {
                    val playlistTracks = readTracks(item.optJSONArray("tracks"))
                    imported.add(Playlist(item.optString("id"), item.optString("title"), item.optString("source", "custom"), playlistTracks.toMutableList()))
                }
            }
            Library.replacePlaylists(imported)
        }
        call.resolve()
    }

    @PluginMethod
    fun download(call: PluginCall) {
        val track = call.getObject("track")?.let { readTrack(it) }
        if (track == null) {
            call.reject("Falta la canción")
            return
        }
        if (Library.isDownloaded(track.id)) {
            call.resolve(JSObject().put("alreadyDownloaded", true))
            return
        }
        if (!Downloader.enqueue(track)) {
            call.reject("La canción ya está en la cola de descargas")
            return
        }
        call.resolve(JSObject().put("queued", true))
    }

    private fun readTracks(items: JSONArray?): List<Track> {
        if (items == null) return emptyList()
        val tracks = ArrayList<Track>(items.length())
        for (index in 0 until items.length()) {
            val item = items.optJSONObject(index) ?: continue
            tracks.add(readTrack(item))
        }
        return tracks
    }

    private fun onMain(block: () -> Unit) {
        com.ritmo.auto.core.main.post { block() }
    }

    private fun readTrack(item: JSONObject): Track = if (item.has("i")) {
        Track.fromJson(item)
    } else {
        Track(
            id = item.optString("id"),
            title = item.optString("title"),
            artist = item.optString("artist"),
            art = item.optString("art"),
            duration = item.optLong("duration"),
            genre = item.optString("genre"),
            size = item.optLong("size"),
            streamUrl = item.optString("streamUrl"),
            official = item.optBoolean("official") || item.optBoolean("verified")
        )
    }

    private fun snapshot(): JSObject {
        val player = PlayerHub.player
        val queue = JSArray()
        PlayerHub.queue().forEach { queue.put(trackObject(it)) }
        val current = PlayerHub.currentTrack()?.let(::trackObject)
        return JSObject().apply {
            put("queue", queue)
            put("index", player?.currentMediaItemIndex ?: -1)
            put("current", current)
            put("playing", player?.isPlaying ?: false)
            put("position", player?.currentPosition?.coerceAtLeast(0L) ?: 0L)
            put("duration", player?.duration?.takeIf { it > 0L } ?: 0L)
            put("shuffle", PlayerHub.isShuffle())
            put("repeat", player?.repeatMode ?: 0)
        }
    }

    private fun downloadsSnapshot(): JSObject {
        val downloaded = JSArray()
        Library.downloads().forEach { downloaded.put(it.id) }
        val tasks = JSArray()
        Downloader.snapshot().values.forEach { progress ->
            tasks.put(JSObject().apply {
                put("id", progress.id)
                put("state", progress.state)
                put("percent", progress.percent)
                put("message", progress.message)
            })
        }
        return JSObject().put("downloaded", downloaded).put("tasks", tasks)
    }

    private fun trackObject(track: Track): JSObject = JSObject().apply {
        put("id", track.id)
        put("videoId", track.videoId)
        put("title", track.title)
        put("artist", track.artist)
        put("art", track.art)
        put("duration", track.duration)
        put("genre", track.genre)
        put("size", track.size)
        put("streamUrl", track.streamUrl)
        put("official", track.official)
    }
}
