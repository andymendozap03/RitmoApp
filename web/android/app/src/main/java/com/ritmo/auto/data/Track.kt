package com.ritmo.auto.data

import org.json.JSONObject

data class Track(
    val id: String = "",
    val title: String = "",
    val artist: String = "",
    val art: String = "",
    val duration: Long = 0L,
    val genre: String = "",
    val size: Long = 0L,
    val streamUrl: String = "",
    val official: Boolean = false
) {
    val videoId: String get() = if (id.startsWith(VIDEO_PREFIX)) id.removePrefix(VIDEO_PREFIX) else id

    fun toJson(): JSONObject = JSONObject().apply {
        put("i", id)
        put("t", title)
        put("a", artist)
        put("art", art)
        put("d", duration)
        put("g", genre)
        if (official) put("o", true)
        if (size > 0L) put("s", size)
        if (streamUrl.isNotBlank()) put("u", streamUrl)
    }

    companion object {
        const val VIDEO_PREFIX = "yt:"

        fun fromInnerTube(id: String, title: String, artist: String, art: String, durationMs: Long): Track = Track(
            id = VIDEO_PREFIX + id,
            title = title.ifBlank { "Canción" },
            artist = artist.ifBlank { "Artista" },
            art = art,
            duration = durationMs,
            genre = "Música"
        )

        fun fromJson(o: JSONObject): Track = Track(
            id = o.optString("i"),
            title = o.optString("t"),
            artist = o.optString("a"),
            art = o.optString("art"),
            duration = o.optLong("d"),
            genre = o.optString("g"),
            size = o.optLong("s"),
            streamUrl = o.optString("u"),
            official = o.optBoolean("o", false)
        )
    }
}

data class Playlist(
    val id: String,
    val title: String,
    val source: String = "custom",
    val tracks: MutableList<Track> = mutableListOf()
) {
    fun toJson(): JSONObject = JSONObject().apply {
        put("id", id)
        put("title", title)
        put("source", source)
        val items = org.json.JSONArray()
        tracks.forEach { items.put(it.toJson()) }
        put("tracks", items)
    }

    companion object {
        fun fromJson(o: JSONObject): Playlist {
            val items = o.optJSONArray("tracks") ?: org.json.JSONArray()
            val tracks = mutableListOf<Track>()
            for (i in 0 until items.length()) {
                items.optJSONObject(i)?.let { tracks.add(Track.fromJson(it)) }
            }
            return Playlist(
                id = o.optString("id"),
                title = o.optString("title"),
                source = o.optString("source", "custom"),
                tracks = tracks
            )
        }
    }
}
