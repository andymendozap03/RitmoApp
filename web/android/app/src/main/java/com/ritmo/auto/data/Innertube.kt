package com.ritmo.auto.data

import org.json.JSONArray
import org.json.JSONObject
import android.net.Uri
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * YouTube InnerTube client.
 * Connects directly to YouTube and YouTube Music internal endpoints.
 * Requires NO Google Cloud API keys, has NO daily quotas.
 */
object Innertube {

    data class CatalogEntry(val id: String, val title: String, val subtitle: String, val art: String, val type: String)

    private const val YT_MUSIC_API = "https://music.youtube.com/youtubei/v1"
    private const val YOUTUBE_API = "https://www.youtube.com/youtubei/v1"
    const val STREAM_USER_AGENT = "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36"

    private val INVIDIOUS_INSTANCES = arrayOf(
        "https://yt.artemislena.eu",
        "https://invidious.nerdvpn.de",
        "https://inv.nadeko.net",
        "https://invidious.drgns.space"
    )

    private val PIPED_INSTANCES = arrayOf(
        "https://pipedapi.kavin.rocks",
        "https://api.piped.privacy.com.de",
        "https://piped-api.garudalinux.org"
    )

    /**
     * Search songs and music on YouTube / YouTube Music.
     */
    fun search(query: String, limit: Int = 30): List<Track> {
        val cleanQuery = query.trim()
        if (cleanQuery.isEmpty()) return emptyList()

        return try {
            searchYoutube(cleanQuery, limit).sortedByDescending { it.official }.take(limit)
        } catch (_: Throwable) {
            try {
                searchMusic(cleanQuery, limit).sortedByDescending { it.official }.take(limit)
            } catch (_: Throwable) {
                emptyList()
            }
        }
    }

    fun searchEntities(query: String): List<CatalogEntry> {
        val cleanQuery = query.trim()
        if (cleanQuery.isEmpty()) return emptyList()
        val payload = JSONObject().apply {
            put("context", JSONObject().put("client", JSONObject().apply {
                put("clientName", "WEB"); put("clientVersion", "2.20240101.00.00"); put("hl", "es"); put("gl", "US")
            }))
            put("query", cleanQuery)
        }
        val response = try { Net.postJson("$YOUTUBE_API/search?prettyPrint=false", payload.toString()) }
        catch (_: Throwable) {
            val musicPayload = JSONObject().apply {
                put("context", JSONObject().put("client", JSONObject().apply {
                    put("clientName", "WEB_REMIX"); put("clientVersion", "1.20240101.01.00"); put("hl", "es"); put("gl", "US")
                }))
                put("query", cleanQuery)
            }
            Net.postJson("$YT_MUSIC_API/search?prettyPrint=false", musicPayload.toString())
        }
        val entries = linkedMapOf<String, CatalogEntry>()
        fun text(value: JSONObject?): String {
            if (value == null) return ""
            value.optString("simpleText").takeIf { it.isNotBlank() }?.let { return it }
            value.optString("content").takeIf { it.isNotBlank() }?.let { return it }
            return value.optJSONArray("runs")?.let { runs -> (0 until runs.length()).joinToString("") { runs.optJSONObject(it)?.optString("text").orEmpty() } }.orEmpty()
        }
        fun thumbArray(value: JSONArray?): String = value?.optJSONObject(value.length() - 1)?.optString("url").orEmpty()
        fun thumbObject(value: JSONObject?): String = thumbArray(value?.optJSONArray("thumbnails"))
        fun visit(value: Any?) {
            when (value) {
                is JSONObject -> {
                    val playlist = value.optJSONObject("playlistRenderer")
                    if (playlist != null) {
                        val id = playlist.optString("playlistId")
                        val title = text(playlist.optJSONObject("title"))
                        if (id.isNotBlank() && title.isNotBlank()) entries.putIfAbsent("playlist:$id", CatalogEntry(id, title, text(playlist.optJSONObject("shortBylineText")), thumbArray(playlist.optJSONArray("thumbnails")), "playlist"))
                    }
                    val artist = value.optJSONObject("artistRenderer") ?: value.optJSONObject("channelRenderer")
                    if (artist != null) {
                        val id = artist.optString("browseId").ifBlank { artist.optString("channelId") }
                        val title = text(artist.optJSONObject("title"))
                        val art = thumbObject(artist.optJSONObject("thumbnail")?.optJSONObject("musicThumbnailRenderer")?.optJSONObject("thumbnail"))
                            .ifBlank { thumbObject(artist.optJSONObject("thumbnail")) }.ifBlank { thumbArray(artist.optJSONArray("thumbnails")) }
                        if (id.isNotBlank() && title.isNotBlank()) entries.putIfAbsent("artist:$id", CatalogEntry(id, title, "Artista", art, "artist"))
                    }
                    val twoRow = value.optJSONObject("musicTwoRowItemRenderer")
                    if (twoRow != null) {
                        val nav = twoRow.optJSONObject("navigationEndpoint")?.optJSONObject("browseEndpoint")
                        val id = nav?.optString("browseId").orEmpty()
                        val title = text(twoRow.optJSONObject("title"))
                        val subtitle = text(twoRow.optJSONObject("subtitle"))
                        val art = thumbObject(twoRow.optJSONObject("thumbnailRenderer")?.optJSONObject("musicThumbnailRenderer")?.optJSONObject("thumbnail"))
                        val type = if (id.startsWith("VL") || id.startsWith("PL")) "playlist" else if (id.startsWith("UC") || id.startsWith("MPRE")) "artist" else ""
                        if (type.isNotBlank() && title.isNotBlank()) entries.putIfAbsent("$type:$id", CatalogEntry(id, title, subtitle, art, type))
                    }
                    val keys = value.keys()
                    while (keys.hasNext()) visit(value.opt(keys.next()))
                }
                is JSONArray -> for (i in 0 until value.length()) visit(value.opt(i))
            }
        }
        visit(JSONObject(response))
        return entries.values.take(12)
    }

