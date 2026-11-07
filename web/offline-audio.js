// Commit metadata and audio together: partial downloads never get a check mark.
let database;
async function openDatabase() {
  if (!database) database = new Promise((resolve, reject) => {
    const request = indexedDB.open('ritmo-audio', 1);
    request.onupgradeneeded = () => {
      request.result.createObjectStore('tracks', { keyPath: 'id' });
      request.result.createObjectStore('audio');
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  }).catch(error => { database = null; throw error; });
  return database;
}
export async function offlineTracks() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const request = db.transaction('tracks').objectStore('tracks').getAll();
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}
export async function offlineAudio(id) {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const request = db.transaction('audio').objectStore('audio').get(id);
    request.onsuccess = () => resolve(request.result || null);
    request.onerror = () => reject(request.error);
  });
}
export async function saveOfflineAudio(track, blob) {
  if (!(blob instanceof Blob) || blob.size < 1024) throw new Error('El archivo de audio está incompleto');
  const db = await openDatabase();
  await new Promise((resolve, reject) => {
    const tx = db.transaction(['tracks', 'audio'], 'readwrite');
    tx.objectStore('tracks').put(track);
    tx.objectStore('audio').put(blob, track.id);
    tx.oncomplete = resolve;
    tx.onerror = tx.onabort = () => reject(tx.error || new Error('No se pudo guardar el audio'));
  });
}
