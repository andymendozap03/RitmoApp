'use strict';

import { Capacitor, registerPlugin } from '@capacitor/core';
import { uniqueTracks, orderUpcoming, rankTracks, libraryCsv, normalizeText, parseLibraryCsv } from './music-utils.js';
import { cachedLyrics, storeLyrics } from './lyrics-cache.js';

const isNativeApp = Capacitor.isNativePlatform();
const NativePlayback = registerPlugin('RitmoPlayback');
const nativeApiHosts = [
  ['/yt/', 'https://music.youtube.com/'],
  ['/spotify/', 'https://open.spotify.com/'],
  ['/lyrics/', 'https://lrclib.net/'],
  ['/letras/', 'https://www.letras.com/'],
  ['/lyrics-ovh/', 'https://api.lyrics.ovh/']
];
function apiUrl(path) {
  if (!isNativeApp) return path;
  const request = new URL(path, window.location.href);
  const match = nativeApiHosts.find(([prefix]) => request.pathname.startsWith(prefix));
  return match ? match[1] + request.pathname.slice(match[0].length) + request.search : request.href;
}
function apiFetch(path, options) { return fetch(apiUrl(path), options); }

const $ = (selector) => document.querySelector(selector);
const audio = $('#audio');
const ui = {
  title: $('#title'), search: $('#search'), content: $('#content'),
  miniArt: $('#miniArt'), miniTitle: $('#miniTitle'), miniArtist: $('#miniArtist'),
  miniPlay: $('#miniPlay'), miniPrev: $('#miniPrev'), miniNext: $('#miniNext'),
  miniFav: $('#miniFav'), miniOpen: $('#miniOpen'), miniRange: $('#miniRange'),
  miniCurrent: $('#miniCurrent'), miniDuration: $('#miniDuration'),
  player: $('#playerScreen'), playerArt: $('#playerArt'), playerTitle: $('#playerTitle'),
  playerArtist: $('#playerArtist'), playerPlay: $('#playerPlay'), playerPrev: $('#playerPrev'),
  playerNext: $('#playerNext'), playerShuffle: $('#playerShuffle'), playerRepeat: $('#playerRepeat'),
  playerFav: $('#playerFav'), playerDownload: $('#playerDownload'), playerLyrics: $('#playerLyrics'), playerQueue: $('#playerQueue'),
  queuePanel: $('#queuePanel'), queueList: $('#queueList'), queueSummary: $('#queueSummary'),
  queueAnchor: $('#queueAnchor'), lyricsQueueSlot: $('#lyricsQueueSlot'),
  lyricsQueueToggle: $('#lyricsQueueToggle'),
  lyricsTrackArt: $('#lyricsTrackArt'), lyricsTrackTitle: $('#lyricsTrackTitle'), lyricsTrackArtist: $('#lyricsTrackArtist'),
  lyricsTrackPanel: $('.lyrics-track-panel'), lyricsTransport: $('.lyrics-transport'),
  lyricsPlay: $('#lyricsPlay'), lyricsPrev: $('#lyricsPrev'), lyricsNext: $('#lyricsNext'),
  lyricsShuffle: $('#lyricsShuffle'), lyricsRepeat: $('#lyricsRepeat'), lyricsRange: $('#lyricsRange'),
  lyricsCurrent: $('#lyricsCurrent'), lyricsDuration: $('#lyricsDuration'),
  playerRange: $('#playerRange'), playerCurrent: $('#playerCurrent'), playerDuration: $('#playerDuration'),
  lyrics: $('#lyrics'), lyricsText: $('#lyricsText'), lyricsTitle: $('#lyricsTitle'), toast: $('#toast')
};

const state = {
  view: 'home', query: '', queue: [], index: -1, current: null,
  favorites: readList('ritmo.favorites'), playlists: readList('ritmo.playlists'), activePlaylistId: null,
  shuffle: false, repeat: 0, request: 0, searchRequest: 0, searchResults: [], searchContinuation: null, searchLoading: false,
  queueOpen: false, lyricLines: [], lyricActive: -1, lyricsSynced: false, lyricsRequest: 0, lyricsTrackId: null, isPlaying: false,
  nativePosition: 0, nativeDuration: 0, nativeSyncing: false,
  downloadedIds: new Set(), downloadTasks: new Map()
};
let originalQueue = [];
let shuffleChanging = false;
const pendingLyrics = new Map();

function readList(key) {
  try { const value = JSON.parse(localStorage.getItem(key)); return Array.isArray(value) ? value : []; }
  catch { return []; }
}
function save() {
  localStorage.setItem('ritmo.favorites', JSON.stringify(state.favorites));
  localStorage.setItem('ritmo.playlists', JSON.stringify(state.playlists));
  if (isNativeApp) NativePlayback.syncLibrary({ favorites: state.favorites, playlists: state.playlists }).catch(() => {});
}
function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
const iconMarkup = {
  plus: '<path d="M12 5v14M5 12h14"/>',
  home: '<path d="m3 10 9-7 9 7"/><path d="M5 9v11h14V9"/><path d="M9 20v-6h6v6"/>',
  search: '<circle cx="10.8" cy="10.8" r="6.8"/><path d="m16 16 5 5"/>',
  heart: '<path d="M20.8 8.8c0 5.1-8.8 10.2-8.8 10.2S3.2 13.9 3.2 8.8A4.8 4.8 0 0 1 12 6.1a4.8 4.8 0 0 1 8.8 2.7Z"/>',
  library: '<path d="M4 5h16M4 12h16M4 19h16"/><path d="M7 5v.01M7 12v.01M7 19v.01"/>',
  folder: '<path d="M3 6h6l2 2h10v11H3z"/><path d="M3 8h18"/>',
  previous: '<path d="M6 5v14"/><path d="m19 5-11 7 11 7z"/>',
  next: '<path d="M18 5v14"/><path d="m5 5 11 7-11 7z"/>',
  play: '<path class="fill" d="M8 5.5v13l10.5-6.5L8 5.5Z"/>',
  pause: '<path class="fill" d="M7 5h4v14H7zM14 5h4v14h-4z"/>',
  shuffle: '<path d="m16 3 5 4-5 4"/><path d="M3 7h2.5c5 0 8 10 13 10H21"/><path d="m16 13 5 4-5 4"/><path d="M3 17h2.5c1.5 0 2.7-1.5 3.8-3"/><path d="M12.7 9c1.1-1.3 2.2-2 3.8-2H21"/>',
  repeat: '<path d="m17 2 4 4-4 4"/><path d="M3 11V9a3 3 0 0 1 3-3h15"/><path d="m7 22-4-4 4-4"/><path d="M21 13v2a3 3 0 0 1-3 3H3"/>',
  repeatOne: '<path d="m17 2 4 4-4 4"/><path d="M3 11V9a3 3 0 0 1 3-3h15"/><path d="m7 22-4-4 4-4"/><path d="M21 13v2a3 3 0 0 1-3 3H3"/><path d="M11 11v5m-1-4 1-1 1 1"/>',
  download: '<path d="M12 3v12"/><path d="m7 10 5 5 5-5"/><path d="M4 19v2h16v-2"/>',
  check: '<path d="m5 12 4 4L19 6"/>',
  close: '<path d="m6 6 12 12M18 6 6 18"/>',
  down: '<path d="m6 9 6 6 6-6"/>',
  note: '<path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>',
  lyrics: '<path d="M5 4h14v16H5z"/><path d="M8 8h8M8 12h8M8 16h5"/>'
};
function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('aria-hidden', 'true');
  svg.classList.add('ui-icon');
  svg.innerHTML = iconMarkup[name] || iconMarkup.note;
  return svg;
}
function setIcon(button, name) { button.replaceChildren(icon(name)); }
function updateDownloadButton(button, trackId) {
  if (!button || !trackId) return;
  button.dataset.downloadTrack = trackId;
  const task = state.downloadTasks.get(trackId);
  const downloaded = state.downloadedIds.has(trackId);
  button.classList.toggle('downloaded', downloaded);
  button.classList.toggle('busy', !downloaded && task?.state === 0);
  button.disabled = !downloaded && task?.state === 0;
  const label = button.dataset.downloadLabel || '';
  if (downloaded) {
    button.replaceChildren(icon('check'), ...(label ? [document.createTextNode('Disponible')] : []));
    button.title = 'Disponible sin conexión';
    button.setAttribute('aria-label', button.title);
  } else {
    button.replaceChildren(icon('download'), ...(label ? [document.createTextNode(label)] : []));
    button.title = task?.state === 0 ? `Descargando ${task.percent || 0}%` : task?.state === 2 ? `Reintentar descarga: ${task.message || 'error'}` : 'Descargar';
    button.setAttribute('aria-label', button.title);
  }
}
function applyDownloadsState(result = {}) {
  const previousTasks = state.downloadTasks;
  state.downloadedIds = new Set(Array.isArray(result.downloaded) ? result.downloaded : []);
  state.downloadTasks = new Map((Array.isArray(result.tasks) ? result.tasks : []).map((task) => [task.id, task]));
  document.querySelectorAll('[data-download-track]').forEach((button) => updateDownloadButton(button, button.dataset.downloadTrack));
  for (const [id, task] of state.downloadTasks) {
    if (task.state !== 2 || previousTasks.get(id)?.state === 2) continue;
    const reason = task.message === 'wifi' ? 'Conéctate a Wi-Fi para descargar.'
      : task.message === 'offline' ? 'No hay conexión a internet.'
        : task.message === 'space' ? 'No hay espacio suficiente.'
          : task.message || 'Error de red';
    toast('No se pudo descargar · ' + reason);
  }
}
async function syncDownloads() {
  if (!isNativeApp) return;
  try { applyDownloadsState(await NativePlayback.getDownloads()); } catch { }
}
function labeledIcon(name, label) {
  const wrapper = document.createDocumentFragment();
  wrapper.append(icon(name), document.createTextNode(label));
  return wrapper;
}
function isFavorite(track) { return state.favorites.some((item) => item.id === track.id); }
function videoId(track) { return track.videoId || String(track.id || '').replace(/^yt:/, ''); }
function formatTime(ms) {
  if (!ms || !Number.isFinite(ms)) return '0:00';
  const seconds = Math.floor(ms / 1000);
  return Math.floor(seconds / 60) + ':' + String(seconds % 60).padStart(2, '0');
}
let toastTimer;
let selectingPlaybackSource = false;
let activeSeekRange = null;
let nativeSeekGeneration = 0;
const failedRecordingIds = new Set();
const recordingFallbackCounts = new Map();
function toast(message) {
  ui.toast.textContent = message;
  ui.toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { ui.toast.hidden = true; }, 2600);
}
function route(view) {
  state.view = view;
  document.querySelectorAll('.nav-btn').forEach((button) => button.classList.toggle('active', button.dataset.view === view));
  ui.title.textContent = ({ home: 'Inicio', search: 'Buscar', favorites: 'Favoritos', playlists: 'Playlists' })[view];
  ui.search.value = '';
  ui.search.placeholder = view === 'search' ? 'Canción o artista' : 'Buscar música';
  if (view === 'home') loadHome();
  else if (view === 'favorites') renderFavorites();
  else if (view === 'playlists') renderPlaylists();
  else renderSearchIdle();
}