    private fun searchYoutube(query: String, limit: Int): List<Track> {
        val payload = JSONObject().apply {
            put("context", JSONObject().apply {
                put("client", JSONObject().apply {
                    put("clientName", "WEB")
                    put("clientVersion", "2.20240101.00.00")
                    put("hl", "es")
                    put("gl", "US")
                })
            })
            put("query", query)
        }

        val response = Net.postJson("$YOUTUBE_API/search?prettyPrint=false", payload.toString())
        val json = JSONObject(response)
        val out = ArrayList<Track>()

        val primaryContents = json.optJSONObject("contents")
            ?.optJSONObject("twoColumnSearchResultsRenderer")
            ?.optJSONObject("primaryContents")
            ?.optJSONObject("sectionListRenderer")
            ?.optJSONArray("contents") ?: return emptyList()

        for (i in 0 until primaryContents.length()) {
            val section = primaryContents.optJSONObject(i)?.optJSONObject("itemSectionRenderer") ?: continue
            val items = section.optJSONArray("contents") ?: continue
            for (j in 0 until items.length()) {
                val item = items.optJSONObject(j) ?: continue
                val vr = item.optJSONObject("videoRenderer") ?: continue
                val videoId = vr.optString("videoId")
                if (videoId.isBlank()) continue

                val title = vr.optJSONObject("title")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text")
                    ?: vr.optJSONObject("title")?.optString("simpleText") ?: "Canción"
                val channel = vr.optJSONObject("ownerText")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text")
                    ?: vr.optJSONObject("shortBylineText")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text")
                    ?: "Artista"
                val official = channel.endsWith("- Topic", ignoreCase = true) ||
                    vr.optJSONArray("ownerBadges")?.toString()?.contains("VERIFIED", true) == true ||
                    vr.optJSONArray("badges")?.toString()?.contains("VERIFIED", true) == true
                val durationText = vr.optJSONObject("lengthText")?.optString("simpleText") ?: ""
                val durationMs = parseDuration(durationText)
                val thumb = vr.optJSONObject("thumbnail")?.optJSONArray("thumbnails")?.let { thumbs ->
                    thumbs.optJSONObject(thumbs.length() - 1)?.optString("url")
                }.orEmpty().ifBlank { thumbnailUrl(videoId) }

                out.add(
                    Track(
                        id = Track.VIDEO_PREFIX + videoId,
                        title = cleanTitle(title),
                        artist = cleanChannel(channel),
                        art = thumb,
                        duration = durationMs,
                        genre = "Música",
                        official = official
                    )
                )
            }
        }
        return out
    }

