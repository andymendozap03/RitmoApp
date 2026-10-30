package com.ritmo.auto.play

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.Player
import androidx.media3.common.PlaybackException
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.ResolvingDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.LibraryResult
import androidx.media3.session.MediaLibraryService
import androidx.media3.session.MediaSession
import androidx.media3.session.DefaultMediaNotificationProvider
import com.ritmo.auto.R
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionResult
import com.google.common.collect.ImmutableList
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import com.ritmo.auto.data.Library
import com.ritmo.auto.data.Innertube
import com.ritmo.auto.data.Track
import java.util.concurrent.Executors
import java.util.concurrent.ConcurrentHashMap

/**
 * Foreground playback service with Android Auto (MediaLibraryService) support.
 * Keeps audio alive in background, on steering wheel controls and car dashboard.
 */
class PlaybackService : MediaLibraryService() {

    private var session: MediaLibrarySession? = null
    private val resolver = Executors.newFixedThreadPool(2)
    private val searchCache = ConcurrentHashMap<String, List<Track>>()
    private val lastStreamUrl = ConcurrentHashMap<String, String>()
    private val rejectedStreamHosts = ConcurrentHashMap<String, MutableSet<String>>()
    private val streamRetryCount = ConcurrentHashMap<String, Int>()
    private val failedRecordingIds = ConcurrentHashMap.newKeySet<String>()
    private val recordingFallbackCount = ConcurrentHashMap<String, Int>()
    private val recentSeekPositions = ConcurrentHashMap<String, Pair<Long, Long>>()
    private val pendingResumePositions = ConcurrentHashMap<String, Long>()

    companion object {
        const val ROOT_ID = "root"
        const val NODE_DOWNLOADS = "downloads"
        const val NODE_FAVORITES = "favorites"
        const val NODE_PLAYLISTS = "playlists"
        const val NODE_RECENT = "recent"
    }

