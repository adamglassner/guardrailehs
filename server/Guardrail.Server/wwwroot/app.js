// GuardHawk Server web panel. Plain JavaScript, no build step.
import { ICONS } from './icons.js';

const $app = document.getElementById('app');
const state = { page: 'overview', overview: null, timer: null, me: null, version: '', orgs: [], dataOrg: null, dataCollection: null, dataQuery: '', dataOffset: 0 };

// ----- Helpers -----

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

function icon(name, cls = '') {
  const paths = ICONS[name] || ICONS[name.replace('-fill', '')] || [];
  return `<svg class="icon ${cls}" viewBox="0 0 20 20" aria-hidden="true">${paths.map((d) => `<path d="${d}"/>`).join('')}</svg>`;
}

async function api(path, { method = 'GET', body } = {}) {
  const res = await fetch('/admin/api' + path, {
    method,
    headers: { 'Content-Type': 'application/json', 'X-Guardrail': 'admin' },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'same-origin',
  });
  if (res.status === 401 && path !== '/login') { showSignIn(); throw new Error('Signed out'); }
  const text = await res.text();
  const data = text ? JSON.parse(text) : {};
  if (!res.ok) throw new Error(data.message || `Error ${res.status}`);
  return data;
}

function toast(message, error = false) {
  const el = document.createElement('div');
  el.className = 'toast' + (error ? ' error' : '');
  el.innerHTML = icon(error ? 'warning' : 'checkmark') + `<span>${esc(message)}</span>`;
  document.getElementById('toasts').appendChild(el);
  setTimeout(() => { el.style.transition = 'opacity .3s'; el.style.opacity = '0'; setTimeout(() => el.remove(), 300); }, 3800);
}

