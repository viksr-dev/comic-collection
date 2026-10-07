// Cloudflare Worker that lets the Comic Collection app look up comics on
// Metron (metron.cloud). Metron's API can't be called straight from a web
// page, and this also keeps your Metron password out of the app.
//
// Secrets to set in Cloudflare: METRON_USER and METRON_PASS.
// Optional variable: ALLOWED_ORIGIN (e.g. https://viksr-dev.github.io).
// Optional KV namespace binding: INBOX, for sending phone scans to the desktop app.
//
// Routes:
//   GET /ping                      check the Metron login works (and say which version this is)
//   POST /inbox/<sync code>        phone: leave a batch of comics (CSV text) for the desktop app
//   GET /inbox/<sync code>         desktop: collect waiting batches
//   DELETE /inbox/<sync code>/<id> desktop: remove a batch once it's saved
//   PUT /library/<sync code>       desktop: leave a list of what you own and want, for the phone
//   GET /library/<sync code>       phone: read that list
//   GET /upc/<barcode>?issue=<n>   look up by barcode (UPC + 5-digit add-on)
//   GET /search?series=<name>&number=<n>
//   GET /issue/<metron id>         full details, including the story arcs it's part of
//   GET /releases?series=<name>&after=<YYYY-MM-DD>   issues in shops on or after a day
//   GET /arc/<metron arc id>       how many issues a story arc has, and which they are

const METRON = 'https://metron.cloud/api';
const CACHE_SECONDS = 60 * 60 * 24 * 7;
const RELEASES_CACHE_SECONDS = 60 * 60 * 12;

export default {
  async fetch(request, env, ctx) {
    const cors = {
      'Access-Control-Allow-Origin': env.ALLOWED_ORIGIN || '*',
      'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
      'Access-Control-Max-Age': '86400',
      Vary: 'Origin',
    };
    if (request.method === 'OPTIONS') return new Response(null, { headers: cors });
    const url = new URL(request.url);
    if (url.pathname.startsWith('/inbox/')) return inbox(request, url, env, cors);
    if (url.pathname.startsWith('/library/')) return library(request, url, env, cors);
    if (request.method !== 'GET') return json({ error: 'Method not allowed' }, 405, cors);

    // Lookups are cached for a week to stay well inside Metron's rate limits.
    const cache = caches.default;
    const cacheKey = new Request(request.url, { method: 'GET' });
    if (url.pathname !== '/ping') {
      const hit = await cache.match(cacheKey);
      if (hit) return hit;
    }

    try {
      const metron = metronClient(env);
      let body;
      const parts = url.pathname.split('/').filter(Boolean);
      if (parts[0] === 'ping') {
        await metron('/publisher/?page=1');
        return json({ ok: true, version: 6, mailbox: !!env.INBOX }, 200, cors);
      } else if (parts[0] === 'upc' && parts[1]) {
        body = { results: await byBarcode(metron, parts[1], url.searchParams.get('issue')) };
      } else if (parts[0] === 'search') {
        body = { results: await search(metron, url.searchParams.get('series'), url.searchParams.get('number')) };
      } else if (parts[0] === 'issue' && /^\d+$/.test(parts[1] || '')) {
        body = { result: toIssue(await metron(`/issue/${parts[1]}/`)) };
      } else if (parts[0] === 'releases') {
        body = { results: await releases(metron, url.searchParams.get('series'), url.searchParams.get('after')) };
      } else if (parts[0] === 'arc' && /^\d+$/.test(parts[1] || '')) {
        body = await arcIssues(metron, parts[1]);
      } else {
        return json({ error: 'Not found' }, 404, cors);
      }
      const maxAge = parts[0] === 'releases' ? RELEASES_CACHE_SECONDS : CACHE_SECONDS;
      const response = json(body, 200, { ...cors, 'Cache-Control': `public, max-age=${maxAge}` });
      ctx.waitUntil(cache.put(cacheKey, response.clone()));
      return response;
    } catch (err) {
      const status = err.status === 429 ? 429 : err.status === 401 ? 401 : 502;
      return json({ ok: false, error: err.message }, status, cors);
    }
  },
};

function metronClient(env) {
  if (!env.METRON_USER || !env.METRON_PASS) {
    throw Object.assign(new Error('METRON_USER and METRON_PASS are not set'), { status: 401 });
  }
  const auth = 'Basic ' + btoa(`${env.METRON_USER}:${env.METRON_PASS}`);
  return async (path) => {
    const res = await fetch(METRON + path, {
      headers: { Authorization: auth, Accept: 'application/json', 'User-Agent': 'comic-collection-relay/1.0' },
    });
    if (!res.ok) {
      const msg = res.status === 401 ? 'Metron rejected the username or password' : `Metron answered ${res.status}`;
      throw Object.assign(new Error(msg), { status: res.status });
    }
    return res.json();
  };
}

async function byBarcode(metron, code, issue) {
  const digits = code.replace(/\D/g, '');
  const base = digits.length > 13 ? digits.slice(0, digits.length - 5) : digits;
  // Metron usually stores the full barcode (UPC + add-on); some entries only
  // have the 12-digit UPC, which is shared by every issue of a series.
  const attempts = [digits];
  if (base !== digits) attempts.push(base);

  for (const upc of attempts) {
    const list = await metron(`/issue/?upc=${encodeURIComponent(upc)}`);
    const rows = list.results || [];
    // If the filter were ignored we'd get the whole catalogue; treat that as no match.
    if (!rows.length || list.count > 25) continue;
    const details = await Promise.all(rows.slice(0, 5).map((r) => metron(`/issue/${r.id}/`)));
    let matches = details.filter((d) => String(d.upc || '').replace(/\D/g, '') === upc);
    if (upc === base && issue) matches = matches.filter((d) => String(d.number) === String(issue));
    if (matches.length) return matches.map(toIssue);
  }
  return [];
}

