import { createIcons, Play, RadioTower, ClipboardX, Radio, X, ScanLine, SlidersHorizontal, ChevronDown, TriangleAlert, RotateCw, Crosshair } from 'lucide';
import { createBridge, isFlvAddress } from './bridge.js';
import { LowLatencyPlayer } from './player.js';
import { DEFAULT_SETTINGS } from './lib/settings.js';
import { createStreamRouter } from './stream-router.js';
import './styles.css';
import './ui.css';

const $ = (id) => document.getElementById(id);
const bridge = createBridge();
const state = { playback: { ...DEFAULT_SETTINGS }, capture: null, monitoring: false, lastUrl: '' };
const actions = { normal: '正常播放', speed: '变速追帧', jump: '跳至最新' };
const playbackIds = ['catchup-enabled', 'speed-threshold', 'speed-rate', 'jump-enabled', 'jump-threshold'];
const captureIds = ['capture-interval', 'confirm-frames', 'cooldown-seconds', 'scale-factor', 'grab-on-start', 'ocr-engine', 'sound-enabled', 'clear-seconds', 'auto-paste', 'auto-click', 'paste-delay'];
let toastTimer;

createIcons({ icons: { Play, RadioTower, ClipboardX, Radio, X, ScanLine, SlidersHorizontal, ChevronDown, TriangleAlert, RotateCw, Crosshair } });

function toast(message) {
  $('toast').textContent = message;
  $('toast').hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, 3000);
}

function indicator(id, kind) {
  $(id).classList.remove('is-active', 'is-warning', 'is-error');
  if (kind) $(id).classList.add(`is-${kind}`);
}

function playbackError(message) {
  $('playback-badge').textContent = '播放中断';
  $('playback-badge').className = 'state-pill error';
  $('error-message').textContent = message;
  $('retry-url').value = state.lastUrl;
  bridge.send('playback.status', { state: 'error' });
  if (!$('error-dialog').open) $('error-dialog').showModal();
}

const player = new LowLatencyPlayer($('video'), {
  onWatermark: (ms) => { $('watermark').textContent = `缓冲 ${Math.round(ms)} ms`; },
  onAction: (action) => { $('playback-action').textContent = actions[action] ?? '正常播放'; },
  onMedia: () => {
    $('playback-badge').textContent = '直播中';
    $('playback-badge').className = 'state-pill live';
    bridge.send('playback.status', { state: 'live' });
  },
  onRetry: (attempt, delayMs) => {
    $('playback-badge').textContent = `重试 ${attempt}/3`;
    $('playback-badge').className = 'state-pill';
    $('playback-action').textContent = `${delayMs / 1000} 秒后重试`;
    bridge.send('playback.status', { state: 'retrying' });
  },
  onError: playbackError,
  onWarning: toast,
});

function startPlayback(url, source) {
  if (!player.start(url, state.playback)) return false;
  state.lastUrl = url;
  $('url-input').value = url;
  $('manual-error').hidden = true;
  $('video-empty').hidden = true;
  $('playback-badge').textContent = source === 'monitor' ? '自动接入中' : '连接中';
  $('playback-badge').className = 'state-pill';
  $('watermark').textContent = '缓冲 -- ms';
  $('playback-action').textContent = '正在连接';
  $('error-dialog').close();
  bridge.send('playback.status', { state: 'connecting' });
  return true;
}

const streams = createStreamRouter(startPlayback);

function manualStart(url) {
  const trimmed = url.trim();
  if (!isFlvAddress(trimmed)) return false;
  if (streams.manual(trimmed)) bridge.send('playback.manual-start', { url: trimmed });
  return true;
}

$('manual-form').addEventListener('submit', (event) => {
  event.preventDefault();
  if (!manualStart($('url-input').value)) {
    $('manual-error').textContent = '请输入有效的 HTTP(S) .flv 直播地址';
    $('manual-error').hidden = false;
    $('url-input').focus();
  }
});
$('retry-form').addEventListener('submit', (event) => {
  event.preventDefault();
  if (!manualStart($('retry-url').value)) {
    $('retry-url').setCustomValidity('请输入有效的 HTTP(S) .flv 直播地址');
    $('retry-url').reportValidity();
  }
});
$('retry-url').addEventListener('input', () => $('retry-url').setCustomValidity(''));
$('dismiss-error').addEventListener('click', () => $('error-dialog').close());