function bytes(n) {
  if (n == null || n < 0) return '—';
  const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0;
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return `${n < 10 && i > 0 ? n.toFixed(1) : Math.round(n)} ${u[i]}`;
}
const num = (n) => (n ?? 0).toLocaleString();
function ago(iso) {
  if (!iso) return 'never';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`;
  if (s < 86400 * 2) return 'yesterday';
  if (s < 86400 * 30) return `${Math.floor(s / 86400)} days ago`;
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}
const date = (iso) => iso ? new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
const dateTime = (iso) => new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
function uptime(sec) {
  const d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
  return d ? `${d}d ${h}h` : h ? `${h}h ${m}m` : `${m} min`;
}
const COLORS = ['#1f6b4f', '#2563a8', '#7c3aed', '#b4235a', '#c2410c', '#0e7490', '#4d5b8a', '#9a6b10'];
const avatar = (name, size = 34) => `<span class="avatar" style="background:${COLORS[[...name].reduce((a, c) => a + c.charCodeAt(0), 0) % COLORS.length]};width:${size}px;height:${size}px;font-size:${Math.round(size * 0.42)}px">${esc(name[0])}</span>`;
const platformIcon = (p) => /android|ios|phone/i.test(p || '') ? 'phone' : 'desktop';
const ACT_ICONS = { signup: 'person', signin: 'sign-out', password: 'key', admin: 'settings', invite: 'ticket-diagonal', service: 'server', settings: 'settings' };

async function copy(text) {
  try { await navigator.clipboard.writeText(text); toast('Copied'); }
  catch { toast('Copy it from the box (your browser blocked copying)', true); }
}

function confirmBox({ title, message, confirm = 'OK', danger = false, iconName }) {
  return new Promise((resolve) => {
    const scrim = document.createElement('div'); scrim.className = 'scrim modal-scrim';
    const m = document.createElement('div'); m.className = 'modal'; m.setAttribute('role', 'dialog'); m.setAttribute('aria-modal', 'true');
    m.innerHTML = `<div class="badge-icon ${danger ? 'warn' : ''}">${icon(iconName || (danger ? 'warning' : 'info'), 'lg')}</div>
      <h2>${esc(title)}</h2><p>${esc(message)}</p>
      <div class="actions"><button class="btn" data-no>Cancel</button><button class="btn ${danger ? 'danger-fill' : 'primary'}" data-yes>${esc(confirm)}</button></div>`;
    const done = (v) => { scrim.remove(); m.remove(); document.removeEventListener('keydown', key); resolve(v); };
    const key = (e) => { if (e.key === 'Escape') done(false); };
    scrim.onclick = () => done(false);
    m.querySelector('[data-no]').onclick = () => done(false);
    m.querySelector('[data-yes]').onclick = () => done(true);
    document.addEventListener('keydown', key);
    document.body.append(scrim, m);
    m.querySelector('[data-yes]').focus();
  });
}

function toggleTheme() {
  const dark = document.documentElement.dataset.theme
    ? document.documentElement.dataset.theme === 'dark'
    : matchMedia('(prefers-color-scheme: dark)').matches;
  document.documentElement.dataset.theme = dark ? 'light' : 'dark';
  try { localStorage.setItem('gr-theme', document.documentElement.dataset.theme); } catch {}
  render();
}
const isDark = () => document.documentElement.dataset.theme ? document.documentElement.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;

// ----- Labels -----

const ROLES = [['admin', 'Admin', 'Everything, including managing people'], ['manager', 'Manager', 'Adds, edits and deletes records'],
  ['member', 'Member', 'Adds, edits and deletes records'], ['viewer', 'Viewer', 'Read-only']];
const roleLabel = (r) => (ROLES.find(([id]) => id === r) || [r, r])[1];
const rolePill = (r) => `<span class="role ${esc(r)}">${esc(roleLabel(r))}</span>`;
const roleOptions = (sel) => ROLES.map(([id, label, d]) => `<option value="${id}" ${id === sel ? 'selected' : ''}>${label} — ${d}</option>`).join('');

const COLLECTIONS = {
  incidents: 'Incidents', actions: 'Corrective actions', observations: 'Observations', inspections: 'Inspections', 'inspection-templates': 'Checklists',
  training: 'Training', compliance: 'Compliance', claims: 'Claims', library: 'Library', monthly: 'Monthly data', bbs: 'BBS observations',
  permits: 'Work permits', loto: 'LOTO procedures', sds: 'Safety data sheets', 'risk-assessments': 'Risk assessments', moc: 'Management of change',
  'fleet-drivers': 'Drivers', 'fleet-vehicles': 'Vehicles', 'fleet-dvirs': 'DVIRs', 'fleet-maintenance': 'Maintenance', 'fleet-hos': 'Hours of service',
  'fleet-accidents': 'Fleet accidents', 'crash-reviews': 'Crash reviews', locations: 'Locations',
};
const PREFIX = {
  incidents: 'INC', actions: 'ACT', observations: 'OBS', inspections: 'INS', 'inspection-templates': 'TPL', training: 'TRN', compliance: 'CMP', claims: 'CLM',
  library: 'DOC', monthly: 'MON', bbs: 'BBS', permits: 'PTW', loto: 'LOTO', sds: 'SDS', 'risk-assessments': 'RA', moc: 'MOC', 'fleet-drivers': 'DRV',
  'fleet-vehicles': 'VEH', 'fleet-dvirs': 'DVIR', 'fleet-maintenance': 'WO', 'fleet-hos': 'HOS', 'fleet-accidents': 'ACC', 'crash-reviews': 'CR', locations: 'LOC',
};
const collName = (c) => COLLECTIONS[c] || c;
const recNumber = (c, n) => n ? `${PREFIX[c] || c.slice(0, 3).toUpperCase()}-${String(n).padStart(4, '0')}` : '—';

function drawer(title, html, wire) {
  const scrim = document.createElement('div'); scrim.className = 'scrim';
  const el = document.createElement('aside'); el.className = 'drawer'; el.setAttribute('role', 'dialog'); el.setAttribute('aria-label', title);
  const close = () => { scrim.remove(); el.remove(); document.removeEventListener('keydown', key); };
  const key = (e) => { if (e.key === 'Escape' && !document.querySelector('.modal')) close(); };
  el.innerHTML = html;
  scrim.onclick = close;
  document.addEventListener('keydown', key);
  document.body.append(scrim, el);
  el.querySelector('[data-close]')?.addEventListener('click', close);
  wire?.(el, close);
  return close;
}

/** Shows a one-time secret (a new password) with a copy button. */
function secretBox(title, intro, secret) {
  return new Promise((resolve) => {
    const scrim = document.createElement('div'); scrim.className = 'scrim modal-scrim';
    const m = document.createElement('div'); m.className = 'modal'; m.setAttribute('role', 'dialog');
    m.innerHTML = `<div class="badge-icon">${icon('key', 'lg')}</div><h2>${esc(title)}</h2><p>${esc(intro)}</p>
      <div class="copy-box" style="margin-bottom:18px"><code class="secret">${esc(secret)}</code><button class="btn sm" data-copy>${icon('copy', 'sm')}Copy</button></div>
      <div class="actions"><button class="btn primary" data-yes>Done</button></div>`;
    const done = () => { scrim.remove(); m.remove(); resolve(); };
    m.querySelector('[data-copy]').onclick = () => copy(secret);
    m.querySelector('[data-yes]').onclick = done;
    document.body.append(scrim, m);
  });
}

/** A sleek one-field dialog. Resolves to the text, or null when cancelled. */
function promptBox({ title, label, value = '', confirm = 'Save', iconName = 'settings' }) {
  return new Promise((resolve) => {
    const scrim = document.createElement('div'); scrim.className = 'scrim modal-scrim';
    const m = document.createElement('form'); m.className = 'modal'; m.setAttribute('role', 'dialog');
    m.innerHTML = `<div class="badge-icon">${icon(iconName, 'lg')}</div><h2>${esc(title)}</h2>
      <div class="field" style="text-align:left;margin:14px 0 18px"><label for="pb">${esc(label)}</label><input class="input" id="pb" value="${esc(value)}" required maxlength="80"></div>
      <div class="actions"><button type="button" class="btn" data-no>Cancel</button><button class="btn primary">${esc(confirm)}</button></div>`;
    const done = (v) => { scrim.remove(); m.remove(); document.removeEventListener('keydown', key); resolve(v); };
    const key = (e) => { if (e.key === 'Escape') done(null); };
    scrim.onclick = () => done(null);
    m.querySelector('[data-no]').onclick = () => done(null);
    m.onsubmit = (e) => { e.preventDefault(); done(m.querySelector('#pb').value.trim()); };
    document.addEventListener('keydown', key);
    document.body.append(scrim, m);
    const input = m.querySelector('#pb'); input.focus(); input.select();
  });
}

async function loadOrgs() {
  try { state.orgs = await api('/orgs'); } catch { state.orgs = []; }
  return state.orgs;
}

// ----- Sign-in and first-time setup -----

function authPage(setup) {
  clearInterval(state.timer);
  $app.innerHTML = `<div class="auth"><form class="card" autocomplete="on">
    <img class="logo" src="logo.png" alt="">
    <h1>${setup ? 'Set up GuardHawk Server' : 'GuardHawk Server'}</h1>
    <p class="lead">${setup ? 'Create the admin account for this web panel. It\'s separate from the accounts people use in the GuardHawk app.' : 'Sign in to manage your GuardHawk Server.'}</p>
    ${setup ? '<div class="field"><label for="o">Organization name</label><input class="input" id="o" name="org" autocomplete="organization" placeholder="e.g. Acme Logistics"><span class="hint">Shown to people when they connect. You can change it later.</span></div>' : ''}
    <div class="field"><label for="u">Admin name</label><input class="input" id="u" name="username" autocomplete="username" required minlength="3" value="${setup ? 'admin' : ''}"></div>
    <div class="field"><label for="p">Password</label><input class="input" id="p" name="password" type="password" autocomplete="${setup ? 'new-password' : 'current-password'}" required ${setup ? 'minlength="10"' : ''}>
      ${setup ? '<span class="hint">At least 10 characters.</span>' : ''}</div>
    <div class="error-text" hidden></div>
    <button class="btn primary" style="width:100%;height:46px">${setup ? 'Create admin and continue' : 'Sign in'}</button>
    <p class="muted small" style="text-align:center;margin:18px 0 0">GuardHawk Server ${esc(state.version)} · Bytehawk Studio</p>
  </form></div>`;
  const form = $app.querySelector('form');
  form.querySelector(setup ? '#o' : '#u').focus();
  form.onsubmit = async (e) => {
    e.preventDefault();
    const err = form.querySelector('.error-text');
    err.hidden = true;
    try {
      await api(setup ? '/setup' : '/login', { method: 'POST', body: { username: form.u.value, password: form.p.value, orgName: setup ? form.o.value : undefined } });
      start();
    } catch (ex) { err.textContent = ex.message; err.hidden = false; }
  };
}
const showSignIn = () => authPage(false);

// ----- Shell -----

const PAGES = [
  ['overview', 'Overview', 'home'],
  ['users', 'People', 'people'],
  ['invites', 'Invites', 'ticket-diagonal'],
  ['data', 'Data', 'table'],
  ['nodes', 'Servers', 'server-multiple'],
  ['settings', 'Settings', 'settings'],
  ['logs', 'Logs', 'document-text'],
];

function shell(content) {
  const o = state.overview;
  const running = o ? o.running : true;
  return `<div class="shell">
    <nav class="sidebar" aria-label="Main">
      <div class="brand"><img src="logo.png" alt=""><div><b>GuardHawk</b><span>Server</span></div></div>
      ${PAGES.map(([id, label, ic]) => `<button class="nav ${state.page === id ? 'active' : ''}" data-page="${id}" ${state.page === id ? 'aria-current="page"' : ''}>
        ${icon(state.page === id ? ic + '-fill' : ic)}<span>${label}</span>${id === 'users' && o ? `<span class="count">${o.users}</span>` : ''}${id === 'nodes' && o && o.nodesOnline > 1 ? `<span class="count">${o.nodesOnline}</span>` : ''}</button>`).join('')}
      <div class="spacer"></div>
      <div class="status-card"><div class="row"><span class="dot ${running ? '' : 'off'}"></span>${running ? 'Syncing' : 'Sync paused'}</div>
        <div class="detail">${o ? `${num(o.onlineUsers)} online · up ${uptime(o.uptimeSeconds)}` : ''}</div></div>
      <button class="nav" data-theme-toggle>${icon(isDark() ? 'weather-sunny' : 'weather-moon')}<span>${isDark() ? 'Light mode' : 'Dark mode'}</span></button>
      <button class="nav" data-signout>${icon('sign-out')}<span>Sign out ${esc(state.me || '')}</span></button>
    </nav>
    <main class="main" id="page">${content}</main>
  </div>`;
}

function wireShell() {
  $app.querySelectorAll('[data-page]').forEach((b) => b.onclick = () => go(b.dataset.page));
  $app.querySelector('[data-theme-toggle]').onclick = toggleTheme;
  $app.querySelector('[data-signout]').onclick = async () => { await api('/logout', { method: 'POST', body: {} }); showSignIn(); };
}

function go(page) {
  state.page = page;
  history.replaceState(null, '', '#' + page);
  render();
}

async function render() {
  clearInterval(state.timer);
  const pages = { overview: overviewPage, users: usersPage, invites: invitesPage, data: dataPage, nodes: nodesPage, settings: settingsPage, logs: logsPage };
  await (pages[state.page] || overviewPage)();
}

function paint(html, wire) {
  const scrollY = window.scrollY;
  $app.innerHTML = shell(html);
  wireShell();
  $app.querySelectorAll('[data-goto]').forEach((b) => b.addEventListener('click', (e) => { e.preventDefault(); go(b.dataset.goto); }));
  $app.querySelectorAll('[data-copy]').forEach((b) => b.addEventListener('click', () => copy(b.dataset.copy)));
  wire?.();
  window.scrollTo(0, scrollY);
}

// ----- Overview -----

function chart(o) {
  const w = 720, h = 170, pad = 28, n = o.requestsPerMinute.length;
  const max = Math.max(4, ...o.requestsPerMinute);
  const bw = (w - pad) / n;
  const y = (v) => h - 22 - (v / max) * (h - 40);
  const bars = o.requestsPerMinute.map((v, i) => {
    const e = o.errorsPerMinute[i];
    return `<rect class="bar" x="${pad + i * bw + 1}" y="${y(v)}" width="${Math.max(1, bw - 2)}" height="${h - 22 - y(v)}" rx="2"><title>${v} requests, ${o.syncsPerMinute[i]} syncs (${60 - i} min ago)</title></rect>` +
      (e ? `<rect class="bar err" x="${pad + i * bw + 1}" y="${y(e)}" width="${Math.max(1, bw - 2)}" height="${h - 22 - y(e)}" rx="2"/>` : '');
  }).join('');
  const grid = [0, .5, 1].map((f) => `<line class="grid-line" x1="${pad}" x2="${w}" y1="${y(max * f)}" y2="${y(max * f)}"/><text x="0" y="${y(max * f) + 4}">${Math.round(max * f)}</text>`).join('');
  return `<svg class="chart" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" role="img" aria-label="Requests per minute over the last hour">${grid}${bars}
    <text x="${pad}" y="${h - 4}">60 min ago</text><text x="${w - 40}" y="${h - 4}">now</text></svg>`;
}

async function overviewPage() {
  let o;
  try { o = state.overview = await api('/overview'); } catch { return; }
  const address = location.origin;
  const cluster = o.nodesOnline > 1;
  paint(`
    <div class="page-head"><div class="grow"><h1>${esc(o.serverName)}</h1><p>${esc(o.orgName)}${o.orgs > 1 ? ` and ${o.orgs - 1} more ${o.orgs === 2 ? 'organization' : 'organizations'}` : ''} · your GuardHawk Server at a glance.</p></div></div>
    <div class="grid">
      <section class="card hero ${o.running ? '' : 'stopped'}">
        <div class="grow">
          <div class="big"><span class="pulse"></span>${o.running ? 'Syncing' : 'Syncing is paused'}</div>
          <div class="sub">${o.running ? `Up ${uptime(o.uptimeSeconds)} · version ${esc(o.version)} · ${esc(o.runsAs)}${cluster ? ` · ${o.nodesOnline} servers` : ''}` : 'GuardHawk keeps working on every device with the data it has, and catches up when you start syncing again.'}</div>
        </div>
        ${o.running
          ? `<button class="btn" data-act="stop">${icon('pause')}Pause sync</button>`
          : `<button class="btn primary" data-act="start">${icon('play')}Start sync</button>`}
        <button class="btn" data-act="restart">${icon('arrow-clockwise')}Restart</button>
        <button class="btn" data-act="shutdown">${icon('power')}Shut down</button>
      </section>

      <div class="grid cols-4">
        ${stat('people', 'People', num(o.users), `${num(o.onlineUsers)} online now · ${num(o.activeThisWeek)} this week`)}
        ${stat('phone-laptop', 'Devices', num(o.devices), 'Signed-in PCs, tablets and phones')}
        ${stat('arrow-sync', 'Records', num(o.records), `${num(o.totalSyncs)} syncs on this server since it started`)}
        ${stat('storage', 'Storage', bytes(Math.max(0, o.databaseBytes) + o.fileBytes), `${num(o.files)} ${o.files === 1 ? 'file' : 'files'}${o.freeDiskBytes >= 0 ? ` · ${bytes(o.freeDiskBytes)} free` : ''}`)}
      </div>

      <div class="grid cols-3">
        <section class="card span-2">
          <div class="card-head"><h2>Requests, last hour${cluster ? ' (this server)' : ''}</h2><div class="legend"><span><i style="background:var(--accent)"></i>Requests</span><span><i style="background:var(--danger)"></i>Errors</span></div></div>
          ${chart(o)}
          ${cluster ? `<p class="muted small" style="margin:8px 0 0">${num(o.clusterRequestsPerMinute)} requests in the last minute across all ${o.nodesOnline} servers. <a href="#nodes" data-goto="nodes">See servers</a></p>` : ''}
        </section>
        <section class="card">
          <div class="card-head"><h2>Connect GuardHawk</h2></div>
          <p class="muted small" style="margin:0 0 12px">In GuardHawk, open <b>Settings → GuardHawk Server</b>, choose <b>Connect</b> and enter this address:</p>
          <div class="copy-box"><code>${esc(address)}</code><button class="btn sm" data-copy="${esc(address)}">${icon('copy', 'sm')}Copy</button></div>
          <p class="muted small" style="margin:12px 0 0">${o.openSignup ? 'Anyone who can reach this server can create an account.' : 'New people need an <a href="#invites" data-goto="invites">invite code</a>, or <a href="#users" data-goto="users">add them yourself</a>.'}
            On other computers, use this machine's network address or name instead of “localhost”.</p>
        </section>
      </div>

      <div class="grid cols-3">
        <section class="card span-2">
          <div class="card-head"><h2>Recent activity</h2><button class="btn sm ghost" data-goto="logs">See all</button></div>
          ${activityList(o.recentActivity)}
        </section>
        <section class="card">
          <div class="card-head"><h2>Server</h2></div>
          <dl class="kv">
            <dt>Runs as</dt><dd>${esc(o.runsAs)}</dd>
            <dt>Version</dt><dd>${esc(o.version)}</dd>
            <dt>Database</dt><dd>${esc(o.databaseVersion)}${o.databaseBytes >= 0 ? ` · ${bytes(o.databaseBytes)}` : ''}</dd>
            <dt>Files</dt><dd>${esc(o.storage)}</dd>
            <dt>Servers</dt><dd>${o.nodesOnline} online</dd>
            <dt>Port</dt><dd>${o.port}${o.httpsPort ? ` (HTTPS ${o.httpsPort})` : ''}</dd>
            <dt>Memory</dt><dd>${bytes(o.memoryBytes)}</dd>
            <dt>System</dt><dd>${esc(o.os)}</dd>
            <dt>Data folder</dt><dd class="mono">${esc(o.dataFolder)}</dd>
          </dl>
        </section>
      </div>
    </div>`, () => {
    const act = (name) => $app.querySelector(`[data-act="${name}"]`);
    act('stop')?.addEventListener('click', async () => {
      if (!await confirmBox({ title: 'Pause syncing?', message: 'GuardHawk keeps working on every device with the data it has. Devices catch up when you start syncing again. The web panel stays on.', confirm: 'Pause sync', iconName: 'pause' })) return;
      await api('/service/stop', { method: 'POST', body: {} }); toast('Syncing paused'); render();
    });
    act('start')?.addEventListener('click', async () => { await api('/service/start', { method: 'POST', body: {} }); toast('Syncing started'); render(); });
    act('restart').onclick = async () => {
      if (!await confirmBox({ title: 'Restart the server?', message: `It takes a few seconds. Syncs in progress pick up again on their own.${cluster ? ' Only this server restarts; the others keep running.' : ''}`, confirm: 'Restart', iconName: 'arrow-clockwise' })) return;
      await api('/service/restart', { method: 'POST', body: {} });
      toast('Restarting…');
      waitForRestart();
    };
    act('shutdown').onclick = () => shutdown(o);
  });
  state.timer = setInterval(async () => {
    if (state.page !== 'overview' || document.querySelector('.modal')) return;
    try { state.overview = await api('/overview'); overviewPage(); } catch {}
  }, 5000);
}

async function shutdown(o) {
  const how = {
    'Windows service': 'To start it again: open Services on this PC and start “GuardHawk Server”, or restart the PC.',
    'systemd service': 'To start it again: sudo systemctl start guardrail-server',
    Docker: 'To start it again: docker start guardrail-server (or docker compose up -d).',
    Kubernetes: 'Kubernetes starts a new copy automatically. To stop it for good, scale the deployment to 0.',
    Standalone: 'To start it again, run GuardHawk Server the same way you started it.',
  }[o.runsAs] || '';
  const cluster = o.nodesOnline > 1;
  if (!await confirmBox({
    title: cluster ? 'Shut down this server?' : 'Shut down GuardHawk Server?',
    message: `${cluster ? `Only this server (${o.nodeId}) stops. The other ${o.nodesOnline - 1} keep syncing.` : 'Syncing stops and this web panel goes offline. GuardHawk keeps working on every device and catches up later.'} ${how}`,
    confirm: 'Shut down', danger: true, iconName: 'power',
  })) return;
  await api('/service/shutdown', { method: 'POST', body: {} });
  clearInterval(state.timer);
  $app.innerHTML = `<div class="auth"><div class="card" style="text-align:center"><div class="badge-icon" style="margin:0 auto 14px">${icon('power', 'lg')}</div>
    <h1>GuardHawk Server is shutting down</h1><p class="lead">${esc(how)}</p>
    <button class="btn" onclick="location.reload()">${icon('arrow-clockwise')}Reload this page</button></div></div>`;
}

async function waitForRestart() {
  $app.querySelector('#page').insertAdjacentHTML('afterbegin', '<div class="note" style="margin-bottom:16px">' + icon('arrow-clockwise') + '<span>Restarting the server… this page reconnects by itself.</span></div>');
  await new Promise((r) => setTimeout(r, 2500));
  for (let i = 0; i < 40; i++) {
    try { const r = await fetch('/admin/api/status'); if (r.ok) { toast('Server restarted'); return start(); } } catch {}
    await new Promise((r) => setTimeout(r, 1500));
  }
  toast('The server hasn\'t come back. If it runs in a terminal, start it again there.', true);
}

const stat = (ic, label, value, foot) => `<section class="card stat"><div class="label">${icon(ic)}${label}</div><div class="value">${value}</div><div class="foot">${foot}</div></section>`;

function activityList(items) {
  if (!items.length) return `<div class="empty">${icon('history', 'lg')}<p>Nothing yet. Sign-ups, sign-ins and admin changes show up here.</p></div>`;
  return `<div class="list">${items.map((a) => `<div class="item">
    <span class="badge-icon">${icon(ACT_ICONS[a.kind] || 'info', 'sm')}</span>
    <div style="flex:1"><div>${esc(a.detail)}</div><div class="muted small">${a.username ? esc(a.username) + ' · ' : ''}${a.org && state.orgs.length > 1 ? esc(a.org) + ' · ' : ''}${esc(ago(a.at))}</div></div></div>`).join('')}</div>`;
}

// ----- People -----

async function usersPage() {
  let users;
  try { [users] = await Promise.all([api('/users'), loadOrgs()]); } catch { return; }
  const multi = state.orgs.length > 1;
  const q = (state.userQuery || '').toLowerCase();
  const shown = users.filter((u) => u.username.includes(q) || u.displayName.toLowerCase().includes(q) || (u.email || '').toLowerCase().includes(q));
  paint(`
    <div class="page-head"><div class="grow"><h1>People</h1><p>Everyone who signs in to GuardHawk with this server, their role and their devices.</p></div>
      <button class="btn" data-goto="invites">${icon('ticket-diagonal')}Invite</button>
      <button class="btn primary" data-add>${icon('add')}Add person</button></div>
    <section class="card">
      <div class="card-head"><div class="search" style="flex:1;max-width:340px">${icon('search')}<input class="input" id="q" placeholder="Search people" value="${esc(state.userQuery || '')}"></div>
        <span class="muted small">${users.length} ${users.length === 1 ? 'account' : 'accounts'}</span></div>
      ${users.length === 0
        ? `<div class="empty"><div class="badge-icon">${icon('people', 'lg')}</div><b>No one yet</b><p>Add people yourself, or create an invite code. People join from GuardHawk under Settings → GuardHawk Server.</p></div>`
        : `<div class="table-wrap"><table><thead><tr><th>Person</th>${multi ? '<th>Organization</th>' : ''}<th>Role</th><th>Status</th><th>Devices</th><th>Records edited</th><th>Last seen</th></tr></thead><tbody>
          ${shown.map((u) => `<tr class="clickable" data-user="${u.id}" tabindex="0">
            <td><div class="user-cell">${avatar(u.displayName || u.username)}<div><b>${esc(u.displayName)}</b><div class="muted small">${esc(u.username)}${u.email && u.email !== u.username ? ' · ' + esc(u.email) : ''}</div></div></div></td>
            ${multi ? `<td>${esc(u.org)}</td>` : ''}
            <td>${rolePill(u.role)}</td>
            <td>${u.disabled ? '<span class="pill off">Turned off</span>' : '<span class="pill">Active</span>'}</td>
            <td>${u.devices}</td><td>${num(u.recordsUpdated)}</td><td>${esc(ago(u.lastSeenAt))}</td></tr>`).join('')}
          </tbody></table></div>`}
    </section>`, () => {
    const qi = $app.querySelector('#q');
    if (qi) qi.oninput = () => { state.userQuery = qi.value; usersPage().then(() => { const n = $app.querySelector('#q'); n.focus(); n.setSelectionRange(n.value.length, n.value.length); }); };
    $app.querySelectorAll('[data-user]').forEach((r) => {
      r.onclick = () => userDrawer(+r.dataset.user);
      r.onkeydown = (e) => { if (e.key === 'Enter') userDrawer(+r.dataset.user); };
    });
    $app.querySelector('[data-add]').onclick = addPerson;
  });
}

function addPerson() {
  const multi = state.orgs.length > 1;
  drawer('Add person', `
    <div style="display:flex;align-items:center;gap:14px;margin-bottom:20px"><div class="badge-icon">${icon('person', 'lg')}</div>
      <div style="flex:1"><h2 style="font-size:24px">Add a person</h2><div class="muted small">They get a temporary password to sign in with.</div></div>
      <button class="btn icon-only ghost" data-close aria-label="Close">${icon('dismiss')}</button></div>
    <form id="np" class="card">
      <div class="field"><label for="nn">Name</label><input class="input" id="nn" placeholder="Jane Doe" required maxlength="80"></div>
      <div class="field"><label for="nu">Username</label><input class="input" id="nu" placeholder="jane.doe or jane@company.com" required autocomplete="off"><span class="hint">What they type to sign in. Letters, numbers and . _ - + @</span></div>
      <div class="field"><label for="ne">Email <span class="muted">(optional)</span></label><input class="input" id="ne" type="email" autocomplete="off"></div>
      ${multi ? `<div class="field"><label for="no">Organization</label><select class="input" id="no">${state.orgs.map((o) => `<option value="${o.id}">${esc(o.name)}</option>`).join('')}</select></div>` : ''}
      <div class="field"><label for="nr">Role</label><select class="input" id="nr">${roleOptions('member')}</select></div>
      <button class="btn primary" style="width:100%">${icon('add')}Add person</button>
    </form>`, (el, close) => {
    el.querySelector('#nn').focus();
    el.querySelector('#nn').oninput = (e) => {
      const u = el.querySelector('#nu');
      if (!u.dataset.touched) u.value = e.target.value.trim().toLowerCase().replace(/\s+/g, '.').replace(/[^a-z0-9._+@-]/g, '');
    };
    el.querySelector('#nu').oninput = (e) => { e.target.dataset.touched = '1'; };
    el.querySelector('#np').onsubmit = async (e) => {
      e.preventDefault();
      try {
        const r = await api('/users', { method: 'POST', body: {
          username: el.querySelector('#nu').value, displayName: el.querySelector('#nn').value, email: el.querySelector('#ne').value || null,
          role: el.querySelector('#nr').value, orgId: multi ? +el.querySelector('#no').value : null } });
        close();
        await secretBox(`${r.user.displayName} can sign in`, `Give them this temporary password with the username “${r.user.username}”. They can change it in GuardHawk under Settings → GuardHawk Server.`, r.password);
        usersPage();
      } catch (ex) { toast(ex.message, true); }
    };
  });
}

async function userDrawer(id) {
  const d = await api('/users/' + id);
  const u = d.user;
  drawer(u.displayName, `
    <div style="display:flex;align-items:center;gap:14px;margin-bottom:20px">${avatar(u.displayName || u.username, 48)}
      <div style="flex:1"><h2 style="font-size:24px">${esc(u.displayName)}</h2><div class="muted small">${esc(u.username)}${state.orgs.length > 1 ? ' · ' + esc(u.org) : ''} · joined ${esc(date(u.createdAt))} · last seen ${esc(ago(u.lastSeenAt))}</div></div>
      <button class="btn icon-only ghost" data-close aria-label="Close">${icon('dismiss')}</button></div>
    <div class="grid cols-3" style="margin-bottom:16px">
      ${stat('shield', 'Role', roleLabel(u.role), '')}${stat('arrow-sync', 'Records edited', num(u.recordsUpdated), '')}${stat('phone-laptop', 'Devices', u.devices, '')}</div>
    <section class="card" style="margin-bottom:16px"><div class="card-head"><h2>Profile and role</h2></div>
      <form id="pf">
        <div class="grid cols-2" style="gap:12px">
          <div class="field"><label for="dn">Name</label><input class="input" id="dn" value="${esc(u.displayName)}" maxlength="80"></div>
          <div class="field"><label for="em">Email</label><input class="input" id="em" type="email" value="${esc(u.email || '')}"></div></div>
        <div class="field"><label for="rl">Role</label><select class="input" id="rl">${roleOptions(u.role)}</select></div>
        <button class="btn">Save</button>
      </form>
    </section>
    <section class="card" style="margin-bottom:16px"><div class="card-head"><h2>Devices</h2></div>
      ${d.devices.length ? `<div class="list">${d.devices.map((v) => `<div class="item"><span class="badge-icon">${icon(platformIcon(v.platform), 'sm')}</span>
        <div style="flex:1"><b>${esc(v.name)}</b><div class="muted small">${esc(v.platform)} ${esc(v.appVersion)} · ${esc(ago(v.lastSeen))}${v.ip ? ' · ' + esc(v.ip) : ''}</div></div>
        <button class="btn sm" data-device="${esc(v.id)}">Sign out</button></div>`).join('')}</div>` : '<p class="muted">Not signed in anywhere.</p>'}
    </section>
    <section class="card" style="margin-bottom:16px"><div class="card-head"><h2>Manage</h2></div>
      <div style="display:flex;flex-wrap:wrap;gap:8px">
        <button class="btn" data-a="reset">${icon('key', 'sm')}Reset password</button>
        ${u.disabled ? `<button class="btn" data-a="enable">${icon('play', 'sm')}Turn on</button>` : `<button class="btn" data-a="disable">${icon('prohibited', 'sm')}Turn off</button>`}
        <button class="btn" data-a="signout">${icon('sign-out', 'sm')}Sign out everywhere</button>
        <button class="btn danger" data-a="delete">${icon('delete', 'sm')}Delete account</button>
      </div>
    </section>
    <section class="card"><div class="card-head"><h2>Activity</h2></div>${activityList(d.activity)}</section>`, (el, close) => {
    const refresh = () => { close(); userDrawer(id); usersPage(); };
    el.querySelector('#pf').onsubmit = async (e) => {
      e.preventDefault();
      try {
        await api(`/users/${id}/profile`, { method: 'PUT', body: { displayName: el.querySelector('#dn').value, email: el.querySelector('#em').value } });
        if (el.querySelector('#rl').value !== u.role) await api(`/users/${id}/role`, { method: 'PUT', body: { role: el.querySelector('#rl').value } });
        toast('Saved'); refresh();
      } catch (ex) { toast(ex.message, true); }
    };
    el.querySelectorAll('[data-device]').forEach((b) => b.onclick = async () => {
      await api('/devices/' + encodeURIComponent(b.dataset.device), { method: 'DELETE' });
      toast('Device signed out'); refresh();
    });
    const actions = {
      reset: { title: `Reset ${u.displayName}'s password?`, message: 'They get a new temporary password and are signed out on every device until they sign in with it.', confirm: 'Reset password', iconName: 'key' },
      disable: { title: `Turn off ${u.displayName}?`, message: 'They\'re signed out everywhere and can\'t sign in or sync until you turn the account back on. Records they made stay.', confirm: 'Turn off', danger: true, done: 'Account turned off' },
      enable: { title: `Turn on ${u.displayName}?`, message: 'They can sign in and sync again.', confirm: 'Turn on', done: 'Account turned on' },
      signout: { title: 'Sign out everywhere?', message: `Every device ${u.displayName} uses has to sign in again.`, confirm: 'Sign out', done: 'Signed out everywhere' },
      delete: { title: `Delete ${u.displayName}?`, message: 'Deletes the account. Records they made stay with the organization. This can\'t be undone.', confirm: 'Delete account', danger: true, done: 'Account deleted' },
    };
    el.querySelectorAll('[data-a]').forEach((b) => b.onclick = async () => {
      const name = b.dataset.a, a = actions[name];
      if (!await confirmBox(a)) return;
      try {
        if (name === 'reset') {
          const r = await api(`/users/${id}/reset-password`, { method: 'POST', body: {} });
          await secretBox('New temporary password', `Give this to ${u.displayName} (username “${u.username}”).`, r.password);
          return refresh();
        }
        await api(`/users/${id}${name === 'delete' ? '' : '/' + name}`, { method: name === 'delete' ? 'DELETE' : 'POST', body: name === 'delete' ? undefined : {} });
        toast(a.done);
        if (name === 'delete') { close(); usersPage(); } else refresh();
      } catch (e) { toast(e.message, true); }
    });
  });
}

