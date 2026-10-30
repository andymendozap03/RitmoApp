import { defineConfig } from 'vite';
import { Readable } from 'node:stream';

const invidious = [
  'https://yt.artemislena.eu',
  'https://invidious.nerdvpn.de',
  'https://inv.nadeko.net',
  'https://invidious.drgns.space'
];
const piped = [
  'https://pipedapi.kavin.rocks',
  'https://api.piped.privacy.com.de',
  'https://piped-api.garudalinux.org'
];

function musicStreamProxy() {
  const cache = new Map();
  async function resolve(id, variant = 'audio') {
    const cacheKey = `${id}:${variant}`;
    const cached = cache.get(cacheKey);
    if (cached && cached.expires > Date.now()) return cached.value;
    try {
      const response = await fetch('https://music.youtube.com/youtubei/v1/player?prettyPrint=false', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          context: { client: { clientName: 'ANDROID', clientVersion: '20.10.38', androidSdkVersion: 35, hl: 'es', gl: 'US' } },
          videoId: id
        }),
        signal: AbortSignal.timeout(10000)
      });
      if (response.ok) {
        const data = await response.json();
        if (variant === 'progressive') {
          const compatible = (data.streamingData?.formats || []).filter((f) => /video\/mp4/i.test(f.mimeType || '') && /mp4a/i.test(f.mimeType || '') && f.url);
          compatible.sort((a, b) => (b.bitrate || 0) - (a.bitrate || 0));
          if (compatible.length) {
            const value = { url: compatible[0].url, mime: compatible[0].mimeType };
            cache.set(cacheKey, { value, expires: Date.now() + 20 * 60 * 1000 });
            return value;
          }
        }
        const formats = (data.streamingData?.adaptiveFormats || []).filter((f) => /audio/i.test(f.mimeType || '') && f.url);
        // Prefer AAC in MP4 for wider browser and embedded WebView support.
        formats.sort((a, b) => {
          const aScore = (String(a.mimeType).includes('audio/mp4') ? 1_000_000 : 0) + (a.bitrate || 0);
          const bScore = (String(b.mimeType).includes('audio/mp4') ? 1_000_000 : 0) + (b.bitrate || 0);
          return bScore - aScore;
        });
        if (formats.length) {
          const value = { url: formats[0].url, mime: formats[0].mimeType || 'audio/mp4' };
          cache.set(cacheKey, { value, expires: Date.now() + 20 * 60 * 1000 });
          return value;
        }
      }
    } catch { /* Fall back to public stream resolvers. */ }
    for (const host of invidious) {
      try {
        const response = await fetch(`${host}/api/v1/videos/${id}`, { signal: AbortSignal.timeout(8000) });
        if (!response.ok) continue;
        const data = await response.json();
        const formats = (data.adaptiveFormats || []).filter((f) => /audio/i.test(f.type || '') && f.url);
        formats.sort((a, b) => (b.bitrate || 0) - (a.bitrate || 0));
        if (formats.length) {
          const value = { url: formats[0].url, mime: formats[0].type || 'audio/mp4' };
          cache.set(cacheKey, { value, expires: Date.now() + 20 * 60 * 1000 });
          return value;
        }
      } catch { /* Try another public resolver. */ }
    }
    for (const host of piped) {
      try {
        const response = await fetch(`${host}/streams/${id}`, { signal: AbortSignal.timeout(8000) });
        if (!response.ok) continue;
        const data = await response.json();
        const streams = data.audioStreams || [];
        streams.sort((a, b) => {
          const aScore = (String(a.mimeType).includes('audio/mp4') ? 1_000_000 : 0) + (a.bitrate || 0);
          const bScore = (String(b.mimeType).includes('audio/mp4') ? 1_000_000 : 0) + (b.bitrate || 0);
          return bScore - aScore;
        });
        if (streams[0]?.url) {
          const value = { url: streams[0].url, mime: streams[0].mimeType || 'audio/webm' };
          cache.set(cacheKey, { value, expires: Date.now() + 20 * 60 * 1000 });
          return value;
        }
      } catch { /* Try another public resolver. */ }
    }
    throw new Error('No se pudo resolver el audio');
  }

  async function handle(req, res, next) {
    const requestUrl = new URL(req.url || '/', 'http://localhost');
    if (requestUrl.pathname !== '/stream') return next();
    const id = requestUrl.searchParams.get('id') || '';
    if (!/^[A-Za-z0-9_-]{11}$/.test(id)) {
      res.statusCode = 400;
      res.end('ID de canción no válido');
      return;
    }
    try {
      const stream = await resolve(id, requestUrl.searchParams.get('variant') || 'audio');
      if (requestUrl.searchParams.get('download') === '1' || requestUrl.searchParams.get('play') === '1') {
        const hasClientRange = Boolean(req.headers.range);
        if (!hasClientRange) {
          const chunkSize = 1024 * 1024;
          const first = await fetch(stream.url, {
            headers: { Range: `bytes=0-${chunkSize - 1}` },
            signal: AbortSignal.timeout(30000)
          });
          if (!first.ok && first.status !== 206) throw new Error('HTTP ' + first.status);
          const contentRange = first.headers.get('content-range') || '';
          const total = Number(/\/(\d+)$/.exec(contentRange)?.[1] || new URL(stream.url).searchParams.get('clen'));
          if (!Number.isFinite(total) || total <= 0) throw new Error('No se pudo determinar el tamaño del audio');
          res.statusCode = 200;
          res.setHeader('Content-Type', stream.mime || 'audio/mp4');
          res.setHeader('Content-Length', total);
          res.setHeader('Accept-Ranges', 'bytes');
          res.setHeader('Cache-Control', 'no-store');

          async function* chunks() {
            for (let start = 0; start < total; start += chunkSize) {
              const end = Math.min(start + chunkSize - 1, total - 1);
              const response = start === 0 ? first : await fetch(stream.url, {
                headers: { Range: `bytes=${start}-${end}` },
                signal: AbortSignal.timeout(30000)
              });
              if (!response.ok && response.status !== 206) throw new Error('HTTP ' + response.status);
              if (response.body) {
                for await (const part of Readable.fromWeb(response.body)) yield part;
              }
            }
          }
          const body = Readable.from(chunks());
          body.on('error', (error) => res.destroy(error));
          body.pipe(res);
          return;
        }
        const upstream = await fetch(stream.url, {
          headers: { Range: req.headers.range },
          signal: AbortSignal.timeout(30000)
        });
        if (!upstream.ok && upstream.status !== 206) throw new Error('HTTP ' + upstream.status);
        res.statusCode = upstream.status;
        // Keep the codec declaration from the player response; some embedded browsers
        // reject fragmented audio when the response only says "audio/mp4".
        res.setHeader('Content-Type', stream.mime || upstream.headers.get('content-type') || 'audio/mp4');
        const length = upstream.headers.get('content-length');
        if (length) res.setHeader('Content-Length', length);
        const range = upstream.headers.get('content-range');
        if (hasClientRange && range) res.setHeader('Content-Range', range);
        res.setHeader('Accept-Ranges', upstream.headers.get('accept-ranges') || 'bytes');
        res.setHeader('Cache-Control', 'no-store');
        if (upstream.body) Readable.fromWeb(upstream.body).pipe(res);
      } else {
        res.setHeader('Content-Type', 'application/json; charset=utf-8');
        res.end(JSON.stringify(stream));
      }
    } catch (error) {
      res.statusCode = 502;
      res.setHeader('Content-Type', 'application/json; charset=utf-8');
      res.end(JSON.stringify({ error: error?.message || 'No se pudo resolver el audio' }));
    }
  }
  return {
    name: 'ritmo-music-stream-proxy',
    configureServer(server) { server.middlewares.use(handle); },
    configurePreviewServer(server) { server.middlewares.use(handle); }
  };
}

