import { test, expect } from '@playwright/test';

const tracks = Array.from({ length: 45 }, (_, i) => ({
  id: 'yt:' + String(i).padStart(11, '0'), title: 'Canción ' + i, artist: 'Artista', duration: 120000, art: ''
}));
const wav = Buffer.alloc(44 + 120 * 8000 * 2);
wav.write('RIFF'); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8);
wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22);
wav.writeUInt32LE(8000, 24); wav.writeUInt32LE(16000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34);
wav.write('data', 36); wav.writeUInt32LE(wav.length - 44, 40);

test.beforeEach(async ({ page }) => {
  page.on('pageerror', error => console.log('PAGE ERROR', error.message));
  page.on('requestfailed', request => console.log('REQUEST FAILED', request.url(), request.failure()?.errorText));
  await page.addInitScript(tracks => {
    if (!localStorage.getItem('fixture-initialized')) {
      localStorage.setItem('ritmo.favorites', JSON.stringify(tracks));
      localStorage.setItem('fixture-initialized', 'true');
    }
  }, tracks);
  await page.route('**/yt/**', route => route.fulfill({ json: {} }));
  await page.route('**/lyrics/**', route => route.fulfill({ json: { syncedLyrics: '[00:00.00]Primera línea\n[00:30.00]Segunda línea', plainLyrics: 'Primera línea\nSegunda línea' } }));
  await page.route('**/stream?**', route => {
    const range = /bytes=(\d+)-(\d*)/.exec(route.request().headers().range || '');
    const start = range ? Number(range[1]) : 0;
    const end = range?.[2] ? Math.min(Number(range[2]), wav.length - 1) : wav.length - 1;
    return route.fulfill({ status: range ? 206 : 200, contentType: 'audio/wav', body: wav.subarray(start, end + 1),
      headers: { 'accept-ranges': 'bytes', 'content-length': String(end - start + 1), ...(range ? {'content-range': `bytes ${start}-${end}/${wav.length}`} : {}) } });
  });
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

test('download persists audio and lyrics, reloads offline and seeks beyond 40 seconds', async ({ page, context }) => {
  await page.goto('/');
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.locator('[data-view="favorites"]').click();
  await page.locator('[data-download-track]').first().click();
  await expect(page.locator('[data-download-track]').first()).toHaveClass(/downloaded/);
  await page.waitForFunction(async () => {
    const db = await new Promise(resolve => { const r = indexedDB.open('ritmo-offline'); r.onsuccess = () => resolve(r.result); });
    return new Promise(resolve => { const r = db.transaction('lyrics').objectStore('lyrics').get('yt:00000000000'); r.onsuccess = () => { db.close(); resolve(Boolean(r.result)); }; });
  });
  await context.setOffline(true);
  await page.reload();
  await page.locator('[data-view="downloads"]').click();
  await expect(page.locator('.row')).toHaveCount(1);
  await page.locator('.row-text').click();
  await expect.poll(() => page.locator('#audio').evaluate(audio => audio.readyState)).toBeGreaterThan(1);
  expect(await page.locator('#audio').getAttribute('src')).toMatch(/^blob:/);
  await page.locator('#playerRange').evaluate(range => { range.value = '750'; range.dispatchEvent(new Event('input', {bubbles:true})); range.dispatchEvent(new Event('change', {bubbles:true})); });
  await expect.poll(() => page.locator('#audio').evaluate(audio => audio.currentTime)).toBeGreaterThan(89);
  await page.locator('#playerLyrics').click();
  await expect(page.locator('.lyric-line')).toHaveCount(2);
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
