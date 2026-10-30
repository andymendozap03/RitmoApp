package com.ritmo.auto.play

import android.content.ComponentName
import android.content.Context
import android.net.Uri
import android.os.Bundle
import androidx.core.content.ContextCompat
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import com.ritmo.auto.R
import com.ritmo.auto.core.toast
import com.ritmo.auto.data.Library
import com.ritmo.auto.data.Net
import com.ritmo.auto.data.Track
import java.util.concurrent.CopyOnWriteArrayList

/**
 * Process-wide connection to the playback service.
 * The activity never owns the player, so audio keeps running in background.
 */
object PlayerHub {

    private lateinit var appContext: Context
    private var controller: MediaController? = null

    /**
     * Disabled by JVM tests: MediaController needs a real bound service, and
     * Robolectric's shadow passes a null ComponentName to onServiceConnected.
     */
    internal var autoConnect = true
    private var connecting = false
    private var reconnectAttempts = 0

    private val listeners = CopyOnWriteArrayList<() -> Unit>()
    private var pending: (() -> Unit)? = null
    private var restored = false
    private var lastErrorToast = 0L
    private var shuffleEnabled = false
    private var originalOrder = emptyList<String>()

    val player: Player? get() = controller

    private val playerListener = object : Player.Listener {
        override fun onEvents(player: Player, events: Player.Events) {
            notifyChanged()
            if (events.contains(Player.EVENT_MEDIA_ITEM_TRANSITION) ||
                events.contains(Player.EVENT_PLAYBACK_STATE_CHANGED)
            ) {
                saveState()
            }
        }

        override fun onPlayerError(error: PlaybackException) {
            android.util.Log.e(
                "RitmoPlayback",
                "Media3 falló: code=${error.errorCodeName} message=${error.message}; cause=${error.cause?.javaClass?.name}: ${error.cause?.message}",
                error
            )
            handleError()
        }
    }

    private val controllerListener = object : MediaController.Listener {
        override fun onDisconnected(controller: MediaController) {
            if (this@PlayerHub.controller === controller) {
                this@PlayerHub.controller = null
                notifyChanged()
                if (reconnectAttempts < 5 && ::appContext.isInitialized) {
                    reconnectAttempts++
                    com.ritmo.auto.core.main.postDelayed({
                        if (this@PlayerHub.controller == null) connect()
                    }, 1500)
                }
            }
        }
    }

    fun init(context: Context) {
        if (::appContext.isInitialized) return
        appContext = context.applicationContext
        connect()
    }

    private fun connect() {
        if (!autoConnect) return
        if (controller != null || connecting) return
        connecting = true
        try {
            val token = SessionToken(appContext, ComponentName(appContext, PlaybackService::class.java))
            val future = MediaController.Builder(appContext, token)
                .setListener(controllerListener)
                .buildAsync()
            future.addListener({
                try {
                    val c = future.get()
                    controller = c
                    c.addListener(playerListener)
                    reconnectAttempts = 0
                } catch (_: Throwable) {
                    controller = null
                }
                connecting = false
                notifyChanged()
                pending?.let { action ->
                    pending = null
                    action()
                }
                if (!restored) {
                    restored = true
                    restoreLast()
                }
            }, ContextCompat.getMainExecutor(appContext))
        } catch (_: Throwable) {
            connecting = false
        }
    }

    fun addListener(listener: () -> Unit) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeListener(listener: () -> Unit) {
        listeners.remove(listener)
    }

    fun notifyChanged() {
        for (listener in listeners) {
            try {
                listener()
            } catch (_: Throwable) {
            }
        }
    }

    // ------------------------------------------------------------------ playback

