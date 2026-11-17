'use strict';
const $ = (id) => document.getElementById(id);
const peers = new Map(); // viewer id -> RTCPeerConnection
const viewers = new Set();
let stream = null;
let ws = null;

function showError(text) {
  $('err').textContent = text || '';
  $('err').hidden = !text;
}
function render() {
  $('toggle').textContent = stream ? 'Stop sharing' : 'Start sharing';
  $('toggle').className = stream ? 'danger' : '';
  $('dot').className = 'dot' + (stream ? ' live' : '');
  $('status').textContent = stream ? 'Sharing live' : 'Not sharing';
  $('count').textContent = viewers.size;
}

// ---- join info -------------------------------------------------------------
fetch('/info').then((r) => r.json()).then((info) => {
  $('pin').textContent = info.pin;
  const pick = (u) => {
    $('qr').src = '/qr.svg?u=' + encodeURIComponent(u);
    $('url').textContent = u.split('/?')[0];
  };
  if (!info.urls.length) {
    $('url').textContent = 'No network address found – connect this PC to Wi-Fi and restart.';
    $('qr').hidden = true;
    return;
  }
  pick(info.urls[0]);
  if (info.urls.length > 1) {
    const sel = $('urls');
    sel.hidden = false;
    for (const u of info.urls) sel.add(new Option(u.split('/?')[0], u));
    sel.onchange = () => pick(sel.value);
  }
});

// ---- signaling -------------------------------------------------------------
function connect() {
  ws = new WebSocket(`ws://${location.host}`);
  ws.onopen = () => ws.send(JSON.stringify({ type: 'hello', role: 'host' }));
  ws.onmessage = async (e) => {
    const m = JSON.parse(e.data);
    if (m.type === 'welcome') {
      viewers.clear();
      for (const id of m.viewers) viewers.add(id);
      if (stream) for (const id of viewers) if (!peers.has(id)) offerTo(id);
    } else if (m.type === 'viewer-joined') {
      viewers.add(m.id);
      if (stream) offerTo(m.id);
    } else if (m.type === 'viewer-left') {
      viewers.delete(m.id);
      closePeer(m.id);
    } else if (m.type === 'signal') {
      const pc = peers.get(m.from);
      if (!pc) return;
      try {
        if (m.data.answer) await pc.setRemoteDescription(m.data.answer);
        else if (m.data.candidate) await pc.addIceCandidate(m.data.candidate);
      } catch (err) {
        console.warn('signal ignored', err);
      }
    } else if (m.type === 'error') {
      ws.onclose = null;
      showError(m.reason === 'replaced' ? 'Sharing was taken over by another tab on this PC.' : 'Open this page on the PC that runs the server (http://localhost).');
      stop();
    }
    render();
  };
  ws.onclose = () => setTimeout(connect, 1500);
}
const signal = (to, data) => ws && ws.readyState === 1 && ws.send(JSON.stringify({ type: 'signal', to, data }));

// ---- WebRTC ----------------------------------------------------------------
function closePeer(id) {
  const pc = peers.get(id);
  if (pc) pc.close();
  peers.delete(id);
}

async function applyQuality(pc) {
  const sender = pc.getSenders().find((s) => s.track && s.track.kind === 'video');
  if (!sender) return;
  const p = sender.getParameters();
  if (!p.encodings || !p.encodings.length) p.encodings = [{}];
  p.encodings[0].maxBitrate = Number($('quality').value);
  p.degradationPreference = 'maintain-resolution';
  await sender.setParameters(p).catch(() => {});
}

async function offerTo(id) {
  closePeer(id);
  const pc = new RTCPeerConnection({ iceServers: [] }); // LAN only: no STUN/TURN
  peers.set(id, pc);
  for (const t of stream.getTracks()) pc.addTrack(t, stream);
  pc.onicecandidate = (e) => e.candidate && signal(id, { candidate: e.candidate });
  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'failed' && peers.get(id) === pc && stream && viewers.has(id)) offerTo(id);
  };
  const offer = await pc.createOffer();
  await pc.setLocalDescription(offer);
  signal(id, { offer: pc.localDescription });
  applyQuality(pc);
}

async function start() {
  showError('');
  if (!navigator.mediaDevices || !navigator.mediaDevices.getDisplayMedia) {
    return showError('This browser cannot capture the screen. Use Chrome, Edge or Firefox on the PC, at http://localhost.');
  }
  try {
    stream = await navigator.mediaDevices.getDisplayMedia({
      video: { frameRate: { ideal: 30, max: 60 } },
      audio: true,
    });
  } catch (err) {
    if (err.name !== 'NotAllowedError') showError('Could not start capture: ' + err.message);
    return;
  }
  const video = stream.getVideoTracks()[0];
  video.contentHint = 'detail'; // keep text crisp
  video.onended = stop; // user pressed the browser's "Stop sharing" bar
  $('preview').srcObject = stream;
  for (const id of viewers) offerTo(id);
  render();
}

function stop() {
  if (stream) for (const t of stream.getTracks()) t.stop();
  stream = null;
  $('preview').srcObject = null;
  for (const id of [...peers.keys()]) closePeer(id);
  if (ws && ws.readyState === 1) ws.send(JSON.stringify({ type: 'stopped' }));
  render();
}

$('toggle').onclick = () => (stream ? stop() : start());
$('quality').onchange = () => peers.forEach(applyQuality);
connect();
render();
