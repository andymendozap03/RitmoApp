// Use Safari/OS controls without creating a second player or audio source.
export function connectMediaSession(audio, { next, previous, error }) {
  const session = navigator.mediaSession;
  if (!session) return () => {};
  const seekTo = seconds => {
    if (Number.isFinite(seconds) && Number.isFinite(audio.duration)) {
      audio.currentTime = Math.max(0, Math.min(audio.duration, seconds));
    }
  };
  const handlers = {
    play: () => audio.play().catch(error), pause: () => audio.pause(),
    nexttrack: next, previoustrack: previous,
    seekto: details => seekTo(details.seekTime),
    seekforward: details => seekTo(audio.currentTime + (details.seekOffset || 10)),
    seekbackward: details => seekTo(audio.currentTime - (details.seekOffset || 10))
  };
  for (const [action, handler] of Object.entries(handlers)) {
    try { session.setActionHandler(action, handler); } catch { /* Not every Safari version exposes every action. */ }
  }
  const update = () => {
    session.playbackState = audio.paused || audio.ended ? 'paused' : 'playing';
    try {
      if (audio.duration > 0 && Number.isFinite(audio.duration)) session.setPositionState?.({
        duration: audio.duration, playbackRate: audio.playbackRate,
        position: Math.max(0, Math.min(audio.duration, audio.currentTime))
      });
      else session.setPositionState?.();
    } catch { /* Position reporting is optional; normal controls remain active. */ }
  };
  for (const event of ['playing', 'pause', 'ended', 'timeupdate', 'loadedmetadata', 'emptied']) audio.addEventListener(event, update);
  let lastMetadata = '';
  return track => {
    if (!track || !window.MediaMetadata) return;
    const key = JSON.stringify([track.id, track.title, track.artist, track.art]);
    if (key === lastMetadata) return;
    lastMetadata = key;
    try {
      session.metadata = new MediaMetadata({ title: track.title, artist: track.artist, album: 'Ritmo',
        artwork: [{src: track.art || '/icons/icon-512.png'}] });
    } catch { /* Invalid optional artwork must not interrupt playback. */ }
  };
}