// ----- Invites -----

async function invitesPage(fresh) {
  let invites, settings;
  try { [invites, settings] = await Promise.all([api('/invites'), api('/settings'), loadOrgs()]); } catch { return; }
  const active = invites.filter((i) => i.usesLeft > 0 && (!i.expiresAt || new Date(i.expiresAt) > new Date()));
  const multi = state.orgs.length > 1;
  paint(`
    <div class="page-head"><div class="grow"><h1>Invites</h1><p>Share a code so someone can create their own account. They enter it in GuardHawk when joining the server.</p></div></div>
    <div class="grid cols-3">
      <section class="card">
        <div class="card-head"><h2>New invite</h2></div>
        <form id="inv">
          <div class="field"><label for="ir">Role</label><select class="input" id="ir">${roleOptions('member')}</select></div>
          <div class="field"><label for="io">Organization</label><select class="input" id="io">
            ${state.orgs.map((o) => `<option value="${o.id}">${esc(o.name)}</option>`).join('')}<option value="new">New organization…</option></select>
            <span class="hint">${multi ? '' : 'Hosting GuardHawk for more than one company? Pick “New organization”: each one only sees its own data.'}</span></div>
          <div class="field" id="newOrgRow" hidden><label for="ion">New organization name</label><input class="input" id="ion" placeholder="Company name" autocomplete="off"><span class="hint">Created when the first person uses the code. Give it the Admin role.</span></div>
          <div class="field"><label for="iu">For username <span class="muted">(optional)</span></label><input class="input" id="iu" placeholder="Anyone" autocomplete="off"></div>
          <div class="grid cols-2" style="gap:12px"><div class="field"><label for="in">Uses</label><input class="input" id="in" type="number" min="1" max="10000" value="1"></div>
            <div class="field"><label for="id">Expires in</label><select class="input" id="id"><option value="1">1 day</option><option value="7">7 days</option><option value="14" selected>14 days</option><option value="30">30 days</option><option value="0">Never</option></select></div></div>
          <div class="field"><label for="ino">Note <span class="muted">(optional)</span></label><input class="input" id="ino" placeholder="Who it's for" autocomplete="off"></div>
          <button class="btn primary" style="width:100%">${icon('ticket-diagonal')}Create invite</button>
        </form>
        ${fresh ? `<div style="margin-top:18px"><div class="muted small" style="margin-bottom:6px">New invite code</div>
          <div class="copy-box"><code style="font-size:20px;letter-spacing:.06em">${esc(fresh.code)}</code><button class="btn sm" data-copy="${esc(fresh.code)}">${icon('copy', 'sm')}Copy</button></div></div>` : ''}
      </section>
      <section class="card span-2">
        <div class="card-head"><h2>Invites</h2><span class="muted small">${active.length} active</span></div>
        ${invites.length === 0 ? `<div class="empty"><div class="badge-icon">${icon('ticket-diagonal', 'lg')}</div><p>No invites yet.</p></div>` : `
        <div class="table-wrap"><table><thead><tr><th>Code</th><th>Role</th><th>${multi || invites.some((i) => i.newOrgName) ? 'Organization' : 'For'}</th><th>Uses left</th><th>Expires</th><th>Used by</th><th></th></tr></thead><tbody>
          ${invites.map((i) => { const dead = i.usesLeft <= 0 || (i.expiresAt && new Date(i.expiresAt) < new Date());
            return `<tr><td><span class="mono" style="font-size:14px;${dead ? 'text-decoration:line-through;opacity:.6' : ''}">${esc(i.code)}</span>${i.note ? `<div class="muted small">${esc(i.note)}</div>` : ''}</td>
            <td>${rolePill(i.role)}</td>
            <td>${i.newOrgName ? `New: ${esc(i.newOrgName)}` : multi ? esc(i.org || '') : ''}${i.username ? `<div class="muted small">for ${esc(i.username)}</div>` : (multi || i.newOrgName ? '' : '<span class="muted">Anyone</span>')}</td>
            <td>${dead ? '<span class="pill grey">Used up</span>' : i.usesLeft}</td>
            <td>${i.expiresAt ? esc(date(i.expiresAt)) : 'Never'}</td><td>${i.usedBy ? esc(i.usedBy) : '—'}</td>
            <td style="text-align:right;white-space:nowrap">${dead ? '' : `<button class="btn sm ghost" data-copy="${esc(i.code)}" aria-label="Copy">${icon('copy', 'sm')}</button>`}
              <button class="btn sm ghost" data-del="${esc(i.code)}" aria-label="Delete">${icon('delete', 'sm')}</button></td></tr>`; }).join('')}
        </tbody></table></div>`}
      </section>
    </div>
    <section class="card" style="margin-top:16px">
      <label class="switch"><input type="checkbox" id="open" ${settings.openSignup ? 'checked' : ''}><span class="track"></span>
        <span class="text"><b>Open sign-up</b><span>Let anyone who can reach this server create an account without an invite. They join ${esc(state.orgs[0]?.name || 'the organization')} as ${esc(roleLabel(settings.signupRole))}.</span></span></label>
    </section>`, () => {
    const orgSel = $app.querySelector('#io');
    orgSel.onchange = () => {
      $app.querySelector('#newOrgRow').hidden = orgSel.value !== 'new';
      if (orgSel.value === 'new') $app.querySelector('#ir').value = 'admin';
    };
    $app.querySelectorAll('[data-del]').forEach((b) => b.onclick = async () => {
      if (!await confirmBox({ title: 'Delete this invite?', message: `${b.dataset.del} stops working. Accounts already created with it stay.`, confirm: 'Delete', danger: true })) return;
      await api('/invites/' + encodeURIComponent(b.dataset.del), { method: 'DELETE' }); toast('Invite deleted'); invitesPage();
    });
    $app.querySelector('#inv').onsubmit = async (e) => {
      e.preventDefault();
      const isNew = orgSel.value === 'new';
      try {
        const inv = await api('/invites', { method: 'POST', body: {
          role: $app.querySelector('#ir').value, orgId: isNew ? null : +orgSel.value, newOrgName: isNew ? ($app.querySelector('#ion').value || 'New organization') : null,
          username: $app.querySelector('#iu').value || null, note: $app.querySelector('#ino').value || null,
          uses: +$app.querySelector('#in').value || 1, days: +$app.querySelector('#id').value || null } });
        toast('Invite created'); invitesPage(inv);
      } catch (ex) { toast(ex.message, true); }
    };
    $app.querySelector('#open').onchange = async (e) => {
      await api('/settings', { method: 'PUT', body: { openSignup: e.target.checked } });
      toast(e.target.checked ? 'Open sign-up is on' : 'Invite-only');
    };
  });
}

