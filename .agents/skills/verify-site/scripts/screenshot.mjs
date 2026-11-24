import { createRequire } from 'node:module';
const require = createRequire(process.cwd() + '/');
const { chromium } = require('playwright');

const [url, output, width = '1280', height = '800'] = process.argv.slice(2);
if (!url || !output) {
  console.error('Usage: node screenshot.mjs <url> <output.png> [width] [height]');
  process.exit(2);
}

const origin = new URL(url).origin;
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: Number(width), height: Number(height) } });
  const scriptErrors = [];
  const failedRequests = [];
  page.on('pageerror', error => scriptErrors.push(error.message));
  page.on('requestfailed', request => failedRequests.push({ url: request.url(), error: request.failure()?.errorText }));
  page.on('response', response => {
    if (response.status() >= 400) failedRequests.push({ url: response.url(), status: response.status() });
  });
  const response = await page.goto(url, { waitUntil: 'networkidle' });
  await page.screenshot({ path: output, fullPage: false });
  const sameOrigin = failedRequests.filter(request => new URL(request.url).origin === origin);
  const thirdParty = failedRequests.filter(request => new URL(request.url).origin !== origin);
  console.log(JSON.stringify({
    url, status: response?.status(), title: await page.title(), scriptErrors, sameOrigin, thirdParty, output,
  }, null, 2));
  if (!response || response.status() >= 400 || scriptErrors.length > 0 || sameOrigin.length > 0) process.exitCode = 1;
} finally {
  await browser.close();
}
