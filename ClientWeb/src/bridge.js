export const BRIDGE_VERSION = 1;
const EVENTS = new Set(['app.init', 'app.error', 'monitor.status', 'monitor.stream', 'capture.status', 'capture.code', 'clipboard.status', 'playback.command']);
const EMPTY = new Set(['monitor.stop', 'capture.select', 'capture.cursor.record', 'clipboard.clear', 'credential.clear']);

export function isFlvAddress(value) {
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) && url.pathname.toLowerCase().endsWith('.flv');
  } catch { return false; }
}

export function validateCommand(type, payload) {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return false;
  if (EMPTY.has(type)) return Object.keys(payload).length === 0;
  const int = (key, lo, hi) => Number.isInteger(payload[key]) && payload[key] >= lo && payload[key] <= hi;
  const bool = (key) => typeof payload[key] === 'boolean';
  switch (type) {
    case 'monitor.start': return typeof payload.room === 'string' && payload.room.trim().length > 0 && payload.room.length <= 512 && int('intervalSec', 1, 300) && (payload.credential === undefined || payload.credential === null || (typeof payload.credential === 'string' && payload.credential.length <= 16384));
    case 'playback.manual-start': return typeof payload.url === 'string' && payload.url.length <= 8192 && isFlvAddress(payload.url);
    case 'playback.settings': return bool('catchupEnabled') && int('speedThresholdMs', 0, 60000) && typeof payload.speedRate === 'number' && payload.speedRate >= 1.05 && payload.speedRate <= 2 && (payload.jumpThresholdMs === null || int('jumpThresholdMs', 1, 120000));
    case 'capture.toggle': return bool('enabled');
    case 'capture.settings': return int('cooldownSeconds', 0, 600) && int('captureIntervalMs', 10, 200) && int('confirmFrames', 1, 4) && int('scaleFactor', 2, 6) && int('clipboardClearSeconds', 0, 3600) && bool('autoPasteEnabled') && bool('autoClickEnabled') && int('autoPasteDelayMs', 0, 10000) && bool('grabOnStart') && bool('soundEnabled') && int('ocrEngine', 0, 1);
    case 'playback.status': return typeof payload.state === 'string' && payload.state.length > 0 && payload.state.length <= 64;
    case 'app.error': return typeof payload.message === 'string' && payload.message.trim().length > 0 && payload.message.length <= 1024;
    default: return false;
  }
}

export function parseHostMessage(raw) {
  try {
    const message = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (!message || message.version !== BRIDGE_VERSION || !EVENTS.has(message.type) || !message.payload || typeof message.payload !== 'object' || Array.isArray(message.payload)) return null;
    const payload = message.payload;
    switch (message.type) {
      case 'app.init':
        if (!payload.playback || !payload.monitor || !payload.capture) return null;
        break;
      case 'monitor.status': if (typeof payload.enabled !== 'boolean') return null; break;
      case 'monitor.stream':
      case 'playback.command': if (!isFlvAddress(payload.url)) return null; break;
      case 'capture.status': if (typeof payload.running !== 'boolean') return null; break;
      case 'capture.code':
        if (typeof payload.code !== 'string' || !/^[A-Z0-9]{1,64}$/.test(payload.code) ||
            !Number.isFinite(payload.latencyMs) || payload.latencyMs < 0) return null;
        break;
      case 'app.error':
      case 'clipboard.status':
        if (typeof payload.message !== 'string' || payload.message.length < 1 || payload.message.length > 1024) return null;
        break;
      default: return null;
    }
    return message;
  } catch { return null; }
}

export function createBridge(transport = globalThis.chrome?.webview) {
  const listeners = new Map();
  const receive = (event) => {
    const message = parseHostMessage(event.data);
    if (message) listeners.get(message.type)?.forEach((listener) => listener(message.payload));
  };
  transport?.addEventListener('message', receive);
  return {
    connected: Boolean(transport?.postMessage),
    on(type, listener) {
      if (!EVENTS.has(type)) throw new Error(`Unknown host event: ${type}`);
      if (!listeners.has(type)) listeners.set(type, new Set());
      listeners.get(type).add(listener);
      return () => listeners.get(type)?.delete(listener);
    },
    send(type, payload = {}) {
      if (!validateCommand(type, payload) || !transport?.postMessage) return false;
      transport.postMessage({ version: BRIDGE_VERSION, type, payload });
      return true;
    },
    dispose() { transport?.removeEventListener('message', receive); listeners.clear(); },
  };
}