    private fun searchMusic(query: String, limit: Int): List<Track> {
        val payload = JSONObject().apply {
            put("context", JSONObject().apply {
                put("client", JSONObject().apply {
                    put("clientName", "WEB_REMIX")
                    put("clientVersion", "1.20240101.01.00")
                    put("hl", "es")
                    put("gl", "US")
                })
            })
            put("query", query)
        }

        val response = Net.postJson("$YT_MUSIC_API/search?prettyPrint=false", payload.toString())
        val json = JSONObject(response)
        val out = ArrayList<Track>()

        val tabs = json.optJSONObject("contents")
            ?.optJSONObject("tabbedSearchResultsRenderer")
            ?.optJSONArray("tabs") ?: return emptyList()

        val sections = tabs.optJSONObject(0)
            ?.optJSONObject("tabRenderer")
            ?.optJSONObject("content")
            ?.optJSONObject("sectionListRenderer")
            ?.optJSONArray("contents") ?: return emptyList()

        for (s in 0 until sections.length()) {
            val sec = sections.optJSONObject(s) ?: continue
            val shelf = sec.optJSONObject("musicCardShelfRenderer") ?: sec.optJSONObject("musicShelfRenderer") ?: continue
            val items = shelf.optJSONArray("contents") ?: continue

            for (i in 0 until items.length()) {
                val item = items.optJSONObject(i)?.optJSONObject("musicResponsiveListItemRenderer") ?: continue
                val flex = item.optJSONArray("flexColumns") ?: continue

                val title = flex.optJSONObject(0)
                    ?.optJSONObject("musicResponsiveListItemFlexColumnRenderer")
                    ?.optJSONObject("text")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text")
                    ?: continue

                val artist = flex.optJSONObject(1)
                    ?.optJSONObject("musicResponsiveListItemFlexColumnRenderer")
                    ?.optJSONObject("text")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text")
                    ?: "Artista"
                val official = artist.contains("- Topic", ignoreCase = true) ||
                    item.optJSONArray("badges")?.toString()?.contains("VERIFIED", true) == true

                val videoId = item.optJSONObject("playlistItemData")?.optString("videoId")
                    ?: flex.optJSONObject(0)
                        ?.optJSONObject("musicResponsiveListItemFlexColumnRenderer")
                        ?.optJSONObject("text")?.optJSONArray("runs")?.optJSONObject(0)
                        ?.optJSONObject("navigationEndpoint")?.optJSONObject("watchEndpoint")
                        ?.optString("videoId") ?: ""

                if (videoId.isBlank()) continue

                val thumb = item.optJSONObject("thumbnail")
                    ?.optJSONObject("musicThumbnailRenderer")
                    ?.optJSONObject("thumbnail")
                    ?.optJSONArray("thumbnails")
                    ?.let { it.optJSONObject(it.length() - 1)?.optString("url") }
                    ?: thumbnailUrl(videoId)

                out.add(
                    Track(
                        id = Track.VIDEO_PREFIX + videoId,
                        title = cleanTitle(title),
                        artist = cleanChannel(artist),
                        art = thumb,
                        duration = 0L,
                        genre = "Música",
                        official = official
                    )
                )
            }
        }
        return out
    }

    /**
     * Get top weekly trends and music hits.
     */
    fun trending(limit: Int = 40): List<Track> {
        val tracks = search("top exitos canciones de la semana", limit)
        if (tracks.isNotEmpty()) return tracks
        return search("tendencias musicales 2026", limit)
    }