// ----- Data (read-only) -----

function prettyJson(value) {
  const json = esc(JSON.stringify(value, null, 2));
  return json.replace(/(&quot;(?:[^&]|&(?!quot;))*?&quot;)(\s*:)?|\b(true|false|null)\b|-?\b\d+(?:\.\d+)?\b/g, (m, str, colon, lit) => {
    if (str) return colon ? `<span class="k">${str}</span>${colon}` : `<span class="s">${str}</span>`;
    if (lit) return `<span class="b">${lit}</span>`;
    return `<span class="n">${m}</span>`;
  });
}

async function dataPage() {
  let collections;
  try {
    await loadOrgs();
    if (!state.dataOrg || !state.orgs.some((o) => o.id === state.dataOrg)) state.dataOrg = state.orgs[0]?.id;
    collections = await api('/data/collections?org=' + state.dataOrg);
  } catch { return; }
  if (!state.dataCollection || !collections.some((c) => c.collection === state.dataCollection)) state.dataCollection = collections[0]?.collection || null;
  let rows = [], total = 0;
  if (state.dataCollection) {
    try {
      const r = await api(`/data/records?org=${state.dataOrg}&collection=${encodeURIComponent(state.dataCollection)}&offset=${state.dataOffset}${state.dataQuery ? '&q=' + encodeURIComponent(state.dataQuery) : ''}`);
      rows = r.rows; total = r.total;
    } catch { return; }
  }
  const multi = state.orgs.length > 1;
  paint(`
    <div class="page-head"><div class="grow"><h1>Data</h1><p>What's been synced, by type. Read-only: records are added and changed in the GuardHawk app.</p></div>
      ${multi ? `<select class="input" id="org" style="width:auto">${state.orgs.map((o) => `<option value="${o.id}" ${o.id === state.dataOrg ? 'selected' : ''}>${esc(o.name)}</option>`).join('')}</select>` : ''}
      <a class="btn" href="/admin/api/data/export?org=${state.dataOrg}" download>${icon('arrow-download')}Export all</a></div>
    ${collections.length === 0 ? `<section class="card"><div class="empty"><div class="badge-icon">${icon('table', 'lg')}</div><b>Nothing synced yet</b><p>Records show up here once someone connects GuardHawk to this server.</p></div></section>` : `
    <div class="chips" style="margin-bottom:16px">${collections.map((c) => `<button class="chip ${c.collection === state.dataCollection ? 'on' : ''}" data-col="${esc(c.collection)}">${esc(collName(c.collection))}<span class="n">${num(c.records)}</span></button>`).join('')}</div>
    <section class="card">
      <div class="card-head"><h2>${esc(collName(state.dataCollection))}</h2>
        <div class="search" style="flex:1;max-width:320px;margin-left:auto">${icon('search')}<input class="input" id="dq" placeholder="Search in ${esc(collName(state.dataCollection).toLowerCase())}" value="${esc(state.dataQuery)}"></div></div>
      ${rows.length === 0 ? '<div class="empty"><p>No records match.</p></div>' : `
      <div class="table-wrap"><table><thead><tr><th>Number</th><th>Title</th><th>Last changed by</th><th>Changed</th></tr></thead><tbody>
        ${rows.map((r) => `<tr class="clickable" data-rec="${esc(r.id)}" tabindex="0"><td class="mono">${esc(recNumber(state.dataCollection, r.number))}</td>
          <td>${r.title ? esc(r.title) : '<span class="muted">(untitled)</span>'}</td><td>${esc(r.updatedBy || '—')}</td><td>${esc(ago(r.updatedAt))}</td></tr>`).join('')}
      </tbody></table></div>
      <div class="pager"><span class="muted small">${num(state.dataOffset + 1)}–${num(state.dataOffset + rows.length)} of ${num(total)}</span>
        <button class="btn sm" data-page-prev ${state.dataOffset === 0 ? 'disabled' : ''}>Previous</button>
        <button class="btn sm" data-page-next ${state.dataOffset + rows.length >= total ? 'disabled' : ''}>Next</button></div>`}
    </section>`}`, () => {
    $app.querySelector('#org')?.addEventListener('change', (e) => { state.dataOrg = +e.target.value; state.dataCollection = null; state.dataOffset = 0; dataPage(); });
    $app.querySelectorAll('[data-col]').forEach((b) => b.onclick = () => { state.dataCollection = b.dataset.col; state.dataOffset = 0; state.dataQuery = ''; dataPage(); });
    const dq = $app.querySelector('#dq');
    let t;
    if (dq) dq.oninput = () => { clearTimeout(t); t = setTimeout(() => { state.dataQuery = dq.value; state.dataOffset = 0; dataPage().then(() => { const n = $app.querySelector('#dq'); n?.focus(); n?.setSelectionRange(n.value.length, n.value.length); }); }, 300); };
    $app.querySelector('[data-page-prev]')?.addEventListener('click', () => { state.dataOffset = Math.max(0, state.dataOffset - 50); dataPage(); });
    $app.querySelector('[data-page-next]')?.addEventListener('click', () => { state.dataOffset += 50; dataPage(); });
    $app.querySelectorAll('[data-rec]').forEach((r) => { r.onclick = () => recordDrawer(r.dataset.rec); r.onkeydown = (e) => { if (e.key === 'Enter') recordDrawer(r.dataset.rec); }; });
  });
}

