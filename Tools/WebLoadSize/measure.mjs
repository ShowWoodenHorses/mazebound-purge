// Bytes a new player downloads before the first CrazyGames gameplayStart (headless Chrome over CDP; Node 22+).
// 1. Build Purge → Build → WebGL CrazyGames (Release).
// 2. Serve it: cd Builds/WebGL-CrazyGames && python -m http.server 8080 --bind 127.0.0.1  (localhost: the SDK's local mode)
// 3. node Tools/WebLoadSize/measure.mjs "C:/Program Files/Google/Chrome/Application/chrome.exe" http://localhost:8080/index.html <empty profile dir>
//    A fresh profile = a new player (no save, cache off). The end mark is the SDK's "Requesting gameplay start" log;
//    set ALLCONSOLE=1 to print the page's console. Limit (CrazyGames mobile home page): 20 MB.
import { spawn } from 'node:child_process';

const [chrome, url, profile] = process.argv.slice(2);
const port = 9333;
const proc = spawn(chrome, [
  '--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist',
  '--autoplay-policy=no-user-gesture-required', '--window-size=1280,720', 'about:blank'
], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));
let version;
for (let i = 0; i < 50 && !version; i++) {
  try { version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json(); } catch { await sleep(200); }
}
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page = targets.find(t => t.type === 'page');
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0;
const pending = new Map();
const send = (method, params = {}) => new Promise(res => { const i = ++id; pending.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });

const requests = new Map();
const start = Date.now();
let bytes = 0, done = false, marks = [];
const files = [];
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); return; }
  if (done) return;
  if (m.method === 'Network.requestWillBeSent') requests.set(m.params.requestId, m.params.request.url);
  if (m.method === 'Network.loadingFinished') {
    bytes += m.params.encodedDataLength;
    files.push([m.params.encodedDataLength, requests.get(m.params.requestId) || '?']);
  }
  if (m.method === 'Runtime.exceptionThrown') {
    const d = m.params.exceptionDetails;
    marks.push('exception: ' + (d.exception && d.exception.description || d.text).slice(0, 400));
  }
  if (m.method === 'Runtime.consoleAPICalled' && process.env.ALLCONSOLE) {
    marks.push('log: ' + m.params.args.map(a => a.value ?? a.description ?? '').join(' ').slice(0, 200));
  }
  if (m.method === 'Runtime.consoleAPICalled') {
    const text = m.params.args.map(a => a.value ?? '').join(' ');
    if (/PURGE_GAMEPLAY_START|Requesting gameplay start/.test(text)) { done = true; marks.push(`gameplayStart at ${((Date.now() - start) / 1000).toFixed(1)} s`); }
    else if (/error|exception/i.test(text)) marks.push('console: ' + text.slice(0, 200));
  }
});

await send('Network.enable');
await send('Runtime.enable');
await send('Page.enable');
await send('Network.setCacheDisabled', { cacheDisabled: true });
await send('Page.addScriptToEvaluateOnNewDocument', { source: `
  const timer = setInterval(() => {
    try {
      const p = window.purgeCrazy;
      const g = p && p.sdk && p.sdk.game; // set by the game's bridge after SDK.init
      if (g && !g.__purgeWrapped && typeof g.gameplayStart === 'function') {
        const original = g.gameplayStart.bind(g);
        g.gameplayStart = function () { console.log('PURGE_GAMEPLAY_START'); return original.apply(null, arguments); };
        g.__purgeWrapped = true;
        clearInterval(timer);
      }
    } catch (e) { }
  }, 5);` });
await send('Page.navigate', { url });

for (let t = 0; t < 480 && !done; t++) await sleep(500);
const mb = b => (b / 1048576).toFixed(2);
files.sort((a, b) => b[0] - a[0]);
console.log(done ? marks.join('\n') : 'gameplayStart NOT reached in 240 s\n' + marks.slice(-10).join('\n'));
console.log(`TOTAL before gameplayStart: ${mb(bytes)} MB in ${files.length} requests`);
const local = files.filter(f => f[1].includes('127.0.0.1') || f[1].includes('localhost'));
console.log(`  from the build: ${mb(local.reduce((s, f) => s + f[0], 0))} MB; external: ${mb(bytes - local.reduce((s, f) => s + f[0], 0))} MB`);
for (const [b, u] of files.slice(0, 25)) console.log(`  ${mb(b).padStart(6)}  ${u.replace(/^https?:\/\/[^/]+\//, '').slice(0, 110)}`);
ws.close();
proc.kill();
process.exit(0);
