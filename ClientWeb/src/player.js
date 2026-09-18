import mpegts from 'mpegts.js';
import { ACTION, decideAction, getWatermarkMs } from './lib/catchup.js';
import { retryPlan } from './lib/connect-retry.js';

export const PLAYER_CONFIG = {
  enableStashBuffer: false,
  stashInitialSize: 384,
  lazyLoad: false,
  liveBufferLatencyChasing: false,
  autoCleanupSourceBuffer: true,
  enableWorker: true,
};

const STALL_TIMEOUT_MS = 15000;
const TICK_INTERVAL_MS = 250;

export function isFlvUrl(value) {
  try {
    const url = new URL(value);
    return (url.protocol === 'http:' || url.protocol === 'https:') &&
      url.pathname.toLowerCase().endsWith('.flv');
  } catch {
    return false;
  }
}

export class LowLatencyPlayer {
  constructor(videoEl, callbacks = {}, dependencies = {}) {
    this.videoEl = videoEl;
    this.callbacks = callbacks;
    this.mpegts = dependencies.mpegtsImpl ?? mpegts;
    this.setInterval = dependencies.setIntervalImpl ?? globalThis.setInterval.bind(globalThis);
    this.clearInterval = dependencies.clearIntervalImpl ?? globalThis.clearInterval.bind(globalThis);
    this.setTimeout = dependencies.setTimeoutImpl ?? globalThis.setTimeout.bind(globalThis);
    this.clearTimeout = dependencies.clearTimeoutImpl ?? globalThis.clearTimeout.bind(globalThis);
    this.now = dependencies.nowImpl ?? Date.now;
    this.player = null;
    this.timerId = null;
    this.retryTimerId = null;
    this.currentUrl = null;
    this.settings = null;
    this.previousAction = ACTION.NORMAL;
    this.connectFailCount = 0;
    this.hasReceivedMedia = false;
    this.lastBufferedEnd = null;
    this.lastBufferedEndAt = this.now();
    this.stallReported = false;
    this.generation = 0;
    this.videoEl.addEventListener('error', this.onVideoError = () => {
      if (this.currentUrl) this._fail('视频解码失败，请更换直播流地址');
    });
  }

  start(url, settings) {
    if (!isFlvUrl(url)) {
      this.callbacks.onError?.('地址不是有效的 HTTP(S) FLV 直播流');
      return false;
    }
    this.stop();
    this.currentUrl = url;
    this.settings = settings;
    this.lastBufferedEndAt = this.now();
    this.stallReported = false;

    let mseAvailable;
    try { mseAvailable = this.mpegts.getFeatureList().mseLivePlayback; }
    catch { mseAvailable = false; }
    if (!mseAvailable) {
      this._fail('当前 WebView2 Runtime 不支持 MSE 直播播放');
      return false;
    }

    const generation = ++this.generation;
    this.timerId = this.setInterval(() => this._tick(), TICK_INTERVAL_MS);
    return this._createAndLoad(url, generation);
  }

  _createAndLoad(url, generation) {
    if (generation !== this.generation) return false;
    try {
      this.player = this.mpegts.createPlayer(
        { type: 'flv', isLive: true, url },
        PLAYER_CONFIG,
      );
      this.player.attachMediaElement(this.videoEl);
      this.videoEl.muted = true;
      this.player.on(this.mpegts.Events.ERROR, (type, detail) => {
        this._onPlayerError(type, detail, url, generation);
      });
      this.player.load();
      const playing = this.videoEl.play();
      playing?.catch?.(() => {
        if (generation === this.generation) this.callbacks.onWarning?.('自动播放被阻止，请在播放器中手动播放');
      });
      return true;
    } catch (error) {
      this._fail('启动播放失败，请确认流地址和 WebView2 Runtime');
      return false;
    }
  }

  _onPlayerError(type, detail, url, generation) {
    if (generation !== this.generation || this.retryTimerId !== null) return;
    const plan = retryPlan({
      failedAttempts: this.connectFailCount + 1,
      hasMedia: this.hasReceivedMedia,
      isNetworkError: type === 'NetworkError',
    });
    if (!plan.retry) {
      this._fail('直播流连接中断，请提供新的 FLV 地址');
      return;
    }
    this.connectFailCount = plan.attempt;
    this._teardownPlayer();
    this.callbacks.onRetry?.(plan.attempt, plan.delayMs);
    this.retryTimerId = this.setTimeout(() => {
      this.retryTimerId = null;
      this._createAndLoad(url, generation);
    }, plan.delayMs);
  }

  applySettings(settings) {
    this.settings = settings;
    if (!settings.catchupEnabled && this.videoEl) {
      this.videoEl.playbackRate = 1;
      this.previousAction = ACTION.NORMAL;
      this.callbacks.onAction?.(ACTION.NORMAL);
    }
  }

  _tick() {
    const video = this.videoEl;
    let watermark;
    try { watermark = getWatermarkMs(video.buffered, video.currentTime); }
    catch { watermark = null; }
    if (watermark === null || !this.settings) {
      if (this.hasReceivedMedia) this._watchdog();
      return;
    }

    if (!this.hasReceivedMedia) {
      this.hasReceivedMedia = true;
      this.connectFailCount = 0;
      this.callbacks.onMedia?.();
    }
    this.callbacks.onWatermark?.(watermark);

    const action = decideAction(watermark, this.settings, this.previousAction);
    try {
      if (action === ACTION.JUMP && video.buffered.length) {
        video.currentTime = video.buffered.end(video.buffered.length - 1);
        video.playbackRate = 1;
      } else if (action === ACTION.SPEED) {
        video.playbackRate = this.settings.speedRate;
      } else {
        video.playbackRate = 1;
      }
    } catch {
      this._fail('追帧失败，请更换直播流地址');
      return;
    }
    if (action !== this.previousAction) {
      this.previousAction = action;
      this.callbacks.onAction?.(action);
    }
    this._watchdog();
  }

  _watchdog() {
    const video = this.videoEl;
    const buffered = video.buffered;
    const end = buffered.length ? buffered.end(buffered.length - 1) : null;
    const now = this.now();
    if (end !== null && end !== this.lastBufferedEnd) {
      this.lastBufferedEnd = end;
      this.lastBufferedEndAt = now;
      return;
    }
    if (!this.stallReported && end !== null && video.readyState < 3 &&
        now - this.lastBufferedEndAt > STALL_TIMEOUT_MS) {
      this.stallReported = true;
      this._fail('直播流已中断，缓冲 15 秒未继续增长');
    }
  }

  _fail(message) {
    this.stop();
    this.callbacks.onError?.(message);
  }

  _teardownPlayer() {
    if (!this.player) return;
    const active = this.player;
    this.player = null;
    for (const method of ['pause', 'unload', 'detachMediaElement', 'destroy']) {
      try { active[method](); }
      catch { /* Release the remaining resources even when a stream is broken. */ }
    }
  }

  stop() {
    ++this.generation;
    if (this.timerId !== null) this.clearInterval(this.timerId);
    if (this.retryTimerId !== null) this.clearTimeout(this.retryTimerId);
    this.timerId = null;
    this.retryTimerId = null;
    this.previousAction = ACTION.NORMAL;
    this.connectFailCount = 0;
    this.hasReceivedMedia = false;
    this.lastBufferedEnd = null;
    this.currentUrl = null;
    this.videoEl.playbackRate = 1;
    this._teardownPlayer();
  }

  dispose() {
    this.stop();
    this.videoEl.removeEventListener('error', this.onVideoError);
  }
}