    private val callback = object : MediaLibrarySession.Callback {

        /** Resolve YouTube media IDs requested by Android Auto into playable streams.
         * Browse results intentionally contain stable IDs; their artwork URI is not
         * an audio source. Resolve here so car playback uses the same queue as the app.
         */
        override fun onAddMediaItems(
            session: MediaSession,
            controller: MediaSession.ControllerInfo,
            mediaItems: List<MediaItem>
        ): ListenableFuture<List<MediaItem>> {
            val future = com.google.common.util.concurrent.SettableFuture.create<List<MediaItem>>()
            resolver.execute {
                try {
                    Library.init(applicationContext)
                    // Android Auto may ask to play a browsed row as a one-item
                    // request. Keep the rest of its containing playlist/list in
                    // the session queue so the car's next/previous controls work.
                    val resolved = if (mediaItems.size == 1) {
                        val requested = mediaItems.first()
                        val track = findTrack(requested.mediaId)
                        val contextQueue = track?.let(::queueContextFor)
                        if (track != null && !contextQueue.isNullOrEmpty()) {
                            val selectedIndex = contextQueue.indexOfFirst { it.id == track.id }
                            contextQueue.drop(selectedIndex.coerceAtLeast(0)).take(100).map(PlayerHub::mediaItem)
                        } else {
                            listOf(track?.let(PlayerHub::mediaItem) ?: requested)
                        }
                    } else {
                        mediaItems.map { requested ->
                            val track = findTrack(requested.mediaId)
                            if (track != null) PlayerHub.mediaItem(track) else requested
                        }
                    }
                    future.set(resolved)
                } catch (error: Throwable) {
                    future.setException(error)
                }
            }
            return future
        }

        override fun onGetLibraryRoot(
            session: MediaLibrarySession,
            browser: MediaSession.ControllerInfo,
            params: LibraryParams?
        ): ListenableFuture<LibraryResult<MediaItem>> {
            val rootItem = MediaItem.Builder()
                .setMediaId(ROOT_ID)
                .setMediaMetadata(
                    MediaMetadata.Builder()
                        .setIsBrowsable(true)
                        .setIsPlayable(false)
                        .setTitle("Ritmo")
                        .build()
                )
                .build()
            return Futures.immediateFuture(LibraryResult.ofItem(rootItem, params))
        }

        override fun onGetChildren(
            session: MediaLibrarySession,
            browser: MediaSession.ControllerInfo,
            parentId: String,
            page: Int,
            pageSize: Int,
            params: LibraryParams?
        ): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> {
            Library.init(applicationContext)
            val list = mutableListOf<MediaItem>()

            when (parentId) {
                ROOT_ID -> {
                    list.add(browsableItem(NODE_DOWNLOADS, "📥 Descargas (Offline)"))
                    list.add(browsableItem(NODE_FAVORITES, "❤️ Favoritos (Me gusta)"))
                    list.add(browsableItem(NODE_PLAYLISTS, "📑 Playlists"))
                    list.add(browsableItem(NODE_RECENT, "🕒 Recientes"))
                }
                NODE_DOWNLOADS -> {
                    Library.downloads().forEach { list.add(PlayerHub.mediaItem(it)) }
                }
                NODE_FAVORITES -> {
                    Library.favorites().forEach { list.add(PlayerHub.mediaItem(it)) }
                }
                NODE_RECENT -> {
                    Library.recent().forEach { list.add(PlayerHub.mediaItem(it)) }
                }
                NODE_PLAYLISTS -> {
                    Library.playlists().forEach { pl ->
                        list.add(browsableItem("pl_${pl.id}", "${pl.title} (${pl.tracks.size})"))
                    }
                }
                else -> {
                    if (parentId.startsWith("pl_")) {
                        val plId = parentId.removePrefix("pl_")
                        Library.getPlaylist(plId)?.tracks?.forEach {
                            list.add(PlayerHub.mediaItem(it))
                        }
                    }
                }
            }

            return Futures.immediateFuture(
                LibraryResult.ofItemList(ImmutableList.copyOf(list), params)
            )
        }

        override fun onGetItem(
            session: MediaLibrarySession,
            browser: MediaSession.ControllerInfo,
            mediaId: String
        ): ListenableFuture<LibraryResult<MediaItem>> {
            val track = findTrack(mediaId)
            return if (track != null) {
                Futures.immediateFuture(LibraryResult.ofItem(PlayerHub.mediaItem(track), null))
            } else {
                Futures.immediateFuture(LibraryResult.ofError(SessionResult.RESULT_ERROR_BAD_VALUE))
            }
        }

        override fun onSearch(
            session: MediaLibrarySession,
            browser: MediaSession.ControllerInfo,
            query: String,
            params: LibraryParams?
        ): ListenableFuture<LibraryResult<Void>> {
            val future = com.google.common.util.concurrent.SettableFuture.create<LibraryResult<Void>>()
            resolver.execute {
                val results = Innertube.search(query, 50)
                searchCache[query.trim().lowercase()] = results
                session.notifySearchResultChanged(browser, query, results.size, params)
                future.set(LibraryResult.ofVoid(params))
            }
            return future
        }

        override fun onGetSearchResult(
            session: MediaLibrarySession,
            browser: MediaSession.ControllerInfo,
            query: String,
            page: Int,
            pageSize: Int,
            params: LibraryParams?
        ): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> {
            val key = query.trim().lowercase()
            val future = com.google.common.util.concurrent.SettableFuture.create<LibraryResult<ImmutableList<MediaItem>>>()
            resolver.execute {
                val tracks = searchCache[key] ?: Innertube.search(query, 50).also { searchCache[key] = it }
                val safeSize = pageSize.coerceAtLeast(1)
                val start = (page.coerceAtLeast(0) * safeSize).coerceAtMost(tracks.size)
                val items = tracks.drop(start).take(safeSize).map { PlayerHub.mediaItem(it) }
                future.set(LibraryResult.ofItemList(ImmutableList.copyOf(items), params))
            }
            return future
        }
    }