    fun play(tracks: List<Track>, startIndex: Int = 0, shuffle: Boolean = false) {
        if (tracks.isEmpty()) return
        val selected = tracks[startIndex.coerceIn(0, tracks.lastIndex)].id
        val unique = tracks.distinctBy { it.id }
        val index = unique.indexOfFirst { it.id == selected }.coerceAtLeast(0)
        // Resolve each song when Media3 reaches it; queue entries keep stable IDs
        // and are turned into playable sources by PlaybackService's data source.
        executePlay(unique, index, shuffle)
    }
    private fun executePlay(tracks: List<Track>, index: Int, shuffle: Boolean) {
        val action = {
            val c = controller
            if (c == null) {
                appContext.toast(appContext.getString(R.string.failed_play))
            } else {
                originalOrder = tracks.map { it.id }
                val ordered = if (shuffle && tracks.size > 1) {
                    tracks.take(index) + tracks[index] + tracks.drop(index + 1).shuffled()
                } else tracks
                val items = ordered.map { mediaItem(it) }
                c.setMediaItems(items, index, 0L)
                c.shuffleModeEnabled = false
                shuffleEnabled = shuffle
                c.prepare()
                c.play()
                Library.addRecent(tracks[index])
            }
        }
        if (controller == null) {
            pending = action
            init(appContext)
            connect()
        } else {
            action()
        }
    }

    fun playSingle(track: Track, shuffledQueue: List<Track>? = null, shuffle: Boolean = false) {
        val queue = shuffledQueue?.takeIf { it.isNotEmpty() } ?: listOf(track)
        val index = queue.indexOfFirst { it.id == track.id }.coerceAtLeast(0)
        play(queue, index, shuffle)
    }

    fun toggle() {
        val c = controller ?: return
        if (c.isPlaying) {
            c.pause()
        } else {
            if (c.playbackState == Player.STATE_IDLE) c.prepare()
            if (c.playbackState == Player.STATE_ENDED) c.seekToDefaultPosition()
            c.play()
        }
    }

    fun pause() {
        controller?.pause()
    }

    fun next() {
        val c = controller ?: return
        if (c.hasNextMediaItem()) c.seekToNextMediaItem()
        else if (c.repeatMode == Player.REPEAT_MODE_ALL) c.seekTo(0, 0L)
    }

    fun previous() {
        val c = controller ?: return
        if (c.currentPosition > 4000) {
            c.seekTo(0)
        } else if (c.hasPreviousMediaItem()) {
            c.seekToPreviousMediaItem()
        } else if (c.currentPosition > 0) {
            c.seekTo(0)
        }
    }

    fun seekTo(positionMs: Long) {
        val c = controller ?: return
        val index = c.currentMediaItemIndex
        if (index !in 0 until c.mediaItemCount) return
        val duration = c.duration
        val target = if (duration > 0L) positionMs.coerceIn(0L, duration) else positionMs.coerceAtLeast(0L)
        android.util.Log.d("RitmoPlayback", "Seek index=$index from=${c.currentPosition} to=$target duration=$duration")
        // Seek within the current timeline window. The indexed overload can
        // resolve to the beginning on some MediaController implementations.
        c.seekTo(target)
    }

    fun currentTrack(): Track? = trackFromItem(controller?.currentMediaItem)

    fun setShuffle(enabled: Boolean) {
        val c = controller ?: return
        if (shuffleEnabled == enabled) return
        val currentIndex = c.currentMediaItemIndex
        val items = queue()
        shuffleEnabled = enabled
        if (currentIndex !in items.indices || items.size < 2) {
            c.shuffleModeEnabled = false
            notifyChanged()
            return
        }
        if (originalOrder.isEmpty()) originalOrder = items.map { it.id }
        val upcoming = items.drop(currentIndex + 1)
        val desired = if (enabled) upcoming.shuffled() else upcoming.sortedBy {
            originalOrder.indexOf(it.id).takeIf { index -> index >= 0 } ?: Int.MAX_VALUE
        }
        c.shuffleModeEnabled = false
        // Move only future windows: the current MediaSource, buffer and position survive.
        val liveOrder = items.map { it.id }.toMutableList()
        desired.forEachIndexed { offset, track ->
            val target = currentIndex + 1 + offset
            val from = liveOrder.indexOf(track.id)
            if (from > currentIndex && from != target) {
                c.moveMediaItem(from, target)
                liveOrder.add(target, liveOrder.removeAt(from))
            }
        }
        notifyChanged()
    }

    fun isShuffle(): Boolean = shuffleEnabled