async function recordDrawer(id) {
  const r = await api(`/data/record?org=${state.dataOrg}&collection=${encodeURIComponent(state.dataCollection)}&id=${encodeURIComponent(id)}`);
  const title = (r.data && (r.data.Title || r.data.Name || r.data.FullName || r.data.Description)) || recNumber(r.collection, r.number);
  drawer(title, `
    <div style="display:flex;align-items:center;gap:14px;margin-bottom:20px"><div class="badge-icon">${icon('document-text', 'lg')}</div>
      <div style="flex:1"><h2 style="font-size:22px">${esc(title)}</h2><div class="muted small">${esc(collName(r.collection))} · ${esc(recNumber(r.collection, r.number))} · changed ${esc(ago(r.updatedAt))}${r.updatedBy ? ' by ' + esc(r.updatedBy) : ''}</div></div>
      <button class="btn icon-only ghost" data-close aria-label="Close">${icon('dismiss')}</button></div>
    <pre class="json mono">${prettyJson(r.data)}</pre>
    <p class="muted small">Record id ${esc(r.id)} · sync sequence ${num(r.seq)}</p>`);
}

// ----- Servers (cluster nodes) -----

async function nodesPage() {
  let nodes, o;
  try { [nodes, o] = await Promise.all([api('/nodes'), api('/overview')]); state.overview = o; } catch { return; }
  const online = nodes.filter((n) => n.online);
  paint(`
    <div class="page-head"><div class="grow"><h1>Servers</h1><p>Every copy of GuardHawk Server sharing this database. Add more behind a load balancer to handle more people.</p></div></div>
    <div class="grid cols-4" style="margin-bottom:16px">
      ${stat('server-multiple', 'Online', num(online.length), `${nodes.length - online.length} offline`)}
      ${stat('data-bar', 'Requests, last minute', num(online.reduce((a, n) => a + n.requestsLastMinute, 0)), 'All servers together')}
      ${stat('database', 'Database', esc(o.database), esc(o.databaseVersion))}
      ${stat('storage', 'Files', esc(o.storage.split(' (')[0]), esc(o.storageWhere))}
    </div>
    <section class="card">
      <div class="table-wrap"><table><thead><tr><th>Server</th><th>Status</th><th>Runs as</th><th>Version</th><th>Requests</th><th>Last minute</th><th>Memory</th><th>Started</th><th>Seen</th></tr></thead><tbody>
        ${nodes.map((n) => `<tr><td><div class="user-cell"><span class="node-dot ${n.stopping ? 'stopping' : n.online ? '' : 'off'}"></span><div><b>${esc(n.name)}</b>${n.this ? ' <span class="pill">This one</span>' : ''}<div class="muted small mono">${esc(n.id)}</div></div></div></td>
          <td>${n.stopping ? '<span class="pill grey">Stopping</span>' : n.online ? '<span class="pill">Online</span>' : '<span class="pill off">Offline</span>'}</td>
          <td>${esc(n.runsAs)}</td><td>${esc(n.version)}</td><td>${num(n.requests)}${n.errors ? ` <span class="muted small">(${num(n.errors)} errors)</span>` : ''}</td>
          <td>${num(n.requestsLastMinute)}</td><td>${bytes(n.memoryBytes)}</td><td>${esc(dateTime(n.startedAt))}</td><td>${esc(ago(n.lastSeen))}</td></tr>`).join('')}
      </tbody></table></div>
    </section>
    <section class="card" style="margin-top:16px">
      <div class="card-head"><h2>Scaling up</h2></div>
      ${o.database === 'SQLite' ? `<div class="note" style="margin-bottom:14px">${icon('info')}<span>This server uses <b>SQLite</b>, which is right for one server. To run more than one, switch to <b>PostgreSQL</b> (<span class="mono">GR_DB=postgres</span> and <span class="mono">GR_DB_CONNECTION</span>), and keep files somewhere every server can reach: S3-compatible storage (<span class="mono">GR_STORAGE=s3</span>) or a shared folder (<span class="mono">GR_FILES</span>).</span></div>` : ''}
      <ol class="steps muted">
        <li>Point every server at the same PostgreSQL database and the same file storage.</li>
        <li>Put them behind a load balancer (Caddy, nginx, Traefik, a cloud load balancer or a Kubernetes Service) and set <span class="mono">GR_TRUST_PROXY=true</span>.</li>
        <li>Have the load balancer check <span class="mono">/readyz</span>. Each server stops answering it while it shuts down, so no requests are lost.</li>
        <li>Collect <span class="mono">/metrics</span> with Prometheus if you want graphs and alerts.</li>
      </ol>
      <p class="muted small" style="margin-bottom:0">The repository has a ready-made <span class="mono">docker-compose.scale.yml</span> (three servers, PostgreSQL, S3 storage and a load balancer) and Kubernetes files with autoscaling.</p>
    </section>`);
  state.timer = setInterval(() => { if (state.page === 'nodes' && !document.querySelector('.modal')) nodesPage(); }, 10000);
}

