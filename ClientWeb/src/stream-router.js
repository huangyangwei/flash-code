import { isFlvAddress } from './bridge.js';

export function createStreamRouter(startPlayback) {
  const start = (url, source) => isFlvAddress(url) ? startPlayback(url, source) : false;
  return { manual: (url) => start(url, 'manual'), monitored: (url) => start(url, 'monitor') };
}