function textOf(value) {
  if (!value) return '';
  if (typeof value === 'string') return value;
  if (value.simpleText) return value.simpleText;
  if (Array.isArray(value.runs)) return value.runs.map((run) => run.text || '').join('');
  return '';
}
function durationMs(text) {
  const parts = String(text || '').split(':').map(Number);
  if (!parts.length || parts.some((part) => !Number.isFinite(part))) return 0;
  return parts.reduce((sum, part) => sum * 60 + part, 0) * 1000;
}
async function innerTubeRequest(endpoint, body) {
  const clients = [
    { name: 'WEB_REMIX', version: '1.20250601.01.00' },
    { name: 'WEB', version: '2.20250601.00.00' }
  ];
  let lastError = 'No se pudo conectar';
  for (const client of clients) {
    try {
      const payload = {
          context: { client: { clientName: client.name, clientVersion: client.version, hl: 'es', gl: 'US' } },
          ...body
      };
      if (isNativeApp) return await NativePlayback.innerTube({ endpoint, body: payload });
      const response = await apiFetch('/yt/youtubei/v1/' + endpoint + '?prettyPrint=false', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) { lastError = 'HTTP ' + response.status; continue; }
      return await response.json();
    } catch (error) { lastError = error?.message || lastError; }
  }
  throw new Error(lastError);
}
function extractTracks(data) {
  const results = [], seen = new Set();
  const visit = (value) => {
    if (!value || typeof value !== 'object') return;
    const item = value.videoRenderer || value.playlistVideoRenderer || value.musicResponsiveListItemRenderer || value.musicTwoRowItemRenderer;
    if (item) {
      const columns = item.flexColumns || [];
      const titleData = item.title || columns[0]?.musicResponsiveListItemFlexColumnRenderer?.text;
      const nav = [item.navigationEndpoint, ...(titleData?.runs || []).map((run) => run.navigationEndpoint),
        item.overlay?.musicItemThumbnailOverlayRenderer?.content?.musicPlayButtonRenderer?.playNavigationEndpoint]
        .find((endpoint) => endpoint?.watchEndpoint);
      const id = item.videoId || item.playlistItemData?.videoId || nav?.watchEndpoint?.videoId;
      const title = textOf(titleData);
      const byline = item.ownerText || item.shortBylineText || item.longBylineText || item.subtitle || columns[1]?.musicResponsiveListItemFlexColumnRenderer?.text;
      const artistRuns = (byline?.runs || []).filter((run) => {
        const endpoint = run.navigationEndpoint?.browseEndpoint;
        return endpoint?.browseEndpointContextSupportedConfigs?.browseEndpointContextMusicConfig?.pageType === 'MUSIC_PAGE_TYPE_ARTIST' || /^UC/.test(endpoint?.browseId || '');
      });
      const rawArtist = textOf(byline);
      const artist = artistRuns.length ? [...new Set(artistRuns.map((run) => run.text))].join(', ') :
        rawArtist.replace(/^(canci[\u00f3o]n|song|v[\u00edi]deo|episodio)\s*[\u2022\u00b7]\s*/i, '').split(/\s*[\u2022\u00b7]\s*/)[0].replace(/ - Topic$/i, '').trim();
      const badges = JSON.stringify([item.ownerBadges, item.badges]);
      const musicType = nav?.watchEndpoint?.watchEndpointMusicSupportedConfigs?.watchEndpointMusicConfig?.musicVideoType;
      const song = musicType === 'MUSIC_VIDEO_TYPE_ATV' || /^(canci[\u00f3o]n|song)\s*[\u2022\u00b7]/i.test(rawArtist);
      const verified = /VERIFIED|OFFICIAL_ARTIST/i.test(badges);
      const official = musicType === 'MUSIC_VIDEO_TYPE_ATV' || /OFFICIAL_ARTIST/i.test(badges) || / - Topic$/i.test(rawArtist) || (song && artistRuns.length > 0);
      if (id && title && !seen.has(id)) {
        seen.add(id);
        const thumbs = item.thumbnail?.thumbnails || item.thumbnail?.musicThumbnailRenderer?.thumbnail?.thumbnails || item.thumbnailRenderer?.musicThumbnailRenderer?.thumbnail?.thumbnails;
        const duration = textOf(item.lengthText) || textOf(item.fixedColumns?.[0]?.musicResponsiveListItemFixedColumnRenderer?.text) || rawArtist.match(/\b\d{1,2}:\d{2}(?::\d{2})?\b/)?.[0] || '';
        results.push({ id: 'yt:' + id, videoId: id, title: cleanTitle(title), artist: artist || 'Artista',
          kind: song ? 'song' : 'video', verified, official, artistId: artistRuns[0]?.navigationEndpoint?.browseEndpoint?.browseId || '',
          art: thumbs?.at(-1)?.url || 'https://i.ytimg.com/vi/' + id + '/hqdefault.jpg', duration: durationMs(duration) });
      }
    }
    const lockup = value.lockupViewModel;
    if (lockup?.contentId && /^[A-Za-z0-9_-]{11}$/.test(lockup.contentId) && !seen.has(lockup.contentId)) {
      const metadata = lockup.metadata?.lockupMetadataViewModel;
      const title = metadata?.title?.content || '';
      const artist = metadata?.metadata?.contentMetadataViewModel?.metadataRows?.[0]?.metadataParts?.[0]?.text?.content || 'Artista';
      if (title) {
        seen.add(lockup.contentId);
        results.push({ id: 'yt:' + lockup.contentId, videoId: lockup.contentId, title: cleanTitle(title), artist, kind: 'video',
          art: 'https://i.ytimg.com/vi/' + lockup.contentId + '/hqdefault.jpg', duration: 0 });
      }
    }
    for (const child of Object.values(value)) visit(child);
  };
  visit(data);
  return results;
}
function artworkBackground(url) {
  const source = String(url || '').trim();
  if (!source) return '';
  const video = source.match(/^https?:\/\/i\.ytimg\.com\/vi(?:_webp)?\/([A-Za-z0-9_-]{11})\/[^/?#]+/i);
  let candidates = [source];
  if (video) {
    const base = `https://i.ytimg.com/vi/${video[1]}/`;
    candidates = ['maxresdefault.jpg', 'sddefault.jpg', 'hqdefault.jpg', 'default.jpg'].map((file) => base + file);
  } else if (/googleusercontent\.com/i.test(source)) {
    const highResolution = source.replace(/=w\d+-h\d+(?:-[^?&#]*)?/i, '=w1200-h1200-l90-rj');
    if (highResolution !== source) candidates = [highResolution, source];
  }
  return candidates.map((candidate) => `url("${candidate.replace(/"/g, '%22')}")`).join(', ');
}
function cleanTitle(value) {
  return String(value).replace(/\s*[([](?:official\s*)?(?:music\s*)?video[)\]]/ig, '').trim();
}
function shuffleItems(items) {
  for (let index = items.length - 1; index > 0; index--) {
    const swap = Math.floor(Math.random() * (index + 1));
    [items[index], items[swap]] = [items[swap], items[index]];
  }
  return items;
}
async function searchMusic(query, limit = 30) {
  const data = await innerTubeRequest('search', { query });
  return rankTracks(extractTracks(data), query).slice(0, limit);
}
async function loadHome() {
  loading();
  try {
    const discoveryQueries = [
      'nuevos lanzamientos música latina', 'éxitos pop en español', 'reggaetón actual', 'indie latino nuevo',
      'música urbana popular', 'bachata y salsa actual', 'rock en español', 'música para descubrir',
      'pop internacional nuevos éxitos', 'electrónica latina', 'canciones virales actuales', 'música alternativa'
    ];
    const chosen = shuffleItems([...discoveryQueries]).slice(0, 3);
    const responses = await Promise.allSettled(chosen.map((query) => searchMusic(query, 30)));
    const tracks = [...new Map(responses.flatMap((result) => result.status === 'fulfilled' ? result.value : [])
      .map((track) => [track.id, track])).values()];
    if (!tracks.length) throw new Error('No hay recomendaciones disponibles en este momento.');
    renderHome(shuffleItems(tracks));
  } catch (error) { empty('No se pudo cargar música', error.message); }
}
function renderHome(tracks) {
  const songs = tracks.filter((track) => track.kind === 'song' && track.art);
  const recommendations = (songs.length >= 4 ? songs : tracks).slice(0, 18);
  const featured = recommendations[0];
  ui.content.replaceChildren();
  const hour = new Date().getHours();
  const greeting = hour < 12 ? 'Buenos días' : hour < 19 ? 'Buenas tardes' : 'Buenas noches';
  const welcome = el('div', 'home-welcome');
  welcome.append(el('p', 'eyebrow', 'RITMO · PARA TI'),
    el('h2', null, `${greeting}, ¿qué quieres escuchar?`),
    el('p', 'welcome-subtitle', 'Una selección de música para acompañar tu día.'));
  ui.content.append(welcome);

  if (featured) {
    const hero = el('section', 'featured');
    const cover = el('div', 'featured-cover');
    cover.style.backgroundImage = artworkBackground(featured.art);
    const shade = el('div', 'featured-shade');
    const copy = el('div', 'featured-copy');
    copy.append(el('span', 'featured-label', 'RECOMENDADO PARA TI'),
      el('h3', null, featured.title), el('p', null, featured.artist));
    const play = el('button', 'btn featured-play');
    play.append(labeledIcon('play', 'Escuchar ahora'));
    play.addEventListener('click', () => playQueue(recommendations, 0));
    const download = el('button', 'btn featured-download');
    download.dataset.downloadLabel = 'Descargar';
    updateDownloadButton(download, featured.id);
    download.addEventListener('click', (event) => { event.stopPropagation(); downloadTrack(featured); });
    const featuredActions = el('div', 'featured-actions');
    featuredActions.append(play, download);
    copy.append(featuredActions);
    hero.append(cover, shade, copy);
    hero.addEventListener('click', (event) => { if (event.target !== play) playQueue(recommendations, 0); });
    ui.content.append(hero);
  }
  appendMusicSection('Hecho para ti', 'Selección basada en lo que suena ahora', recommendations.slice(0, 8), recommendations);
  if (state.favorites.length) appendMusicSection('Tu música', 'Tus favoritos, listos para volver a escuchar', state.favorites.slice(0, 8), state.favorites);
  if (recommendations.length > 8) appendMusicSection('Más para descubrir', 'Sigue explorando nuevos sonidos', recommendations.slice(8, 16), recommendations);
}
function appendMusicSection(title, subtitle, items, queue) {
  if (!items.length) return;
  const section = el('section', 'music-section');
  const heading = el('div', 'music-section-heading');
  const headingCopy = el('div');
  headingCopy.append(el('h2', null, title), el('p', null, subtitle));
  heading.append(headingCopy);
  section.append(heading);
  const grid = el('div', 'music-grid');
  items.forEach((track) => {
    const card = el('article', 'music-card');
    const cover = el('div', 'music-cover');
    if (track.art) cover.style.backgroundImage = artworkBackground(track.art);
    const actions = el('div', 'music-card-actions');
    const play = el('button', 'music-play');
    setIcon(play, 'play');
    play.setAttribute('aria-label', `Reproducir ${track.title}`);
    play.addEventListener('click', () => playQueue(queue, Math.max(0, queue.indexOf(track))));
    const download = el('button', 'music-download');
    updateDownloadButton(download, track.id);
    download.setAttribute('aria-label', `Descargar ${track.title}`);
    download.addEventListener('click', (event) => { event.stopPropagation(); downloadTrack(track); });
    actions.append(play, download);
    cover.append(actions, addToPlaylistButton(track, 'icon-btn music-add'));
    card.append(cover, el('h3', 'music-title', track.title), el('p', 'music-artist', track.artist));
    card.addEventListener('click', (event) => { if (!event.target.closest('button')) playQueue(queue, Math.max(0, queue.indexOf(track))); });
    grid.append(card);
  });
  section.append(grid);
  ui.content.append(section);
}
function loading() { ui.content.replaceChildren(el('div', 'empty', 'Cargando música…')); }
function empty(title, detail) {
  const box = el('div', 'empty');
  box.append(el('strong', null, title));
  if (detail) box.append(el('div', 'empty-detail', detail));
  ui.content.replaceChildren(box);
}
function renderSearchIdle() { empty('Busca una canción', 'Escribe el nombre de una canción o artista.'); }
function renderTracks(tracks, heading) {
  ui.content.replaceChildren();
  if (heading) ui.content.append(el('h2', 'section-title', heading));
  if (!tracks.length) { empty('Sin resultados', 'Prueba otra búsqueda.'); return; }
  const rows = el('div', 'rows');
  tracks.forEach((track, index) => rows.append(trackRow(track, tracks, index)));
  ui.content.append(rows);
}
function trackRow(track, queue, index) {
  const row = el('div', 'row');
  row.dataset.id = track.id;
  const art = el('div', 'row-art');
  if (track.art) art.style.backgroundImage = artworkBackground(track.art);
  const text = el('div', 'row-text');
  text.append(el('div', 'row-title', track.title), el('div', 'row-who', track.artist));
  const actions = el('div', 'row-actions');
  const favorite = el('button', 'icon-btn' + (isFavorite(track) ? ' on' : ''));
  setIcon(favorite, 'heart');
  favorite.title = 'Me gusta';
  favorite.addEventListener('click', (event) => { event.stopPropagation(); toggleFavorite(track); });
  const download = el('button', 'icon-btn');
  updateDownloadButton(download, track.id);
  download.addEventListener('click', (event) => { event.stopPropagation(); downloadTrack(track); });
  actions.append(favorite, download, addToPlaylistButton(track));
  row.append(art, text, el('span', 'row-meta', track.duration ? formatTime(track.duration) : ''), actions);
  row.addEventListener('click', () => playQueue(queue, index));
  return row;
}
async function searchView(query) {
  const requestId = ++state.request;
  state.searchRequest = requestId;
  state.searchResults = [];
  state.searchContinuation = null;
  state.searchLoading = false;
  loading();
  try {
    const data = await innerTubeRequest('search', { query });
    if (requestId !== state.request || state.view !== 'search') return;
    const sections = extractSearchSections(data);
    state.searchResults = rankTracks(sections.tracks, query);
    state.searchContinuation = findContinuation(data);
    sections.tracks = state.searchResults;
    renderSearchResults(query, sections, false);
  } catch (error) {
    if (requestId === state.request) empty('No se pudo buscar', error.message + '. Recarga la página para volver a conectar el proxy.');
  }
}

function extractSearchSections(data) {
  const sections = { tracks: [], artists: [], playlists: [] };
  const seen = { tracks: new Set(), artists: new Set(), playlists: new Set() };
  const add = (target, item) => {
    const key = item.id || item.title;
    if (!key || seen[target].has(key)) return;
    seen[target].add(key); sections[target].push(item);
  };
  const visit = (node) => {
    if (!node || typeof node !== 'object') return;
    const lockup = node.musicResponsiveListItemRenderer || node.musicTwoRowItemRenderer || node.musicCardShelfRenderer;
    const playlistRenderer = node.playlistRenderer;
    const artistRenderer = node.artistRenderer || node.channelRenderer;
    if (playlistRenderer?.playlistId && textOf(playlistRenderer.title)) add('playlists', {
      id: 'VL' + playlistRenderer.playlistId, title: textOf(playlistRenderer.title),
      subtitle: textOf(playlistRenderer.shortBylineText) || 'Playlist', art: playlistRenderer.thumbnails?.at(-1)?.url || ''
    });
    const artistId = artistRenderer?.browseId || artistRenderer?.channelId;
    if (artistId && textOf(artistRenderer.title)) add('artists', {
      id: artistId, title: textOf(artistRenderer.title), subtitle: 'Artista',
      art: artistRenderer.thumbnail?.musicThumbnailRenderer?.thumbnail?.thumbnails?.at(-1)?.url ||
        artistRenderer.thumbnail?.thumbnails?.at(-1)?.url || artistRenderer.thumbnails?.at(-1)?.url || ''
    });
    const title = textOf(lockup?.title || lockup?.flexColumns?.[0]?.musicResponsiveListItemFlexColumnRenderer?.text || lockup?.header?.title);
    const navigation = lockup?.navigationEndpoint || lockup?.thumbnail?.musicThumbnailRenderer?.thumbnail?.navigationEndpoint;
    const endpoint = navigation?.browseEndpoint || navigation?.watchEndpoint || {};
    const thumbnail = lockup?.thumbnail?.musicThumbnailRenderer?.thumbnail?.thumbnails?.at(-1)?.url ||
      lockup?.thumbnailRenderer?.musicThumbnailRenderer?.thumbnail?.thumbnails?.at(-1)?.url || '';
    const browseId = endpoint.browseId || '';
    const playlistId = endpoint.playlistId || lockup?.playlistId || (/^(VL|PL|RD|OLAK5)/.test(browseId) ? browseId : '');
    const subtitle = textOf(lockup?.subtitle || lockup?.flexColumns?.[1]?.musicResponsiveListItemFlexColumnRenderer?.text);
    const channel = browseId.startsWith('UC') || /artist|channel/i.test(browseId);
    if (title && channel && thumbnail) add('artists', { id: browseId, title, subtitle: subtitle || 'Artista', art: thumbnail });
    if (title && playlistId) add('playlists', { id: playlistId, title, subtitle: subtitle || 'Playlist', art: thumbnail });
    for (const child of Object.values(node)) visit(child);
  };
  visit(data);
  sections.tracks = extractTracks(data);
  return sections;
}
function findContinuation(data) {
  let token = null;
  const visit = (node) => {
    if (!node || typeof node !== 'object' || token) return;
    const continuation = node.continuationItemRenderer?.continuationEndpoint?.continuationCommand?.token ||
      node.nextContinuationData?.continuation || node.continuation?.reloadContinuationData?.continuation;
    if (continuation) { token = continuation; return; }
    for (const child of Object.values(node)) visit(child);
  };
  visit(data); return token;
}
function renderSearchResults(query, sections, append) {
  if (!append) ui.content.replaceChildren();
  else ui.content.querySelector('.search-more')?.remove();
  if (!append) ui.content.append(el('h2', 'section-title', `Resultados para “${query}”`));
  const addShelf = (title, entries, kind) => {
    if (!entries.length) return;
    let shelf = append && kind === 'tracks' ? ui.content.querySelector('.rows') : null;
    if (!shelf) {
      const section = el('section', 'search-shelf');
      section.append(el('h3', 'section-title', title));
      shelf = el('div', kind === 'tracks' ? 'rows' : 'search-entity-grid');
      section.append(shelf); ui.content.append(section);
    }
    if (append && kind === 'tracks') shelf.replaceChildren();
    if (kind === 'tracks') entries.forEach((track) => {
      const index = state.searchResults.findIndex((item) => item.id === track.id);
      shelf.append(trackRow(track, state.searchResults, Math.max(0, index)));
    });
    else entries.forEach((entry) => {
      const button = el('button', 'search-entity');
      const art = el('span', 'search-entity-art'); if (entry.art) art.style.backgroundImage = artworkBackground(entry.art);
      button.append(art, el('span', 'search-entity-title', entry.title), el('span', 'search-entity-subtitle', entry.subtitle));
      button.addEventListener('click', () => {
        if (kind === 'artists') { ui.search.value = entry.title; searchView(entry.title); }
        else loadPlaylistSearch(entry);
      });
      shelf.append(button);
    });
  };
  addShelf('Canciones', sections.tracks, 'tracks');
  if (!append) { addShelf('Artistas', sections.artists, 'artists'); addShelf('Playlists', sections.playlists, 'playlists'); }
  if (!append && !sections.tracks.length && !sections.artists.length && !sections.playlists.length) {
    ui.content.append(el('div', 'empty', 'No se encontraron resultados.'));
  }
  if (state.searchContinuation) {
    const more = el('div', 'search-more');
    const button = el('button', 'btn ghost', state.searchLoading ? 'Cargando más…' : 'Cargar más resultados');
    button.disabled = state.searchLoading;
    button.addEventListener('click', loadMoreSearch);
    more.append(button); ui.content.append(more);
    if (!state.searchObserver) state.searchObserver = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) loadMoreSearch();
    }, { rootMargin: '500px' });
    state.searchObserver.disconnect(); state.searchObserver.observe(more);
  }
}
async function loadMoreSearch() {
  if (!state.searchContinuation || state.searchLoading || state.view !== 'search') return;
  state.searchLoading = true;
  const requestId = state.searchRequest;
  const token = state.searchContinuation;
  try {
    const data = await innerTubeRequest('search', { continuation: token });
    if (requestId !== state.request || state.view !== 'search') return;
    const section = extractSearchSections(data);
    const existing = new Set(state.searchResults.map((track) => track.id));
    const fresh = section.tracks.filter((track) => !existing.has(track.id));
    state.searchResults = rankTracks([...state.searchResults, ...fresh], ui.search.value.trim());
    state.searchContinuation = findContinuation(data);
    state.searchLoading = false;
    const main = document.querySelector('.main');
    const anchor = [...ui.content.querySelectorAll('.row')].find((row) => row.getBoundingClientRect().top >= main.getBoundingClientRect().top);
    const anchorId = anchor?.dataset.id, anchorTop = anchor?.getBoundingClientRect().top;
    renderSearchResults(ui.search.value.trim(), { tracks: state.searchResults, artists: section.artists, playlists: section.playlists }, true);
    const restored = [...ui.content.querySelectorAll('.row')].find((row) => row.dataset.id === anchorId);
    if (restored && anchorTop !== undefined) main.scrollTop += restored.getBoundingClientRect().top - anchorTop;
  } catch (error) { state.searchLoading = false; toast('No se pudieron cargar más resultados · ' + error.message); }
}
async function loadPlaylistSearch(playlist) {
  try {
    const data = await innerTubeRequest('browse', { browseId: playlist.id });
    const tracks = extractTracks(data);
    if (!tracks.length) throw new Error('La playlist no tiene canciones disponibles');
    renderTracks(tracks, playlist.title);
    const heading = ui.content.querySelector('.section-title');
    if (heading) heading.textContent = playlist.title;
  } catch (error) { toast('No se pudo abrir la playlist · ' + error.message); }
}

function toggleFavorite(track) {
  const index = state.favorites.findIndex((item) => item.id === track.id);
  if (index >= 0) state.favorites.splice(index, 1);
  else state.favorites.unshift({ ...track });
  if (isNativeApp) NativePlayback.setFavorite({ track, favorite: index < 0 }).catch(() => {});
  save();
  updatePlayer();
  if (state.view === 'favorites') renderFavorites();
  else document.querySelectorAll('.row').forEach((row) => {
    if (row.dataset.id === track.id) row.querySelector('.icon-btn')?.classList.toggle('on', isFavorite(track));
  });
}
function renderFavorites() {
  if (state.favorites.length) {
    renderTracks(state.favorites, 'Me gusta');
    addBulkDownload('Descargar todo', state.favorites);
  } else empty('Aún no tienes canciones guardadas', 'Pulsa el corazón en una canción para añadirla.');
  addSpotifyLikesImport();
  ui.content.prepend(csvExportButton('Me gusta', state.favorites));
}
function addSpotifyLikesImport(container = ui.content) {
  const section = el('section', 'spotify-import');
  const input = el('input');
  input.type = 'file'; input.accept = '.json,.csv,application/json,text/csv'; input.hidden = true;
  const button = el('button', 'btn ghost', 'Importar Me gusta de Spotify');
  button.addEventListener('click', () => input.click());
  input.addEventListener('change', async () => {
    const file = input.files?.[0];
    if (!file) return;
    button.disabled = true;
    try {
      const imported = await importSpotifyLikesFile(file, (done, total) => { button.textContent = `Importando ${done}/${total}…`; });
      toast(`Se importaron ${imported} canciones a Favoritos`);
      route('favorites');
    } catch (error) {
      toast(error.message || 'No se pudo importar el archivo');
      button.disabled = false;
      button.textContent = 'Importar Me gusta de Spotify';
      input.value = '';
    }
  });
  const help = el('a', 'link', 'Cómo descargar tus datos');
  help.href = 'https://support.spotify.com/es/article/data-rights-and-privacy-settings/';
  help.target = '_blank'; help.rel = 'noopener noreferrer';
  section.append(button, input, el('p', 'hint', 'Selecciona YourLibrary.json o un CSV con las columnas de canción y artista.'), help);
  if (container === ui.content) container.prepend(section);
  else container.append(section);
}
async function importSpotifyLikesFile(file, onProgress) {
  const text = await file.text();
  const entries = /\.csv$/i.test(file.name) || !/^\s*[\[{]/.test(text)
    ? parseSpotifyLikesCsv(text)
    : parseSpotifyLikesJson(text);
  if (!entries.length) throw new Error('No encontré canciones guardadas en ese archivo. Usa YourLibrary.json.');

  const unique = new Map();
  for (const track of entries) unique.set(`${normalizeMusicText(track.artist)}|${normalizeMusicText(track.title)}`, track);
  const items = [...unique.values()];
  const existing = new Set(state.favorites.map((track) => `${normalizeMusicText(track.artist)}|${normalizeMusicText(track.title)}`));
  let imported = 0;
  for (let offset = 0; offset < items.length; offset += 4) {
    const batch = items.slice(offset, offset + 4);
    const matches = await Promise.all(batch.map(resolveImportedTrack));
    for (const match of matches) {
      if (!match) continue;
      const key = `${normalizeMusicText(match.artist)}|${normalizeMusicText(match.title)}`;
      if (existing.has(key)) continue;
      existing.add(key);
      state.favorites.unshift(match);
      imported++;
    }
    onProgress?.(Math.min(offset + batch.length, items.length), items.length);
  }
  save();
  if (!imported) throw new Error('No se encontraron canciones nuevas que coincidan.');
  return imported;
}
async function resolveImportedTrack(source) {
  if (/^yt:[A-Za-z0-9_-]{11}$/.test(source.id || '')) return { ...source, videoId: source.id.slice(3) };
  const results = await searchMusic(source.artist + ' ' + source.title, 20).catch(() => []);
  const title = normalizeMusicText(source.title), artist = normalizeMusicText(source.artist);
  const best = results.find((item) => {
    const candidateTitle = normalizeMusicText(item.title), candidateArtist = normalizeMusicText(item.artist);
    return title && artist && (candidateTitle === title || candidateTitle.includes(title)) &&
      (candidateArtist === artist || candidateArtist.includes(artist));
  });
  return best ? { ...best, title: source.title, artist: source.artist } : null;
}
function parseSpotifyLikesJson(text) {
  let data;
  try { data = JSON.parse(text.replace(/^\uFEFF/, '')); }
  catch { throw new Error('No pude leer el JSON. Selecciona YourLibrary.json o un CSV válido.'); }
  const sourceTracks = Array.isArray(data) ? data : (data.tracks || data.items || data.savedTracks || []);
  return sourceTracks.map((entry) => {
    const item = entry.track || entry.item || entry;
    const title = spotifyText(item.track || item.trackName || item.name || item.title);
    const artist = spotifyText(item.artist || item.artistName || item.artists || item.creator);
    return title && artist ? { title, artist } : null;
  }).filter(Boolean);
}
function parseSpotifyLikesCsv(text) { return parseLibraryCsv(text); }
function normalizeMusicText(value) {
  return String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
    .replace(/\([^)]*\)|\[[^\]]*\]/g, ' ').replace(/[^a-z0-9]+/g, ' ').trim().replace(/\s+/g, ' ');
}
function addBulkDownload(label, tracks) {
  const button = el('button', 'btn ghost bulk-download');
  button.append(labeledIcon('download', label));
  button.addEventListener('click', async () => {
    button.disabled = true;
    for (const track of tracks) await downloadTrack(track);
    button.disabled = false;
  });
  ui.content.prepend(button);
}

function addToPlaylistButton(track, className = 'icon-btn') {
  const button = el('button', className);
  button.type = 'button';
  button.title = 'Añadir a playlist';
  button.setAttribute('aria-label', 'Añadir ' + track.title + ' a una playlist');
  button.append(icon('plus'));
  button.addEventListener('click', (event) => { event.stopPropagation(); showPlaylistPicker(track); });
  return button;
}
function showPlaylistPicker(track = null) {
  document.querySelector('.playlist-dialog')?.close();
  const previousFocus = document.activeElement;
  const dialog = el('dialog', 'playlist-dialog');
  const heading = el('header', 'playlist-dialog-heading');
  const title = el('h2', null, track ? 'Añadir a playlist' : 'Nueva playlist');
  title.id = 'playlistDialogTitle';
  dialog.setAttribute('aria-labelledby', title.id);
  const close = el('button', 'icon-btn'); close.type = 'button'; close.append(icon('close'));
  close.setAttribute('aria-label', 'Cerrar'); close.addEventListener('click', () => dialog.close());
  heading.append(title, close); dialog.append(heading);
  if (track) dialog.append(el('p', 'playlist-dialog-track', track.title + ' · ' + track.artist));
  const form = el('form', 'field playlist-create');
  const label = el('label', null, 'Crear una playlist'); label.htmlFor = 'newPlaylistName';
  const input = el('input'); input.id = 'newPlaylistName'; input.placeholder = 'Nombre de la playlist';
  input.required = true; input.maxLength = 80; input.autocomplete = 'off';
  const create = el('button', 'btn', track ? 'Crear y añadir' : 'Crear'); create.type = 'submit';
  form.append(label, input, create);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const name = input.value.trim();
    if (!name) { input.focus(); return; }
    const playlist = { id: 'local_' + crypto.randomUUID(), title: name, source: 'custom', tracks: track ? [{ ...track }] : [] };
    state.playlists.unshift(playlist); save();
    if (state.view === 'playlists') { state.activePlaylistId = playlist.id; renderPlaylists(); }
    dialog.close(); toast(track ? 'Canción añadida a ' + name : 'Playlist creada');
  });
  dialog.append(form);
  if (track) {
    const list = el('div', 'playlist-picker-list');
    state.playlists.forEach((playlist) => {
      const added = playlist.tracks.some((item) => item.id === track.id);
      const choose = el('button', 'playlist-choice'); choose.type = 'button';
      choose.append(icon(added ? 'check' : 'folder'), el('span', null, playlist.title), el('small', null, added ? 'Ya está añadida' : playlist.tracks.length + ' canciones'));
      choose.disabled = added;
      choose.addEventListener('click', () => {
        if (!playlist.tracks.some((item) => item.id === track.id)) playlist.tracks.push({ ...track });
        save(); if (state.view === 'playlists') renderPlaylists(); dialog.close(); toast('Añadida a ' + playlist.title);
      });
      list.append(choose);
    });
    if (!state.playlists.length) list.append(el('p', 'hint', 'Crea tu primera playlist para guardar esta canción.'));
    dialog.append(list);
  }
  dialog.addEventListener('click', (event) => { if (event.target === dialog) dialog.close(); });
  dialog.addEventListener('close', () => { dialog.remove(); if (previousFocus?.isConnected) previousFocus.focus(); });
  document.body.append(dialog); dialog.showModal();
}
function csvExportButton(title, tracks) {
  const button = el('button', 'btn ghost csv-export', 'Respaldar CSV'); button.type = 'button';
  button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      const content = libraryCsv(title, tracks);
      const filename = safeName(title) + '.csv';
      if (isNativeApp) {
        const result = await NativePlayback.exportCsv({ filename, content });
        if (!result.cancelled) toast('Respaldo guardado');
      } else {
        const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }));
        const link = el('a'); link.href = url; link.download = filename;
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 30000);
        toast('Respaldo CSV preparado');
      }
    } catch (error) { toast(error.message || 'No se pudo guardar el respaldo'); }
    finally { button.disabled = false; }
  });
  return button;
}
function renderPlaylists() {
  ui.content.replaceChildren();
  if (state.activePlaylistId) {
    const playlist = state.playlists.find((item) => item.id === state.activePlaylistId);
    if (playlist) { renderPlaylistFolder(playlist); return; }
    state.activePlaylistId = null;
  }
  const toolbar = el('div', 'library-toolbar');
  const create = el('button', 'btn', 'Nueva playlist');
  create.addEventListener('click', () => showPlaylistPicker());
  const csv = el('button', 'btn ghost', 'Importar CSV');
  const csvFile = el('input'); csvFile.type = 'file'; csvFile.accept = '.csv,text/csv'; csvFile.hidden = true;
  csv.addEventListener('click', () => csvFile.click());
  csvFile.addEventListener('change', async () => {
    const file = csvFile.files?.[0]; if (!file) return;
    csv.disabled = true; csv.textContent = 'Importando…';
    try {
      const sources = parseSpotifyLikesCsv(await file.text());
      if (!sources.length) throw new Error('El CSV no contiene canciones');
      const tracks = [];
      for (const source of sources) {
        const match = await resolveImportedTrack(source);
        if (match) tracks.push(match);
      }
      if (!tracks.length) throw new Error('No se pudieron encontrar las canciones del CSV');
      const playlist = { id: 'local_' + crypto.randomUUID(), title: file.name.replace(/\.csv$/i, ''), source: 'csv', tracks: uniqueTracks(tracks) };
      state.playlists.unshift(playlist); save(); state.activePlaylistId = playlist.id; renderPlaylists();
      toast('Importadas ' + playlist.tracks.length + ' de ' + sources.length + ' canciones');
    } catch (error) { toast(error.message); csv.disabled = false; csv.textContent = 'Importar CSV'; }
    finally { csvFile.value = ''; }
  });
  toolbar.append(create, csv, csvFile); ui.content.append(toolbar);
  const box = el('div', 'yt-hero');
  box.append(el('h2', null, 'Importar playlist'), el('p', null, 'Pega un enlace compartido de Spotify o una playlist pública.'));
  const form = el('div', 'field');
  const input = el('input');
  input.type = 'url'; input.placeholder = 'https://open.spotify.com/playlist/...';
  const button = el('button', 'btn', 'Importar');
  button.addEventListener('click', async () => {
    if (!input.value.trim()) return;
    button.disabled = true; button.textContent = 'Importando…';
    try {
      const playlist = await importPlaylist(input.value.trim());
      state.playlists.unshift(playlist); save(); renderPlaylists();
      toast('Playlist importada · ' + playlist.tracks.length + ' canciones');
    } catch (error) { toast(error.message || 'No se pudo importar'); button.disabled = false; button.textContent = 'Importar'; }
  });
  form.append(input, button); box.append(form); ui.content.append(box);
  addSpotifyLikesImport(box);
  if (!state.playlists.length) { ui.content.append(el('div', 'empty', 'Tus playlists aparecerán aquí.')); return; }
  const folders = el('div', 'playlist-folder-grid');
  state.playlists.forEach((playlist) => {
    const folder = el('button', 'playlist-folder');
    folder.type = 'button';
    folder.setAttribute('aria-label', playlist.title + ', ' + playlist.tracks.length + ' canciones');
    const cover = el('span', 'playlist-folder-art');
    if (playlist.tracks[0]?.art) cover.style.backgroundImage = artworkBackground(playlist.tracks[0].art);
    const badge = el('span', 'playlist-folder-badge');
    badge.append(icon('folder'));
    cover.append(badge);
    const copy = el('span', 'playlist-folder-copy');
    copy.append(el('strong', null, playlist.title), el('span', null, playlist.tracks.length + ' canciones'));
    folder.append(cover, copy);
    folder.addEventListener('click', () => { state.activePlaylistId = playlist.id; renderPlaylists(); });
    folders.append(folder);
  });
  ui.content.append(folders);
}
function renderPlaylistFolder(playlist) {
  const back = el('button', 'btn ghost playlist-back');
  back.append(labeledIcon('previous', 'Mis playlists'));
  back.addEventListener('click', () => { state.activePlaylistId = null; renderPlaylists(); });
  ui.content.append(back);
  const section = el('section', 'playlist-card playlist-detail');
  const header = el('div', 'playlist-head');
  header.append(el('strong', null, playlist.title + ' · ' + playlist.tracks.length + ' canciones'));
  const actions = el('div', 'row-actions');
  const play = el('button', 'btn ghost');
  play.append(labeledIcon('play', 'Reproducir'));
  play.addEventListener('click', () => playQueue(playlist.tracks, 0));
  const download = el('button', 'btn ghost');
  download.append(labeledIcon('download', 'Descargar'));
  download.addEventListener('click', async () => { for (const track of playlist.tracks) await downloadTrack(track); });
  const remove = el('button', 'btn ghost');
  remove.append(labeledIcon('close', 'Quitar playlist'));
  remove.addEventListener('click', () => {
    state.playlists = state.playlists.filter((item) => item.id !== playlist.id);
    state.activePlaylistId = null;
    save(); renderPlaylists();
  });
  play.disabled = download.disabled = !playlist.tracks.length;
  actions.append(play, download, csvExportButton(playlist.title, playlist.tracks), remove); header.append(actions); section.append(header);
  const rows = el('div', 'rows');
  playlist.tracks.forEach((track, index) => rows.append(trackRow(track, playlist.tracks, index)));
  if (!playlist.tracks.length) rows.append(el('p', 'hint', 'Busca una canción y pulsa + para añadirla a esta playlist.'));
  section.append(rows); ui.content.append(section);
}
async function importPlaylist(url) {
  const youtubeList = /[?&]list=([A-Za-z0-9_-]+)/.exec(url);
  if (youtubeList) {
    const data = await innerTubeRequest('browse', { browseId: 'VL' + youtubeList[1] });
    const tracks = extractTracks(data);
    if (!tracks.length) throw new Error('La playlist no contiene canciones disponibles');
    return { id: 'list_' + youtubeList[1], title: findPlaylistTitle(data), tracks };
  }
  const spotify = /(?:open\.spotify\.com\/(?:intl-[a-z]{2}(?:-[A-Z]{2})?\/)?(?:user\/[^/]+\/)?(?:embed\/)?playlist\/|spotify:playlist:)([A-Za-z0-9]+)/i.exec(url);
  if (!spotify) throw new Error('Enlace de playlist no reconocido');
  const response = await apiFetch('/spotify/embed/playlist/' + encodeURIComponent(spotify[1]));
  if (!response.ok) throw new Error('Spotify no dejó leer la playlist. Revisa que el enlace se pueda compartir.');
  const entity = parseSpotifyPlaylist(await response.text());
  if (!entity?.trackList?.length) throw new Error('No se encontraron canciones. Asegúrate de que la playlist sea pública o esté compartida con enlace.');
  const items = entity.trackList.filter((item) => item && (item.title || item.name));
  const tracks = [];
  for (let offset = 0; offset < items.length; offset += 4) {
    const batch = items.slice(offset, offset + 4);
    const matches = await Promise.all(batch.map(async (item) => {
      const title = spotifyText(item.title || item.name);
      const artist = spotifyText(item.subtitle || item.artist || item.artists?.map?.((artist) => artist.name).join(', ')) || 'Artista';
      if (!title) return null;
      const result = await searchMusic(`${artist} - ${title}`, 1).catch(() => []);
      return result[0] ? { ...result[0], title, artist } : null;
    }));
    tracks.push(...matches.filter(Boolean));
  }
  if (!tracks.length) throw new Error('No se encontraron canciones para esta playlist');
  return { id: 'spotify_' + spotify[1], title: spotifyText(entity.title || entity.name) || 'Playlist de Spotify', tracks };
}
function spotifyText(value) {
  if (typeof value === 'string') return value.trim();
  if (value?.text) return spotifyText(value.text);
  if (value?.title) return spotifyText(value.title);
  if (value?.name) return spotifyText(value.name);
  if (Array.isArray(value)) return value.map(spotifyText).filter(Boolean).join(', ');
  return '';
}
function parseSpotifyPlaylist(html) {
  const scripts = [...String(html).matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)];
  for (const [, attributes, content] of scripts) {
    const body = content.trim();
    if (!body || !/(?:__NEXT_DATA__|trackList|MusicPlaylist|playlist)/i.test(attributes + body)) continue;
    try {
      const playlist = findSpotifyPlaylistEntity(JSON.parse(body));
      if (playlist) return playlist;
    } catch { /* Busca otro bloque JSON del embed */ }
  }
  return null;
}
function findSpotifyPlaylistEntity(root) {
  const pending = [root];
  const visited = new Set();
  while (pending.length) {
    const node = pending.pop();
    if (!node || typeof node !== 'object' || visited.has(node)) continue;
    visited.add(node);
    if (Array.isArray(node.trackList) && node.trackList.length) return node;
    if (Array.isArray(node.tracks?.items) && node.tracks.items.length) {
      return { title: node.name || node.title, trackList: node.tracks.items.map((entry) => {
        const track = entry.item || entry.track || entry;
        return { title: track.name || track.title, subtitle: track.artists?.map?.((artist) => artist.name).join(', ') || track.artist };
      }) };
    }
    pending.push(...Object.values(node));
  }
  return null;
}
function findPlaylistTitle(data) {
  return data.header?.playlistHeaderRenderer?.title?.simpleText ||
    data.header?.pageHeaderRenderer?.pageTitle?.content || 'Playlist';
}

