import test from 'node:test';
import assert from 'node:assert/strict';

globalThis.self = globalThis;
const { isFlvUrl, LowLatencyPlayer, PLAYER_CONFIG } = await import('../src/player.js');

function setupPlayer() {
  const calls = [];
  const mediaPlayer = {
    attachMediaElement: () => calls.push('attach'),
    on: (event, handler) => { calls.push('listen'); mediaPlayer.error = handler; },
    load: () => calls.push('load'),
    pause: () => calls.push('pause'),
    unload: () => calls.push('unload'),
    detachMediaElement: () => calls.push('detach'),
    destroy: () => calls.push('destroy'),
  };
  const mpegtsImpl = {
    Events: { ERROR: 'error' },
    getFeatureList: () => ({ mseLivePlayback: true }),
    createPlayer: (source, options) => {
      assert.equal(source.isLive, true);
      assert.equal(options.enableStashBuffer, false);
      calls.push('create');
      return mediaPlayer;
    },
  };
  const video = {
    currentTime: 2,
    playbackRate: 1,
    readyState: 4,
    buffered: { length: 1, end: () => 3.5 },
    addEventListener: () => {},
    removeEventListener: () => {},
    play: () => Promise.resolve(),
  };
  let tick;
  let retry;
  let cleared = 0;
  let clock = 0;
  const events = [];
  const player = new LowLatencyPlayer(video, {
    onWatermark: (ms) => events.push(['watermark', ms]),
    onAction: (action) => events.push(['action', action]),
    onRetry: (attempt, delay) => events.push(['retry', attempt, delay]),
    onError: (message) => events.push(['error', message]),
  }, {
    mpegtsImpl,
    setIntervalImpl: (handler) => { tick = handler; return 1; },
    clearIntervalImpl: () => { cleared++; },
    setTimeoutImpl: (handler, delay) => { retry = handler; calls.push(`wait:${delay}`); return 2; },
    clearTimeoutImpl: () => { cleared++; },
    nowImpl: () => clock,
  });
  return { player, video, mediaPlayer, calls, events, tick: () => tick(), retry: () => retry(), advance: (ms) => { clock += ms; }, cleared: () => cleared };
}

const settings = { catchupEnabled: true, speedThresholdMs: 1000, speedRate: 1.25, jumpThresholdMs: 2000 };

test('manual URL validation requires absolute HTTP(S) FLV', () => {
  assert.equal(isFlvUrl('https://cdn.example/live.flv?sign=x'), true);
  assert.equal(isFlvUrl('http://cdn.example/live.FLV'), true);
  assert.equal(isFlvUrl('/live.flv'), false);
  assert.equal(isFlvUrl('file:///live.flv'), false);
  assert.equal(isFlvUrl('https://cdn.example/page'), false);
});

test('autoplay rejection prompts manual playback without marking the stream expired', async () => {
  const context = setupPlayer();
  const warnings = [];
  context.video.play = () => Promise.reject(new Error('autoplay denied'));
  context.player.callbacks.onWarning = (message) => warnings.push(message);
  context.player.start('https://cdn.example/live.flv', settings);
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.deepEqual(warnings, ['自动播放被阻止，请在播放器中手动播放']);
  assert.equal(context.events.some(([kind]) => kind === 'error'), false);
  assert.equal(context.player.currentUrl, 'https://cdn.example/live.flv');
  context.player.dispose();
});

test('player starts low-buffer playback and applies catch-up immediately', () => {
  const context = setupPlayer();
  assert.equal(context.player.start('https://cdn.example/live.flv', settings), true);
  context.tick();
  assert.equal(context.video.playbackRate, 1.25);
  assert.deepEqual(context.events.slice(0, 2), [['watermark', 1500], ['action', 'speed']]);
  context.player.applySettings({ ...settings, catchupEnabled: false });
  assert.equal(context.video.playbackRate, 1);
  context.player.dispose();
  assert.ok(context.calls.includes('destroy'));
  assert.ok(context.cleared() >= 1);
  assert.equal(PLAYER_CONFIG.enableWorker, true);
});

test('initial network errors retry three times, then stop and report error', () => {
  const context = setupPlayer();
  context.player.start('https://cdn.example/live.flv', settings);
  for (let attempt = 1; attempt <= 3; attempt++) {
    context.mediaPlayer.error('NetworkError', 'not ready');
    assert.deepEqual(context.events.at(-1), ['retry', attempt, [2000, 4000, 8000][attempt - 1]]);
    context.retry();
  }
  context.mediaPlayer.error('NetworkError', 'still down');
  assert.deepEqual(context.events.at(-1), ['error', '直播流连接中断，请提供新的 FLV 地址']);
  assert.equal(context.player.player, null);
});

test('replacing an active stream tears down the previous player', () => {
  const context = setupPlayer();
  context.player.start('https://cdn.example/old.flv', settings);
  context.player.start('https://cdn.example/new.flv', settings);
  assert.equal(context.calls.filter((call) => call === 'destroy').length, 1);
  context.player.dispose();
  assert.equal(context.calls.filter((call) => call === 'destroy').length, 2);
});

test('stalled, exhausted buffer interrupts playback even without a positive watermark', () => {
  const context = setupPlayer();
  context.player.start('https://cdn.example/live.flv', settings);
  context.tick();
  context.video.currentTime = 4;
  context.video.readyState = 2;
  context.advance(15001);
  context.tick();
  assert.deepEqual(context.events.at(-1), ['error', '直播流已中断，缓冲 15 秒未继续增长']);
  assert.equal(context.player.player, null);
  assert.equal(context.calls.filter((call) => call === 'destroy').length, 1);
});

test('unavailable MSE and immediate create failure do not leave a polling timer', () => {
  const context = setupPlayer();
  context.player.mpegts.getFeatureList = () => ({ mseLivePlayback: false });
  assert.equal(context.player.start('https://cdn.example/live.flv', settings), false);
  assert.equal(context.player.timerId, null);
  context.player.mpegts.getFeatureList = () => ({ mseLivePlayback: true });
  context.player.mpegts.createPlayer = () => { throw new Error('signed-address?credential=do-not-show'); };
  assert.equal(context.player.start('https://cdn.example/live.flv', settings), false);
  assert.equal(context.player.timerId, null);
  assert.doesNotMatch(context.events.at(-1)[1], /do-not-show/);
});