    /**
     * Parse YouTube or YouTube Music playlist.
     */
    fun playlist(playlistId: String): Pair<String, List<Track>> {
        val cleanId = playlistId.removePrefix("VL").trim()
        val payload = JSONObject().apply {
            put("context", JSONObject().apply {
                put("client", JSONObject().apply {
                    put("clientName", "WEB")
                    put("clientVersion", "2.20240101.00.00")
                    put("hl", "es")
                    put("gl", "US")
                })
            })
            put("browseId", "VL$cleanId")
        }

        val response = Net.postJson("$YOUTUBE_API/browse?prettyPrint=false", payload.toString())
        val json = JSONObject(response)
        val out = ArrayList<Track>()
        var playlistTitle = "Playlist"

        val header = json.optJSONObject("header")
        if (header != null) {
            playlistTitle = header.optJSONObject("playlistHeaderRenderer")
                ?.optJSONObject("title")?.optString("simpleText")
                ?: header.optJSONObject("pageHeaderRenderer")
                    ?.optJSONObject("pageTitle")?.optString("content") ?: "Playlist"
        }

        val tabs = json.optJSONObject("contents")
            ?.optJSONObject("twoColumnBrowseResultsRenderer")
            ?.optJSONArray("tabs")

        val sectionList = tabs?.optJSONObject(0)
            ?.optJSONObject("tabRenderer")
            ?.optJSONObject("content")
            ?.optJSONObject("sectionListRenderer")
            ?.optJSONArray("contents")

        if (sectionList != null) {
            for (s in 0 until sectionList.length()) {
                val sec = sectionList.optJSONObject(s) ?: continue
                val itemSection = sec.optJSONObject("itemSectionRenderer") ?: continue
                val items = itemSection.optJSONArray("contents") ?: continue

                for (i in 0 until items.length()) {
                    val item = items.optJSONObject(i) ?: continue

                    // Check modern lockupViewModel
                    val vm = item.optJSONObject("lockupViewModel")
                    if (vm != null) {
                        val videoId = vm.optString("contentId")
                        val meta = vm.optJSONObject("metadata")?.optJSONObject("lockupMetadataViewModel")
                        val title = meta?.optJSONObject("title")?.optString("content") ?: ""
                        val artistRows = meta?.optJSONObject("metadata")?.optJSONObject("contentMetadataViewModel")?.optJSONArray("metadataRows")
                        val artist = artistRows?.optJSONObject(0)?.optJSONArray("metadataParts")?.optJSONObject(0)?.optJSONObject("text")?.optString("content") ?: "Artista"

                        if (videoId.isNotBlank() && title.isNotBlank()) {
                            out.add(
                                Track(
                                    id = Track.VIDEO_PREFIX + videoId,
                                    title = cleanTitle(title),
                                    artist = cleanChannel(artist),
                                    art = thumbnailUrl(videoId),
                                    duration = 0L,
                                    genre = "Playlist"
                                )
                            )
                        }
                        continue
                    }

                    // Check classic playlistVideoRenderer
                    val vr = item.optJSONObject("playlistVideoRenderer")
                    if (vr != null) {
                        val videoId = vr.optString("videoId")
                        val title = vr.optJSONObject("title")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text")
                            ?: vr.optJSONObject("title")?.optString("simpleText") ?: ""
                        val artist = vr.optJSONObject("shortBylineText")?.optJSONArray("runs")?.optJSONObject(0)?.optString("text") ?: "Artista"
                        val duration = vr.optJSONObject("lengthText")?.optString("simpleText") ?: ""

                        if (videoId.isNotBlank() && title.isNotBlank()) {
                            out.add(
                                Track(
                                    id = Track.VIDEO_PREFIX + videoId,
                                    title = cleanTitle(title),
                                    artist = cleanChannel(artist),
                                    art = thumbnailUrl(videoId),
                                    duration = parseDuration(duration),
                                    genre = "Playlist"
                                )
                            )
                        }
                    }
                }
            }
        }
        return playlistTitle to out
    }