    override fun onCreate() {
        super.onCreate()
        setMediaNotificationProvider(DefaultMediaNotificationProvider.Builder(this).build().apply {
            setSmallIcon(R.drawable.ic_stat_ritmo)
        })
        Library.init(applicationContext)
        val httpDataSourceFactory = ChunkedRangeDataSource.Factory(
            "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36"
        )
        // Mirror the web proxy: fetch bounded byte ranges and expose them as one stream.
        val dataSourceFactory = DefaultDataSource.Factory(this, httpDataSourceFactory)
        val resolvingDataSourceFactory = ResolvingDataSource.Factory(dataSourceFactory) { dataSpec ->
            if (dataSpec.uri.scheme != "ritmo") {
                dataSpec
            } else {
                val videoId = dataSpec.uri.lastPathSegment.orEmpty()
                if (!videoId.matches(Regex("[A-Za-z0-9_-]{11}"))) {
                    throw java.io.IOException("ID de audio inválido en la cola")
                }
                android.util.Log.i("RitmoPlayback", "Resolviendo flujo para $videoId")
                val rejectedHosts = rejectedStreamHosts[videoId].orEmpty()
                // Use the same progressive MP4 source as the working mobile
                // downloads. The adaptive audio source may allow its first
                // block but reject later byte ranges with HTTP 403.
                val resolvedUrl = Innertube.streamUrl(
                    videoId,
                    rejectedHosts,
                    preferProgressive = true
                )
                if (!resolvedUrl.startsWith("https://")) {
                    throw java.io.IOException("InnerTube no devolvió una URL HTTPS para $videoId")
                }
                lastStreamUrl[videoId] = resolvedUrl
                android.util.Log.i("RitmoPlayback", "Flujo resuelto para $videoId (${android.net.Uri.parse(resolvedUrl).host})")
                dataSpec.withUri(android.net.Uri.parse(resolvedUrl))
            }
        }
        val player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(DefaultMediaSourceFactory(this).setDataSourceFactory(resolvingDataSourceFactory))
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(C.USAGE_MEDIA)
                    .setContentType(C.AUDIO_CONTENT_TYPE_MUSIC)
                    .build(),
                true
            )
            .setHandleAudioBecomingNoisy(true)
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .setSeekBackIncrementMs(10_000)
            .setSeekForwardIncrementMs(10_000)
            .build()
        player.addListener(object : Player.Listener {
            override fun onPlaybackStateChanged(playbackState: Int) {
                if (playbackState != Player.STATE_READY) return
                val videoId = player.currentMediaItem?.mediaId?.removePrefix(Track.VIDEO_PREFIX) ?: return
                streamRetryCount.remove(videoId)
                rejectedStreamHosts.remove(videoId)
                val resumePosition = pendingResumePositions.remove(videoId)
                if (resumePosition != null && resumePosition > 0L) {
                    val duration = player.duration
                    val safePosition = if (duration > 0L) resumePosition.coerceAtMost(duration) else resumePosition
                    player.seekTo(player.currentMediaItemIndex, safePosition)
                    player.playWhenReady = true
                }
            }

            override fun onPositionDiscontinuity(
                oldPosition: Player.PositionInfo,
                newPosition: Player.PositionInfo,
                reason: Int
            ) {
                // Preserve the user's target if a CDN rejects a seek range and
                // playback has to reopen the source. The extractor can report
                // a follow-up seek adjustment after the original seek event.
                if (reason != Player.DISCONTINUITY_REASON_SEEK &&
                    reason != Player.DISCONTINUITY_REASON_SEEK_ADJUSTMENT
                ) return
                val videoId = newPosition.mediaItem?.mediaId?.removePrefix(Track.VIDEO_PREFIX) ?: return
                if (videoId.matches(Regex("[A-Za-z0-9_-]{11}"))) {
                    recentSeekPositions[videoId] = newPosition.positionMs to System.currentTimeMillis()
                    android.util.Log.d("RitmoPlayback", "Posición de seek guardada: $videoId ${newPosition.positionMs}ms")
                }
            }

            override fun onPlayerError(error: PlaybackException) {
                val item = player.currentMediaItem ?: return
                val videoId = item.mediaId.removePrefix(Track.VIDEO_PREFIX)
                if (!videoId.matches(Regex("[A-Za-z0-9_-]{11}"))) return
                val attempt = (streamRetryCount[videoId] ?: 0) + 1
                val now = System.currentTimeMillis()
                val savedSeek = recentSeekPositions[videoId]?.takeIf { now - it.second < 8000L }?.first
                val resumePosition = savedSeek ?: player.currentPosition.coerceAtLeast(0L)
                if (attempt > 2) {
                    android.util.Log.w("RitmoPlayback", "Se agotaron los orígenes alternativos para $videoId")
                    streamRetryCount.remove(videoId)
                    rejectedStreamHosts.remove(videoId)
                    val title = item.mediaMetadata.title?.toString().orEmpty()
                    val artist = item.mediaMetadata.artist?.toString().orEmpty()
                    val songKey = "$title|$artist".lowercase().trim()
                    val fallbackAttempt = recordingFallbackCount.merge(songKey, 1, Int::plus) ?: 1
                    if (fallbackAttempt <= 2) {
                        failedRecordingIds.add(videoId)
                        val failedItemId = item.mediaId
                        val failedIndex = player.currentMediaItemIndex
                        resolver.execute {
                            try {
                                val normalizedTitle = title.lowercase().replace(Regex("[^\\p{L}\\p{N}]"), "")
                                val normalizedArtist = artist.lowercase().replace(Regex("[^\\p{L}\\p{N}]"), "")
                                val alternate = Innertube.search("$title $artist", 30).firstOrNull { candidate ->
                                    val candidateTitle = candidate.title.lowercase().replace(Regex("[^\\p{L}\\p{N}]"), "")
                                    val candidateArtist = candidate.artist.lowercase().replace(Regex("[^\\p{L}\\p{N}]"), "")
                                    candidate.videoId != videoId && candidate.videoId !in failedRecordingIds &&
                                        normalizedTitle.isNotBlank() &&
                                        (candidateTitle.contains(normalizedTitle) || normalizedTitle.contains(candidateTitle)) &&
                                        (normalizedArtist.isBlank() || candidateArtist.contains(normalizedArtist) || normalizedArtist.contains(candidateArtist))
                                }
                                if (alternate == null) return@execute
                                android.util.Log.i("RitmoPlayback", "Probando otra grabacion para $title - $artist: ${alternate.videoId}")
                                android.os.Handler(mainLooper).post {
                                    if (player.currentMediaItem?.mediaId != failedItemId || failedIndex !in 0 until player.mediaItemCount) return@post
                                    if (resumePosition > 0L) pendingResumePositions[alternate.videoId] = resumePosition
                                    player.replaceMediaItem(failedIndex, PlayerHub.mediaItem(alternate))
                                    player.prepare()
                                    player.play()
                                }
                            } catch (retryError: Throwable) {
                                android.util.Log.w("RitmoPlayback", "Fallo la busqueda de otra grabacion para $title", retryError)
                            }
                        }
                    }
                    return
                }
                streamRetryCount[videoId] = attempt
                lastStreamUrl[videoId]?.let { Uri.parse(it).host }?.let { host ->
                    rejectedStreamHosts.computeIfAbsent(videoId) { ConcurrentHashMap.newKeySet<String>() }.add(host)
                }
                val failedItemId = item.mediaId
                val failedIndex = player.currentMediaItemIndex
                resolver.execute {
                    try {
                        val excluded = rejectedStreamHosts[videoId].orEmpty().toSet()
                        val alternateUrl = Innertube.streamUrl(
                            videoId,
                            excluded,
                            preferProgressive = true
                        )
                        android.util.Log.i("RitmoPlayback", "Reintentando $videoId con otro origen (intento $attempt)")
                        android.os.Handler(mainLooper).post {
                            if (player.currentMediaItem?.mediaId != failedItemId || failedIndex !in 0 until player.mediaItemCount) return@post
                            lastStreamUrl[videoId] = alternateUrl
                            if (resumePosition > 0L) pendingResumePositions[videoId] = resumePosition
                            player.replaceMediaItem(failedIndex, item.buildUpon().setUri(Uri.parse(alternateUrl)).build())
                            player.prepare()
                            player.play()
                        }
                    } catch (retryError: Throwable) {
                        android.util.Log.w("RitmoPlayback", "No se encontró otro origen para $videoId", retryError)
                    }
                }
            }
        })
        player.repeatMode = Player.REPEAT_MODE_OFF