    fun cycleRepeat(): Int {
        val c = controller ?: return Player.REPEAT_MODE_OFF
        val next = when (c.repeatMode) {
            Player.REPEAT_MODE_OFF -> Player.REPEAT_MODE_ALL
            Player.REPEAT_MODE_ALL -> Player.REPEAT_MODE_ONE
            else -> Player.REPEAT_MODE_OFF
        }
        c.repeatMode = next
        return next
    }

    fun queue(): List<Track> {
        val c = controller ?: return emptyList()
        val out = ArrayList<Track>(c.mediaItemCount)
        for (i in 0 until c.mediaItemCount) out.add(trackFromItem(c.getMediaItemAt(i)) ?: continue)
        return out
    }

    fun seekToQueueIndex(index: Int) {
        val c = controller ?: return
        if (index in 0 until c.mediaItemCount) {
            c.seekTo(index, 0L)
            c.play()
        }
    }

    // --------------------------------------------------------------------- state

    fun saveState() {
        val c = controller ?: return
        val track = trackFromItem(c.currentMediaItem) ?: return
        Library.saveLastPlayed(track, c.currentPosition)
    }

    fun restoreLast() {
        val c = controller ?: return
        if (c.mediaItemCount > 0) return
        val track = Library.lastPlayed() ?: return
        if (!Library.isDownloaded(track.id) && !Net.isOnline(appContext)) return
        try {
            c.setMediaItem(mediaItem(track), Library.lastPlayedPosition())
            c.prepare()
            c.playWhenReady = false
        } catch (_: Throwable) {
        }
    }

    fun mediaItem(track: Track): MediaItem {
        val downloaded = Library.isDownloaded(track.id)
        val extras = Bundle().apply {
            putString("genre", track.genre)
            putLong("duration", track.duration)
            putBoolean("offline", downloaded)
        }
        val metadata = MediaMetadata.Builder()
            .setTitle(track.title)
            .setArtist(track.artist)
            .setArtworkUri(track.art.takeIf { it.isNotBlank() }?.let { Uri.parse(it) })
            .setIsBrowsable(false)
            .setIsPlayable(true)
            .setExtras(extras)
            .build()
        val builder = MediaItem.Builder()
            .setMediaId(track.id)
            .setMediaMetadata(metadata)
        if (downloaded) {
            builder.setUri(Uri.fromFile(Library.fileFor(track.id)))
        } else if (track.videoId.matches(Regex("[A-Za-z0-9_-]{11}"))) {
            // Remote stream URLs are signed and expire. Always resolve a fresh
            // InnerTube URL when playback starts, matching the web /stream path.
            builder.setUri(Uri.Builder().scheme("ritmo").authority("stream").appendPath(track.videoId).build())
        } else if (track.streamUrl.isNotBlank()) {
            builder.setUri(Uri.parse(track.streamUrl))
        }
        return builder.build()
    }

    fun trackFromItem(item: MediaItem?): Track? {
        if (item == null) return null
        val metadata = item.mediaMetadata
        val extras: Bundle? = metadata.extras
        return Track(
            id = item.mediaId,
            title = metadata.title?.toString().orEmpty(),
            artist = metadata.artist?.toString().orEmpty(),
            art = metadata.artworkUri?.toString().orEmpty(),
            duration = extras?.getLong("duration") ?: 0L,
            genre = extras?.getString("genre").orEmpty()
        )
    }

    private fun handleError() {
        val c = controller ?: return
        val item = c.currentMediaItem
        val track = trackFromItem(item)
        if (track != null && Library.isDownloaded(track.id)) {
            try {
                c.replaceMediaItem(c.currentMediaItemIndex, mediaItem(track))
                c.prepare()
                c.play()
                return
            } catch (_: Throwable) {
            }
        }
        val now = System.currentTimeMillis()
        if (now - lastErrorToast > 5000) {
            lastErrorToast = now
            val message = if (!Net.isOnline(appContext)) {
                appContext.getString(R.string.offline_need_download)
            } else {
                appContext.getString(R.string.failed_play)
            }
            appContext.toast(message)
        }
    }
}
