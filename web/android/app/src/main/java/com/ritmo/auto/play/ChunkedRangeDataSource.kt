package com.ritmo.auto.play

import android.net.Uri
import androidx.media3.common.C
import androidx.media3.datasource.BaseDataSource
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import java.io.EOFException
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import kotlin.math.min

/** Reads audio in bounded byte ranges like the web /stream proxy. */
class ChunkedRangeDataSource(private val userAgent: String) : BaseDataSource(true) {
    class Factory(private val userAgent: String) : DataSource.Factory {
        override fun createDataSource(): DataSource = ChunkedRangeDataSource(userAgent)
    }

    private var spec: DataSpec? = null
    private var uri: Uri? = null
    private var connection: HttpURLConnection? = null
    private var input: java.io.InputStream? = null
    private var position = 0L
    private var remaining = C.LENGTH_UNSET.toLong()
    private var totalLength = C.LENGTH_UNSET.toLong()
    private var chunkRemaining = 0L
    private var wholeResponse = false
    private var opened = false

    override fun open(dataSpec: DataSpec): Long {
        spec = dataSpec
        uri = dataSpec.uri
        position = dataSpec.position
        remaining = dataSpec.length
        totalLength = C.LENGTH_UNSET.toLong()
        chunkRemaining = 0L
        wholeResponse = false
        transferInitializing(dataSpec)
        openChunk(position)
        opened = true
        transferStarted(dataSpec)
        return if (remaining != C.LENGTH_UNSET.toLong()) remaining
        else if (totalLength != C.LENGTH_UNSET.toLong()) (totalLength - position).coerceAtLeast(0L)
        else C.LENGTH_UNSET.toLong()
    }

    override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
        if (length == 0) return 0
        if (remaining == 0L) return C.RESULT_END_OF_INPUT
        if (chunkRemaining == 0L) {
            if (wholeResponse || (totalLength != C.LENGTH_UNSET.toLong() && position >= totalLength)) return C.RESULT_END_OF_INPUT
            if (!openChunk(position)) return C.RESULT_END_OF_INPUT
        }

