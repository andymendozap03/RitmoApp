import { test, expect } from '@playwright/test';

const tracks = Array.from({ length: 45 }, (_, i) => ({
  id: 'yt:' + String(i).padStart(11, '0'), title: 'Canción ' + i, artist: 'Artista', duration: 120000, art: ''
}));
test.afterEach(async ({page}, info) => {
  if (info.status === info.expectedStatus || page.isClosed()) return;
  console.log('Playback/storage diagnostic', await page.evaluate(() => ({
    toast: document.querySelector('#toast')?.textContent, events: window.audioEvents,
    audio: { src: document.querySelector('#audio')?.src, code: document.querySelector('#audio')?.error?.code,
      message: document.querySelector('#audio')?.error?.message, wav: document.querySelector('#audio')?.canPlayType('audio/wav') }
  })));
});

test.beforeEach(async ({ page }) => {
  page.on('pageerror', error => console.log('PAGE ERROR', error.message));
  page.on('requestfailed', request => console.log('REQUEST FAILED', request.url(), request.failure()?.errorText));
  await page.addInitScript(tracks => {
    window.audioEvents = [];
    document.addEventListener('DOMContentLoaded', () => {
      const audio = document.querySelector('#audio');
      audio.addEventListener('error', () => window.audioEvents.push({src:audio.src, code:audio.error?.code, message:audio.error?.message}));
    });
    if (!localStorage.getItem('fixture-initialized')) {
      localStorage.setItem('ritmo.favorites', JSON.stringify(tracks));
      localStorage.setItem('fixture-initialized', 'true');
    }
  }, tracks);
});

test('same recording keeps position/pause and queue shows current plus next 20 without rebuilding on ticks', async ({ page }) => {
  await page.goto('/');
  await page.locator('[data-view="favorites"]').click();
  await page.locator('.row-text').first().click();
  await expect.poll(() => page.locator('#audio').evaluate(audio => audio.readyState)).toBeGreaterThan(1);
  await page.locator('#playerPlay').click();
  await page.locator('#audio').evaluate(audio => { audio.currentTime = 65; });
  await expect.poll(() => page.locator('#audio').evaluate(audio => audio.currentTime)).toBeGreaterThan(64);
  await page.locator('#closePlayer').click();
  await page.locator('.row-text').first().click();
  expect(await page.locator('#audio').evaluate(audio => audio.paused && audio.currentTime >= 65)).toBeTruthy();
  await page.locator('#playerQueue').click();
  await expect(page.locator('.queue-item')).toHaveCount(21);
  await page.locator('.queue-item').first().evaluate(node => { node.dataset.retained = 'true'; });
  await page.waitForTimeout(1100);
  await expect(page.locator('.queue-item').first()).toHaveAttribute('data-retained', 'true');
  await page.locator('#playerShuffle').click();
  await expect(page.locator('#playerShuffle')).toHaveClass(/on/);
  expect(await page.locator('#audio').evaluate(audio => audio.paused && audio.currentTime >= 65)).toBeTruthy();
  const nextTitle = await page.locator('.queue-title').nth(1).textContent();
  await page.locator('#playerNext').click();
  await expect(page.locator('.queue-title').first()).toHaveText(nextTitle);
  await expect(page.locator('.queue-item')).toHaveCount(21);
  await page.locator('#playerQueue').click();
  await page.locator('#playerLyrics').click();
  await expect(page.locator('.lyric-line')).toHaveCount(2);
  await expect(page.locator('#queuePanel')).toBeHidden();
});

