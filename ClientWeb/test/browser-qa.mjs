import { chromium } from 'playwright-core';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

const outputDir = process.argv[2];
const runId = process.argv[3] ?? 'qa';
const qaPort = Number(process.env.FLASH_QA_PORT ?? 5175);
if (!outputDir || !existsSync(outputDir)) throw new Error('Pass an existing screenshot output directory');
if (!/^[a-z0-9-]+$/.test(runId)) throw new Error('Screenshot run id must use lowercase letters, digits or hyphens');
if (!Number.isInteger(qaPort) || qaPort < 1 || qaPort > 65535) throw new Error('Invalid QA port');
const browser = await chromium.launch({
  executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true,
});

try {
  for (const [width, height] of [[1440, 900], [1024, 768], [390, 844]]) {
    const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
    await context.addInitScript(() => {
      const listeners = new Set();
      window.__commands = [];
      window.__hostEvent = (type, payload) => {
        for (const listener of listeners) listener({ data: { version: 1, type, payload } });
      };
      window.chrome ??= {};
      Object.defineProperty(window.chrome, 'webview', {
        configurable: true,
        value: {
          postMessage: (message) => window.__commands.push(message),
          addEventListener: (_, listener) => listeners.add(listener),
          removeEventListener: (_, listener) => listeners.delete(listener),
        },
      });
    });
    const page = await context.newPage();
    const errors = [];
    const requests = [];
    page.on('pageerror', (error) => errors.push(error.message));
    page.on('request', (request) => requests.push(request.url()));
    await page.goto(`http://127.0.0.1:${qaPort}/`, { waitUntil: 'networkidle' });
    await page.evaluate(() => window.__hostEvent('app.init', {
      playback: { catchupEnabled: true, speedThresholdMs: 1000, speedRate: 1.1, jumpThresholdMs: 2000 },
      monitor: { enabled: false, room: '123456', intervalSec: 15, live: false, credentialConfigured: false },
      capture: { running: false, region: null, engine: 'Windows OCR', captureIntervalMs: 20, confirmFrames: 1, cooldownSeconds: 30, scaleFactor: 3, grabOnStart: true, ocrEngine: 0, soundEnabled: true, clipboardClearSeconds: 180, autoPasteEnabled: false, autoPasteDelayMs: 0, autoClickEnabled: false, autoClickPosition: null },
    }));
    if (await page.locator('#host-state').textContent() !== '客户端已连接') throw new Error('Host init was not rendered');

    const layout = await page.evaluate(() => {
      const playback = document.querySelector('.playback-column').getBoundingClientRect();
      const controls = document.querySelector('.control-rail').getBoundingClientRect();
      const monitor = document.querySelector('[aria-labelledby="monitor-heading"]').getBoundingClientRect();
      const capture = document.querySelector('[aria-labelledby="capture-heading"]').getBoundingClientRect();
      return { playbackBottom: playback.bottom, controlsTop: controls.top, monitorTop: monitor.top, captureTop: capture.top };
    });
    if (layout.controlsTop < layout.playbackBottom - 1) throw new Error(`Controls are not below playback: ${JSON.stringify(layout)}`);
    if (width > 710 && Math.abs(layout.monitorTop - layout.captureTop) > 1) throw new Error(`Desktop controls are not aligned: ${JSON.stringify(layout)}`);
    if (width <= 710 && layout.captureTop <= layout.monitorTop) throw new Error(`Narrow controls did not stack: ${JSON.stringify(layout)}`);

    const output = join(outputDir, `client-${width}-${runId}.png`);
    if (existsSync(output)) throw new Error(`Screenshot already exists: ${output}`);
    await page.screenshot({ path: output, fullPage: true });
    const metrics = await page.evaluate(() => ({ viewport: innerWidth, content: document.documentElement.scrollWidth, tabOrder: document.querySelector('#play-btn')?.getAttribute('aria-label') }));
    if (metrics.content > metrics.viewport) throw new Error(`${width}px has horizontal overflow: ${JSON.stringify(metrics)}`);
    if (metrics.tabOrder !== '播放 FLV') throw new Error('Play button has no accessible name');

    await page.locator('#url-input').fill('https://cdn.example/not-flv');
    await page.locator('#play-btn').click();
    if (!await page.locator('#manual-error').isVisible()) throw new Error('Invalid FLV address was not rejected');
    await page.locator('#room-input').fill('123456');
    await page.locator('#monitor-toggle').click();
    await page.evaluate(() => window.__hostEvent('monitor.status', { enabled: true, room: '123456', intervalSec: 15, live: true, credentialConfigured: true }));
    if (await page.locator('#monitor-summary').textContent() !== '正在直播' || await page.locator('#credential-state').textContent() !== '已保存 Cookie') throw new Error('Monitor status was not rendered');
    await page.locator('#monitor-toggle').click();
    await page.locator('#settings-details summary').click();
    if (!await page.locator('#auto-click').isDisabled() || await page.locator('#cursor-position').textContent() !== '未记录') throw new Error('Click is available without a recorded position');
    await page.locator('#record-cursor').click();
    await page.evaluate(() => window.__hostEvent('app.init', {
      playback: { catchupEnabled: true, speedThresholdMs: 1000, speedRate: 1.1, jumpThresholdMs: 2000 },
      monitor: { enabled: false, room: '123456', intervalSec: 15, live: false, credentialConfigured: false },
      capture: { running: false, region: null, engine: 'Windows OCR', captureIntervalMs: 20, confirmFrames: 1, cooldownSeconds: 30, scaleFactor: 3, grabOnStart: true, ocrEngine: 0, soundEnabled: true, clipboardClearSeconds: 180, autoPasteEnabled: false, autoPasteDelayMs: 0, autoClickEnabled: false, autoClickPosition: { x: -100, y: 240 } },
    }));
    if (await page.locator('#auto-click').isDisabled() || await page.locator('#cursor-position').textContent() !== '-100, 240') throw new Error('Recorded position was not rendered');
    await page.locator('#auto-click').check();
    await page.locator('#speed-rate').fill('1.2');
    await page.locator('#speed-rate').dispatchEvent('change');
    await page.locator('#confirm-frames').fill('2');
    await page.locator('#confirm-frames').dispatchEvent('change');
    await page.locator('#grab-on-start').uncheck();
    await page.locator('#ocr-engine').selectOption('1');
    await page.locator('#clear-clipboard').click();
    const commands = await page.evaluate(() => window.__commands);
    for (const type of ['monitor.start', 'monitor.stop', 'playback.settings', 'capture.settings', 'capture.cursor.record', 'clipboard.clear']) {
      if (!commands.some((command) => command.type === type)) throw new Error(`${type} command was not sent`);
    }
    if (!commands.some(({ type, payload }) => type === 'monitor.start' && payload.room === '123456')) throw new Error('Monitor command payload is wrong');
    if (!commands.some(({ type, payload }) => type === 'capture.settings' && payload.grabOnStart === false && payload.ocrEngine === 1 && payload.autoClickEnabled)) throw new Error('OCR settings payload is wrong');
    await page.evaluate(() => window.__hostEvent('app.init', {
      playback: { catchupEnabled: false, speedThresholdMs: 850, speedRate: 1.35, jumpThresholdMs: null },
      monitor: { enabled: true, room: '123456', intervalSec: 15, live: true, credentialConfigured: true },
      capture: { running: false, region: null, engine: 'Windows OCR', captureIntervalMs: 40, confirmFrames: 2, cooldownSeconds: 10, scaleFactor: 4, grabOnStart: false, ocrEngine: 1, soundEnabled: false, clipboardClearSeconds: 0, autoPasteEnabled: true, autoPasteDelayMs: 350, autoClickEnabled: true, autoClickPosition: { x: -100, y: 240 } },
    }));
    if (await page.locator('#grab-on-start').isChecked() || !await page.locator('#auto-click').isChecked() || await page.locator('#ocr-engine').inputValue() !== '1' || await page.locator('#speed-rate').inputValue() !== '1.35' || await page.locator('#monitor-summary').textContent() !== '正在直播') throw new Error('Settings/status acknowledgement was not rendered');
    const expanded = await page.evaluate(() => ({ viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth,
      rail: document.querySelector('#settings-details').getBoundingClientRect().toJSON(),
      control: document.querySelector('#record-cursor').getBoundingClientRect().toJSON() }));
    if (expanded.scrollWidth > expanded.viewport || expanded.rail.left < 0 || expanded.control.left < 0 || expanded.control.right > expanded.viewport) throw new Error(`Expanded settings overflow: ${JSON.stringify(expanded)}`);
    const settingsOutput = join(outputDir, `client-${width}-${runId}-settings.png`);
    if (existsSync(settingsOutput)) throw new Error(`Screenshot already exists: ${settingsOutput}`);
    await page.locator('#settings-details').screenshot({ path: settingsOutput });
    await page.locator('#settings-details summary').click();

    await page.evaluate(() => window.__hostEvent('capture.status', { running: true, region: { width: 180, height: 80 }, engine: 'Windows OCR' }));
    await page.evaluate(() => window.__hostEvent('capture.code', { code: 'ABC123', latencyMs: 48 }));
    if (await page.locator('#last-code').textContent() !== 'ABC123') throw new Error('Capture code was not rendered');
    await page.evaluate(() => window.__hostEvent('capture.status', { running: true, region: { width: 180, height: 80 }, engine: 'Windows OCR', debug: 'A'.repeat(140) }));
    await page.locator('.ocr-debug summary').click();
    if ((await page.locator('#ocr-output').textContent()).length !== 140) throw new Error('OCR debug output was not rendered');
    const debugOutput = join(outputDir, `client-${width}-${runId}-debug.png`);
    if (existsSync(debugOutput)) throw new Error(`Screenshot already exists: ${debugOutput}`);
    await page.screenshot({ path: debugOutput, fullPage: width === 390 });
    const debugWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    if (debugWidth > width) throw new Error(`OCR debug caused horizontal overflow: ${debugWidth}/${width}`);
    await page.locator('#url-input').focus();
    await page.keyboard.press('Tab');
    const focus = await page.evaluate(() => ({ id: document.activeElement.id, outline: getComputedStyle(document.activeElement).outlineStyle }));
    if (focus.id !== 'play-btn' || focus.outline === 'none') throw new Error(`No visible play-button keyboard focus: ${JSON.stringify(focus)}`);
    await page.locator('#settings-summary').focus();
    await page.keyboard.press('Enter');
    if (!await page.locator('#settings-details').evaluate((details) => details.open)) throw new Error('Settings are not keyboard operable');
    await page.keyboard.press('Enter');
    await page.evaluate(() => window.__hostEvent('app.error', { message: '监测服务暂时不可用' }));
    if (await page.locator('#toast').textContent() !== '监测服务暂时不可用') throw new Error('Host error was not rendered');
    await page.evaluate(() => window.__hostEvent('monitor.stream', { url: 'https://cdn.example/live.flv?sign=1' }));
    if (await page.locator('#url-input').inputValue() !== 'https://cdn.example/live.flv?sign=1') throw new Error('Monitored stream did not reach playback');
    await page.locator('#url-input').fill('https://cdn.example/manual.flv');
    await page.locator('#play-btn').click();
    if (!await page.evaluate(() => window.__commands.some(({ type, payload }) => type === 'playback.manual-start' && payload.url === 'https://cdn.example/manual.flv'))) throw new Error('Manual playback command was not sent');
    if (errors.length || requests.some((url) => url.includes('/__watch/api'))) throw new Error(`Browser errors/legacy requests: ${JSON.stringify({ errors, requests })}`);
    console.log(`${width}x${height}: ${output} · overflow ${metrics.content}/${metrics.viewport} · focus ${focus.id}`);
    await context.close();
  }
} finally {
  await browser.close();
}
