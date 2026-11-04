// Keeps each person's collection saved online when the app is opened from keyissue.app, so every
// phone they sign in on shows the same comics and nothing is lost with a phone.
//
// The server (keyissue-apps, apps/comics) keeps one copy of each comic per person. A sync fetches
// what other phones changed since last time, then sends what changed here. When the same comic was
// changed on two phones, the later change wins. What this phone has saved online is remembered as
// { seq, items: { id: [fingerprint, updatedAt, deleted] } }, so only real changes go up.

// About this phone and the computer it sends to, not the comic itself: never sent online.
const LOCAL = ['changes', 'boxChanged', 'pendingSync', 'deletedAt'];
const ISO = /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?Z$/;
const CHUNK = 200;
const MAX_DATA = 19000;

/** The comic as saved online: everything but this phone's own notes. */
export function shared(c) {
  const out = {};
  for (const k of Object.keys(c).sort()) if (!LOCAL.includes(k) && c[k] !== undefined) out[k] = c[k];
  return out;
}

/** A short fingerprint of what's saved online, to tell whether a comic changed. */
export function fingerprint(c) {
  const s = JSON.stringify(shared(c));
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(36) + '.' + s.length.toString(36);
}

const after = (t) => new Date(Date.parse(t) + 1).toISOString();
const latest = (...ts) => ts.filter(Boolean).sort().pop();

/**
 * One round: fetch other phones' changes, then send this phone's.
 * d: { allComics, binComics, putMany, toBin, removeFromBin, get(since), post(changes), load(), save(state), now() }
 * Returns { pulled, pushed, changed } (changed: this phone's collection or bin was updated).
 */
export async function syncOnce(d) {
  const state = d.load() || { seq: 0, items: {} };
  const items = state.items;
  let comics = await d.allComics();
  let bin = await d.binComics();
  const here = new Map(comics.map((c) => [c.id, c]));
  const binned = new Map(bin.map((c) => [c.id, c]));
  const unsaved = (c) => !items[c.id] || items[c.id][2] || items[c.id][0] !== fingerprint(c);
  let pulled = 0;

  // 1. What other phones changed.
  for (;;) {
    const page = await d.get(state.seq);
    const puts = [];
    const toBin = [];
    const unbin = [];
    for (const r of page.comics) {
      const known = items[r.id];
      const local = here.get(r.id);
      if (known && known[1] === r.updatedAt && !!known[2] === !!r.deleted) continue; // our own change coming back
      if (r.deleted) {
        // A change made here after it was deleted elsewhere keeps it (and goes up next).
        if (local && !(unsaved(local) && (local.updatedAt || '') > r.updatedAt)) {
          toBin.push(r.id);
          here.delete(r.id);
        }
        if (!local || toBin.includes(r.id)) items[r.id] = ['', r.updatedAt, 1];
        continue;
      }
      if (local && unsaved(local) && (local.updatedAt || '') > r.updatedAt) continue;
      const gone = binned.get(r.id);
      if (!local && gone && (!known || !known[2]) && (gone.deletedAt || '') > r.updatedAt) continue; // deleted here later
      const next = { ...r.data, id: r.id };
      for (const k of LOCAL) if (local && local[k] !== undefined && k !== 'deletedAt') next[k] = local[k];
      puts.push(next);
      here.set(r.id, next);
      if (gone) unbin.push(r.id);
      items[r.id] = [fingerprint(next), r.updatedAt, 0];
    }
    if (puts.length) await d.putMany(puts);
    if (toBin.length) await d.toBin(toBin);
    if (unbin.length) await d.removeFromBin(unbin);
    pulled += puts.length + toBin.length;
    state.seq = page.seq;
    d.save(state);
    if (!page.more) break;
  }
  if (pulled) {
    comics = await d.allComics();
    bin = await d.binComics();
  }

  // 2. What changed on this phone.
  const now = d.now();
  // A change has to be later than what's online, or it would be skipped there.
  const stamp = (t, known) => (known && !(t > known[1]) ? latest(now, after(known[1])) : t);
  const changes = [];
  const alive = new Set();
  for (const c of comics) {
    alive.add(c.id);
    const known = items[c.id];
    const h = fingerprint(c);
    if (known && !known[2] && known[0] === h) continue;
    const data = shared(c);
    if (JSON.stringify(data).length > MAX_DATA) continue;
    const t = ISO.test(c.updatedAt || '') ? c.updatedAt : ISO.test(c.addedAt || '') ? c.addedAt : now;
    changes.push({ id: c.id, data, updatedAt: stamp(t, known), h });
  }
  const binnedNow = new Map(bin.map((c) => [c.id, c]));
  for (const [id, known] of Object.entries(items)) {
    if (alive.has(id) || known[2]) continue;
    const t = binnedNow.get(id)?.deletedAt;
    changes.push({ id, deleted: true, updatedAt: stamp(ISO.test(t || '') ? t : now, known) });
  }
  for (let i = 0; i < changes.length; i += CHUNK) {
    const part = changes.slice(i, i + CHUNK);
    await d.post(part.map(({ h, ...c }) => c));
    for (const c of part) items[c.id] = c.deleted ? ['', c.updatedAt, 1] : [c.h, c.updatedAt, 0];
    d.save(state);
  }
  return { pulled, pushed: changes.length, changed: pulled > 0 };
}

/** Talks to /api/comics on this site. Errors carry the HTTP status. */
export async function api(method, body) {
  const since = method === 'GET' ? `?since=${body || 0}` : '';
  const res = await fetch(`/api/comics${since}`, {
    method,
    credentials: 'same-origin',
    cache: 'no-store',
    headers: method === 'POST' ? { 'Content-Type': 'application/json' } : {},
    body: method === 'POST' ? JSON.stringify({ changes: body }) : undefined,
  });
  if (!res.ok) {
    const err = new Error((await res.json().catch(() => ({}))).error || `Online save failed (${res.status})`);
    err.status = res.status;
    throw err;
  }
  return res.json();
}
