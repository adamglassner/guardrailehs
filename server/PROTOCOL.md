# Guardrail Server API (protocol 1)

The Guardrail apps talk to `/api/v1` with JSON (camelCase). Errors come back as `{ "error": "code", "message": "…" }`
with a matching HTTP status; `message` is written to be shown to people as is. Signed-in calls send
`Authorization: Bearer <token>`.

## Accounts

| Call | Body | Answer |
| --- | --- | --- |
| `GET /api/v1/info` | | `{ product, name, version, protocol, openSignup, running }` |
| `POST /api/v1/auth/register` | `{ username, password, displayName?, email?, invite?, deviceName?, platform?, appVersion? }` | session |
| `POST /api/v1/auth/login` | `{ username, password, deviceName?, platform?, appVersion? }` | session |
| `POST /api/v1/auth/logout` | `{}` | |
| `POST /api/v1/auth/password` | `{ currentPassword, newPassword }` | signs out the account's other devices |
| `GET /api/v1/account` | | `{ user, org, devices[] }` |
| `DELETE /api/v1/account/devices/{id}` | | |
| `GET /api/v1/directory` | | people in the same organization `[{ username, displayName, role }]` |

A session is `{ token, deviceId, user: { id, username, displayName, email, role }, org: { id, name } }`.
Usernames are case-insensitive (3–64 characters; an email address works). Roles: `admin`, `manager`, `member`,
`viewer` (read-only).

## Sync

Records belong to an **organization**; everyone in it shares them. Each record is the app's own JSON for that
record (`payload`), stored as is, in a **collection** named after the app's data file (`incidents`,
`fleet-vehicles`, …) with the record's id.

- `POST /api/v1/sync/push` `{ changes: [{ collection, id, modified, deleted, payload }] }` (up to 500).
  `modified` is the edit time in Unix milliseconds; when the server already has a newer edit, the result is
  `stale` and the app should keep the server's copy (it arrives on the next pull). Viewers get `forbidden`.
  Answer: `{ results: [{ collection, id, status, seq, number }], latest }`.
- `GET /api/v1/sync/changes?since=<seq>` → `{ changes: [{ collection, id, seq, modified, deleted, payload, number, updatedBy }], latest, more }`
  (up to 1,000; call again with the last `seq` while `more` is true). Deleted records come back with
  `deleted: true` and no payload.

Every accepted change gets the organization's next sequence number, so "changes after N" never misses anything.
**Record numbers** (INC-0042) are assigned by the server, per organization and collection, when a record is first
pushed; the server writes the number into the payload's `Seq` field and returns it as `number`.

## Files

`PUT /api/v1/files/{name}` (raw bytes, up to 1 GB), `GET` / `HEAD` / `DELETE` the same path. Names are paths such as
`library/<record id>/<file name>`, shared within the organization.

## Health and metrics

`GET /healthz` (process is up), `GET /readyz` (database and file storage answer, not shutting down),
`GET /metrics` (Prometheus text format; needs `Authorization: Bearer <GR_METRICS_TOKEN>` when that's set).
