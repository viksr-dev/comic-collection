// Cloudflare Worker that lets the Comic Collection app look up comics on
// Metron (metron.cloud). Metron's API can't be called straight from a web
// page, and this also keeps your Metron password out of the app.
//
// Secrets to set in Cloudflare: METRON_USER and METRON_PASS.
// Optional variable: ALLOWED_ORIGIN (e.g. https://viksr-dev.github.io).
//
// Routes:
//   GET /ping                      check the Metron login works
//   GET /upc/<barcode>?issue=<n>   look up by barcode (UPC + 5-digit add-on)
//   GET /search?series=<name>&number=<n>
//   GET /issue/<metron id>

const METRON = 'https://metron.cloud/api';
const CACHE_SECONDS = 60 * 60 * 24 * 7;

export default {
  async fetch(request, env, ctx) {
    const cors = {
      'Access-Control-Allow-Origin': env.ALLOWED_ORIGIN || '*',
      'Access-Control-Allow-Methods': 'GET, OPTIONS',
      'Access-Control-Max-Age': '86400',
      Vary: 'Origin',
    };
    if (request.method === 'OPTIONS') return new Response(null, { headers: cors });
    if (request.method !== 'GET') return json({ error: 'Method not allowed' }, 405, cors);

    // Lookups are cached for a week to stay well inside Metron's rate limits.
    const cache = caches.default;
    const cacheKey = new Request(request.url, { method: 'GET' });
    const url = new URL(request.url);
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
        return json({ ok: true }, 200, cors);
      } else if (parts[0] === 'upc' && parts[1]) {
        body = { results: await byBarcode(metron, parts[1], url.searchParams.get('issue')) };
      } else if (parts[0] === 'search') {
        body = { results: await search(metron, url.searchParams.get('series'), url.searchParams.get('number')) };
      } else if (parts[0] === 'issue' && /^\d+$/.test(parts[1] || '')) {
        body = { result: toIssue(await metron(`/issue/${parts[1]}/`)) };
      } else {
        return json({ error: 'Not found' }, 404, cors);
      }
      const response = json(body, 200, { ...cors, 'Cache-Control': `public, max-age=${CACHE_SECONDS}` });
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
  };
}

function json(body, status, headers) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  });
}