// ----- Settings -----

const UNINSTALL = {
  'Windows service': `<p>Open <b>Settings → Apps → Installed apps</b> on this PC, find <b>GuardHawk Server</b> and choose <b>Uninstall</b> (or use the Bytehawk Studio Launcher). If you used the zip, run <span class="mono">Uninstall-Service.ps1</span> from its folder.</p><p class="muted small">Your data stays in C:\\ProgramData\\Guardrail Server unless you delete that folder.</p>`,
  'systemd service': `<p>In the folder you installed from, run:</p><div class="copy-box"><code>sudo ./uninstall.sh</code></div><p class="muted small">Add <span class="mono">--purge</span> to delete the data in /var/lib/guardrail-server too.</p>`,
  Docker: `<p>Stop and remove the container:</p><div class="copy-box"><code>docker compose down</code></div><p class="muted small">Add <span class="mono">-v</span> to delete the data volume too, or <span class="mono">docker rm -f guardrail-server</span> if you used docker run.</p>`,
  Kubernetes: `<p>Delete the resources you applied:</p><div class="copy-box"><code>kubectl delete -k deploy/kubernetes</code></div><p class="muted small">The database and storage you provided aren't touched.</p>`,
  Standalone: `<p>Shut the server down (Overview → Shut down), then delete its folder. Your data is in the <span class="mono">data</span> folder next to the program.</p>`,
};

