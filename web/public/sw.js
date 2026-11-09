// The production shell is cached; downloaded audio stays in IndexedDB and is
// served locally with ranges. Online APIs and streams keep their server protocols.
const CACHE = 'ritmo-shell-__BUILD_ID__';
self.addEventListener('message', event => {
  if (event.data?.type === 'ritmo-capabilities') event.ports[0]?.postMessage({offlineAudio:true});
});
self.addEventListener('install', event => event.waitUntil((async () => {
  const response = await fetch('/index.html', { cache: 'no-cache' });
  if (!response.ok) throw new Error('No se pudo guardar la aplicación');
  const html = await response.clone().text();
  const cache = await caches.open(CACHE);
  const assets = [...html.matchAll(/(?:src|href)=["']([^"']+\.(?:js|css))["']/g)]
    .map(match => new URL(match[1], self.location.origin).href);
  await cache.addAll([...assets, '/manifest.webmanifest', '/icons/apple-touch-icon.png', '/icons/icon-192.png', '/icons/icon-512.png']);
  await cache.put('/index.html', response);
  await self.skipWaiting();
})()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  await self.clients.claim();
  // Keep the previous shell until open clients have fetched the new version.
})()));
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (url.origin === self.location.origin && url.pathname.startsWith('/_offline/audio/') && ['GET', 'HEAD'].includes(event.request.method)) {
    event.respondWith(offlineAudioResponse(event.request, url));
    return;
  }
  if (event.request.method !== 'GET' || url.origin !== self.location.origin) return;
  const navigation = event.request.mode === 'navigate' && (url.pathname === '/' || url.pathname === '/index.html');
  const shellAsset = url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/') || url.pathname === '/manifest.webmanifest';
  if (!navigation && !shellAsset) return;
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    // Vite adds Vary: Origin; preload and installation fetches have different
    // Origin headers. Hashed same-origin assets are identical in both cases.
    const saved = await cache.match(navigation ? '/index.html' : event.request, { ignoreVary: true });
    // Serve HTML and its matching hashed scripts as one installed version.
    // Browser service-worker updates install the next version in the background.
    if (saved) return saved;
    try {
      const response = await fetch(event.request);
      if (!response.ok) throw new Error('Sin conexión');
      // Navigation uses the shell installed with its matching asset set.
      if (!navigation) await cache.put(event.request, response.clone());
      return response;
    } catch (error) {
      if (saved) return saved;
      throw error;
    }
  })());
});

async function offlineAudioResponse(request, url) {
  try {
    const id = decodeURIComponent(url.pathname.slice('/_offline/audio/'.length));
    const stored = await new Promise((resolve, reject) => {
      const open = indexedDB.open('ritmo-audio', 1);
      open.onupgradeneeded = () => {
        open.result.createObjectStore('tracks', {keyPath:'id'});
        open.result.createObjectStore('audio');
      };
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const db = open.result;
        const tx = db.transaction('audio');
        const read = tx.objectStore('audio').get(id);
        read.onsuccess = () => resolve(read.result);
        read.onerror = () => reject(read.error);
        tx.oncomplete = tx.onabort = () => db.close();
      };
    });
    if (!stored) return new Response('Canción no descargada', {status:404});
    // Accept old Blob downloads as well as the portable ArrayBuffer format.
    const bytes = stored instanceof Blob ? await stored.arrayBuffer() : stored.bytes;
    if (!(bytes instanceof ArrayBuffer) || !bytes.byteLength) return new Response('Descarga incompleta', {status:404});
    const size = bytes.byteLength;
    const headers = {'Content-Type': stored.type || 'audio/mp4', 'Accept-Ranges':'bytes', 'Cache-Control':'no-store'};
    const requestedRange = request.headers.get('Range');
    let start = 0, end = size - 1;
    if (requestedRange) {
      const match = /^bytes=(\d*)-(\d*)$/.exec(requestedRange);
      if (!match || (!match[1] && !match[2])) return new Response(null, {status:416, headers:{...headers, 'Content-Range':`bytes */${size}`}});
      start = match[1] ? Number(match[1]) : Math.max(0, size - Number(match[2]));
      end = match[1] && match[2] ? Math.min(Number(match[2]), size - 1) : size - 1;
      if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start > end || start >= size) {
        return new Response(null, {status:416, headers:{...headers, 'Content-Range':`bytes */${size}`}});
      }
      headers['Content-Range'] = `bytes ${start}-${end}/${size}`;
    }
    headers['Content-Length'] = String(end - start + 1);
    return new Response(request.method === 'HEAD' ? null : bytes.slice(start, end + 1), {status:requestedRange ? 206 : 200, headers});
  } catch {
    return new Response('No se pudo abrir la descarga', {status:503});
  }
}
