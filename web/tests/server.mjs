import { preview } from 'vite';

// Real fixture HTTP responses: WebKit requests controlled by a service worker
// bypass Playwright page.route(). Keep the production service worker enabled.
const wav = Buffer.alloc(44 + 120 * 8000 * 2);
wav.write('RIFF'); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8);
wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22);
wav.writeUInt32LE(8000, 24); wav.writeUInt32LE(16000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34);
wav.write('data', 36); wav.writeUInt32LE(wav.length - 44, 40);

await preview({configFile:false, preview:{host:'127.0.0.1', port:8107, strictPort:true}, plugins:[{
  name:'local-test-fixtures',
  configurePreviewServer(server) {
    server.middlewares.use((req, res, next) => {
      const path = new URL(req.url, 'http://localhost').pathname;
      if (path === '/stream') {
        const range = /bytes=(\d+)-(\d*)/.exec(req.headers.range || '');
        const start = range ? Number(range[1]) : 0;
        const end = range?.[2] ? Math.min(Number(range[2]), wav.length - 1) : wav.length - 1;
        if (start > end || start >= wav.length) {
          res.writeHead(416, {'Content-Range':`bytes */${wav.length}`}); res.end(); return;
        }
        res.writeHead(range ? 206 : 200, {'Content-Type':'audio/wav', 'Accept-Ranges':'bytes',
          'Cache-Control':'no-store', 'Content-Length':end-start+1,
          ...(range ? {'Content-Range':`bytes ${start}-${end}/${wav.length}`} : {})});
        res.end(req.method === 'HEAD' ? undefined : wav.subarray(start,end+1));
      } else if (path.startsWith('/yt/') || path.startsWith('/lyrics/')) {
        res.writeHead(200, {'Content-Type':'application/json', 'Cache-Control':'no-store'});
        res.end(JSON.stringify(path.startsWith('/yt/') ? {} : {
          syncedLyrics:'[00:00.00]Primera línea\n[00:30.00]Segunda línea', plainLyrics:'Primera línea\nSegunda línea'
        }));
      } else next();
    });
  }
}]});
