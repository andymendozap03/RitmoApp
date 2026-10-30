export function normalizeText(value) {
  return String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ').trim().replace(/\s+/g, ' ');
}

export function uniqueTracks(tracks) {
  const seen = new Set();
  return tracks.filter((track) => track?.id && !seen.has(track.id) && seen.add(track.id));
}

// Only future items move: history and the currently playing recording stay intact.
export function orderUpcoming(queue, index, random = Math.random, original = null) {
  const upcoming = queue.slice(index + 1);
  if (original) {
    const order = new Map(original.map((track, i) => [track.id, i]));
    upcoming.sort((a, b) => (order.get(a.id) ?? Infinity) - (order.get(b.id) ?? Infinity));
  } else {
    for (let i = upcoming.length - 1; i > 0; i--) {
      const j = Math.floor(random() * (i + 1));
      [upcoming[i], upcoming[j]] = [upcoming[j], upcoming[i]];
    }
  }
  return [...queue.slice(0, index + 1), ...upcoming];
}

export function rankTracks(tracks, query) {
  const tokens = normalizeText(query).split(' ').filter(Boolean);
  const versions = ['cover', 'karaoke', 'remix', 'slowed', 'sped', 'live', 'instrumental', 'reaction'];
  const score = (track) => {
    const title = normalizeText(track.title);
    const artist = normalizeText(track.artist);
    const combined = `${artist} ${title}`;
    const words = new Set(combined.split(' '));
    const matched = tokens.filter((token) => words.has(token)).length;
    const relevance = tokens.length ? matched / tokens.length : 1;
    const official = track.official || track.verified || track.kind === 'song';
    const versionPenalty = versions.some((term) => title.split(' ').includes(term) && !tokens.includes(term)) ? 35 : 0;
    return relevance * 120 + (relevance >= .6 && official ? 50 : 0) +
      (track.verified && relevance >= .6 ? 10 : 0) + (tokens.length && artist === tokens.join(' ') ? 15 : 0) - versionPenalty;
  };
  return uniqueTracks(tracks).map((track, index) => ({ track, index, score: score(track) }))
    .sort((a, b) => b.score - a.score || a.index - b.index).map(({ track }) => track);
}

export function libraryCsv(title, tracks) {
  const cell = (value) => {
    let text = String(value ?? '');
    // Spreadsheet applications must not interpret imported titles as formulas.
    if (/^[\s]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
  };
  const rows = [['Track Name', 'Artist Name', 'Track ID', 'Artwork', 'Duration (ms)', 'Playlist'],
    ...tracks.map((track) => [track.title, track.artist, track.id, track.art, track.duration || 0, title])];
  return '\uFEFF' + rows.map((row) => row.map(cell).join(',')).join('\r\n') + '\r\n';
}

export function parseLibraryCsv(text) {
  const rows = [];
  let row = [], value = '', quoted = false;
  const source = String(text || '').replace(/^\uFEFF/, '');
  for (let index = 0; index < source.length; index++) {
    const char = source[index];
    if (quoted) {
      if (char === '"' && source[index + 1] === '"') { value += '"'; index++; }
      else if (char === '"') quoted = false;
      else value += char;
    } else if (char === '"' && value.length === 0) quoted = true;
    else if (char === ',') { row.push(value); value = ''; }
    else if (char === '\n' || char === '\r') {
      if (char === '\r' && source[index + 1] === '\n') index++;
      row.push(value); rows.push(row); row = []; value = '';
    } else value += char;
  }
  if (quoted) throw new Error('El CSV contiene comillas sin cerrar');
  if (value.length || row.length) { row.push(value); rows.push(row); }
  if (rows.length < 2) return [];
  const headers = rows[0].map(normalizeText);
  const titleIndex = headers.findIndex((header) => ['track name', 'track', 'song name', 'song', 'title'].includes(header));
  const artistIndex = headers.findIndex((header) => ['artist name', 'artist', 'artists', 'creator'].includes(header));
  if (titleIndex < 0 || artistIndex < 0) throw new Error('El CSV debe tener columnas de canción y artista');
  const idIndex = headers.indexOf('track id'), artIndex = headers.indexOf('artwork'), durationIndex = headers.indexOf('duration ms');
  const ownBackup = idIndex >= 0 && artIndex >= 0 && durationIndex >= 0;
  const cell = (fields, index) => {
    const raw = fields[index] || '';
    return ownBackup ? raw.replace(/^'(?=\s*[=+\-@])/, '') : raw.trim();
  };
  return rows.slice(1).map((fields) => {
    const title = cell(fields, titleIndex), artist = cell(fields, artistIndex);
    if (!title || !artist) return null;
    const id = cell(fields, idIndex), art = cell(fields, artIndex);
    const duration = Number(cell(fields, durationIndex));
    return { title, artist, id: /^yt:[A-Za-z0-9_-]{11}$/.test(id) ? id : '',
      art: /^https:\/\//.test(art) ? art : '', duration: Number.isFinite(duration) && duration > 0 ? duration : 0 };
  }).filter(Boolean);
}
