package com.ritmo.auto.data

import android.net.Uri
import android.os.StatFs
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import com.ritmo.auto.App
import com.ritmo.auto.core.ui
import com.ritmo.auto.play.ChunkedRangeDataSource
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.atomic.AtomicInteger

/** Downloads resolved audio using a low-memory streaming connection. */
object Downloader {

    const val STATE_ACTIVE = 0
    const val STATE_DONE = 1
    const val STATE_FAILED = 2
    const val STATE_CANCELLED = 3

    data class Progress(
        val id: String,
        val state: Int,
        val percent: Int,
        val received: Long,
        val total: Long,
        val message: String? = null
    )

    private class Cancelled : Exception()

    private val lock = Any()
    private val queue = LinkedBlockingQueue<Track>()
    private val active = LinkedHashMap<String, Progress>()
    private val cancelled = HashSet<String>()
    private val listeners = CopyOnWriteArrayList<() -> Unit>()
    private var workersStarted = false
    private val workerId = AtomicInteger(0)

    fun addListener(listener: () -> Unit) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeListener(listener: () -> Unit) {
        listeners.remove(listener)
    }

    fun progressFor(id: String): Progress? = synchronized(lock) { active[id] }

    fun snapshot(): Map<String, Progress> = synchronized(lock) { HashMap(active) }

    fun enqueue(track: Track): Boolean {
        if (Library.isDownloaded(track.id)) return false
        synchronized(lock) {
            val current = active[track.id]
            if (current != null && (current.state == STATE_ACTIVE || current.state == STATE_DONE)) return false
            active[track.id] = Progress(track.id, STATE_ACTIVE, 0, 0, 0)
            cancelled.remove(track.id)
        }
        queue.offer(track)
        startWorkers()
        notifyChanged()
        return true
    }

    fun enqueueAll(tracks: List<Track>): Int {
        var count = 0
        for (track in tracks) {
            if (enqueue(track)) count++
        }
        return count
    }

    fun cancel(trackId: String) {
        synchronized(lock) {
            cancelled.add(trackId)
            active[trackId] = Progress(trackId, STATE_CANCELLED, 0, 0, 0)
        }
        queue.removeAll { it.id == trackId }
        Library.partFor(trackId).delete()
        notifyChanged()
    }

    /** Deletes the offline file (and any leftover partial file). */
    fun delete(track: Track) {
        Library.fileFor(track.id).delete()
        Library.partFor(track.id).delete()
        synchronized(lock) {
            active.remove(track.id)
        }
        Library.removeDownload(track.id)
        notifyChanged()
    }

    fun deleteAll(): Int {
        val list = Library.downloads()
        for (track in list) {
            Library.fileFor(track.id).delete()
            Library.partFor(track.id).delete()
        }
        synchronized(lock) {
            active.clear()
            cancelled.clear()
        }
        Library.clearDownloadEntries()
        notifyChanged()
        return list.size
    }

    private fun startWorkers() {
        synchronized(lock) {
            if (workersStarted) return
            workersStarted = true
        }
        repeat(2) {
            val index = workerId.incrementAndGet()
            Thread({
                while (true) {
                    val track = try {
                        queue.take()
                    } catch (interrupted: InterruptedException) {
                        return@Thread
                    }
                    try {
                        download(track)
                    } catch (t: Throwable) {
                        fail(track.id, t)
                    }
                }
            }, "ritmo-download-$index").apply {
                isDaemon = true
                priority = Thread.MIN_PRIORITY + 1
                start()
            }
        }
    }

    private fun download(track: Track) {
        val context = App.instance
        if (Prefs.wifiOnly && !Net.isWifi(context)) {
            fail(track.id, IOException("wifi"))
            return
        }
        if (!Net.isOnline(context)) {
            fail(track.id, IOException("offline"))
            return
        }

        val target = Library.fileFor(track.id)
        val part = Library.partFor(track.id)
        val folder = target.parentFile ?: Library.dir()
        if (target.exists()) target.delete()
        part.delete()

        val rejectedHosts = LinkedHashSet<String>()
        var lastError: Throwable? = null
        try {
            repeat(4) {
                if (isCancelled(track.id)) throw Cancelled()
                // A failed CDN is excluded so the resolver can return a different host.
                // The web downloader requests its progressive MP4 variant.
                // Prefer the same format on Android, then fall back to audio-only
                // formats through Innertube's resolver if it is unavailable.
                val audioUrl = Innertube.streamUrl(track.videoId, rejectedHosts, preferProgressive = true)
                val host = Uri.parse(audioUrl).host
                try {
                    // A normal GET is the most compatible mobile CDN path.
                    // Keep bounded ranges as fallback for hosts that require it.
                    val received = try {
                        downloadSequential(track, audioUrl, part, folder)
                    } catch (sequentialError: Throwable) {
                        if (sequentialError is Cancelled || sequentialError.message == "space") throw sequentialError
                        android.util.Log.w("RitmoDownload", "GET continuo falló; probando rangos", sequentialError)
                        part.delete()
                        downloadByRanges(track, audioUrl, part, folder)
                    }
                    finishDownload(track, part, received)
                    return
                } catch (error: Throwable) {
                    if (error is Cancelled) throw error
                    lastError = error
                    part.delete()
                    if (!host.isNullOrBlank()) rejectedHosts.add(host)
                    android.util.Log.w("RitmoDownload", "Origen de descarga rechazado (${host ?: "desconocido"}); intentando otro", error)
                    if (error.message == "space") throw error
                }
            }
            fail(track.id, lastError ?: IOException("No se encontrÃ³ un origen descargable"))
        } catch (_: Cancelled) {
            part.delete()
            synchronized(lock) {
                active[track.id] = Progress(track.id, STATE_CANCELLED, 0, 0, 0)
                cancelled.remove(track.id)
            }
            notifyChanged()
        } catch (error: Throwable) {
            android.util.Log.e("RitmoDownload", "Descarga fallida para ${track.id}", error)
            fail(track.id, error)
        }
    }

