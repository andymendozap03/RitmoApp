import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.InetSocketAddress;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.net.URLDecoder;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.concurrent.Executors;

/**
 * Mini servidor para la versión web de Ritmo (PC).
 *
 * - Sirve esta carpeta (index.html, app.js, style.css).
 * - Hace de puente para búsqueda, letras y reproducción de audio.
 *
 * Uso:  java Servidor.java 8101
 */
public class Servidor {

    private static final String YOUTUBE_MUSIC = "https://music.youtube.com";
    private static final String SPOTIFY = "https://open.spotify.com";
    private static final String LRCLIB = "https://lrclib.net";
    private static final String LETRAS = "https://www.letras.com";
    private static final String LYRICS_OVH = "https://api.lyrics.ovh";
    private static Path root;

    public static void main(String[] args) throws Exception {
        int port = args.length > 0 ? Integer.parseInt(args[0]) : 8101;
        root = Paths.get("").toAbsolutePath().normalize();

        HttpServer server = null;
        int chosen = port;
        for (int attempt = 0; attempt < 12 && server == null; attempt++) {
            try {
                server = HttpServer.create(new InetSocketAddress("127.0.0.1", port + attempt), 0);
                chosen = port + attempt;
            } catch (java.net.BindException busy) {
                // Puerto ocupado: prueba el siguiente.
            }
        }
        if (server == null) {
            System.out.println("No encontre un puerto libre entre " + port + " y " + (port + 11));
            return;
        }

        server.createContext("/yt/", exchange -> proxy(exchange, YOUTUBE_MUSIC, "/yt"));
        server.createContext("/spotify/", exchange -> proxy(exchange, SPOTIFY, "/spotify"));
        server.createContext("/lyrics/", exchange -> proxy(exchange, LRCLIB, "/lyrics"));
        server.createContext("/letras/", exchange -> proxy(exchange, LETRAS, "/letras"));
        server.createContext("/lyrics-ovh/", exchange -> proxy(exchange, LYRICS_OVH, "/lyrics-ovh"));
        server.createContext("/stream", Servidor::stream);
        server.createContext("/", Servidor::staticFile);
        server.setExecutor(Executors.newFixedThreadPool(6));
        server.start();

        System.out.println();
        System.out.println("  Ritmo web listo:   http://localhost:" + chosen);
        System.out.println("  Carpeta servida:   " + root);
        System.out.println("  Ctrl+C para detener el servidor.");
        System.out.println();
    }

