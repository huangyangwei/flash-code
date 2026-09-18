import { chromium } from 'playwright-core';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

const endpoint = process.argv[2];
const outputDir = process.argv[3];
const runId = process.argv[4];
if (!endpoint || !outputDir || !existsSync(outputDir) || !/^[a-z0-9-]+$/.test(runId ?? '')) {
  throw new Error('Usage: node packaged-monitor-smoke.mjs <cdp-url> <existing-output-dir> <run-id>');
}

const browser = await chromium.connectOverCDP(endpoint);
try {
  const page = browser.contexts().flatMap((context) => context.pages())
    .find((candidate) => candidate.url().startsWith('https://flash-client.local/'));
  if (!page) throw new Error('Packaged WebView2 page is missing');
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/first.flv*', (route) => route.abort('failed'));
  await page.route('**/fresh.flv*', () => new Promise(() => {}));
  await page.evaluate(() => {
    window.__qaHostEvents = [];
    chrome.webview.addEventListener('message', (event) => window.__qaHostEvents.push(event.data));
  });

  async function screenshot(state) {
    const destination = join(outputDir, `monitor-${state}-${runId}.png`);
    if (existsSync(destination)) throw new Error(`Screenshot already exists: ${destination}`);
    await page.screenshot({ path: destination });
    console.log(`${state}: ${destination}`);
  }
  const wait = (predicate, timeout = 30000) => page.waitForFunction(predicate, undefined, { timeout });
  await page.locator('#room-input').fill('123');
  await page.locator('#monitor-toggle').click();
  await wait(() => window.__qaHostEvents.some((e) => e.type === 'monitor.status' && e.payload.enabled && !e.payload.live));
  await screenshot('normal');
  await wait(() => window.__qaHostEvents.some((e) => e.type === 'monitor.stream' && e.payload.url.includes('first.flv')));
  await wait(() => window.__qaHostEvents.some((e) => e.type === 'monitor.status' && e.payload.error));
  await screenshot('error');
  await wait(() => window.__qaHostEvents.filter((e) => e.type === 'monitor.status' && e.payload.live && !e.payload.error).length >= 3);
  const beforeFailure = await page.evaluate(() => window.__qaHostEvents.filter((e) => e.type === 'monitor.stream').length);
  if (beforeFailure !== 1) throw new Error(`Duplicate stream during continuous live period: ${beforeFailure}`);
  await wait(() => document.querySelector('#playback-badge').textContent.startsWith('重试'));
  await screenshot('retry');
  await wait(() => document.querySelector('#error-dialog').open, 30000);
  await screenshot('playback-error');
  await page.evaluate(() => {
    const video = document.querySelector('#video');
    Object.defineProperty(video, 'buffered', { configurable: true, get: () => ({ length: 1, end: () => 1.5 }) });
    Object.defineProperty(video, 'currentTime', { configurable: true, writable: true, value: 0 });
  });
  await wait(() => window.__qaHostEvents.filter((e) => e.type === 'monitor.stream').length === 2);
  await wait(() => document.querySelector('#watermark').textContent.includes('1500 ms'));
  const outcome = await page.evaluate(() => ({
    streams: window.__qaHostEvents.filter((e) => e.type === 'monitor.stream').map((e) => e.payload.url),
    watermark: document.querySelector('#watermark').textContent,
    action: document.querySelector('#playback-action').textContent,
    monitor: document.querySelector('#monitor-summary').textContent,
  }));
  if (!outcome.streams[0].includes('first.flv') || !outcome.streams[1].includes('fresh.flv')) throw new Error('Fresh address was not handed off');
  console.log(JSON.stringify(outcome));
  await screenshot('refreshed');
  await page.locator('#monitor-toggle').click();
  await wait(() => document.querySelector('#monitor-summary').textContent === '未监测');
  const count = await page.evaluate(() => window.__qaHostEvents.length);
  await page.waitForTimeout(1700);
  if (await page.evaluate(() => window.__qaHostEvents.length) !== count) throw new Error('Host callback after monitor stopped');
  if (errors.length) throw new Error(`WebView page errors: ${errors.join('; ')}`);
} finally {
  await browser.close();
}