    private fun downloadByRanges(track: Track, url: String, part: File, folder: File): Long {
        val source: DataSource = ChunkedRangeDataSource(Innertube.STREAM_USER_AGENT)
        try {
            val declared = source.open(DataSpec.Builder().setUri(Uri.parse(url)).build())
            if (declared > 0L) {
                val free = try { StatFs(folder.path).availableBytes } catch (_: Throwable) { Long.MAX_VALUE }
                if (free < declared + 40L * 1024 * 1024) throw IOException("space")
            }
            var received = 0L
            var lastUpdate = 0L
            FileOutputStream(part).use { output ->
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    if (isCancelled(track.id)) throw Cancelled()
                    val read = source.read(buffer, 0, buffer.size)
                    if (read < 0) break
                    output.write(buffer, 0, read)
                    received += read
                    val now = System.currentTimeMillis()
                    if (now - lastUpdate > 250) {
                        lastUpdate = now
                        updateProgress(track.id, received, declared)
                    }
                }
                output.fd.sync()
            }
            if (received == 0L || (declared > 0L && received != declared)) throw IOException("incomplete")
            if (isCancelled(track.id)) throw Cancelled()
            return received
        } finally {
            try { source.close() } catch (_: Throwable) { }
        }
    }

    private fun downloadSequential(track: Track, url: String, part: File, folder: File): Long {
        val connection = (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15000
            readTimeout = 30000
            instanceFollowRedirects = true
            requestMethod = "GET"
            useCaches = false
            setRequestProperty("User-Agent", Innertube.STREAM_USER_AGENT)
            setRequestProperty("Accept", "*/*")
            setRequestProperty("Accept-Encoding", "identity")
            setRequestProperty("Origin", "https://music.youtube.com")
            setRequestProperty("Referer", "https://music.youtube.com/")
        }
        try {
            val code = connection.responseCode
            if (code !in 200..299) throw IOException("HTTP $code al descargar audio")
            val total = Uri.parse(url).getQueryParameter("clen")?.toLongOrNull()
                ?: connection.contentLengthLong.takeIf { it > 0L }
                ?: 0L
            if (total > 0L) {
                val free = try { StatFs(folder.path).availableBytes } catch (_: Throwable) { Long.MAX_VALUE }
                if (free < total + 40L * 1024 * 1024) throw IOException("space")
            }
            var received = 0L
            var lastUpdate = 0L
            connection.inputStream.use { input ->
                FileOutputStream(part).use { output ->
                    val buffer = ByteArray(64 * 1024)
                    while (true) {
                        if (isCancelled(track.id)) throw Cancelled()
                        val read = input.read(buffer)
                        if (read < 0) break
                        output.write(buffer, 0, read)
                        received += read
                        val now = System.currentTimeMillis()
                        if (now - lastUpdate > 250) {
                            lastUpdate = now
                            updateProgress(track.id, received, total)
                        }
                    }
                    output.fd.sync()
                }
            }
            if (received == 0L || (total > 0L && received != total)) {
                throw IOException("incomplete: $received/$total bytes")
            }
            if (isCancelled(track.id)) throw Cancelled()
            return received
        } finally {
            connection.disconnect()
        }
    }

    private fun finishDownload(track: Track, part: File, received: Long) {
        val target = Library.fileFor(track.id)
        if (target.exists()) target.delete()
        if (!part.renameTo(target)) {
            part.copyTo(target, overwrite = true)
            part.delete()
        }
        synchronized(lock) { cancelled.remove(track.id) }
        Library.addDownload(track.copy(size = received))
        com.ritmo.auto.core.io.execute {
            runCatching { LyricsStore.getOrFetch(App.instance, track) }
        }
        synchronized(lock) {
            active[track.id] = Progress(track.id, STATE_DONE, 100, received, received)
        }
        notifyChanged()
    }

    private fun isCancelled(id: String): Boolean = synchronized(lock) { cancelled.contains(id) }

    private fun updateProgress(id: String, received: Long, total: Long) {
        val percent = if (total > 0) ((received * 100) / total).toInt().coerceIn(0, 100) else 0
        synchronized(lock) {
            if (active[id]?.state != STATE_ACTIVE) return
            active[id] = Progress(id, STATE_ACTIVE, percent, received, total)
        }
        notifyChanged()
    }

    private fun fail(id: String, error: Throwable) {
        val message = when {
            error.message == "wifi" -> "wifi"
            error.message == "offline" -> "offline"
            error.message == "space" -> "space"
            else -> error.message?.take(100)?.ifBlank { "network" } ?: "network"
        }
        synchronized(lock) {
            active[id] = Progress(id, STATE_FAILED, 0, 0, 0, message)
            cancelled.remove(id)
        }
        Library.partFor(id).delete()
        notifyChanged()
    }

    private fun notifyChanged() {
        ui { for (listener in listeners) listener() }
    }
}
