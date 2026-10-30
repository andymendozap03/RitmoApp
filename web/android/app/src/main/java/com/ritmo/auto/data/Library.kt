package com.ritmo.auto.data

import android.content.Context
import android.content.SharedPreferences
import com.ritmo.auto.core.ui
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.concurrent.CopyOnWriteArrayList

/**
 * Offline library: favorites, downloads and recently played tracks.
 * Stored as one small JSON blob: no database, no extra dependencies.
 */
object Library {

    private val lock = Any()
    private lateinit var prefs: SharedPreferences
    private lateinit var appContext: Context
    private var loaded = false

    private val favorites = LinkedHashMap<String, Track>()
    private val downloads = LinkedHashMap<String, Track>()
    private val playlists = LinkedHashMap<String, Playlist>()
    private val recent = ArrayList<Track>()

    private val listeners = CopyOnWriteArrayList<() -> Unit>()

    private var lastTrack: Track? = null
    private var lastPosition: Long = 0L

    fun init(context: Context) {
        synchronized(lock) {
            if (loaded) return
            appContext = context.applicationContext
            prefs = appContext.getSharedPreferences("ritmo_library", Context.MODE_PRIVATE)
            parse(prefs.getString("data", null))
            loaded = true
        }
    }

    fun addListener(listener: () -> Unit) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeListener(listener: () -> Unit) {
        listeners.remove(listener)
    }

    private fun notifyChanged() {
        ui { for (listener in listeners) listener() }
    }

    // ---------------------------------------------------------------- favorites

    fun favorites(): List<Track> = synchronized(lock) { favorites.values.toList().asReversed() }

    fun isFavorite(id: String): Boolean = synchronized(lock) { favorites.containsKey(id) }

    fun toggleFavorite(track: Track): Boolean {
        val nowFavorite: Boolean
        synchronized(lock) {
            if (favorites.containsKey(track.id)) {
                favorites.remove(track.id)
                nowFavorite = false
            } else {
                favorites[track.id] = track
                nowFavorite = true
            }
            save()
        }
        notifyChanged()
        return nowFavorite
    }

    fun addFavorites(tracks: Collection<Track>): Int {
        val added = synchronized(lock) {
            var count = 0
            tracks.forEach { track ->
                if (!favorites.containsKey(track.id)) { favorites[track.id] = track; count++ }
            }
            if (count > 0) save()
            count
        }
        if (added > 0) notifyChanged()
        return added
    }

    // ---------------------------------------------------------------- downloads

    fun dir(): File {
        val folder = appContext.getExternalFilesDir("music") ?: File(appContext.filesDir, "music")
        if (!folder.exists()) folder.mkdirs()
        return folder
    }

    fun fileFor(trackId: String): File {
        val clean = trackId.replace(":", "_").replace("/", "_")
        val m4a = File(dir(), "$clean.m4a")
        if (m4a.exists()) return m4a
        val mp3 = File(dir(), "$clean.mp3")
        if (mp3.exists()) return mp3
        return m4a
    }

    fun partFor(trackId: String): File {
        val clean = trackId.replace(":", "_").replace("/", "_")
        return File(dir(), "$clean.part")
    }

    // ---------------------------------------------------------------- playlists

    fun playlists(): List<Playlist> = synchronized(lock) { playlists.values.toList().asReversed() }

    fun getPlaylist(id: String): Playlist? = synchronized(lock) { playlists[id] }

    fun addPlaylist(playlist: Playlist) {
        synchronized(lock) {
            playlists[playlist.id] = playlist
            save()
        }
        notifyChanged()
    }

    fun replacePlaylists(items: Collection<Playlist>) {
        synchronized(lock) {
            playlists.clear()
            items.forEach { playlists[it.id] = it }
            save()
        }
        notifyChanged()
    }

    fun removePlaylist(id: String) {
        synchronized(lock) {
            playlists.remove(id)
            save()
        }
        notifyChanged()
    }

    fun downloads(): List<Track> = synchronized(lock) {
        downloads.values.filter { fileFor(it.id).exists() }.toList().asReversed()
    }