async function settingsPage() {
  let s, o, admins;
  try { [s, o, admins] = await Promise.all([api('/settings'), api('/overview'), api('/admins'), loadOrgs()]); state.overview = o; } catch { return; }
  const org = state.orgs[0];
  paint(`
    <div class="page-head"><div class="grow"><h1>Settings</h1><p>How this server works for the people who use it.</p></div></div>
    <div class="grid cols-2">
      <section class="card"><div class="card-head"><h2>Server</h2></div>
        <form id="sf">
          <div class="field"><label for="sn">Server name</label><input class="input" id="sn" maxlength="60" value="${esc(s.serverName)}"><span class="hint">Shown in GuardHawk when people connect.</span></div>
          <div class="field"><label for="mu">Account limit</label><input class="input" id="mu" type="number" min="0" value="${s.maxUsers}"><span class="hint">0 for no limit.</span></div>
          <label class="switch" style="margin-bottom:14px"><input type="checkbox" id="os" ${s.openSignup ? 'checked' : ''}><span class="track"></span>
            <span class="text"><b>Open sign-up</b><span>Off: people need an invite code, or you add them.</span></span></label>
          <div class="field"><label for="sr">Role for people who sign up without an invite</label><select class="input" id="sr">${roleOptions(s.signupRole)}</select></div>
          <button class="btn primary">Save</button>
        </form>
      </section>
      <section class="card"><div class="card-head"><h2>Organizations</h2><span class="muted small">${state.orgs.length}</span></div>
        <div class="list">${state.orgs.map((x) => `<div class="item"><span class="badge-icon">${icon('building', 'sm')}</span>
          <div style="flex:1"><b>${esc(x.name)}</b><div class="muted small">${num(x.users)} ${x.users === 1 ? 'person' : 'people'} · ${num(x.records)} records · ${bytes(x.fileBytes)} of files</div></div>
          <button class="btn sm" data-rename="${x.id}" data-name="${esc(x.name)}">Rename</button>
          <button class="btn sm danger" data-erase="${x.id}" data-name="${esc(x.name)}">Erase data</button>
          ${state.orgs.length > 1 ? `<button class="btn sm danger" data-delorg="${x.id}" data-name="${esc(x.name)}" aria-label="Delete">${icon('delete', 'sm')}</button>` : ''}</div>`).join('')}</div>
        <p class="muted small" style="margin-bottom:0">Each organization only sees its own records. Add another with an invite for a “New organization”.</p>
      </section>
      <section class="card"><div class="card-head"><h2>Web panel admins</h2></div>
        <div class="list" style="margin-bottom:14px">${admins.map((a) => `<div class="item"><span class="badge-icon">${icon('person-key', 'sm')}</span><div style="flex:1"><b>${esc(a)}</b>${a === state.me ? ' <span class="muted small">(you)</span>' : ''}</div>
          ${a === state.me ? '' : `<button class="btn sm ghost" data-deladmin="${esc(a)}" aria-label="Remove">${icon('delete', 'sm')}</button>`}</div>`).join('')}</div>
        <form id="af" class="grid cols-2" style="gap:10px;align-items:end">
          <div class="field" style="margin:0"><label for="an">New admin</label><input class="input" id="an" placeholder="Name" autocomplete="off" required minlength="3"></div>
          <div class="field" style="margin:0"><label for="ap">Password</label><input class="input" id="ap" type="password" autocomplete="new-password" placeholder="10+ characters" required minlength="10"></div>
          <button class="btn" style="grid-column:span 2">${icon('add', 'sm')}Add admin</button>
        </form>
        <hr style="border:0;border-top:1px solid var(--border);margin:18px 0">
        <form id="pf">
          <div class="grid cols-2" style="gap:10px">
          <div class="field"><label for="cp">Your current password</label><input class="input" id="cp" type="password" autocomplete="current-password" required></div>
          <div class="field"><label for="np">New password</label><input class="input" id="np" type="password" autocomplete="new-password" minlength="10" required></div></div>
          <button class="btn">Change my password</button>
        </form>
      </section>
      <section class="card"><div class="card-head"><h2>Backup</h2></div>
        ${o.database === 'SQLite'
          ? `<p class="muted" style="margin-top:0">Download a copy of the database: accounts, invites and every record. Files are kept in <span class="mono">${esc(o.storageWhere)}</span>.</p>
             <a class="btn" href="/admin/api/backup" download>${icon('arrow-download')}Download database backup</a>`
          : `<p class="muted" style="margin-top:0">This server uses PostgreSQL. Back it up with your database's own tools, for example:</p>
             <div class="copy-box" style="margin-bottom:12px"><code>pg_dump -Fc guardrail &gt; guardrail.dump</code></div>`}
        <p class="muted small">Or export every record as JSON (one line per record) for reporting or moving elsewhere:</p>
        <a class="btn" href="/admin/api/data/export?org=${org?.id || ''}" download>${icon('arrow-download')}Export all records</a>
      </section>
      <section class="card"><div class="card-head"><h2>About</h2></div>
        <dl class="kv"><dt>Version</dt><dd>GuardHawk Server ${esc(o.version)}</dd><dt>Runs as</dt><dd>${esc(o.runsAs)}</dd><dt>Database</dt><dd>${esc(o.databaseVersion)}</dd>
          <dt>Files</dt><dd>${esc(o.storage)}</dd><dt>Runtime</dt><dd>${esc(o.runtime)}</dd><dt>Made by</dt><dd>Bytehawk Studio</dd></dl>
        <p class="muted small" style="margin-bottom:0">Data is stored on this server so roles, shared records and this panel work. Use HTTPS (or a VPN) when people connect from outside your network.</p>
      </section>
      <section class="card"><div class="card-head"><h2>Uninstall</h2></div>
        ${UNINSTALL[o.runsAs] || UNINSTALL.Standalone}
      </section>
    </div>`, () => {
    $app.querySelector('#sf').onsubmit = async (e) => {
      e.preventDefault();
      try {
        await api('/settings', { method: 'PUT', body: { serverName: $app.querySelector('#sn').value, maxUsers: +$app.querySelector('#mu').value || 0, openSignup: $app.querySelector('#os').checked, signupRole: $app.querySelector('#sr').value } });
        toast('Settings saved');
      } catch (ex) { toast(ex.message, true); }
    };
    $app.querySelector('#pf').onsubmit = async (e) => {
      e.preventDefault();
      try {
        await api('/password', { method: 'POST', body: { currentPassword: $app.querySelector('#cp').value, newPassword: $app.querySelector('#np').value } });
        toast('Password changed'); e.target.reset();
      } catch (ex) { toast(ex.message, true); }
    };
    $app.querySelector('#af').onsubmit = async (e) => {
      e.preventDefault();
      try {
        await api('/admins', { method: 'POST', body: { username: $app.querySelector('#an').value, password: $app.querySelector('#ap').value } });
        toast('Admin added'); settingsPage();
      } catch (ex) { toast(ex.message, true); }
    };
    $app.querySelectorAll('[data-deladmin]').forEach((b) => b.onclick = async () => {
      if (!await confirmBox({ title: `Remove ${b.dataset.deladmin}?`, message: 'They can no longer sign in to this web panel.', confirm: 'Remove', danger: true })) return;
      try { await api('/admins/' + encodeURIComponent(b.dataset.deladmin), { method: 'DELETE' }); toast('Admin removed'); settingsPage(); } catch (ex) { toast(ex.message, true); }
    });
    $app.querySelectorAll('[data-rename]').forEach((b) => b.onclick = async () => {
      const name = await promptBox({ title: 'Rename organization', label: 'Organization name', value: b.dataset.name, confirm: 'Rename', iconName: 'building' });
      if (!name || name === b.dataset.name) return;
      try { await api('/orgs/' + b.dataset.rename, { method: 'PUT', body: { name } }); toast('Renamed'); settingsPage(); } catch (ex) { toast(ex.message, true); }
    });
    $app.querySelectorAll('[data-erase]').forEach((b) => b.onclick = async () => {
      if (!await confirmBox({ title: `Erase ${b.dataset.name}'s synced data?`, message: 'Deletes every synced record and file for this organization from the server. Accounts stay. Devices keep their own copies, and the next one to sync uploads its data again.', confirm: 'Erase data', danger: true })) return;
      try { await api(`/orgs/${b.dataset.erase}/erase`, { method: 'POST', body: {} }); toast('Synced data erased'); settingsPage(); } catch (ex) { toast(ex.message, true); }
    });
    $app.querySelectorAll('[data-delorg]').forEach((b) => b.onclick = async () => {
      if (!await confirmBox({ title: `Delete ${b.dataset.name}?`, message: 'Deletes the organization, its accounts, records and files from this server. This can\'t be undone.', confirm: 'Delete organization', danger: true })) return;
      try { await api('/orgs/' + b.dataset.delorg, { method: 'DELETE' }); toast('Organization deleted'); settingsPage(); } catch (ex) { toast(ex.message, true); }
    });
  });
}

