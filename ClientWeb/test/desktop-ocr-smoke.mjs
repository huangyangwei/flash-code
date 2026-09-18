import { chromium } from 'playwright-core';
import { existsSync } from 'node:fs';

const mode = process.argv[2];
const endpoint = process.argv[3] ?? 'http://127.0.0.1:9245';
const browser = await chromium.connectOverCDP(endpoint);
try {
  const page = browser.contexts().flatMap((context) => context.pages())
    .find((candidate) => candidate.url().startsWith('https://flash-client.local/'));
  if (!page) throw new Error('Packaged WebView2 page is missing');

  if (mode === 'prepare') {
    const region = await page.evaluate(() => {
      const frame = document.querySelector('#video-frame');
      const scene = document.createElement('div');
      scene.id = 'ocr-acceptance-scene';
      scene.textContent = 'ASD123';
      Object.assign(scene.style, {
        position: 'absolute', left: '50%', top: '50%', transform: 'translate(-50%, -50%)',
        width: '350px', height: '136px', display: 'grid', placeItems: 'center',
        background: '#ffffff', color: '#000000', font: 'bold 68px Arial, sans-serif',
        letterSpacing: '0', zIndex: '100', pointerEvents: 'none',
      });
      frame.append(scene);
      const rect = scene.getBoundingClientRect();
      return { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height) };
    });
    console.log(JSON.stringify(region));
  } else if (mode === 'select') {
    await page.evaluate(() => chrome.webview.postMessage({ version: 1, type: 'capture.select', payload: {} }));
  } else if (mode === 'check') {
    await page.locator('#last-code').getByText('ASD123').waitFor({ timeout: 10000 });
    console.log(JSON.stringify(await page.evaluate(() => ({
      code: document.querySelector('#last-code').textContent,
      latency: document.querySelector('#code-latency').textContent,
      region: document.querySelector('#region-detail').textContent,
      status: document.querySelector('#capture-summary').textContent,
    }))));
  } else if (mode === 'clear') {
    await page.locator('#clear-clipboard').click();
    await page.locator('#toast').getByText('剪贴板已清空').waitFor({ timeout: 5000 });
    console.log('Clipboard clear confirmed in packaged UI');
  } else if (mode === 'stop') {
    if (await page.locator('#capture-summary').textContent() === '正在抓取') {
      await page.locator('#capture-toggle').click();
    }
    await page.waitForFunction(() => document.querySelector('#capture-summary').textContent !== '正在抓取');
    console.log('Capture stopped');
  } else if (mode === 'paste') {
    await page.locator('#capture-toggle').click();
    await page.waitForFunction(() => document.querySelector('#capture-summary').textContent === '已暂停');
    await page.locator('#settings-details').evaluate((details) => { details.open = true; });
    await page.locator('#paste-delay').fill('1500');
    await page.locator('#paste-delay').dispatchEvent('change');
    await page.locator('#auto-paste').check();
    await page.locator('#room-input').fill('');
    await page.locator('#room-input').focus();
    try {
      await page.evaluate(() => chrome.webview.postMessage({ version: 1, type: 'capture.toggle', payload: { enabled: true } }));
      await page.waitForFunction(() => document.querySelector('#room-input').value === 'ASD123', undefined, { timeout: 8000 });
      console.log('Auto-paste inserted ASD123 into the packaged client input');
    } finally {
      await page.evaluate(() => chrome.webview.postMessage({ version: 1, type: 'capture.toggle', payload: { enabled: false } }));
      await page.locator('#auto-paste').uncheck();
      await page.locator('#paste-delay').fill('0');
      await page.locator('#paste-delay').dispatchEvent('change');
    }
  } else if (mode === 'inspect') {
    console.log(JSON.stringify(await page.evaluate(() => ({
      badge: document.querySelector('#playback-badge').textContent,
      action: document.querySelector('#playback-action').textContent,
      watermark: document.querySelector('#watermark').textContent,
      dialog: document.querySelector('#error-dialog').open,
      error: document.querySelector('#error-message').textContent,
      monitor: document.querySelector('#monitor-summary').textContent,
      events: (window.__qaHostEvents ?? []).filter((event) => event.type === 'monitor.stream' || (event.type === 'monitor.status' && event.payload.error)).map((event) => ({ type: event.type, payload: event.payload })),
    }))));
  } else if (mode === 'snapshot') {
    const destination = process.argv[4];
    if (!destination || existsSync(destination)) throw new Error('Pass a new screenshot destination');
    await page.screenshot({ path: destination, fullPage: true });
    console.log(`Capture state: ${destination}`);
  } else {
    throw new Error('Specify prepare, select, check, clear, stop, paste, inspect or snapshot');
  }
} finally {
  await browser.close();
}