async function playQueue(queue, index) {
  if (!queue?.length) return;
  const continuingQueue = queue === state.queue;
  if (isNativeApp && continuingQueue) {
    try {
      await NativePlayback.playAtIndex({ index });
      await syncNativeState();
    } catch (error) { toast(error.message || 'No se pudo cambiar de canción'); }
    return;
  }
  if (!continuingQueue) {
    const selectedId = queue[index]?.id;
    queue = uniqueTracks(queue);
    index = Math.max(0, queue.findIndex((track) => track.id === selectedId));
    originalQueue = [...queue];
    if (state.shuffle && !isNativeApp) queue = orderUpcoming(queue, index);
  }
  state.queue = queue;
  state.index = Math.max(0, Math.min(index, queue.length - 1));
  state.current = queue[state.index];
  openPlayer();
  updatePlayer();
  if (isNativeApp) {
    try {
      await NativePlayback.play({ tracks: state.queue, index: state.index, shuffle: state.shuffle });
      setPlayIcon(true);
      syncNativeState();
    } catch (error) { toast('No se pudo iniciar la reproducción · ' + (error?.message || '')); }
    return;
  }
  audio.pause();
  selectingPlaybackSource = true;
  try {
    audio.src = '/stream?id=' + encodeURIComponent(videoId(state.current)) + '&play=1';
    audio.load();
    await audio.play();
    setPlayIcon(true);
  } catch (error) {
    if (/not supported|no supported source/i.test(error?.message || '')) {
      try {
        audio.src = '/stream?id=' + encodeURIComponent(videoId(state.current)) + '&play=1&variant=progressive';
        audio.load();
        await audio.play();
        setPlayIcon(true);
        return;
      } catch (fallbackError) { error = fallbackError; }
    }
    if (await tryAlternateRecording(state.current)) return;
    toast('No se pudo reproducir · ' + error.message);
  } finally {
    selectingPlaybackSource = false;
  }
}
async function tryAlternateRecording(track) {
  if (!track || isNativeApp) return false;
  const id = videoId(track);
  const attempt = (recordingFallbackCounts.get(id) || 0) + 1;
  if (attempt > 2) return false;
  recordingFallbackCounts.set(id, attempt);
  failedRecordingIds.add(id);
  const queue = state.queue;
  const index = state.index;
  try {
    const candidates = await searchMusic(`${track.title} ${track.artist}`, 30);
    if (state.current?.id !== track.id || state.queue !== queue || state.index !== index) return false;
    const normalizedTitle = normalizeMusicText(track.title).replace(/[^\p{L}\p{N}]/gu, '');
    const normalizedArtist = normalizeMusicText(track.artist).replace(/[^\p{L}\p{N}]/gu, '');
    const alternate = candidates.find((candidate) => {
      const candidateTitle = normalizeMusicText(candidate.title).replace(/[^\p{L}\p{N}]/gu, '');
      const candidateArtist = normalizeMusicText(candidate.artist).replace(/[^\p{L}\p{N}]/gu, '');
      return videoId(candidate) !== id && !failedRecordingIds.has(videoId(candidate)) && normalizedTitle &&
        (candidateTitle.includes(normalizedTitle) || normalizedTitle.includes(candidateTitle)) &&
        (!normalizedArtist || candidateArtist.includes(normalizedArtist) || normalizedArtist.includes(candidateArtist));
    });
    if (!alternate) return false;
    queue[index] = alternate;
    toast('Probando otra versión de la canción…');
    await playQueue(queue, index);
    return true;
  } catch { return false; }
}
function openPlayer() { ui.player.hidden = false; document.body.classList.add('player-open'); }
function closePlayer() { ui.player.hidden = true; document.body.classList.remove('player-open'); }
function updatePlayer() {
  const track = state.current;
  if (!track) return;
  ui.miniTitle.textContent = track.title;
  ui.miniArtist.textContent = track.artist;
  ui.playerTitle.textContent = track.title;
  ui.playerArtist.textContent = track.artist;
  ui.lyricsTrackTitle.textContent = track.title;
  ui.lyricsTrackArtist.textContent = track.artist;
  document.querySelector('#playerAddToPlaylist')?.remove();
  const addButton = addToPlaylistButton(track); addButton.id = 'playerAddToPlaylist';
  document.querySelector('.player-utility-actions').prepend(addButton);
  const art = artworkBackground(track.art);
  ui.miniArt.style.backgroundImage = art;
  ui.playerArt.style.backgroundImage = art;
  ui.lyricsTrackArt.style.backgroundImage = art;
  updateDownloadButton(ui.playerDownload, track.id);
  const favorite = isFavorite(track);
  ui.miniFav.classList.toggle('on', favorite);
  ui.playerFav.classList.toggle('on', favorite);
  ui.playerShuffle.classList.toggle('on', state.shuffle);
  ui.lyricsShuffle.classList.toggle('on', state.shuffle);
  setIcon(ui.playerRepeat, state.repeat === 1 ? 'repeatOne' : 'repeat');
  setIcon(ui.lyricsRepeat, state.repeat === 1 ? 'repeatOne' : 'repeat');
  ui.playerRepeat.classList.toggle('on', state.repeat > 0);
  ui.lyricsRepeat.classList.toggle('on', state.repeat > 0);
  renderQueue();
  if (!ui.lyrics.hidden && state.lyricsTrackId !== track.id) loadLyrics();
  setPlayIcon(isNativeApp ? state.isPlaying : !audio.paused);
}
function renderQueue() {
  const hasQueue = state.queue.length > 0;
  ui.playerQueue.hidden = !hasQueue;
  ui.playerQueue.classList.toggle('on', state.queueOpen);
  ui.playerQueue.setAttribute('aria-expanded', String(state.queueOpen));
  ui.playerQueue.setAttribute('aria-label', state.queueOpen ? 'Ocultar cola' : 'Mostrar cola');
  ui.playerQueue.title = state.queueOpen ? 'Ocultar cola' : 'Cola de reproducción';
  ui.lyricsQueueToggle?.classList.toggle('on', state.queueOpen);
  if (ui.lyricsQueueToggle) {
    ui.lyricsQueueToggle.hidden = !hasQueue;
    ui.lyricsQueueToggle.setAttribute('aria-expanded', String(state.queueOpen));
    ui.lyricsQueueToggle.setAttribute('aria-label', state.queueOpen ? 'Ocultar cola' : 'Mostrar cola');
    ui.lyricsQueueToggle.title = state.queueOpen ? 'Ocultar cola' : 'Mostrar cola';
  }
  ui.queuePanel.hidden = !state.queueOpen || !hasQueue;
  ui.lyricsQueueSlot.hidden = ui.lyrics.hidden || !state.queueOpen || !hasQueue;
  ui.lyrics.classList.toggle('has-queue', !ui.lyrics.hidden && state.queueOpen && hasQueue);
  ui.player.classList.toggle('queue-open', state.queueOpen && hasQueue);
  if (!hasQueue) return;
  ui.queueSummary.textContent = `${state.index + 1} de ${state.queue.length}`;
  ui.queueList.replaceChildren();
  state.queue.forEach((track, index) => {
    const item = el('button', 'queue-item' + (index === state.index ? ' is-current' : ''));
    item.type = 'button';
    item.setAttribute('aria-current', index === state.index ? 'true' : 'false');
    const art = el('span', 'queue-art');
    if (track.art) art.style.backgroundImage = artworkBackground(track.art);
    const copy = el('span', 'queue-copy');
    copy.append(el('span', 'queue-title', track.title), el('span', 'queue-artist', track.artist));
    item.append(art, copy);
    if (index === state.index) item.append(el('span', 'queue-playing', 'Reproduciendo'));
    item.addEventListener('click', () => playQueue(state.queue, index));
    ui.queueList.append(item);
  });
}
function setPlayIcon(playing) {
  state.isPlaying = playing;
  setIcon(ui.miniPlay, playing ? 'pause' : 'play');
  setIcon(ui.playerPlay, playing ? 'pause' : 'play');
  setIcon(ui.lyricsPlay, playing ? 'pause' : 'play');
  ui.miniPlay.title = ui.playerPlay.title = playing ? 'Pausar' : 'Reproducir';
  ui.lyricsPlay.title = playing ? 'Pausar' : 'Reproducir';
  ui.lyricsPlay.setAttribute('aria-label', ui.lyricsPlay.title);
}
function applyNativeState(nativeState) {
  if (!nativeState) return;
  const previousId = state.current?.id;
  state.queue = Array.isArray(nativeState.queue) ? nativeState.queue : state.queue;
  state.index = Number.isInteger(nativeState.index) ? nativeState.index : -1;
  state.current = nativeState.current || (state.index >= 0 ? state.queue[state.index] : null);
  state.nativePosition = Math.max(0, Number(nativeState.position) || 0);
  state.nativeDuration = Math.max(0, Number(nativeState.duration) || state.current?.duration || 0);
  state.shuffle = Boolean(nativeState.shuffle);
  state.repeat = Number(nativeState.repeat) === 1 ? 1 : Number(nativeState.repeat) === 2 ? 2 : 0;
  state.isPlaying = Boolean(nativeState.playing);
  if (state.current) {
    if (previousId !== state.current.id) updatePlayer();
    else { setPlayIcon(state.isPlaying); renderQueue(); }
  }
  progress();
  syncLyrics();
}
async function syncNativeState() {
  if (!isNativeApp || state.nativeSyncing) return;
  state.nativeSyncing = true;
  try { applyNativeState(await NativePlayback.getState()); }
  catch { /* El servicio se conecta en segundo plano al iniciar la app. */ }
  finally { state.nativeSyncing = false; }
}
async function toggleShuffle() {
  if (shuffleChanging) return;
  shuffleChanging = true;
  try {
    const enabled = !state.shuffle;
    if (isNativeApp) {
      applyNativeState(await NativePlayback.setShuffle({ enabled }));
    } else {
      state.shuffle = enabled;
      if (state.queue.length && state.index >= 0) {
        state.queue = orderUpcoming(state.queue, state.index, Math.random, enabled ? null : originalQueue);
      }
    }
    updatePlayer();
  } catch { toast('No se pudo cambiar el orden de reproducción'); }
  finally { shuffleChanging = false; }
}
function playNext() {
  if (isNativeApp) { NativePlayback.next().then(syncNativeState).catch(() => {}); return; }
  if (!state.queue.length) return;
  let next = state.index + 1;
  if (next >= state.queue.length) {
    if (state.repeat === 2) next = 0; else return;
  }
  playQueue(state.queue, next);
}
function playPrevious() {
  if (isNativeApp) { NativePlayback.previous().then(syncNativeState).catch(() => {}); return; }
  if (audio.currentTime > 4) { audio.currentTime = 0; return; }
  if (state.index > 0) playQueue(state.queue, state.index - 1);
  else if (state.repeat === 2 && state.queue.length) playQueue(state.queue, state.queue.length - 1);
}
function progress() {
  const duration = isNativeApp ? (state.nativeDuration || state.current?.duration || 0) : (audio.duration && Number.isFinite(audio.duration) ? audio.duration * 1000 : (state.current?.duration || 0));
  const current = isNativeApp ? state.nativePosition : (audio.currentTime || 0) * 1000;
  ui.miniCurrent.textContent = ui.playerCurrent.textContent = formatTime(current);
  ui.miniDuration.textContent = ui.playerDuration.textContent = formatTime(duration);
  ui.lyricsCurrent.textContent = formatTime(current);
  ui.lyricsDuration.textContent = formatTime(duration);
  const value = duration > 0 ? Math.round(current * 1000 / duration) : 0;
  for (const range of [ui.miniRange, ui.playerRange, ui.lyricsRange]) {
    if (range !== activeSeekRange) range.value = value;
  }
}
function seek(value, range = null) {
  activeSeekRange = range;
  const duration = isNativeApp ? (state.nativeDuration || state.current?.duration || 0)
    : (audio.duration && Number.isFinite(audio.duration) ? audio.duration * 1000 : (state.current?.duration || 0));
  if (duration <= 0) { activeSeekRange = null; return; }
  const position = duration * Number(value) / 1000;
  if (isNativeApp) {
    const generation = ++nativeSeekGeneration;
    state.nativePosition = position;
    progress();
    NativePlayback.seek({ position }).then(() => {
      const settleSeek = async () => {
        if (generation !== nativeSeekGeneration) return;
        await syncNativeState();
        if (generation !== nativeSeekGeneration) return;
        const reachedTarget = Math.abs(state.nativePosition - position) < 1500;
        if (reachedTarget || Date.now() - seekStartedAt >= 2200) {
          if (activeSeekRange === range) activeSeekRange = null;
          progress();
        } else {
          setTimeout(settleSeek, 160);
        }
      };
      const seekStartedAt = Date.now();
      setTimeout(settleSeek, 120);
    }).catch(() => {
      if (generation === nativeSeekGeneration && activeSeekRange === range) activeSeekRange = null;
    });
    return;
  }
  audio.currentTime = position / 1000;
}
function previewSeek(value) {
  const duration = isNativeApp ? (state.nativeDuration || state.current?.duration || 0)
    : (audio.duration && Number.isFinite(audio.duration) ? audio.duration * 1000 : (state.current?.duration || 0));
  if (duration <= 0) return;
  const target = duration * Number(value) / 1000;
  const timeLabel = formatTime(target);
  ui.miniCurrent.textContent = ui.playerCurrent.textContent = ui.lyricsCurrent.textContent = timeLabel;
}