    private static void proxy(HttpExchange exchange, String origin, String prefix) throws java.io.IOException {
        String path = exchange.getRequestURI().getRawPath();
        String query = exchange.getRequestURI().getRawQuery();
        String suffix = path.length() > prefix.length() ? path.substring(prefix.length()) : "/";
        String target = origin + suffix + (query == null ? "" : "?" + query);

        HttpURLConnection connection = null;
        try {
            connection = (HttpURLConnection) new URL(target).openConnection();
            connection.setInstanceFollowRedirects(true);
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(60000);
            connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128 Safari/537.36");
            connection.setRequestProperty("Accept", "application/json,text/html,*/*");
            connection.setRequestProperty("Origin", origin);
            connection.setRequestProperty("Referer", origin + "/");
            if ("POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                connection.setRequestMethod("POST");
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", exchange.getRequestHeaders().getFirst("Content-Type") == null
                    ? "application/json; charset=UTF-8" : exchange.getRequestHeaders().getFirst("Content-Type"));
                try (InputStream request = exchange.getRequestBody(); OutputStream body = connection.getOutputStream()) {
                    request.transferTo(body);
                }
            }

            int code = connection.getResponseCode();
            String type = connection.getContentType();
            if (type != null) exchange.getResponseHeaders().set("Content-Type", type);
            exchange.getResponseHeaders().set("Access-Control-Allow-Origin", "*");
            exchange.getResponseHeaders().set("Cache-Control", "no-cache");

            if ("HEAD".equalsIgnoreCase(exchange.getRequestMethod())) {
                exchange.sendResponseHeaders(code, -1);
                return;
            }

            long length = connection.getContentLengthLong();
            exchange.sendResponseHeaders(code, length > 0 ? length : 0);

            InputStream source = code >= 400 ? connection.getErrorStream() : connection.getInputStream();
            if (source == null) source = InputStream.nullInputStream();
            try (InputStream input = source;
                 OutputStream output = exchange.getResponseBody()) {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = input.read(buffer)) >= 0) {
                    output.write(buffer, 0, read);
                }
            }
        } catch (Exception error) {
            byte[] message = ("No se pudo contactar con el servicio: " + error.getMessage())
                .getBytes(StandardCharsets.UTF_8);
            try {
                exchange.sendResponseHeaders(502, message.length);
                try (OutputStream output = exchange.getResponseBody()) {
                    output.write(message);
                }
            } catch (Exception ignored) {
            }
        } finally {
            if (connection != null) connection.disconnect();
        }
    }

    private static void stream(HttpExchange exchange) throws java.io.IOException {
        String query = exchange.getRequestURI().getRawQuery();
        String id = "";
        if (query != null) {
            for (String pair : query.split("&")) {
                String[] parts = pair.split("=", 2);
                if (parts.length == 2 && parts[0].equals("id")) id = URLDecoder.decode(parts[1], StandardCharsets.UTF_8);
            }
        }
        if (!id.matches("[A-Za-z0-9_-]{11}")) {
            byte[] error = "ID de canción no válido".getBytes(StandardCharsets.UTF_8);
            exchange.sendResponseHeaders(400, error.length);
            try (OutputStream output = exchange.getResponseBody()) { output.write(error); }
            return;
        }

        String audioUrl;
        String mime = "audio/mp4";
        boolean progressive = query != null && query.contains("variant=progressive");
        try {
            String playerPayload = "{\"context\":{\"client\":{\"clientName\":\"ANDROID\",\"clientVersion\":\"20.10.38\",\"androidSdkVersion\":35,\"hl\":\"es\",\"gl\":\"US\"}},\"videoId\":\"" + id + "\"}";
            try {
                String playerJson = requestPostText(YOUTUBE_MUSIC + "/youtubei/v1/player?prettyPrint=false", playerPayload);
                audioUrl = findAudioUrl(playerJson, progressive ? "formats" : "adaptiveFormats");
                if (progressive && audioUrl != null) mime = "video/mp4; codecs=\"avc1.42001E, mp4a.40.2\"";
            } catch (Exception ignored) { audioUrl = null; }
            String[] invidious = {
                "https://yt.artemislena.eu", "https://invidious.nerdvpn.de",
                "https://inv.nadeko.net", "https://invidious.drgns.space"
            };
            for (String host : invidious) {
                if (audioUrl != null) break;
                try {
                    String json = requestText(host + "/api/v1/videos/" + id);
                    audioUrl = findAudioUrl(json, "adaptiveFormats");
                    if (audioUrl != null) break;
                } catch (Exception ignored) { }
            }
            if (audioUrl == null) {
                String[] piped = {
                    "https://pipedapi.kavin.rocks", "https://api.piped.privacy.com.de",
                    "https://piped-api.garudalinux.org"
                };
                for (String host : piped) {
                    try {
                        audioUrl = findAudioUrl(requestText(host + "/streams/" + id), "audioStreams");
                        if (audioUrl != null) break;
                    } catch (Exception ignored) { }
                }
            }
            if (audioUrl == null) throw new java.io.IOException("No hay un flujo de audio disponible");
        } catch (Exception error) {
            byte[] body = ("{\"error\":\"" + jsonEscape(error.getMessage()) + "\"}").getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
            exchange.sendResponseHeaders(502, body.length);
            try (OutputStream output = exchange.getResponseBody()) { output.write(body); }
            return;
        }

        boolean download = query != null && query.contains("download=1");
        boolean play = query != null && query.contains("play=1");
        if (!download && !play) {
            String body = "{\"url\":\"" + jsonEscape(audioUrl) + "\",\"mime\":\"" + mime + "\"}";
            byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
            exchange.getResponseHeaders().set("Cache-Control", "no-store");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream output = exchange.getResponseBody()) { output.write(bytes); }
            return;
        }

        String requestedRange = exchange.getRequestHeaders().getFirst("Range");
        if (requestedRange == null) {
            proxyChunkedAudio(exchange, audioUrl, mime, download);
            return;
        }

        HttpURLConnection audio = null;
        try {
            audio = (HttpURLConnection) new URL(audioUrl).openConnection();
            audio.setConnectTimeout(15000);
            audio.setReadTimeout(60000);
            audio.setInstanceFollowRedirects(true);
            audio.setRequestProperty("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128 Safari/537.36");
            audio.setRequestProperty("Range", requestedRange);
            int code = audio.getResponseCode();
            if (code != 206 && code != 200) throw new java.io.IOException("HTTP " + code);
            exchange.getResponseHeaders().set("Content-Type", audio.getContentType() == null ? mime : audio.getContentType());
            if (download) exchange.getResponseHeaders().set("Content-Disposition", "attachment; filename=ritmo-audio" + (mime.startsWith("video/mp4") ? ".mp4" : ".m4a"));
            exchange.getResponseHeaders().set("Accept-Ranges", "bytes");
            if (audio.getHeaderField("Content-Range") != null) exchange.getResponseHeaders().set("Content-Range", audio.getHeaderField("Content-Range"));
            long length = audio.getContentLengthLong();
            exchange.sendResponseHeaders(code, length > 0 ? length : 0);
            try (InputStream input = audio.getInputStream(); OutputStream output = exchange.getResponseBody()) {
                input.transferTo(output);
            }
        } finally {
            if (audio != null) audio.disconnect();
        }
    }

    private static void proxyChunkedAudio(HttpExchange exchange, String audioUrl, String mime, boolean download) throws java.io.IOException {
        final int chunkSize = 1024 * 1024;
        HttpURLConnection connection = openAudioRange(audioUrl, 0, chunkSize - 1);
        try {
            int status = connection.getResponseCode();
            if (status != 206 && status != 200) throw new java.io.IOException("HTTP " + status);
            long total = -1;
            String contentRange = connection.getHeaderField("Content-Range");
            if (contentRange != null) {
                Matcher match = Pattern.compile("/(\\d+)$").matcher(contentRange);
                if (match.find()) total = Long.parseLong(match.group(1));
            }
            if (total <= 0) total = contentLengthFromUrl(audioUrl);
            if (total <= 0) throw new java.io.IOException("No se pudo determinar el tamaño del audio");

            exchange.getResponseHeaders().set("Content-Type", mime);
            exchange.getResponseHeaders().set("Content-Length", Long.toString(total));
            exchange.getResponseHeaders().set("Accept-Ranges", "bytes");
            exchange.getResponseHeaders().set("Cache-Control", "no-store");
            if (download) exchange.getResponseHeaders().set("Content-Disposition", "attachment; filename=ritmo-audio" + (mime.startsWith("video/mp4") ? ".mp4" : ".m4a"));
            exchange.sendResponseHeaders(200, total);
            try (OutputStream output = exchange.getResponseBody()) {
                long start = 0;
                HttpURLConnection current = connection;
                while (start < total) {
                    if (start > 0) {
                        current = openAudioRange(audioUrl, start, Math.min(start + chunkSize - 1, total - 1));
                        int code = current.getResponseCode();
                        if (code != 206) throw new java.io.IOException("HTTP " + code);
                    }
                    try (InputStream input = current.getInputStream()) { input.transferTo(output); }
                    if (current != connection) current.disconnect();
                    start += chunkSize;
                }
            }
        } finally {
            connection.disconnect();
        }
    }

    private static HttpURLConnection openAudioRange(String audioUrl, long start, long end) throws java.io.IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(audioUrl).openConnection();
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(60000);
        connection.setInstanceFollowRedirects(true);
        connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128 Safari/537.36");
        connection.setRequestProperty("Range", "bytes=" + start + "-" + end);
        return connection;
    }

    private static long contentLengthFromUrl(String audioUrl) {
        try {
            String query = new URL(audioUrl).getQuery();
            if (query == null) return -1;
            for (String part : query.split("&")) {
                String[] pair = part.split("=", 2);
                if (pair.length == 2 && pair[0].equals("clen")) return Long.parseLong(pair[1]);
            }
        } catch (Exception ignored) { }
        return -1;
    }

    private static String requestText(String target) throws java.io.IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(target).openConnection();
        try {
            connection.setConnectTimeout(8000);
            connection.setReadTimeout(10000);
            connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/128 Safari/537.36");
            connection.setRequestProperty("Accept", "application/json");
            int code = connection.getResponseCode();
            if (code < 200 || code >= 300) throw new java.io.IOException("HTTP " + code);
            try (InputStream input = connection.getInputStream()) {
                return new String(input.readAllBytes(), StandardCharsets.UTF_8);
            }
        } finally { connection.disconnect(); }
    }

    private static String requestPostText(String target, String body) throws java.io.IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(target).openConnection();
        try {
            connection.setConnectTimeout(10000);
            connection.setReadTimeout(15000);
            connection.setRequestMethod("POST");
            connection.setDoOutput(true);
            connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Chrome/128 Mobile Safari/537.36");
            connection.setRequestProperty("Content-Type", "application/json");
            connection.setRequestProperty("Origin", YOUTUBE_MUSIC);
            connection.setRequestProperty("Referer", YOUTUBE_MUSIC + "/");
            try (OutputStream output = connection.getOutputStream()) { output.write(body.getBytes(StandardCharsets.UTF_8)); }
            int code = connection.getResponseCode();
            if (code < 200 || code >= 300) throw new java.io.IOException("HTTP " + code);
            try (InputStream input = connection.getInputStream()) {
                return new String(input.readAllBytes(), StandardCharsets.UTF_8);
            }
        } finally { connection.disconnect(); }
    }

    private static String findAudioUrl(String json, String arrayName) {
        Matcher array = Pattern.compile("\\\"" + arrayName + "\\\"\\s*:\\s*\\[(.*?)\\]", Pattern.DOTALL).matcher(json);
        if (!array.find()) return null;
        Matcher objects = Pattern.compile("\\{(.*?)\\}", Pattern.DOTALL).matcher(array.group(1));
        String selected = null;
        int bestScore = -1;
        while (objects.find()) {
            String object = objects.group(1);
            Matcher type = Pattern.compile("\\\"(?:type|mimeType)\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"").matcher(object);
            if (arrayName.equals("adaptiveFormats") && (!type.find() || !type.group(1).toLowerCase().contains("audio"))) continue;
            Matcher url = Pattern.compile("\\\"url\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"").matcher(object);
            if (!url.find()) continue;
            Matcher bitrate = Pattern.compile("\\\"bitrate\\\"\\s*:\\s*(\\d+)").matcher(object);
            int rate = bitrate.find() ? Integer.parseInt(bitrate.group(1)) : 0;
            int score = (type.reset().find() && type.group(1).toLowerCase().contains("audio/mp4") ? 1_000_000 : 0) + rate;
            if (score >= bestScore) { selected = url.group(1); bestScore = score; }
        }
        return selected == null ? null : selected.replace("\\u0026", "&").replace("\\u003d", "=").replace("\\/", "/");
    }

    private static String jsonEscape(String value) {
        if (value == null) return "error";
        return value.replace("\\", "\\\\").replace("\"", "\\\"").replace("\r", " ").replace("\n", " ");
    }

    private static void staticFile(HttpExchange exchange) throws java.io.IOException {
        String raw = exchange.getRequestURI().getPath();
        if (raw == null || raw.isEmpty() || raw.equals("/")) raw = "/index.html";
        if (raw.equals("/favicon.ico")) {
            exchange.sendResponseHeaders(204, -1);
            return;
        }

        Path file = root.resolve(raw.substring(1)).normalize();
        if (!file.startsWith(root) || !Files.isRegularFile(file)) {
            byte[] body = "No encontrado".getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "text/plain; charset=utf-8");
            exchange.sendResponseHeaders(404, body.length);
            try (OutputStream output = exchange.getResponseBody()) {
                output.write(body);
            }
            return;
        }

        byte[] data = Files.readAllBytes(file);
        exchange.getResponseHeaders().set("Content-Type", contentType(file.getFileName().toString()));
        exchange.getResponseHeaders().set("Cache-Control", "no-cache");
        exchange.sendResponseHeaders(200, data.length);
        try (OutputStream output = exchange.getResponseBody()) {
            output.write(data);
        }
    }

    private static String contentType(String name) {
        String lower = name.toLowerCase();
        if (lower.endsWith(".html")) return "text/html; charset=utf-8";
        if (lower.endsWith(".css")) return "text/css; charset=utf-8";
        if (lower.endsWith(".js")) return "application/javascript; charset=utf-8";
        if (lower.endsWith(".json")) return "application/json; charset=utf-8";
        if (lower.endsWith(".svg")) return "image/svg+xml";
        if (lower.endsWith(".png")) return "image/png";
        if (lower.endsWith(".jpg") || lower.endsWith(".jpeg")) return "image/jpeg";
        if (lower.endsWith(".ico")) return "image/x-icon";
        if (lower.endsWith(".mp3")) return "audio/mpeg";
        return "application/octet-stream";
    }
}
