// Only the production app shell is cached. Audio stays in IndexedDB and API
// responses/stream ranges must always use the existing server protocols.
const CACHE = 'ritmo-shell-1.3.1';
self.addEventListener('install', event => event.waitUntil((async () => {
  const response = await fetch('/index.html', { cache: 'no-cache' });
  if (!response.ok) throw new Error('No se pudo guardar la aplicación');
  const html = await response.clone().text();
  const cache = await caches.open(CACHE);
  const assets = [...html.matchAll(/(?:src|href)=["']([^"']+\.(?:js|css))["']/g)]
    .map(match => new URL(match[1], self.location.origin).href);
  await cache.addAll(assets);
  await cache.put('/index.html', response);
  await self.skipWaiting();
})()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  await self.clients.claim();
  // Keep the previous shell until open clients have fetched the new version.
})()));
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin) return;
  const navigation = event.request.mode === 'navigate' && (url.pathname === '/' || url.pathname === '/index.html');
  if (!navigation && !url.pathname.startsWith('/assets/')) return;
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    // Vite adds Vary: Origin; preload and installation fetches have different
    // Origin headers. Hashed same-origin assets are identical in both cases.
    const saved = await cache.match(navigation ? '/index.html' : event.request, { ignoreVary: true });
    if (!navigation && saved) return saved;
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