    /**
     * Resolve playable audio stream URL for a given video ID.
     */
    fun streamUrl(
        videoId: String,
        excludedHosts: Set<String> = emptySet(),
        preferProgressive: Boolean = false
    ): String {
        val id = if (videoId.startsWith(Track.VIDEO_PREFIX)) videoId.removePrefix(Track.VIDEO_PREFIX) else videoId

        // Try Android and web music clients. The request UA must match the client
        // profile or the returned media URLs may be rejected by the CDN.
        val clients = listOf(
            Triple("ANDROID", "20.10.38", STREAM_USER_AGENT),
            Triple("WEB_REMIX", "1.20241001.01.00", STREAM_USER_AGENT),
            Triple("ANDROID_VR", "1.60.19", "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12; en_US; Quest 3 Build/SQ3A.220605.009.A1) gzip")
        )
        for ((clientName, clientVersion, userAgent) in clients) try {
            val client = JSONObject().apply {
                put("clientName", clientName)
                put("clientVersion", clientVersion)
                put("hl", "es")
                put("gl", "US")
                if (clientName == "ANDROID") put("androidSdkVersion", 35)
            }
            val payload = JSONObject().put("context", JSONObject().put("client", client)).put("videoId", id)
            val response = Net.postJson(
                "$YT_MUSIC_API/player?prettyPrint=false",
                payload.toString(),
                mapOf("Origin" to "https://music.youtube.com", "Referer" to "https://music.youtube.com/", "User-Agent" to userAgent),
                12000
            )
            val playerJson = JSONObject(response)
            val streamingData = playerJson.optJSONObject("streamingData")
            if (preferProgressive) {
                val progressive = streamingData?.optJSONArray("formats")
                var bestProgressiveUrl = ""
                var bestProgressiveBitrate = 0
                if (progressive != null) for (i in 0 until progressive.length()) {
                    val format = progressive.optJSONObject(i) ?: continue
                    val mime = format.optString("mimeType")
                    if (!mime.contains("video/mp4", true) || !mime.contains("mp4a", true)) continue
                    val url = playableFormatUrl(format)
                    val bitrate = format.optInt("bitrate")
                    if (url.isNotBlank() && bitrate >= bestProgressiveBitrate) {
                        bestProgressiveUrl = url
                        bestProgressiveBitrate = bitrate
                    }
                }
                if (bestProgressiveUrl.isNotBlank() && isStreamUrlReachable(bestProgressiveUrl, userAgent, excludedHosts)) {
                    return bestProgressiveUrl
                }
            }
            val adaptive = streamingData?.optJSONArray("adaptiveFormats")
            var bestAudioUrl = ""
            var highestBitrate = 0
            var bestIsMp4 = false
            if (adaptive != null) for (i in 0 until adaptive.length()) {
                val format = adaptive.optJSONObject(i) ?: continue
                val mime = format.optString("mimeType")
                if (!mime.contains("audio", true)) continue
                val url = playableFormatUrl(format)
                val bitrate = format.optInt("bitrate")
                val isMp4 = mime.contains("audio/mp4", true)
                if (url.isNotBlank() && ((isMp4 && !bestIsMp4) || (isMp4 == bestIsMp4 && bitrate >= highestBitrate))) {
                    bestAudioUrl = url
                    highestBitrate = bitrate
                    bestIsMp4 = isMp4
                }
            }
            if (bestAudioUrl.isNotBlank()) {
                if (isStreamUrlReachable(bestAudioUrl, userAgent, excludedHosts)) return bestAudioUrl
                android.util.Log.w("RitmoPlayback", "El CDN rechazó el flujo de $clientName para $id; probando otro origen")
            }

            // Some InnerTube clients expose only progressive MP4 formats. These include
            // both video and audio, but Media3 can still play their audio track.
            val progressive = streamingData?.optJSONArray("formats")
            if (progressive != null) for (i in 0 until progressive.length()) {
                val format = progressive.optJSONObject(i) ?: continue
                val mime = format.optString("mimeType")
                if (!mime.contains("mp4a", true)) continue
                val url = playableFormatUrl(format)
                val bitrate = format.optInt("bitrate")
                if (url.isNotBlank() && bitrate >= highestBitrate) {
                    bestAudioUrl = url
                    highestBitrate = bitrate
                }
            }
            if (bestAudioUrl.isNotBlank()) {
                if (isStreamUrlReachable(bestAudioUrl, userAgent, excludedHosts)) return bestAudioUrl
                android.util.Log.w("RitmoPlayback", "El CDN rechazó el flujo progresivo de $clientName para $id")
            }
            android.util.Log.w("RitmoPlayback", "InnerTube $clientName no entregó un formato reproducible para $id; status=${playerJson.optJSONObject("playabilityStatus")?.optString("status")}")
        } catch (error: Throwable) {
            android.util.Log.w("RitmoPlayback", "Falló resolución InnerTube $clientName para $id", error)
        }

        // Try Invidious streaming instances first
        for (instance in INVIDIOUS_INSTANCES) {
            try {
                val jsonStr = Net.get("$instance/api/v1/videos/$id", 7000)
                val json = JSONObject(jsonStr)
                val adaptive = json.optJSONArray("adaptiveFormats")
                if (adaptive != null) {
                    var bestAudioUrl = ""
                    var highestBitrate = 0
                    var bestIsMp4 = false
                    for (i in 0 until adaptive.length()) {
                        val f = adaptive.optJSONObject(i) ?: continue
                        val type = f.optString("type")
                        if (type.contains("audio", true)) {
                            val url = f.optString("url")
                            val bitrate = f.optInt("bitrate")
                            val isMp4 = type.contains("audio/mp4", true)
                            if (url.isNotBlank() && ((isMp4 && !bestIsMp4) || (isMp4 == bestIsMp4 && bitrate >= highestBitrate))) {
                                highestBitrate = bitrate
                                bestAudioUrl = url
                                bestIsMp4 = isMp4
                            }
                        }
                    }
                    if (bestAudioUrl.isNotBlank()) {
                        val url = if (bestAudioUrl.startsWith("http")) bestAudioUrl else "$instance$bestAudioUrl"
                        if (isStreamUrlReachable(url, excludedHosts = excludedHosts)) return url
                    }
                }
                // Fallback to progressive format if adaptive not found
                val formats = json.optJSONArray("formatStreams")
                if (formats != null && formats.length() > 0) {
                    val url = formats.optJSONObject(0)?.optString("url")
                    if (!url.isNullOrBlank()) {
                        val absoluteUrl = if (url.startsWith("http")) url else "$instance$url"
                        if (isStreamUrlReachable(absoluteUrl, excludedHosts = excludedHosts)) return absoluteUrl
                    }
                }
            } catch (_: Throwable) {
            }
        }

        // Try Piped instances next
        for (instance in PIPED_INSTANCES) {
            try {
                val jsonStr = Net.get("$instance/streams/$id", 7000)
                val json = JSONObject(jsonStr)
                val audioStreams = json.optJSONArray("audioStreams")
                if (audioStreams != null && audioStreams.length() > 0) {
                    val candidates = (0 until audioStreams.length()).mapNotNull { index ->
                        audioStreams.optJSONObject(index)?.let { stream ->
                            Triple(stream.optString("url"), stream.optString("mimeType"), stream.optInt("bitrate"))
                        }
                    }.filter { it.first.startsWith("https://") }
                        .sortedWith(compareByDescending<Triple<String, String, Int>> { it.second.contains("audio/mp4", true) }
                            .thenByDescending { it.third })
                    val url = candidates.firstOrNull()?.first
                    if (!url.isNullOrBlank() && isStreamUrlReachable(url, excludedHosts = excludedHosts)) return url
                }
            } catch (_: Throwable) {
            }
        }

        throw IOException("No se pudo obtener el audio para este tema")
    }