async function downloadTrack(track) {
  void getTrackLyrics(track).catch(() => {});
  try {
    if (isNativeApp && state.downloadedIds.has(track.id)) { toast('Disponible sin conexión'); return; }
    if (isNativeApp && state.downloadTasks.get(track.id)?.state === 0) return;
    toast('Preparando descarga…');
    if (isNativeApp) {
      const result = await NativePlayback.download({ track });
      await syncDownloads();
      toast(result?.alreadyDownloaded ? 'Disponible sin conexión' : 'Descarga iniciada');
      return;
    }
    const response = await fetch('/stream?id=' + encodeURIComponent(videoId(track)) + '&download=1&variant=progressive');
    if (!response.ok) throw new Error('HTTP ' + response.status);
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = el('a');
    anchor.href = url;
    const extension = (response.headers.get('content-type') || '').includes('video/mp4') ? '.mp4' : '.m4a';
    anchor.download = safeName(track.artist + ' - ' + track.title) + extension;
    document.body.append(anchor); anchor.click(); anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
    toast(extension === '.mp4' ? 'Descarga iniciada · MP4' : 'Descarga iniciada');
  } catch (error) { toast('No se pudo descargar · ' + error.message); }
}
function safeName(name) { return name.replace(/[\\/:*?"<>|]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 120); }

async function loadLyrics() {
  if (!state.current) return;
  const requestedTrack = state.current;
  const requestId = ++state.lyricsRequest;
  state.lyricsTrackId = requestedTrack.id;
  state.lyricActive = -1;
  state.lyricsSynced = false;
  state.lyricLines = [];
  ui.lyricsText.scrollTop = 0;
  ui.lyricsTitle.textContent = state.current.title;
  ui.lyricsTrackTitle.textContent = state.current.title;
  ui.lyricsTrackArtist.textContent = state.current.artist;
  ui.lyricsText.textContent = 'Buscando letra…';
  ui.lyricsText.classList.remove('has-timestamps');
  ui.lyrics.style.setProperty('--lyrics-art', artworkBackground(state.current.art) || 'none');
  ui.lyricsTrackArt.style.backgroundImage = artworkBackground(state.current.art);
  if (ui.queuePanel.parentElement !== ui.lyricsQueueSlot) ui.lyricsQueueSlot.append(ui.queuePanel);
  ui.lyrics.hidden = false;
  placeLyricsTransport();
  renderQueue();
  const data = await getTrackLyrics(requestedTrack);
  if (requestId !== state.lyricsRequest || requestedTrack.id !== state.current?.id) return;
  state.lyricLines = parseSyncedLyrics(data?.syncedLyrics || '');
  state.lyricsSynced = state.lyricLines.length > 0;
  if (state.lyricsSynced) {
    ui.lyricsText.classList.add('has-timestamps');
    renderLyrics();
    syncLyrics(true);
  } else renderPlainLyrics(data?.plainLyrics || 'No se encontr? una letra disponible.');
}
async function getTrackLyrics(track) {
  if (pendingLyrics.has(track.id)) return pendingLyrics.get(track.id);
  const request = (async () => {
    const cached = await cachedLyrics(track.id);
    if (cached?.syncedLyrics || cached?.plainLyrics) return cached;
    let data = null;
    if (isNativeApp) data = await NativePlayback.getLyrics({ track }).catch(() => null);
    if (!data?.syncedLyrics && !data?.plainLyrics) {
      const params = '?artist_name=' + encodeURIComponent(track.artist) + '&track_name=' + encodeURIComponent(track.title);
      try {
        let response = await apiFetch('/lyrics/api/get' + params);
        data = response.ok ? await response.json() : null;
        if (!data?.syncedLyrics && !data?.plainLyrics) {
          response = await apiFetch('/lyrics/api/search?q=' + encodeURIComponent(track.artist + ' ' + track.title));
          const candidates = response.ok ? await response.json() : [];
          data = candidates.filter((entry) => normalizeText(entry.trackName) === normalizeText(track.title) &&
            normalizeText(entry.artistName) === normalizeText(track.artist) &&
            (!track.duration || !entry.duration || Math.abs(entry.duration - track.duration / 1000) < 12))
            .sort((a, b) => Number(Boolean(b.syncedLyrics)) - Number(Boolean(a.syncedLyrics)))[0] || null;
        }
      } catch { /* Continue with the other lyric libraries. */ }
    }
    if (!data?.syncedLyrics && !data?.plainLyrics) {
      const plainLyrics = await fetchLetrasLyrics(track) || await fetchLyricsOvh(track);
      if (plainLyrics) data = { plainLyrics };
    }
    if (!data?.syncedLyrics && !data?.plainLyrics) return null;
    const lyrics = { syncedLyrics: data.syncedLyrics || '', plainLyrics: data.plainLyrics || '' };
    await storeLyrics(track.id, lyrics).catch(() => {});
    if (isNativeApp) await NativePlayback.saveLyrics({ id: track.id, lyrics }).catch(() => {});
    return lyrics;
  })().finally(() => pendingLyrics.delete(track.id));
  pendingLyrics.set(track.id, request);
  return request;
}
function lyricsSlug(value) {
  return String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
    .replace(/&/g, ' y ').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}
async function fetchLetrasLyrics(track) {
  const artist = lyricsSlug(track.artist.replace(/\s+(feat\.?|ft\.?).*$/i, ''));
  const title = lyricsSlug(track.title.replace(/\s*[([](?:official\s*)?(?:lyrics?|video|audio)[)\]]/ig, ''));
  if (!artist || !title) return '';
  try {
    const response = await apiFetch(`/letras/${encodeURIComponent(artist)}/${encodeURIComponent(title)}/`);
    if (!response.ok) return '';
    const document = new DOMParser().parseFromString(await response.text(), 'text/html');
    const lyrics = document.querySelector('.lyric-original');
    if (!lyrics) return '';
    const html = lyrics.innerHTML.replace(/<br\s*\/?>/gi, '\n').replace(/<\/(p|div|li)>/gi, '\n');
    const text = new DOMParser().parseFromString(html, 'text/html').body.textContent || '';
    return text.replace(/\u00a0/g, ' ').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  } catch { return ''; }
}
async function fetchLyricsOvh(track) {
  try {
    const artist = encodeURIComponent(track.artist.replace(/\s+(feat\.?|ft\.?).*$/i, '').trim());
    const title = encodeURIComponent(track.title.trim());
    const response = await apiFetch(`/lyrics-ovh/v1/${artist}/${title}`);
    if (!response.ok) return '';
    const data = await response.json();
    return typeof data.lyrics === 'string' ? data.lyrics.trim() : '';
  } catch { return ''; }
}
function parseSyncedLyrics(value) {
  const lines = [];
  for (const row of String(value || '').split(/\r?\n/)) {
    const stamps = [...row.matchAll(/\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]/g)];
    const text = row.replace(/\[\d{1,2}:\d{2}(?:\.\d{1,3})?\]/g, '').trim();
    if (!text) continue;
    for (const stamp of stamps) {
      const fraction = (stamp[3] || '0').padEnd(3, '0');
      lines.push({ time: Number(stamp[1]) * 60 + Number(stamp[2]) + Number(fraction) / 1000, text });
    }
  }
  return lines.sort((a, b) => a.time - b.time);
}
function renderLyrics() {
  ui.lyricsText.classList.remove('is-plain');
  ui.lyricsText.replaceChildren(...state.lyricLines.map((line, index) => {
    const node = el('p', 'lyric-line', line.text);
    node.dataset.index = index;
    return node;
  }));
}
function renderPlainLyrics(value) {
  ui.lyricsText.classList.add('is-plain');
  ui.lyricsText.classList.remove('has-timestamps');
  const lines = String(value || '').replace(/\r/g, '').split('\n');
  ui.lyricsText.replaceChildren(...lines.map((text) => el('p', 'lyric-line', text || '\u00a0')));
}
function placeLyricsTransport() {
  ui.lyrics.querySelector('.lyrics-layout').append(ui.lyricsTransport);
}
window.addEventListener('resize', () => { if (!ui.lyrics.hidden) placeLyricsTransport(); });
function syncLyrics(force = false) {
  if (!state.lyricsSynced || ui.lyrics.hidden) return;
  const time = isNativeApp ? state.nativePosition / 1000 : (audio.currentTime || 0);
  let active = -1;
  for (let index = 0; index < state.lyricLines.length; index++) {
    if (state.lyricLines[index].time > time) break;
    active = index;
  }
  if (!force && active === state.lyricActive) return;
  state.lyricActive = active;
  ui.lyricsText.querySelectorAll('.lyric-line').forEach((line, index) => {
    line.classList.toggle('is-active', index === active);
    line.classList.toggle('is-past', active >= 0 && index < active);
  });
  if (active >= 0) {
    const line = ui.lyricsText.querySelector(`[data-index="${active}"]`);
    if (line) {
      const containerRect = ui.lyricsText.getBoundingClientRect();
      const lineRect = line.getBoundingClientRect();
      const top = ui.lyricsText.scrollTop + lineRect.top - containerRect.top - (containerRect.height - lineRect.height) / 2;
      ui.lyricsText.scrollTo({ top: Math.max(0, top), behavior: force ? 'auto' : 'smooth' });
    }
  }
}

document.querySelectorAll('.nav-btn').forEach((button) => button.addEventListener('click', () => route(button.dataset.view)));
ui.search.addEventListener('input', () => {
  clearTimeout(searchTimer);
  const typed = ui.search.value;
  if (state.view !== 'search') { route('search'); ui.search.value = typed; }
  const query = typed.trim();
  if (query.length < 2) renderSearchIdle();
  else searchTimer = setTimeout(() => searchView(query), 350);
});
let searchTimer;
ui.search.addEventListener('keydown', (event) => { if (event.key === 'Enter' && ui.search.value.trim().length >= 2) searchView(ui.search.value.trim()); });
$('#openPlayer').addEventListener('click', openPlayer);
$('#closePlayer').addEventListener('click', closePlayer);
ui.miniOpen.addEventListener('click', openPlayer);
async function togglePlayback() {
  if (!state.current) return;
  if (isNativeApp) {
    try {
      if (!state.queue.length) return;
      await NativePlayback.toggle();
      await syncNativeState();
    } catch (error) { toast('No se pudo cambiar la reproducción · ' + (error?.message || '')); }
    return;
  }
  if (!audio.paused || state.isPlaying) {
    audio.pause();
    setPlayIcon(false);
    return;
  }
  try {
    if (!audio.src) await playQueue(state.queue, state.index);
    else await audio.play();
    setPlayIcon(!audio.paused);
  } catch (error) { toast('No se pudo iniciar la reproducción · ' + (error?.message || '')); }
}
ui.miniPlay.addEventListener('click', togglePlayback);
ui.playerPlay.addEventListener('click', togglePlayback);
ui.miniPrev.addEventListener('click', playPrevious); ui.playerPrev.addEventListener('click', playPrevious);
ui.miniNext.addEventListener('click', playNext); ui.playerNext.addEventListener('click', playNext);
ui.miniFav.addEventListener('click', () => state.current && toggleFavorite(state.current));
ui.playerFav.addEventListener('click', () => state.current && toggleFavorite(state.current));
ui.playerDownload.addEventListener('click', () => state.current && downloadTrack(state.current));
ui.playerLyrics.addEventListener('click', loadLyrics);
$('#closeLyrics').addEventListener('click', closeLyrics);
function closeLyrics() {
  ui.lyrics.hidden = true;
  ui.lyrics.querySelector('.lyrics-layout').append(ui.lyricsTransport);
  ui.queueAnchor.parentElement.insertBefore(ui.queuePanel, ui.queueAnchor.nextSibling);
  ui.lyricsQueueSlot.hidden = true;
  renderQueue();
}
ui.playerQueue.addEventListener('click', () => { state.queueOpen = !state.queueOpen; renderQueue(); });
ui.lyricsQueueToggle?.addEventListener('click', () => { state.queueOpen = !state.queueOpen; renderQueue(); });
$('#closeQueue').addEventListener('click', () => { state.queueOpen = false; renderQueue(); });
ui.playerShuffle.addEventListener('click', toggleShuffle);
ui.lyricsShuffle.addEventListener('click', toggleShuffle);
function cycleRepeat() {
  if (isNativeApp) {
    NativePlayback.cycleRepeat().then(({ mode }) => { state.repeat = mode === 1 ? 1 : mode === 2 ? 2 : 0; updatePlayer(); }).catch(() => {});
    return;
  }
  state.repeat = (state.repeat + 1) % 3;
  updatePlayer();
}
ui.playerRepeat.addEventListener('click', cycleRepeat);
ui.lyricsRepeat.addEventListener('click', cycleRepeat);
for (const range of [ui.miniRange, ui.playerRange, ui.lyricsRange]) {
  range.addEventListener('input', () => { activeSeekRange = range; previewSeek(range.value); });
  range.addEventListener('change', () => seek(range.value, range));
}
ui.lyricsPlay.addEventListener('click', togglePlayback);
ui.lyricsPrev.addEventListener('click', playPrevious); ui.lyricsNext.addEventListener('click', playNext);
audio.addEventListener('timeupdate', () => { if (!isNativeApp) { progress(); syncLyrics(); setPlayIcon(!audio.paused); } }); audio.addEventListener('loadedmetadata', () => { if (!isNativeApp) progress(); });
audio.addEventListener('seeked', () => { if (!isNativeApp) { activeSeekRange = null; progress(); syncLyrics(); } });
audio.addEventListener('play', () => { if (!isNativeApp) setPlayIcon(true); }); audio.addEventListener('pause', () => { if (!isNativeApp) setPlayIcon(false); });
audio.addEventListener('ended', () => { if (!isNativeApp) state.repeat === 1 ? (audio.currentTime = 0, audio.play()) : playNext(); });
audio.addEventListener('error', async () => {
  if (isNativeApp) return;
  if (selectingPlaybackSource) return;
  if (!state.current) return;
  if (await tryAlternateRecording(state.current)) return;
  const code = audio.error?.code;
  toast(code === MediaError.MEDIA_ERR_NETWORK
    ? 'No se pudo recibir el audio. Comprueba la conexión.'
    : 'El formato de audio no se pudo reproducir.');
});
document.addEventListener('keydown', (event) => {
  if (document.querySelector('.playlist-dialog[open]')) return;
  if (event.target?.tagName === 'INPUT') return;
  if (event.key === 'Escape') { if (!ui.lyrics.hidden) closeLyrics(); else closePlayer(); }
  if (event.code === 'Space' && state.current) { event.preventDefault(); togglePlayback(); }
});

route('home');
if (isNativeApp) {
  NativePlayback.addListener('playbackState', applyNativeState).catch(() => {});
  NativePlayback.addListener('downloadsState', applyDownloadsState).catch(() => {});
  NativePlayback.syncLibrary({ favorites: state.favorites, playlists: state.playlists }).catch(() => {});
  syncDownloads();
  syncNativeState();
  setInterval(syncNativeState, 900);
}

let lastHomeBackAt = 0;
window.addEventListener('ritmo:back', () => {
  const dialog = document.querySelector('.playlist-dialog[open]');
  if (dialog) { dialog.close(); return; }
  if (!ui.lyrics.hidden) { closeLyrics(); return; }
  if (!ui.player.hidden) { closePlayer(); return; }
  if (state.view === 'playlists' && state.activePlaylistId) {
    state.activePlaylistId = null;
    renderPlaylists();
    return;
  }
  if (state.view !== 'home') {
    lastHomeBackAt = 0;
    state.activePlaylistId = null;
    route('home');
    return;
  }
  const now = Date.now();
  if (now - lastHomeBackAt < 2000) NativePlayback.exitApp().catch(() => {});
  else { lastHomeBackAt = now; toast('Pulsa atrás otra vez para salir'); }
});
