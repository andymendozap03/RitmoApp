import test from 'node:test';
import assert from 'node:assert/strict';
import { uniqueTracks, orderUpcoming, rankTracks, libraryCsv, parseLibraryCsv, queueWindow, isCurrentRecording } from './music-utils.js';

test('queue window follows actual shuffled order, drops history and keeps original jump indices', () => {
  const queue = orderUpcoming(Array.from({length: 60}, (_, i) => ({id: String(i)})), 5, () => .3);
  const first = queueWindow(queue, 5);
  const next = queueWindow(queue, 6);
  assert.equal(first.length, 21);
  assert.equal(first[0].index, 5);
  assert.deepEqual(first.map(item => item.track), queue.slice(5, 26));
  assert.deepEqual(next.slice(0, 20), first.slice(1));
  assert.equal(next[20].index, 26);
  assert.equal(queueWindow(queue, 59).length, 1);
  assert.deepEqual(queueWindow(queue, -1), []);
  assert.equal(queue.length, 60);
});
test('opening the current recording keeps playback but failed recordings can retry', () => {
  assert.equal(isCurrentRecording({id:'a'}, {id:'a', title:'other playlist'}, true), true);
  assert.equal(isCurrentRecording({id:'a'}, {id:'b'}, true), false);
  assert.equal(isCurrentRecording({id:'a'}, {id:'a'}, false), false);
  assert.equal(isCurrentRecording(null, null, true), false);
});

test('shuffle preserves current/history and visits every future track once', () => {
  const tracks = Array.from({ length: 30 }, (_, i) => ({ id: String(i) }));
  const ordered = orderUpcoming(tracks, 6, () => .2);
  assert.deepEqual(ordered.slice(0, 7), tracks.slice(0, 7));
  assert.equal(new Set(ordered.map(t => t.id)).size, 30);
  assert.notDeepEqual(ordered.slice(7), tracks.slice(7));
  assert.deepEqual(tracks.map(t => t.id), Array.from({ length: 30 }, (_, i) => String(i)));
  // Turning shuffle off midway restores only unplayed items, without replaying history.
  const restored = orderUpcoming(ordered, 12, Math.random, tracks);
  assert.deepEqual(restored.slice(0, 13), ordered.slice(0, 13));
  assert.equal(new Set(restored.map(t => t.id)).size, 30);
  assert.deepEqual(restored.slice(13).map(t => +t.id), ordered.slice(13).map(t => +t.id).sort((a,b) => a-b));
  assert.deepEqual(orderUpcoming(tracks, 29), tracks);
});
test('repeated imported recordings do not become duplicate queue entries', () => {
  assert.deepEqual(uniqueTracks([{id:'a'}, {id:'b'}, {id:'a'}]).map(t => t.id), ['a','b']);
});
test('backup roundtrip preserves identity, unicode, multiline and spreadsheet-safe titles', () => {
  const tracks = [
    { id:'yt:abcdefghijk', title:'Canción, "nueva"\nsegunda línea', artist:'Artista Ñ', art:'https://example.org/art.jpg', duration:201000 },
    { id:'yt:12345678901', title:'=HYPERLINK("test")', artist:'@artista', art:'', duration:0 }
  ];
  const csv = libraryCsv('Mi playlist', tracks);
  assert.ok(csv.startsWith('\uFEFF'));
  assert.ok(csv.includes('"\'=HYPERLINK'));
  assert.deepEqual(parseLibraryCsv(csv), tracks);
  assert.equal(parseLibraryCsv('Track Name,Artist Name\r\nHola,Andy')[0].title, 'Hola');
  assert.throws(() => parseLibraryCsv('Track Name,Artist Name\n"sin cerrar'), /comillas/);
});
test('official relevant recordings beat covers and unrelated verified artists', () => {
  const tracks = [
    {id:'cover', title:'Luna cover', artist:'Fan', verified:true},
    {id:'wrong', title:'Otra canción', artist:'Artista distinto', verified:true, official:true},
    {id:'official', title:'Luna', artist:'Feid', official:true, kind:'song'},
    {id:'upload', title:'Feid Luna', artist:'Reuploads'}
  ];
  assert.equal(rankTracks(tracks,'Feid Luna')[0].id, 'official');
  assert.equal(rankTracks(tracks,'Luna cover')[0].id, 'cover');
});