const proxies = {
  '/yt': {
    target: 'https://music.youtube.com',
    changeOrigin: true,
    secure: true,
    rewrite: (path) => path.replace(/^\/yt/, ''),
    configure: (proxy) => proxy.on('proxyReq', (request) => {
      request.setHeader('Origin', 'https://music.youtube.com');
      request.setHeader('Referer', 'https://music.youtube.com/');
      request.setHeader('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128.0.0.0 Safari/537.36');
    }),
  },
  '/spotify': {
    target: 'https://open.spotify.com',
    changeOrigin: true,
    secure: true,
    rewrite: (path) => path.replace(/^\/spotify/, ''),
  },
  '/lyrics': {
    target: 'https://lrclib.net',
    changeOrigin: true,
    secure: true,
    rewrite: (path) => path.replace(/^\/lyrics/, ''),
  },
  '/letras': {
    target: 'https://www.letras.com',
    changeOrigin: true,
    secure: true,
    rewrite: (path) => path.replace(/^\/letras/, ''),
  },
  '/lyrics-ovh': {
    target: 'https://api.lyrics.ovh',
    changeOrigin: true,
    secure: true,
    rewrite: (path) => path.replace(/^\/lyrics-ovh/, ''),
  },
};

export default defineConfig({
  base: './',
  server: {
    port: 8099,
    strictPort: true,
    proxy: proxies,
  },
  preview: {
    port: 8099,
    strictPort: true,
    proxy: proxies,
  },
  build: {
    outDir: 'dist',
    target: 'es2020',
    sourcemap: false,
  },
  plugins: [musicStreamProxy()],
});