    /** Probe one byte before handing a provider URL to Media3. Several public
     * stream endpoints return stale URLs that fail with HTTP 403; without this
     * check Media3 retries the same dead URL and never reaches the next source.
     */
    private fun isStreamUrlReachable(
        url: String,
        userAgent: String = STREAM_USER_AGENT,
        excludedHosts: Set<String> = emptySet()
    ): Boolean {
        val host = Uri.parse(url).host.orEmpty()
        if (host.isBlank() || host in excludedHosts) return false
        var connection: HttpURLConnection? = null
        return try {
            connection = (URL(url).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = 7000
                readTimeout = 7000
                instanceFollowRedirects = true
                setRequestProperty("User-Agent", userAgent)
                setRequestProperty("Accept", "*/*")
                // Match the web proxy's bounded first audio request.
                setRequestProperty("Range", "bytes=0-1048575")
                setRequestProperty("Referer", "https://music.youtube.com/")
            }
            val code = connection.responseCode
            code in 200..299
        } catch (error: Throwable) {
            android.util.Log.w("RitmoPlayback", "No se pudo validar el origen del audio", error)
            false
        } finally {
            connection?.disconnect()
        }
    }

    private fun playableFormatUrl(format: JSONObject): String {
        format.optString("url").takeIf { it.startsWith("https://") }?.let { return it }
        val cipher = format.optString("signatureCipher").ifBlank { format.optString("cipher") }
        if (cipher.isBlank()) return ""
        val parts = Uri.parse("https://local.invalid/?$cipher")
        val url = parts.getQueryParameter("url") ?: return ""
        val signature = parts.getQueryParameter("sig") ?: parts.getQueryParameter("signature")
        if (signature.isNullOrBlank()) return ""
        val parameter = parts.getQueryParameter("sp").orEmpty().ifBlank { "signature" }
        return Uri.parse(url).buildUpon().appendQueryParameter(parameter, signature).build().toString()
    }

