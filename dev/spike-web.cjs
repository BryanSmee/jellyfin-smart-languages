// Jellyfin Web cases of the Mechanism A spike (docs/spike-mechanism-a.md), in headless Chromium.
//
//   JELLYFIN_URL=http://localhost:8096 node dev/spike-web.cjs <user> <item name> [card] [transcode] [switch] [next] [remember=on|off]
//
// Logs in as <user> (password = user name, see dev/setup-server.py), plays the item from its details page (or from
// its card with `card`) and prints what the player hears: the test tracks are sines at 330 Hz (jpn), 262 Hz (fra)
// and 196 Hz (eng), measured with a Web Audio analyser. Also prints the PlaybackInfo requests and responses and the
// server's session. `transcode` sets the player's bitrate limit to 400 kbps, `switch` picks English from the audio
// menu during playback, `next` presses the player's Next button, `remember=` sets the user's "Remember audio
// selections" first. Needs Playwright (`npm install playwright`).
const { chromium } = require('playwright');
const SERVER = (process.env.JELLYFIN_URL || 'http://localhost:8096').replace(/\/$/, '');
const [user, itemName, ...flags] = process.argv.slice(2);
const transcode = flags.includes('transcode');
const remember = (flags.find(f => f.startsWith('remember=')) || '').slice(9);
const FREQ = { 330: 'jpn', 262: 'fra', 196: 'eng' };

async function api(path, token, body) {
  const r = await fetch(SERVER + path, { method: body ? 'POST' : 'GET', headers: { 'Content-Type': 'application/json',
    Authorization: `MediaBrowser Client="t", Device="t", DeviceId="t-${user}", Version="1"${token ? `, Token="${token}"` : ''}` },
    body: body ? JSON.stringify(body) : undefined });
  const t = await r.text(); return t ? JSON.parse(t) : null;
}

