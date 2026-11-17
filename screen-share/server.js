'use strict';
// LAN Screen Share: signaling + static file server.
// The PC that runs this server is the only machine allowed to share (host).
// Every other device on the Wi-Fi (iPad, Android, laptops) joins as a viewer.
// Video travels peer-to-peer over WebRTC and never leaves the local network.

const http = require('http');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { exec, execFile } = require('child_process');
const { WebSocketServer } = require('ws');
const QRCode = require('qrcode');

const PORT = Number(process.env.PORT) || 8080;
const PIN = process.env.PIN || String(crypto.randomInt(0, 10000)).padStart(4, '0');
const PUBLIC = path.join(__dirname, 'public');

const stripV4 = (a) => (a || '').replace(/^::ffff:/, '');
const isLoopback = (a) => {
  const ip = stripV4(a);
  return ip === '::1' || ip.startsWith('127.');
};
const isIPv4 = (a) => /^\d+\.\d+\.\d+\.\d+$/.test(a);

function lanAddresses() {
  const out = [];
  for (const list of Object.values(os.networkInterfaces())) {
    for (const i of list || []) {
      if (i.family === 'IPv4' && !i.internal && !i.address.startsWith('169.254.')) out.push(i.address);
    }
  }
  // Typical home/office Wi-Fi ranges first.
  const rank = (a) => (a.startsWith('192.168.') ? 0 : a.startsWith('10.') ? 1 : 2);
  return out.sort((a, b) => rank(a) - rank(b));
}
// The optional virtual second display is set up by the Windows installer.
const DISPLAY_SCRIPT = path.join(__dirname, 'driver', 'display-mode.ps1');
const hasSecondScreen = () =>
  process.platform === 'win32' && fs.existsSync('C:\\VirtualDisplayDriver\\vdd_settings.xml') && fs.existsSync(DISPLAY_SCRIPT);

const viewerUrls = () => lanAddresses().map((a) => `http://${a}:${PORT}/?pin=${PIN}`);

const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8' };
function sendFile(res, name) {
  fs.readFile(path.join(PUBLIC, name), (err, buf) => {
    if (err) {
      res.writeHead(404).end('Not found');
      return;
    }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(name)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(buf);
  });
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://x');
  const local = isLoopback(req.socket.remoteAddress);
  switch (url.pathname) {
    case '/':
      return sendFile(res, local ? 'host.html' : 'view.html');
    case '/view':
      return sendFile(res, 'view.html');
    case '/host.js':
    case '/view.js':
    case '/style.css':
      return sendFile(res, url.pathname.slice(1));
    case '/info': // PIN and join links: only for the host PC itself.
      if (!local) return res.writeHead(403).end();
      res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
      return res.end(JSON.stringify({ pin: PIN, urls: viewerUrls(), secondScreen: hasSecondScreen() }));
    case '/display': {
      // Same as Win+P: "extend" turns the virtual second screen on, "internal" off.
      const mode = url.searchParams.get('mode');
      const origin = req.headers.origin;
      const sameOrigin = !origin || origin === `http://${req.headers.host}`;
      if (!local || !sameOrigin || req.method !== 'POST') return res.writeHead(403).end();
      if (!hasSecondScreen() || !['extend', 'internal'].includes(mode)) return res.writeHead(400).end();
      execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', DISPLAY_SCRIPT, mode], { windowsHide: true }, (err) => {
        res.writeHead(err ? 500 : 204).end();
      });
      return;
    }
    case '/qr.svg': {
      if (!local) return res.writeHead(403).end();
      const svg = await QRCode.toString(url.searchParams.get('u') || '', { type: 'svg', margin: 1 }).catch(() => null);
      if (!svg) return res.writeHead(400).end();
      res.writeHead(200, { 'Content-Type': 'image/svg+xml', 'Cache-Control': 'no-store' });
      return res.end(svg);
    }
    default:
      res.writeHead(404).end('Not found');
  }
});

// ---- signaling -------------------------------------------------------------
const wss = new WebSocketServer({ server, maxPayload: 256 * 1024 });
let host = null;
const viewers = new Map(); // id -> ws
let nextId = 1;

const send = (ws, msg) => ws && ws.readyState === 1 && ws.send(JSON.stringify(msg));