        session = MediaLibrarySession.Builder(this, player, callback).build()
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaLibrarySession? = session

    override fun onTaskRemoved(rootIntent: Intent?) {
        val player = session?.player
        if (player == null || !player.playWhenReady || player.mediaItemCount == 0) {
            stopSelf()
        }
    }

    override fun onDestroy() {
        session?.run {
            player.release()
            release()
        }
        session = null
        super.onDestroy()
    }

    private fun browsableItem(id: String, title: String): MediaItem =
        MediaItem.Builder()
            .setMediaId(id)
            .setMediaMetadata(
                MediaMetadata.Builder()
                    .setIsBrowsable(true)
                    .setIsPlayable(false)
                    .setTitle(title)
                    .build()
            )
            .build()

    private fun findTrack(id: String): Track? = Library.find(id)
        ?: Library.playlists().asSequence().flatMap { it.tracks.asSequence() }.firstOrNull { it.id == id }
        ?: searchCache.values.asSequence().flatten().firstOrNull { it.id == id }

    private fun queueContextFor(track: Track): List<Track>? {
        Library.playlists().firstOrNull { playlist -> playlist.tracks.any { it.id == track.id } }
            ?.let { return it.tracks }
        Library.downloads().takeIf { tracks -> tracks.any { it.id == track.id } }
            ?.let { return it }
        Library.favorites().takeIf { tracks -> tracks.any { it.id == track.id } }
            ?.let { return it }
        Library.recent().takeIf { tracks -> tracks.any { it.id == track.id } }
            ?.let { return it }
        return null
    }
}