async function search(metron, series, number) {
  if (!series) return [];
  const q = new URLSearchParams({ series_name: series });
  if (number) q.set('number', number);
  const list = await metron(`/issue/?${q}`);
  return (list.results || []).slice(0, 30).map(toIssue);
}

// Issues of a series that are in shops on or after a day. Metron's series search
// also matches longer names; the desktop app keeps only the exact series.
async function releases(metron, series, after) {
  if (!series) return [];
  const q = new URLSearchParams({ series_name: series });
  const day = /^\d{4}-\d{2}-\d{2}$/.test(after || '') ? after : '';
  if (day) q.set('store_date_range_after', day);
  const list = await metron(`/issue/?${q}`);
  return (list.results || [])
    .filter((d) => !day || (d.store_date && String(d.store_date) >= day))
    .slice(0, 100)
    .map(toIssue);
}

// Every issue in a story arc (up to 500), in reading order as Metron lists them.
async function arcIssues(metron, id) {
  let list = await metron(`/arc/${id}/issue_list/`);
  const rows = [...(list.results || [])];
  for (let page = 2; list.next && page <= 5; page++) {
    list = await metron(`/arc/${id}/issue_list/?page=${page}`);
    rows.push(...(list.results || []));
  }
  return { count: list.count || rows.length, results: rows.map(toIssue) };
}

function toIssue(d) {
  const series = d.series || {};
  return {
    metronId: d.id,
    series: series.name || '',
    volume: series.year_began ? String(series.year_began) : series.volume ? String(series.volume) : '',
    number: d.number != null ? String(d.number) : '',
    title: Array.isArray(d.name) ? d.name.filter(Boolean).join('; ') : d.title || '',
    publisher: d.publisher?.name || '',
    coverDate: d.cover_date ? String(d.cover_date).slice(0, 7) : '',
    coverUrl: d.image || '',
    upc: d.upc || '',
    // Original cover price, e.g. "3.99" (only in full issue details, not search results).
    price: d.price != null ? String(d.price) : '',
    // Day it's in shops, "YYYY-MM-DD".
    storeDate: d.store_date ? String(d.store_date).slice(0, 10) : '',
    // Story arcs, e.g. [{ id: 123, name: "Year One" }] (only in full issue details).
    arcs: Array.isArray(d.arcs) ? d.arcs.filter((a) => a && a.id && a.name).map((a) => ({ id: a.id, name: a.name })) : [],
  };
}

// A small mailbox in Cloudflare KV. Each batch is kept for 30 days, under a
// key that starts with the sync code, so only someone with the code can read it.
async function inbox(request, url, env, cors) {
  if (!env.INBOX) {
    return json({ ok: false, error: 'The relay has no mailbox yet. See "Sending scans to your computer" in relay/README.md.' }, 501, cors);
  }
  const [, , code, id] = url.pathname.split('/');
  if (!/^[A-Z0-9]{16,64}$/.test(code || '')) return json({ ok: false, error: 'Bad sync code' }, 400, cors);
  const prefix = `inbox:${code}:`;
  if (request.method === 'POST' && !id) {
    const csv = await request.text();
    if (!csv.trim()) return json({ ok: false, error: 'Nothing to send' }, 400, cors);
    if (csv.length > 2_000_000) return json({ ok: false, error: 'Too much in one go' }, 413, cors);
    const key = `${prefix}${Date.now()}-${crypto.randomUUID().slice(0, 8)}`;
    await env.INBOX.put(key, csv, { expirationTtl: 60 * 60 * 24 * 30 });
    return json({ ok: true }, 200, cors);
  }
  if (request.method === 'GET' && !id) {
    const list = await env.INBOX.list({ prefix, limit: 50 });
    const batches = [];
    for (const k of list.keys) {
      const csv = await env.INBOX.get(k.name);
      if (csv) batches.push({ id: k.name.slice(prefix.length), csv });
    }
    return json({ ok: true, batches }, 200, cors);
  }
  if (request.method === 'DELETE' && id) {
    await env.INBOX.delete(prefix + id);
    return json({ ok: true }, 200, cors);
  }
  return json({ ok: false, error: 'Not found' }, 404, cors);
}

// What the desktop app owns and wants, so the phone can say "you own this" in a
// shop. One copy per sync code, replaced each time the desktop sends a new one.
async function library(request, url, env, cors) {
  if (!env.INBOX) {
    return json({ ok: false, error: 'The relay has no mailbox yet. See "Sending scans to your computer" in relay/README.md.' }, 501, cors);
  }
  const code = url.pathname.split('/')[2] || '';
  if (!/^[A-Z0-9]{16,64}$/.test(code)) return json({ ok: false, error: 'Bad sync code' }, 400, cors);
  const key = `library:${code}`;
  if (request.method === 'PUT') {
    const text = await request.text();
    if (text.length > 10_000_000) return json({ ok: false, error: 'Too big' }, 413, cors);
    try { JSON.parse(text); } catch { return json({ ok: false, error: 'Not JSON' }, 400, cors); }
    await env.INBOX.put(key, text, { expirationTtl: 60 * 60 * 24 * 365 });
    return json({ ok: true }, 200, cors);
  }
  if (request.method === 'GET') {
    const text = await env.INBOX.get(key);
    if (!text) return json({ ok: false, error: 'Nothing sent from the computer yet' }, 404, cors);
    return new Response(text, { status: 200, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', ...cors } });
  }
  return json({ ok: false, error: 'Not found' }, 404, cors);
}

function json(body, status, headers) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  });
}