(async () => {
  const login = await api('/Users/AuthenticateByName', null, { Username: user, Pw: user });
  const admin = (await api('/Users/AuthenticateByName', null, { Username: 'admin', Pw: 'admin' })).AccessToken;
  const items = await api(`/Items?userId=${login.User.Id}&Recursive=true&IncludeItemTypes=Movie,Episode`, login.AccessToken);
  const item = items.Items.find(i => i.Name === itemName);
  if (remember) {
    const configuration = { ...login.User.Configuration, RememberAudioSelections: remember === 'on' };
    await api(`/Users/Configuration?userId=${login.User.Id}`, login.AccessToken, configuration);
  }

  const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  await page.addInitScript(([transcode]) => {
    if (transcode) {
      for (const n of ['true', 'false']) {
        localStorage.setItem(`enableautobitratebitrate-Video-${n}`, 'false');
        localStorage.setItem(`maxbitrate-Video-${n}`, '400000');
      }
    }
    // Dominant frequency of whatever the video element plays.
    window.__freq = () => {
      const v = document.querySelector('video');
      if (!v) return null;
      if (!v.__an) {
        const ctx = new AudioContext(); const src = ctx.createMediaElementSource(v); const an = ctx.createAnalyser();
        an.fftSize = 32768; src.connect(an); an.connect(ctx.destination); v.__an = an; v.__ctx = ctx; return null;
      }
      const data = new Float32Array(v.__an.frequencyBinCount); v.__an.getFloatFrequencyData(data);
      let best = 0; for (let i = 1; i < data.length; i++) if (data[i] > data[best]) best = i;
      return Math.round(best * v.__ctx.sampleRate / v.__an.fftSize);
    };
  }, [transcode]);
  const infos = [];
  page.on('request', r => { if (/PlaybackInfo/.test(r.url())) infos.push({ url: r.url(), body: r.postData() }); });
  page.on('response', async r => { if (/PlaybackInfo/.test(r.url())) { const j = await r.json().catch(() => null);
    const e = infos.find(i => i.url === r.url() && !i.resp); if (e) e.resp = j; } });
  const streams = [];
  page.on('request', r => { if (/\/(stream|master\.m3u8|main\.m3u8)/i.test(r.url())) streams.push(r.url().replace(SERVER, '')); });

  await page.goto(`${SERVER}/web/#/login`);
  await page.click('.btnManual', { timeout: 15000 }).catch(() => {});
  await page.fill('#txtManualName', user);
  await page.fill('#txtManualPassword', user);
  await page.click('.manualLoginForm button[type=submit]');
  await page.waitForURL(/home/, { timeout: 20000 });
  const fromCard = flags.includes('card');
  if (fromCard) {
    // A card's play button: no audio index sent, unlike the details page's Play.
    const parent = item.Type === 'Episode' ? item.SeasonId : null;
    if (parent) await page.goto(`${SERVER}/web/#/details?id=${parent}`);
    const sel = `[data-id="${item.Id}"] [data-action=resume], [data-id="${item.Id}"] [data-action=play]`;
    await page.waitForSelector(sel, { state: 'attached', timeout: 20000 });
    await page.hover(`[data-id="${item.Id}"]`).catch(() => {});
    await page.evaluate((sel) => document.querySelector(sel).click(), sel);
  } else {
    await page.goto(`${SERVER}/web/#/details?id=${item.Id}`);
    await page.click('.detailButton.btnPlay, .btnPlay', { timeout: 20000 });
  }
  await page.waitForSelector('video', { timeout: 20000 });
  await page.waitForFunction(() => { const v = document.querySelector('video'); return v && v.currentTime > 1; }, null, { timeout: 60000 });
  await page.evaluate(() => window.__freq());

  const sample = async () => { await page.waitForTimeout(2500); return page.evaluate(() => window.__freq()); };
  const session = async () => (await api('/Sessions', admin)).find(s => s.UserName === user && s.NowPlayingItem);
  const report = async (label) => {
    const f = await sample(); const s = await session();
    const lang = Object.entries(FREQ).find(([hz]) => Math.abs(hz - f) < 8)?.[1] ?? '?';
    console.log(`${label}: hears ${f} Hz (${lang}); session ${s?.NowPlayingItem?.Name}, ${s?.PlayState?.PlayMethod}, AudioStreamIndex ${s?.PlayState?.AudioStreamIndex}`);
  };
  const dumpInfos = () => { for (const i of infos.splice(0)) {
    const body = i.body ? JSON.parse(i.body) : {}; const src = i.resp?.MediaSources?.[0];
    console.log(`  PlaybackInfo sent AudioStreamIndex=${body.AudioStreamIndex ?? '-'} MediaSourceId=${body.MediaSourceId ?? '-'}; got ${src?.Name} DefaultAudioStreamIndex=${src?.DefaultAudioStreamIndex} ${src?.TranscodingUrl ? 'TranscodingUrl ' + (src.TranscodingUrl.match(/AudioStreamIndex=\d+/) || ['-'])[0] : 'no TranscodingUrl'}`);
  } for (const u of streams.splice(0)) console.log(`  stream ${u.slice(0, 160)}`); };

  console.log(`== ${user}, ${itemName}, ${fromCard ? 'card play button' : 'details page Play'}${transcode ? ', 400 kbps limit' : ''}${remember ? ', remember audio ' + remember : ''}`);
  await report('start'); dumpInfos();

  if (flags.includes('switch')) {
    // Pick the English track from the player's audio menu.
    await page.mouse.move(700, 450);
    await page.evaluate(() => document.querySelector('.videoOsdBottom .btnAudio, .btnAudio').click());
    await page.click('.actionSheetMenuItem:has-text("English")', { timeout: 10000 });
    await page.waitForTimeout(4000);
    await report('after switching to English'); dumpInfos();
    await page.waitForTimeout(8000);
    await report('8 s later'); dumpInfos();
  }
  if (flags.includes('next')) {
    // The player's own Next button: the same path as autoplay, with Jellyfin Web's track carry-over.
    await page.mouse.move(700, 450);
    await page.evaluate(() => document.querySelector('.videoOsdBottom .btnNextTrack, .btnNextTrack').click());
    await page.waitForFunction(() => { const v = document.querySelector('video'); return v && v.currentTime > 1 && v.currentTime < 20; }, null, { timeout: 60000 });
    await page.waitForTimeout(2000);
    await page.evaluate(() => window.__freq());
    await report('next episode'); dumpInfos();
  }
  await page.evaluate(() => document.querySelector('video')?.pause());
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