// ----- Logs -----

async function logsPage() {
  const tab = state.logTab || 'activity';
  let rows;
  try { rows = await api(tab === 'activity' ? '/activity' : '/logs'); } catch { return; }
  const level = state.logLevel || 'all';
  const shown = tab === 'server' && level !== 'all' ? rows.filter((r) => level === 'problems' ? r.level !== 'Information' : true) : rows;
  paint(`
    <div class="page-head"><div class="grow"><h1>Logs</h1><p>What's been happening on the server.</p></div>
      <div class="tabs" role="tablist"><button class="${tab === 'activity' ? 'on' : ''}" data-tab="activity">Activity</button><button class="${tab === 'server' ? 'on' : ''}" data-tab="server">Server log</button></div></div>
    <section class="card" style="padding-bottom:0">
      ${tab === 'activity' ? activityList(rows) : `
        <div class="card-head"><div class="tabs"><button class="${level === 'all' ? 'on' : ''}" data-lvl="all">All</button><button class="${level === 'problems' ? 'on' : ''}" data-lvl="problems">Warnings and errors</button></div><span class="muted small">This server · newest first · refreshes every 5 seconds</span></div>
        <div class="log" style="margin:0 -22px">${shown.length ? shown.map((l) => `<div class="line"><span class="muted">${esc(dateTime(l.at))}</span><span class="lvl-${esc(l.level)}">${esc(l.level)}</span><span class="muted">${esc(l.category)}</span><span>${esc(l.message)}</span></div>`).join('') : '<div class="empty">Nothing logged yet.</div>'}</div>`}
      <div style="height:20px"></div>
    </section>`, () => {
    $app.querySelectorAll('[data-tab]').forEach((b) => b.onclick = () => { state.logTab = b.dataset.tab; logsPage(); });
    $app.querySelectorAll('[data-lvl]').forEach((b) => b.onclick = () => { state.logLevel = b.dataset.lvl; logsPage(); });
  });
  state.timer = setInterval(() => { if (state.page === 'logs') logsPage(); }, 5000);
}

// ----- Start -----

async function start() {
  try {
    const s = await (await fetch('/admin/api/status')).json();
    state.version = s.version;
    if (s.needsSetup) return authPage(true);
    if (!s.signedIn) return showSignIn();
    state.me = s.signedIn;
    await loadOrgs();
    const hash = location.hash.slice(1);
    if (PAGES.some(([id]) => id === hash)) state.page = hash;
    render();
  } catch {
    $app.innerHTML = '<div class="auth"><div class="card"><h1>Can\'t reach the server</h1><p class="lead">Check that GuardHawk Server is running, then reload this page.</p></div></div>';
  }
}

window.addEventListener('hashchange', () => { const h = location.hash.slice(1); if (h && h !== state.page && PAGES.some(([id]) => id === h)) go(h); });
start();