function requireHost(type, payload) {
  if (!bridge.send(type, payload)) toast('客户端未连接或输入无效');
}

$('monitor-form').addEventListener('submit', (event) => {
  event.preventDefault();
  if (state.monitoring) return requireHost('monitor.stop', {});
  const room = $('room-input').value.trim();
  const intervalSec = Number($('monitor-interval').value);
  if (!room || !Number.isInteger(intervalSec) || intervalSec < 1 || intervalSec > 300) {
    $('monitor-detail').textContent = '请输入直播间链接或房间号，并设置 1-300 秒间隔';
    $('room-input').focus();
    return;
  }
  const credential = $('credential-input').value;
  requireHost('monitor.start', { room, intervalSec, ...(credential ? { credential } : {}) });
  $('credential-input').value = '';
});
$('clear-credential').addEventListener('click', () => requireHost('credential.clear', {}));
$('select-region').addEventListener('click', () => requireHost('capture.select', {}));
$('capture-toggle').addEventListener('click', () => requireHost('capture.toggle', { enabled: !state.capture?.running }));
$('clear-clipboard').addEventListener('click', () => requireHost('clipboard.clear', {}));

function renderPlaybackSettings(settings) {
  state.playback = { ...DEFAULT_SETTINGS, ...settings };
  $('catchup-enabled').checked = state.playback.catchupEnabled;
  $('speed-threshold').value = state.playback.speedThresholdMs;
  $('speed-rate').value = state.playback.speedRate;
  $('jump-enabled').checked = state.playback.jumpThresholdMs !== null;
  $('jump-threshold').value = state.playback.jumpThresholdMs ?? DEFAULT_SETTINGS.jumpThresholdMs;
  const enabled = bridge.connected && state.playback.catchupEnabled;
  for (const id of ['speed-threshold', 'speed-rate', 'jump-enabled']) $(id).disabled = !enabled;
  $('jump-threshold').disabled = !enabled || state.playback.jumpThresholdMs === null;
  player.applySettings(state.playback);
}

function number(id) { return Number($(id).value); }

function submitPlaybackSettings() {
  requireHost('playback.settings', {
    catchupEnabled: $('catchup-enabled').checked,
    speedThresholdMs: number('speed-threshold'),
    speedRate: number('speed-rate'),
    jumpThresholdMs: $('jump-enabled').checked ? number('jump-threshold') : null,
  });
}

function submitCaptureSettings() {
  requireHost('capture.settings', {
    captureIntervalMs: number('capture-interval'), confirmFrames: number('confirm-frames'),
    cooldownSeconds: number('cooldown-seconds'), scaleFactor: number('scale-factor'),
    clipboardClearSeconds: number('clear-seconds'), autoPasteEnabled: $('auto-paste').checked,
    autoClickEnabled: $('auto-click').checked,
    autoPasteDelayMs: number('paste-delay'), grabOnStart: $('grab-on-start').checked,
    ocrEngine: number('ocr-engine'), soundEnabled: $('sound-enabled').checked,
  });
}

for (const id of playbackIds) $(id).addEventListener('change', submitPlaybackSettings);
for (const id of captureIds) $(id).addEventListener('change', submitCaptureSettings);
$('record-cursor').addEventListener('click', () => requireHost('capture.cursor.record', {}));

function renderMonitor(monitor) {
  state.monitoring = Boolean(monitor.enabled);
  $('monitor-toggle').querySelector('span').textContent = state.monitoring ? '停止监测' : '开始监测';
  $('monitor-summary').textContent = monitor.error ? '监测失败' : state.monitoring ? monitor.live ? '正在直播' : '等待开播' : '未监测';
  $('monitor-detail').textContent = monitor.error || (state.monitoring ? `${monitor.room || '未配置房间'} · ${monitor.intervalSec ?? 15} 秒间隔` : '等待配置直播间');
  $('credential-state').textContent = monitor.credentialConfigured ? '已保存 Cookie' : '未保存 Cookie';
  indicator('monitor-dot', monitor.error ? 'error' : state.monitoring ? monitor.live ? 'active' : 'warning' : null);
  if (typeof monitor.room === 'string') $('room-input').value = monitor.room;
  if (Number.isInteger(monitor.intervalSec)) $('monitor-interval').value = monitor.intervalSec;
}