    fun isDownloaded(trackId: String): Boolean = synchronized(lock) {
        downloads.containsKey(trackId) && fileFor(trackId).exists()
    }

    fun addDownload(track: Track) {
        synchronized(lock) {
            downloads[track.id] = track
            save()
        }
        notifyChanged()
    }

    fun removeDownload(trackId: String) {
        synchronized(lock) {
            downloads.remove(trackId)
            save()
        }
        notifyChanged()
    }

    fun downloadsSize(): Long = synchronized(lock) {
        downloads.values.sumOf { it.size }
    }

    fun find(trackId: String): Track? = synchronized(lock) {
        favorites[trackId] ?: downloads[trackId] ?: recent.firstOrNull { it.id == trackId }
    }

    fun downloadCount(): Int = synchronized(lock) { downloads.size }

    fun clearDownloadEntries() {
        synchronized(lock) {
            downloads.clear()
            save()
        }
        notifyChanged()
    }

    // ------------------------------------------------------------------ recents

    fun recent(): List<Track> = synchronized(lock) { recent.toList() }

    fun addRecent(track: Track) {
        synchronized(lock) {
            recent.removeAll { it.id == track.id }
            recent.add(0, track)
            while (recent.size > 40) recent.removeAt(recent.size - 1)
            save()
        }
    }

    fun clearRecent() {
        synchronized(lock) {
            recent.clear()
            save()
        }
        notifyChanged()
    }

    // ------------------------------------------------------------------- resume

    fun lastPlayed(): Track? = synchronized(lock) { lastTrack }

    fun lastPlayedPosition(): Long = synchronized(lock) { lastPosition }

    fun saveLastPlayed(track: Track?, position: Long) {
        synchronized(lock) {
            if (track == null) return
            lastTrack = track
            lastPosition = position.coerceAtLeast(0L)
            save()
        }
    }

    // --------------------------------------------------------------- serializing

    private fun parse(raw: String?) {
        if (raw.isNullOrBlank()) return
        try {
            val root = JSONObject(raw)
            readArray(root.optJSONArray("f")) { favorites[it.id] = it }
            readArray(root.optJSONArray("d")) { downloads[it.id] = it }
            readArray(root.optJSONArray("r")) { recent.add(it) }
            val parr = root.optJSONArray("p")
            if (parr != null) {
                for (i in 0 until parr.length()) {
                    val po = parr.optJSONObject(i) ?: continue
                    val pl = Playlist.fromJson(po)
                    if (pl.id.isNotEmpty()) playlists[pl.id] = pl
                }
            }
            val last = root.optJSONObject("last")
            if (last != null) {
                val t = last.optJSONObject("t")?.let { Track.fromJson(it) }
                if (t != null && t.id.isNotEmpty()) {
                    lastTrack = t
                    lastPosition = last.optLong("p")
                }
            }
        } catch (_: Throwable) {
        }
    }

    private inline fun readArray(array: JSONArray?, action: (Track) -> Unit) {
        if (array == null) return
        for (i in 0 until array.length()) {
            val o = array.optJSONObject(i) ?: continue
            val track = Track.fromJson(o)
            if (track.id.isNotEmpty()) action(track)
        }
    }

    private fun save() {
        try {
            val root = JSONObject()
            root.put("f", arrayOf(favorites.values))
            root.put("d", arrayOf(downloads.values))
            root.put("r", arrayOf(recent))
            val parr = JSONArray()
            playlists.values.forEach { parr.put(it.toJson()) }
            root.put("p", parr)
            val last = lastTrack
            if (last != null) {
                root.put("last", JSONObject().put("t", last.toJson()).put("p", lastPosition))
            }
            prefs.edit().putString("data", root.toString()).apply()
        } catch (_: Throwable) {
        }
    }

    private fun arrayOf(tracks: Collection<Track>): JSONArray {
        val array = JSONArray()
        for (track in tracks) array.put(track.toJson())
        return array
    }
}
