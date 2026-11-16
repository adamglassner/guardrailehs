// Demo data for screenshots (CI):  node seed.mjs <url> <admin> <password>
const [url, admin, password] = process.argv.slice(2);
let cookie = '';
async function panel(path, body, method = 'POST') {
  const r = await fetch(url + '/admin/api' + path, { method, headers: { 'Content-Type': 'application/json', 'X-Guardrail': 'admin', Cookie: cookie }, body: body ? JSON.stringify(body) : undefined });
  const set = r.headers.get('set-cookie'); if (set) cookie = set.split(';')[0];
  if (!r.ok) throw new Error(path + ' ' + r.status + ' ' + await r.text());
  return r.json();
}
async function app(path, body, token) {
  const r = await fetch(url + '/api/v1' + path, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: token ? 'Bearer ' + token : '' }, body: JSON.stringify(body) });
  if (!r.ok) throw new Error(path + ' ' + r.status + ' ' + await r.text());
  return r.json();
}
await panel('/login', { username: admin, password });
await panel('/settings', { serverName: 'Acme Safety Server' }, 'PUT');
await panel('/orgs/1', { name: 'Acme Logistics' }, 'PUT');
const people = [
  ['dana.reyes', 'Dana Reyes', 'admin', 'Windows'], ['marcus.lee', 'Marcus Lee', 'manager', 'Windows'], ['priya.shah', 'Priya Shah', 'member', 'Windows'],
  ['tom.okafor', 'Tom Okafor', 'member', 'Android'], ['site.kiosk', 'Warehouse kiosk', 'viewer', 'Windows'],
];
const tokens = [];
for (const [u, name, role, platform] of people) {
  const inv = await panel('/invites', { role, uses: 1, days: 7 });
  const s = await app('/auth/register', { username: u, password: 'demo-password-1', displayName: name, invite: inv.code, deviceName: name.split(' ')[0] + "'s PC", platform, appVersion: '0.13.0' });
  tokens.push(s.token);
}
const now = Date.now();
const uuid = () => crypto.randomUUID();
const incidents = ['Slip on wet floor, dock 3', 'Forklift near miss in aisle 12', 'Hand laceration from box cutter', 'Chemical splash, battery room', 'Back strain lifting tote', 'Vehicle backing incident, yard'];
const actions = ['Install anti-slip mats at dock 3', 'Add convex mirror at aisle 12', 'Switch to safety box cutters', 'Replace eyewash station', 'Refresher training: lifting'];
const vehicles = ['Truck 104', 'Truck 112', 'Van 21', 'Truck 130'];
const push = (token, collection, titles, field = 'Title') => app('/sync/push', { changes: titles.map((t, i) => {
  const id = uuid();
  return { collection, id, modified: now - i * 3600_000, deleted: false, payload: JSON.stringify({ Id: id, Seq: i + 1, [field]: t, Status: i % 3 ? 'Open' : 'Closed', CreatedAt: new Date(now - i * 86400_000).toISOString() }) };
}) }, token);
await push(tokens[0], 'incidents', incidents);
await push(tokens[1], 'actions', actions);
await push(tokens[2], 'fleet-vehicles', vehicles, 'UnitNumber');
await push(tokens[3], 'observations', ['Blocked fire exit, east wall', 'Good catch: spill cleaned up', 'Missing guard on conveyor']);
await push(tokens[1], 'training', ['Forklift certification', 'Hazard communication', 'First aid / CPR'], 'Course');
await fetch(url + '/api/v1/files/library/demo/Safety-Policy.pdf', { method: 'PUT', headers: { Authorization: 'Bearer ' + tokens[0] }, body: new Uint8Array(250_000) });
await panel('/invites', { role: 'member', uses: 10, days: 30, note: 'Night shift' });
await panel('/invites', { role: 'admin', newOrgName: 'Northwind Freight', uses: 1, days: 14, note: 'Second company' });
for (let i = 0; i < 40; i++) await fetch(url + '/api/v1/sync/changes?since=0', { headers: { Authorization: 'Bearer ' + tokens[i % tokens.length] } });
console.log('Seeded', people.length, 'people');