function renderCapture(capture) {
  state.capture = capture;
  $('capture-summary').textContent = capture.error ? '抓取异常' : capture.running ? '正在抓取' : capture.region ? '已暂停' : '未选择区域';
  $('capture-toggle').querySelector('span').textContent = capture.running ? '停止抓取' : '开始抓取';
  $('capture-toggle').disabled = !capture.region || !bridge.connected;
  $('region-detail').textContent = capture.error || `OCR：${capture.engine ?? 'Windows OCR'} · 区域：${capture.region ? `${capture.region.width} × ${capture.region.height}` : '--'}`;
  if (capture.message) $('capture-message').textContent = capture.message;
  if (typeof capture.debug === 'string') $('ocr-output').textContent = capture.debug || '等待画面';
  indicator('capture-dot', capture.error ? 'error' : capture.running ? 'active' : capture.region ? 'warning' : null);
}

bridge.on('app.init', (payload) => {
  $('host-state').textContent = '客户端已连接';
  indicator('host-dot', 'active');
  for (const id of ['monitor-toggle', 'select-region', 'clear-clipboard', 'clear-credential', 'record-cursor', ...captureIds, ...playbackIds]) $(id).disabled = false;
  if (payload.playback) renderPlaybackSettings(payload.playback);
  if (payload.monitor) renderMonitor(payload.monitor);
  if (payload.capture) {
    renderCapture(payload.capture);
    for (const [key, id] of Object.entries({ captureIntervalMs: 'capture-interval', confirmFrames: 'confirm-frames', cooldownSeconds: 'cooldown-seconds', scaleFactor: 'scale-factor', clipboardClearSeconds: 'clear-seconds', autoPasteDelayMs: 'paste-delay' })) {
      if (payload.capture[key] !== undefined) $(id).value = payload.capture[key];
    }
    $('auto-paste').checked = Boolean(payload.capture.autoPasteEnabled);
    const position = payload.capture.autoClickPosition;
    const hasPosition = Number.isInteger(position?.x) && Number.isInteger(position?.y);
    $('auto-click').disabled = !hasPosition;
    $('auto-click').checked = hasPosition && Boolean(payload.capture.autoClickEnabled);
    $('cursor-position').textContent = hasPosition ? `${position.x}, ${position.y}` : '未记录';
    $('grab-on-start').checked = Boolean(payload.capture.grabOnStart);
    $('sound-enabled').checked = Boolean(payload.capture.soundEnabled);
    $('ocr-engine').value = String(payload.capture.ocrEngine ?? 0);
  }
});
bridge.on('monitor.status', renderMonitor);
bridge.on('monitor.stream', ({ url }) => streams.monitored(url));
bridge.on('capture.status', renderCapture);
bridge.on('capture.code', ({ code, latencyMs }) => {
  if (typeof code !== 'string' || !Number.isFinite(latencyMs)) return;
  $('last-code').textContent = code;
  $('code-latency').textContent = `${Math.round(latencyMs)} ms`;
  $('capture-meta').textContent = '已复制到剪贴板';
  $('capture-message').textContent = '房间码已就绪';
  toast(`已抓取 ${code}`);
});
bridge.on('clipboard.status', ({ message }) => { if (typeof message === 'string') toast(message); });
bridge.on('playback.command', ({ url }) => { if (url) streams.monitored(url); });
bridge.on('app.error', ({ message }) => { if (typeof message === 'string') toast(message); });

if (!bridge.connected) {
  $('host-state').textContent = '预览模式 · 未连接客户端';
  for (const id of ['monitor-toggle', 'select-region', 'capture-toggle', 'clear-clipboard', 'clear-credential', 'record-cursor', ...captureIds, ...playbackIds]) $(id).disabled = true;
}

window.addEventListener('pagehide', () => { player.dispose(); bridge.dispose(); clearTimeout(toastTimer); });
