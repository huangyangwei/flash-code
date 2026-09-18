import test from 'node:test';
import assert from 'node:assert/strict';
import { createBridge, parseHostMessage, validateCommand } from '../src/bridge.js';
import { createStreamRouter } from '../src/stream-router.js';

function fakeTransport() {
  let listener;
  const sent = [];
  return {
    sent,
    addEventListener: (_, callback) => { listener = callback; },
    removeEventListener: (_, callback) => { if (listener === callback) listener = null; },
    postMessage: (message) => sent.push(message),
    emit: (message) => listener?.({ data: message }),
  };
}

test('bridge sends only versioned, validated commands', () => {
  const transport = fakeTransport();
  const bridge = createBridge(transport);
  assert.equal(bridge.send('monitor.start', { room: '123', intervalSec: 15 }), true);
  assert.deepEqual(transport.sent[0], { version: 1, type: 'monitor.start', payload: { room: '123', intervalSec: 15 } });
  assert.equal(bridge.send('monitor.start', { room: '', intervalSec: 0 }), false);
  assert.equal(bridge.send('playback.manual-start', { url: 'file:///live.flv' }), false);
  assert.equal(bridge.send('playback.settings', { catchupEnabled: true, speedThresholdMs: 1000, speedRate: 9, jumpThresholdMs: null }), false);
  assert.equal(bridge.send('unknown', {}), false);
  assert.equal(validateCommand('capture.toggle', { enabled: true }), true);
  const captureSettings = { captureIntervalMs: 40, confirmFrames: 2, cooldownSeconds: 10, scaleFactor: 4, grabOnStart: false, ocrEngine: 1, soundEnabled: false, clipboardClearSeconds: 0, autoPasteEnabled: true, autoClickEnabled: true, autoPasteDelayMs: 350 };
  assert.equal(bridge.send('capture.settings', captureSettings), true);
  assert.equal(bridge.send('capture.settings', { ...captureSettings, ocrEngine: 2 }), false);
  assert.equal(bridge.send('capture.settings', { ...captureSettings, autoClickEnabled: 'yes' }), false);
  assert.equal(bridge.send('capture.cursor.record', {}), true);
  assert.equal(bridge.send('clipboard.clear', {}), true);
  assert.equal(transport.sent.length, 4);
  bridge.dispose();
});

test('host events reject malformed envelopes and values, and unsubscribe cleanly', () => {
  const transport = fakeTransport();
  const bridge = createBridge(transport);
  const received = [];
  const off = bridge.on('monitor.stream', (payload) => received.push(payload.url));
  transport.emit('{bad json');
  transport.emit({ version: 2, type: 'monitor.stream', payload: { url: 'https://cdn.example/live.flv' } });
  transport.emit({ version: 1, type: 'unknown', payload: {} });
  transport.emit({ version: 1, type: 'monitor.stream', payload: { url: 'javascript:alert(1)' } });
  transport.emit(JSON.stringify({ version: 1, type: 'monitor.stream', payload: { url: 'https://cdn.example/live.flv?sign=x' } }));
  assert.deepEqual(received, ['https://cdn.example/live.flv?sign=x']);
  off();
  transport.emit({ version: 1, type: 'monitor.stream', payload: { url: 'https://cdn.example/next.flv' } });
  bridge.dispose();
  transport.emit({ version: 1, type: 'monitor.stream', payload: { url: 'https://cdn.example/third.flv' } });
  assert.equal(received.length, 1);
  assert.equal(parseHostMessage({ version: 1, type: 'capture.code', payload: { code: 'BAD!', latencyMs: 9 } }), null);
});

test('manual and monitored streams invoke the same playback entry point', () => {
  const started = [];
  const streams = createStreamRouter((url, source) => { started.push([url, source]); return true; });
  assert.equal(streams.manual('https://cdn.example/manual.flv'), true);
  assert.equal(streams.monitored('https://cdn.example/monitor.flv?sign=1'), true);
  assert.equal(streams.monitored('https://cdn.example/not-flv'), false);
  assert.deepEqual(started, [
    ['https://cdn.example/manual.flv', 'manual'],
    ['https://cdn.example/monitor.flv?sign=1', 'monitor'],
  ]);
});
