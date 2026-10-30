let database;
function openDatabase() {
  if (!database) database = new Promise((resolve, reject) => {
    const request = indexedDB.open('ritmo-offline', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('lyrics');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  }).catch((error) => { database = null; throw error; });
  return database;
}
export async function cachedLyrics(id) {
  try {
    const db = await openDatabase();
    return await new Promise((resolve, reject) => {
      const request = db.transaction('lyrics').objectStore('lyrics').get(id);
      request.onsuccess = () => resolve(request.result || null);
      request.onerror = () => reject(request.error);
    });
  } catch { return null; }
}
export async function storeLyrics(id, lyrics) {
  if (!lyrics?.syncedLyrics && !lyrics?.plainLyrics) return;
  const db = await openDatabase();
  await new Promise((resolve, reject) => {
    const transaction = db.transaction('lyrics', 'readwrite');
    transaction.objectStore('lyrics').put(lyrics, id);
    transaction.oncomplete = resolve;
    transaction.onerror = () => reject(transaction.error);
    transaction.onabort = () => reject(transaction.error);
  });
}