// Browsers hide their LAN IP behind random "xxxx.local" mDNS names in ICE
// candidates. mDNS is often blocked by routers / Windows Firewall, so we also
// forward a copy with the real address, which we know from the socket itself.
function withRealAddress(data, ip) {
  const c = data && data.candidate && data.candidate.candidate;
  if (!c || !isIPv4(ip)) return null;
  const parts = c.split(' ');
  if (!parts[4] || !parts[4].endsWith('.local')) return null;
  parts[4] = ip;
  return { candidate: { ...data.candidate, candidate: parts.join(' ') } };
}

wss.on('connection', (ws, req) => {
  ws.alive = true;
  ws.on('pong', () => (ws.alive = true));
  ws.ip = stripV4(req.socket.remoteAddress);
  ws.serverIp = stripV4(req.socket.localAddress); // this PC's address as seen by that client
  ws.fails = 0;

  ws.on('message', (raw) => {
    let m;
    try {
      m = JSON.parse(raw);
    } catch {
      return;
    }
    if (m.type === 'hello' && !ws.role) {
      if (m.role === 'host') {
        if (!isLoopback(ws.ip)) return send(ws, { type: 'error', reason: 'host-must-be-local' });
        if (host && host !== ws) {
          send(host, { type: 'error', reason: 'replaced' });
          host.close();
        }
        ws.role = 'host';
        host = ws;
        send(ws, { type: 'welcome', viewers: [...viewers.keys()] });
      } else {
        if (String(m.pin) !== PIN) {
          send(ws, { type: 'error', reason: 'bad-pin' });
          if (++ws.fails >= 5) ws.close();
          return;
        }
        ws.role = 'viewer';
        ws.id = nextId++;
        viewers.set(ws.id, ws);
        send(ws, { type: 'welcome', id: ws.id });
        send(host, { type: 'viewer-joined', id: ws.id });
      }
      return;
    }
    if (m.type === 'signal' && ws.role === 'host') {
      const v = viewers.get(m.to);
      if (!v) return;
      send(v, { type: 'signal', data: m.data });
      const extra = withRealAddress(m.data, v.serverIp);
      if (extra) send(v, { type: 'signal', data: extra });
    } else if (m.type === 'signal' && ws.role === 'viewer') {
      send(host, { type: 'signal', from: ws.id, data: m.data });
      const extra = withRealAddress(m.data, ws.ip);
      if (extra) send(host, { type: 'signal', from: ws.id, data: extra });
    } else if (m.type === 'stopped' && ws.role === 'host') {
      for (const v of viewers.values()) send(v, { type: 'stopped' });
    }
  });

  ws.on('close', () => {
    if (ws === host) {
      host = null;
      for (const v of viewers.values()) send(v, { type: 'stopped' });
    } else if (ws.role === 'viewer') {
      viewers.delete(ws.id);
      send(host, { type: 'viewer-left', id: ws.id });
    }
  });
});

setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.alive) {
      ws.terminate();
      continue;
    }
    ws.alive = false;
    ws.ping();
  }
}, 15000).unref();

server.on('error', (e) => {
  console.error(e.code === 'EADDRINUSE' ? `Port ${PORT} is already in use. Set another one, e.g.  set PORT=9090` : e.message);
  process.exit(1);
});

server.listen(PORT, '0.0.0.0', async () => {
  const hostUrl = `http://localhost:${PORT}`;
  const urls = viewerUrls();
  console.log('\n  LAN Screen Share is running\n');
  console.log(`  1) On THIS PC open:      ${hostUrl}   and click "Start sharing"`);
  if (urls.length) {
    console.log(`  2) On iPad / Android:    ${urls[0].split('/?')[0]}   (PIN ${PIN})`);
    for (const u of urls.slice(1)) console.log(`       or:                 ${u.split('/?')[0]}`);
    console.log('     ...or scan this QR code with the camera:\n');
    console.log(await QRCode.toString(urls[0], { type: 'terminal', small: true }));
  } else {
    console.log('  ! No Wi-Fi / LAN address found. Connect this PC to the network and restart.');
  }
  console.log('  Press Ctrl+C to stop.\n');
  if (process.argv.includes('--open')) {
    const cmd = process.platform === 'win32' ? `start "" "${hostUrl}"` : process.platform === 'darwin' ? `open "${hostUrl}"` : `xdg-open "${hostUrl}"`;
    exec(cmd, () => {});
  }
});
