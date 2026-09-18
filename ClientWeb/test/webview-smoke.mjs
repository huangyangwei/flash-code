import { chromium } from 'playwright-core';

const endpoint = process.argv[2] ?? 'http://127.0.0.1:9234';
const browser = await chromium.connectOverCDP(endpoint);
try {
  const pages = browser.contexts().flatMap((context) => context.pages());
  const page = pages.find((candidate) => candidate.url().startsWith('https://flash-client.local/'));
  if (!page) throw new Error(`No embedded client page; observed ${pages.map((candidate) => candidate.url()).join(', ')}`);
  await page.locator('#host-state').waitFor({ state: 'visible', timeout: 10000 });
  const state = await page.locator('#host-state').textContent();
  const visibleControls = await page.locator('#video, #monitor-toggle, #select-region, #clear-clipboard').count();
  if (state !== '客户端已连接' || visibleControls !== 4) {
    throw new Error(`Embedded client did not initialize: ${JSON.stringify({ state, visibleControls })}`);
  }
  console.log(`WebView2 smoke passed: ${page.url()} · ${state} · ${visibleControls} primary controls`);
} finally {
  await browser.close();
}
