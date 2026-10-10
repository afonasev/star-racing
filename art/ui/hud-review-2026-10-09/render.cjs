// Run with Playwright available in NODE_PATH. No Unity processes or game code.
const {chromium} = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const browser = await chromium.launch({headless: true});
  try {
    let page = await browser.newPage({viewport: {width: 1440, height: 1100}, deviceScaleFactor: 1});
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    // Offline artifact rendering: no file:// navigation or existing browser-tab control.
    const html = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8').replace('source-${theme}.png', '${theme==="cloud"?cloudImage:spaceImage}');
    const cloudImage = 'data:image/png;base64,' + fs.readFileSync(path.join(__dirname, 'source-cloud.png')).toString('base64');
    const spaceImage = 'data:image/png;base64,' + fs.readFileSync(path.join(__dirname, 'source-space.png')).toString('base64');
    const offlineHtml = html.replace('<script>', `<script>const cloudImage=${JSON.stringify(cloudImage)},spaceImage=${JSON.stringify(spaceImage)};`);
    async function resetPage() {
      const viewport = page.viewportSize();
      await page.close();
      page = await browser.newPage({viewport, deviceScaleFactor: 1});
      page.on('pageerror', e => errors.push(e.message));
      await page.setContent(offlineHtml);
    }
    for (const id of ['cloud', 'apex', 'edge']) {
      for (const [seats, theme] of [[4, 'space'], [1, 'cloud']]) {
        await resetPage();
        await page.evaluate(({id,seats,theme}) => {query.set('shot',id);document.body.classList.add('shot');document.querySelector('#seats').value=String(seats);document.querySelector('#theme').value=theme;render();}, {id,seats,theme});
        await page.waitForFunction(() => Array.from(document.querySelectorAll('.back image')).every(e => e.getAttribute('href')));
        const height = await page.locator('main').evaluate(e => Math.ceil(e.getBoundingClientRect().bottom));
        await page.screenshot({path: path.join(__dirname, `${id}-${seats === 4 ? 'four' : 'solo'}.png`), clip: {x: 0, y: 0, width: 1440, height}});
      }
    }
    let checked = 0;
    for (const width of [1280, 1440, 1920]) {
      await page.setViewportSize({width, height: 1100});
      await resetPage();
      for (const seats of ['1', '2', '3', '4']) for (const entrants of ['8', '16', '32', '64']) for (const theme of ['space', 'cloud']) {
        await page.selectOption('#seats', seats);
        await page.selectOption('#entrants', entrants);
        await page.selectOption('#theme', theme);
        const valid = await page.evaluate(() => Array.from(document.querySelectorAll('.race')).map(r => {
          const n = +r.dataset.seats, total = +document.querySelector('#entrants').value;
          const bounds = Array.from(r.querySelectorAll('.unit')).every(u => {
            const a = u.getBoundingClientRect(), b = u.parentElement.getBoundingClientRect();
            return a.left >= b.left && a.top >= b.top && a.right <= b.right && a.bottom <= b.bottom;
          });
          return r.querySelectorAll('.unit').length === n && r.querySelectorAll('.rail').length === 1 && r.querySelectorAll('.marker').length === total && r.querySelectorAll('.marker.human').length === n && bounds;
        }));
        if (valid.some(x => !x)) throw new Error(`Layout failed: ${width}/${seats}/${entrants}/${theme}`);
        checked += valid.length;
      }
    }
    if (errors.length) throw new Error(errors.join('\n'));
    console.log(`${checked} concept/layout/roster/theme/width combinations passed; six previews rendered; no page errors.`);
  } finally {await browser.close();}
})().catch(e => {console.error(e); process.exitCode = 1;});