    fun thumbnailUrl(videoId: String): String =
        "https://i.ytimg.com/vi/$videoId/maxresdefault.jpg"

    fun extractVideoId(input: String): String? {
        val text = input.trim()
        if (text.isEmpty()) return null
        val direct = Regex("^[A-Za-z0-9_-]{11}$")
        if (direct.matches(text)) return text
        val patterns = arrayOf(
            Regex("[?&]v=([A-Za-z0-9_-]{11})"),
            Regex("youtu\\.be/([A-Za-z0-9_-]{11})"),
            Regex("/embed/([A-Za-z0-9_-]{11})"),
            Regex("/shorts/([A-Za-z0-9_-]{11})"),
            Regex("/live/([A-Za-z0-9_-]{11})")
        )
        for (pattern in patterns) {
            val found = pattern.find(text)?.groupValues?.getOrNull(1)
            if (!found.isNullOrBlank()) return found
        }
        return null
    }

    fun extractPlaylistId(input: String): String? {
        val text = input.trim()
        val match = Regex("[?&]list=([A-Za-z0-9_-]+)").find(text)
        if (match != null) return match.groupValues[1]
        if (text.startsWith("PL") || text.startsWith("RD") || text.startsWith("OL")) return text
        return null
    }

    private fun cleanTitle(title: String): String =
        title.replace(Regex("(?i)\\(official\\s*(video|audio|music\\s*video)\\)"), "")
            .replace(Regex("(?i)\\[official\\s*(video|audio|music\\s*video)\\]"), "")
            .replace(Regex("(?i)\\(video\\s*oficial\\)"), "")
            .replace(Regex("(?i)\\[video\\s*oficial\\]"), "")
            .replace(Regex("(?i)\\(audio\\s*oficial\\)"), "")
            .replace(Regex("(?i)\\(lyrics?\\)"), "")
            .replace(Regex("(?i)\\[lyrics?\\]"), "")
            .trim()

    private fun cleanChannel(name: String): String =
        name.removeSuffix(" - Topic").removeSuffix("VEVO").trim()

    fun parseDuration(text: String): Long {
        if (text.isBlank()) return 0L
        val parts = text.split(":")
        var total = 0L
        for (p in parts) {
            val num = p.trim().toLongOrNull() ?: 0L
            total = total * 60 + num
        }
        return total * 1000L
    }
}
