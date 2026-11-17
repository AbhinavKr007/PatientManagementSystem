'use strict';
const $ = (id) => document.getElementById(id);
const video = $('video');
let ws = null;
let pc = null;
let pin = new URLSearchParams(location.search).get('pin') || sessionStorage.getItem('pin') || '';
let retry = 0;

function wait(title, text) {
  $('wait').hidden = !title;
  $('waitTitle').textContent = title || '';
  $('waitText').textContent = text || '';
  $('bar').hidden = !!title;
}
function askPin(wrong) {
  $('wait').hidden = true;
  $('pinBox').hidden = false;
  $('pinErr').hidden = !wrong;
  $('pin').value = '';
  $('pin').focus();
}

function closePeer() {
  if (pc) pc.close();
  pc = null;
  video.srcObject = null;
  $('sound').hidden = true;
}

function connect() {
  ws = new WebSocket(`ws://${location.host}`);
  ws.onopen = () => {
    retry = 0;
    ws.send(JSON.stringify({ type: 'hello', role: 'viewer', pin }));
  };
  ws.onmessage = async (e) => {
    const m = JSON.parse(e.data);
    if (m.type === 'welcome') {
      sessionStorage.setItem('pin', pin);
      $('pinBox').hidden = true;
      wait('Waiting for the screen…', 'Press “Start sharing” on the PC.');
    } else if (m.type === 'error') {
      askPin(!!pin);
    } else if (m.type === 'stopped') {
      closePeer();
      wait('Sharing stopped', 'It will resume here automatically.');
    } else if (m.type === 'signal') {
      try {
        if (m.data.offer) await onOffer(m.data.offer);
        else if (m.data.candidate && pc) await pc.addIceCandidate(m.data.candidate);
      } catch (err) {
        console.warn('signal ignored', err);
      }
    }
  };
  ws.onclose = () => {
    closePeer();
    if ($('pinBox').hidden) wait('Reconnecting…', 'Make sure this device is on the same Wi-Fi as the PC.');
    setTimeout(connect, Math.min(5000, 500 * 2 ** retry++));
  };
}
const signal = (data) => ws.readyState === 1 && ws.send(JSON.stringify({ type: 'signal', data }));

async function onOffer(offer) {
  closePeer();
  const conn = (pc = new RTCPeerConnection({ iceServers: [] }));
  conn.onicecandidate = (e) => e.candidate && signal({ candidate: e.candidate });
  conn.ontrack = (e) => {
    if (video.srcObject !== e.streams[0]) video.srcObject = e.streams[0];
    // Phones/tablets only autoplay muted video; offer a one-tap unmute if there is sound.
    if (e.track.kind === 'audio' && video.muted) $('sound').hidden = false;
    video.play().catch(() => {});
  };
  await conn.setRemoteDescription(offer);
  await conn.setLocalDescription(await conn.createAnswer());
  signal({ answer: conn.localDescription });
}

video.addEventListener('playing', () => {
  wait(null);
  showBar();
});

// ---- controls --------------------------------------------------------------
let barTimer;
function showBar() {
  $('bar').classList.remove('hide');
  clearTimeout(barTimer);
  barTimer = setTimeout(() => $('bar').classList.add('hide'), 3500);
}
document.addEventListener('pointerdown', showBar);

$('sound').onclick = () => {
  video.muted = false;
  video.play().catch(() => {});
  $('sound').hidden = true;
};
$('full').onclick = () => {
  const el = document.documentElement;
  if (document.fullscreenElement || document.webkitFullscreenElement) {
    (document.exitFullscreen || document.webkitExitFullscreen).call(document);
  } else if (el.requestFullscreen) el.requestFullscreen().catch(() => {});
  else if (el.webkitRequestFullscreen) el.webkitRequestFullscreen();
  else if (video.webkitEnterFullscreen) video.webkitEnterFullscreen(); // iPhone
};
$('pinForm').onsubmit = (e) => {
  e.preventDefault();
  pin = $('pin').value.trim();
  if (ws.readyState === 1) ws.send(JSON.stringify({ type: 'hello', role: 'viewer', pin }));
};

// Keep the tablet awake while watching (supported browsers only).
if (navigator.wakeLock) {
  const lock = () => navigator.wakeLock.request('screen').catch(() => {});
  lock();
  document.addEventListener('visibilitychange', () => document.visibilityState === 'visible' && lock());
}

connect();
