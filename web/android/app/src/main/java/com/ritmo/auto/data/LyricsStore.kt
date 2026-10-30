package com.ritmo.auto.data

import android.content.Context
import android.net.Uri
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest

/** Persistent sidecar lyrics: independent of the audio download's success state. */
object LyricsStore {
    private fun file(context: Context, id: String): File {
        val key = MessageDigest.getInstance("SHA-256").digest(id.toByteArray())
            .joinToString("") { "%02x".format(it) }
        return File(File(context.filesDir, "lyrics").apply { mkdirs() }, "$key.json")
    }

    private fun hasLyrics(value: JSONObject): Boolean =
        value.optString("syncedLyrics").let { it.isNotBlank() && it != "null" } ||
            value.optString("plainLyrics").let { it.isNotBlank() && it != "null" }

    @Synchronized
    fun save(context: Context, id: String, value: JSONObject) {
        if (!hasLyrics(value)) return
        val target = file(context, id)
        val temp = File(target.path + ".tmp")
        temp.writeText(value.toString(), Charsets.UTF_8)
        if (!temp.renameTo(target)) {
            temp.copyTo(target, overwrite = true)
            temp.delete()
        }
    }

    @Synchronized
    private fun read(context: Context, id: String): JSONObject? = runCatching {
        JSONObject(file(context, id).readText(Charsets.UTF_8)).takeIf(::hasLyrics)
    }.getOrNull()

    fun getOrFetch(context: Context, track: Track): JSONObject? {
        read(context, track.id)?.let { return it }
        if (!Net.isOnline(context)) return null
        val title = Uri.encode(track.title)
        val artist = Uri.encode(track.artist)
        val duration = if (track.duration > 0) "&duration=${track.duration / 1000}" else ""
        var result = runCatching {
            JSONObject(Net.get("https://lrclib.net/api/get?track_name=$title&artist_name=$artist$duration", 10000))
                .takeIf(::hasLyrics)
        }.getOrNull()
        if (result == null) result = runCatching {
            val text = JSONObject(Net.get("https://api.lyrics.ovh/v1/$artist/$title", 10000)).optString("lyrics")
            JSONObject().put("plainLyrics", text).takeIf(::hasLyrics)
        }.getOrNull()
        result?.let { save(context, track.id, it) }
        return result
    }
}