test('download persists audio and lyrics, reloads offline and seeks beyond 40 seconds', async ({ page, context, browserName }) => {
  test.skip(browserName === 'webkit' && process.platform === 'win32', 'Windows WebKit audio pipeline: https://github.com/microsoft/playwright/issues/10892. Run this audio test on macOS or Safari/iPhone. Storage and offline ranges are tested separately on Windows WebKit.');
  // A browser may refuse persistence; that must not prevent a normal download.
  await page.addInitScript(() => {
    window.persistenceRequests = 0;
    Object.defineProperty(navigator.storage, 'persist', { value: async () => { window.persistenceRequests++; return false; } });
  });
  await page.goto('/');
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.locator('[data-view="favorites"]').click();
  await page.locator('[data-download-track]').first().click();
  await expect(page.locator('[data-download-track]').first()).toHaveClass(/downloaded/);
  expect(await page.evaluate(() => window.persistenceRequests)).toBe(1);
  await page.waitForFunction(async () => {
    const db = await new Promise(resolve => { const r = indexedDB.open('ritmo-offline'); r.onsuccess = () => resolve(r.result); });
    return new Promise(resolve => { const r = db.transaction('lyrics').objectStore('lyrics').get('yt:00000000000'); r.onsuccess = () => { db.close(); resolve(Boolean(r.result)); }; });
  });
  await context.setOffline(true);
  await page.reload();
  await page.locator('[data-view="downloads"]').click();
  await expect(page.locator('.row')).toHaveCount(1);
  const localFile = await page.evaluate(async () => {
    const r = await fetch('/_offline/audio/yt%3A00000000000', {headers:{Range:'bytes=0-3'}});
    return {status:r.status, type:r.headers.get('content-type'), range:r.headers.get('content-range'), text:await r.text()};
  });
  expect(localFile).toEqual({status:206, type:'audio/wav', range:'bytes 0-3/1920044', text:'RIFF'});
  await page.locator('.row-text').click();
  await expect.poll(() => page.locator('#audio').evaluate(audio => audio.readyState)).toBeGreaterThan(1);
  expect(await page.locator('#audio').getAttribute('src')).toMatch(/^\/_offline\/audio\//);
  await expect.poll(() => page.locator('#audio').evaluate(audio => !audio.paused && audio.currentTime > 0)).toBeTruthy();
  if (await page.evaluate(() => Boolean(navigator.mediaSession))) {
    expect(await page.evaluate(() => navigator.mediaSession.metadata.title)).toBe('Canción 0');
  }
  await page.locator('#playerRange').evaluate(range => { range.value = '750'; range.dispatchEvent(new Event('input', {bubbles:true})); range.dispatchEvent(new Event('change', {bubbles:true})); });
  await expect.poll(() => page.locator('#audio').evaluate(audio => audio.currentTime)).toBeGreaterThan(89);
  await page.locator('#playerLyrics').click();
  await expect(page.locator('.lyric-line')).toHaveCount(2);
  const partial = await page.evaluate(async () => {
    const response = await fetch('/_offline/audio/yt%3A00000000000', {headers:{Range:'bytes=0-3'}});
    return {status:response.status, range:response.headers.get('content-range'), text:await response.text()};
  });
  expect(partial).toEqual({status:206, range:'bytes 0-3/1920044', text:'RIFF'});
  const invalidRange = await page.evaluate(async () => (await fetch('/_offline/audio/yt%3A00000000000', {headers:{Range:'bytes=9999999-'}})).status);
  expect(invalidRange).toBe(416);
});

test('offline download bytes, lyrics and range requests survive reopening without network', async ({page, context}) => {
  await page.goto('/');
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.locator('[data-view="favorites"]').click();
  await page.locator('[data-download-track]').first().click();
  await expect(page.locator('[data-download-track]').first()).toHaveClass(/downloaded/);
  await page.waitForFunction(async () => {
    const db = await new Promise(resolve => { const r = indexedDB.open('ritmo-offline'); r.onsuccess = () => resolve(r.result); });
    return new Promise(resolve => { const r = db.transaction('lyrics').objectStore('lyrics').get('yt:00000000000'); r.onsuccess = () => {db.close(); resolve(Boolean(r.result));}; });
  });
  await context.setOffline(true);
  const reopened = await context.newPage();
  await page.close();
  await reopened.goto('/?source=homescreen');
  await reopened.locator('[data-view="downloads"]').click();
  await expect(reopened.locator('.row')).toHaveCount(1);
  const responses = await reopened.evaluate(async () => {
    const result = [];
    for (const [method, range] of [['GET','bytes=0-3'], ['GET','bytes=1440044-1440047'], ['GET','bytes=-4'], ['GET','bytes=9999999-'], ['HEAD',null]]) {
      const r = await fetch('/_offline/audio/yt%3A00000000000', {method, headers:range ? {Range:range} : {}});
      const bytes = new Uint8Array(await r.arrayBuffer());
      result.push({status:r.status, range:r.headers.get('content-range'), length:r.headers.get('content-length'), bytes:[...bytes]});
    }
    const missing = await fetch('/_offline/audio/missing');
    result.push({status:missing.status});
    return result;
  });
  expect(responses).toEqual([
    {status:206, range:'bytes 0-3/1920044', length:'4', bytes:[82,73,70,70]},
    {status:206, range:'bytes 1440044-1440047/1920044', length:'4', bytes:[0,0,0,0]},
    {status:206, range:'bytes 1920040-1920043/1920044', length:'4', bytes:[0,0,0,0]},
    {status:416, range:'bytes */1920044', length:null, bytes:[]},
    {status:200, range:null, length:'1920044', bytes:[]},
    {status:404}
  ]);
});

test('installable app caches manifest, matching shell and icons and explains Safari installation', async ({ page, context }) => {
  await page.setViewportSize({width:390, height:844});
  await page.goto('/?source=homescreen');
  await page.evaluate(() => navigator.serviceWorker.ready);
  expect(await page.locator('link[rel="apple-touch-icon"]').evaluate(link => new URL(link.href).pathname)).toBe('/icons/apple-touch-icon.png');
  const manifest = await page.evaluate(async () => (await fetch('/manifest.webmanifest')).json());
  expect(manifest.display).toBe('standalone');
  expect(manifest.scope).toBe('/');
  expect(manifest.icons.map(icon => icon.sizes)).toEqual(['192x192', '512x512']);
  await page.locator('[data-view="downloads"]').click();
  await page.getByRole('button', {name:'Instalar Ritmo', exact:true}).click();
  await expect(page.locator('.pwa-dialog')).toBeVisible();
  await expect(page.locator('.pwa-status')).toContainText('lista para abrirse sin conexión');
  await expect(page.locator('.pwa-dialog')).toContainText('Añadir a pantalla de inicio');
  await page.screenshot({path:'test-results/pwa-installation.png'});
  await page.getByRole('button', {name:'Entendido', exact:true}).click();
  await context.setOffline(true);
  await page.reload();
  await page.locator('[data-view="downloads"]').click();
  await expect(page.getByRole('button', {name:'Instalar Ritmo', exact:true})).toBeVisible();
  for (const path of ['/manifest.webmanifest', '/icons/apple-touch-icon.png', ...manifest.icons.map(icon => icon.src)]) {
    const resource = await page.evaluate(async path => {
      const response = await fetch(path); return {ok:response.ok, size:(await response.blob()).size};
    }, path);
    expect(resource.ok).toBeTruthy(); expect(resource.size).toBeGreaterThan(100);
  }
  const cacheNames = await page.evaluate(() => caches.keys());
  expect(cacheNames.some(name => /^ritmo-shell-[a-f0-9]{16}$/.test(name))).toBeTruthy();
});

for (const viewport of [{width:360,height:800}, {width:1024,height:600}, {width:1440,height:900}]) {
  test(`library and playlist actions fit ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.goto('/');
    await page.locator('[data-view="favorites"]').click();
    await page.locator('.row-actions button').nth(2).click();
    await page.locator('#newPlaylistName').fill('Lista de prueba');
    await page.getByRole('button', {name:'Crear y añadir'}).click();
    await page.locator('[data-view="playlists"]').click();
    await expect(page.getByText('Lista de prueba').first()).toBeVisible();
    await page.getByText('Lista de prueba').first().click();
    await expect(page.locator('.row')).toHaveCount(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
    for (const control of ['[data-view="downloads"]', '#miniLyrics', '#miniQueue']) {
      const rect = await page.locator(control).boundingBox();
      expect(rect.x).toBeGreaterThanOrEqual(0); expect(rect.x+rect.width).toBeLessThanOrEqual(viewport.width+1);
      expect(rect.y+rect.height).toBeLessThanOrEqual(viewport.height+1);
    }
  });
}