        val available = if (chunkRemaining < 0L) length.toLong() else min(length.toLong(), chunkRemaining)
        val limited = if (remaining == C.LENGTH_UNSET.toLong()) available else min(available, remaining)
        if (limited <= 0L) return C.RESULT_END_OF_INPUT
        val count = input?.read(buffer, offset, limited.toInt()) ?: C.RESULT_END_OF_INPUT
        if (count == C.RESULT_END_OF_INPUT) {
            if (chunkRemaining > 0L) throw EOFException("El CDN cortó un bloque de audio antes de completarlo")
            chunkRemaining = 0L
            return if (wholeResponse) C.RESULT_END_OF_INPUT else read(buffer, offset, length)
        }
        position += count
        if (chunkRemaining >= 0L) chunkRemaining -= count
        if (remaining != C.LENGTH_UNSET.toLong()) remaining -= count
        bytesTransferred(count)
        return count
    }

    private fun openChunk(start: Long): Boolean {
        closeConnection()
        val request = spec ?: throw IOException("No hay una solicitud de audio activa")
        val requestedEnd = min(start + CHUNK_SIZE - 1L,
            if (totalLength != C.LENGTH_UNSET.toLong()) totalLength - 1L else Long.MAX_VALUE)
        if (requestedEnd < start) return false
        val next = (URL(request.uri.toString()).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15000
            readTimeout = 30000
            instanceFollowRedirects = true
            requestMethod = "GET"
            useCaches = false
            setRequestProperty("User-Agent", userAgent)
            setRequestProperty("Accept", "*/*")
            setRequestProperty("Accept-Encoding", "identity")
            setRequestProperty("Origin", "https://music.youtube.com")
            setRequestProperty("Referer", "https://music.youtube.com/")
            setRequestProperty("Range", "bytes=$start-$requestedEnd")
            request.httpRequestHeaders.forEach { (key, value) ->
                if (!key.equals("Range", ignoreCase = true)) setRequestProperty(key, value)
            }
        }
        try {
            val code = next.responseCode
            if (code == HttpURLConnection.HTTP_FORBIDDEN) {
                next.disconnect()
                android.util.Log.w(
                    "RitmoPlayback",
                    "El CDN rechazó Range en byte $start; reabriendo el flujo completo y continuando desde esa posición"
                )
                return openUnrangedFromStart(request, start)
            }
            if (code == 416 && start > 0L) {
                next.disconnect()
                return false
            }
            if (code !in 200..299) throw IOException("HTTP $code al solicitar audio")
            val responseInput = next.inputStream
            if (code == HttpURLConnection.HTTP_PARTIAL) {
                val range = CONTENT_RANGE.matchEntire(next.getHeaderField("Content-Range").orEmpty())
                    ?: throw IOException("El CDN respondió 206 sin Content-Range válido")
                val responseStart = range.groupValues[1].toLong()
                if (responseStart != start) throw IOException("El CDN devolvió un rango de audio incorrecto")
                totalLength = range.groupValues[3].toLongOrNull() ?: C.LENGTH_UNSET.toLong()
                chunkRemaining = next.contentLengthLong.takeIf { it >= 0L }
                    ?: (range.groupValues[2].toLong() - responseStart + 1L)
            } else {
                val responseLength = next.contentLengthLong.takeIf { it >= 0L } ?: C.LENGTH_UNSET.toLong()
                val advertisedLength = contentLengthFromUrl(request.uri)
                totalLength = advertisedLength.takeIf { it > 0L }
                    ?: responseLength.takeIf { it > 0L }
                    ?: C.LENGTH_UNSET.toLong()
                // Some CDNs answer 200 to a range request with only a bounded
                // first block. Do not treat that block as end-of-file when the
                // signed URL advertises a larger complete resource.
                wholeResponse = totalLength == C.LENGTH_UNSET.toLong() ||
                    (responseLength > 0L && responseLength >= totalLength)
                if (start > 0L) {
                    var skipped = 0L
                    val discard = ByteArray(32 * 1024)
                    while (skipped < start) {
                        val count = responseInput.read(discard, 0, min(discard.size.toLong(), start - skipped).toInt())
                        if (count < 0) throw EOFException("Stream ended before requested offset")
                        skipped += count
                    }
                    chunkRemaining = responseLength.takeIf { it > 0L }
                        ?.minus(start)?.coerceAtLeast(0L) ?: -1L
                } else {
                    chunkRemaining = responseLength.takeIf { it > 0L } ?: -1L
                }
            }
            connection = next
            input = responseInput
            uri = Uri.parse(next.url.toString())
            return true
        } catch (error: Throwable) {
            next.disconnect()
            if (error is IOException) throw error
            throw IOException("No se pudo abrir el bloque de audio", error)
        }
    }

    /**
     * Some signed audio URLs allow a normal GET but reject later byte ranges
     * with HTTP 403. Reopen the same fresh URL without Range, discard bytes up
     * to the requested offset, and continue from there instead of ending the
     * song at the CDN's first permitted block.
     */
    private fun openUnrangedFromStart(request: DataSpec, start: Long): Boolean {
        val next = (URL(request.uri.toString()).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15000
            readTimeout = 60000
            instanceFollowRedirects = true
            requestMethod = "GET"
            useCaches = false
            setRequestProperty("User-Agent", userAgent)
            setRequestProperty("Accept", "*/*")
            setRequestProperty("Accept-Encoding", "identity")
            setRequestProperty("Origin", "https://music.youtube.com")
            setRequestProperty("Referer", "https://music.youtube.com/")
            request.httpRequestHeaders.forEach { (key, value) ->
                if (!key.equals("Range", ignoreCase = true)) setRequestProperty(key, value)
            }
        }
        try {
            val code = next.responseCode
            if (code !in 200..299) throw IOException("HTTP $code al reabrir el flujo sin rangos")
            val responseInput = next.inputStream
            val responseLength = next.contentLengthLong.takeIf { it > 0L } ?: C.LENGTH_UNSET.toLong()
            val advertisedLength = contentLengthFromUrl(request.uri)
            totalLength = advertisedLength.takeIf { it > 0L }
                ?: responseLength
            var skipped = 0L
            val discard = ByteArray(64 * 1024)
            while (skipped < start) {
                val count = responseInput.read(discard, 0, min(discard.size.toLong(), start - skipped).toInt())
                if (count < 0) throw EOFException("El flujo terminó antes de la posición solicitada")
                skipped += count
            }
            wholeResponse = responseLength <= 0L || totalLength == C.LENGTH_UNSET.toLong() ||
                responseLength >= totalLength
            chunkRemaining = responseLength.takeIf { it > 0L }
                ?.minus(start)?.coerceAtLeast(0L)
                ?: -1L
            connection = next
            input = responseInput
            uri = Uri.parse(next.url.toString())
            return true
        } catch (error: Throwable) {
            next.disconnect()
            if (error is IOException) throw error
            throw IOException("No se pudo reabrir el flujo completo", error)
        }
    }

    private fun closeConnection() {
        try { input?.close() } catch (_: Throwable) { }
        input = null
        connection?.disconnect()
        connection = null
    }

    override fun getUri(): Uri? = uri

    override fun getResponseHeaders(): Map<String, List<String>> =
        connection?.headerFields?.filterKeys { it != null }?.mapKeys { it.key!! } ?: emptyMap()

    override fun close() {
        closeConnection()
        if (opened) {
            opened = false
            transferEnded()
        }
        spec = null
        uri = null
        chunkRemaining = 0L
    }

    private fun contentLengthFromUrl(uri: Uri): Long =
        uri.getQueryParameter("clen")?.toLongOrNull() ?: C.LENGTH_UNSET.toLong()

    companion object {
        private const val CHUNK_SIZE = 1024 * 1024
        private val CONTENT_RANGE = Regex("bytes (\\d+)-(\\d+)/(\\d+)", RegexOption.IGNORE_CASE)
    }
}
