// "On our Plex": films and shows on the family Plex server that match a comic's series, shown
// when you open a comic at comics.keyissue.app. Only for people who can open Popcorn & Parks;
// everyone else (and the GitHub Pages copy) never sees it.
import { onKeyissue } from './account.js';

let library = null;

async function load() {
  if (!onKeyissue) return null;
  if (library) return library;
  try {
    const res = await fetch('/api/plex', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    library = res.ok ? await res.json() : { m: [], s: [] };
  } catch {
    return null; // offline: try again next time
  }
  return library;
}

const norm = (t) => String(t || '').toLowerCase().replace(/^the\s+/, '').replace(/[^a-z0-9]/g, '');
// "The Amazing Spider-Man (2018) Vol. 2" -> "amazingspiderman"
const seriesKey = (s) => norm(String(s || '').replace(/\(.*?\)/g, '').replace(/\b(vol(ume)?\.?\s*\d+|annual|tpb|omnibus)\b.*$/i, ''));

/** Plex films and shows whose title contains the series name (or the other way round). */
export function plexMatches(lib, series) {
  const key = seriesKey(series);
  if (!lib || key.length < 4) return [];
  const hit = (title) => { const t = norm(title); return t.includes(key) || (t.length >= 6 && key.includes(t)); };
  const movies = (lib.m || []).filter((r) => r[2] >= 40 && hit(r[0])).map((r) => ({ title: r[0], year: r[1], kind: 'film', key: r[8] || '', score: r[4] || 0 }));
  const shows = (lib.s || []).filter((r) => hit(r[0])).map((r) => ({ title: r[0], year: r[1], kind: 'TV', key: r[8] || '' }));
  // Best-rated films first, then the TV shows.
  return [...movies.sort((a, b) => b.score - a.score), ...shows].slice(0, 10);
}

const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const link = (mid, x) => (x.key && mid
  ? `https://app.plex.tv/desktop/#!/server/${encodeURIComponent(mid)}/details?key=${encodeURIComponent('/library/metadata/' + x.key)}`
  : `https://app.plex.tv/desktop/#!/search?query=${encodeURIComponent(x.title)}`);

/** Fills `box` with what's on Plex for this series, or hides it. */
export async function showPlexFor(box, series) {
  if (!box) return;
  box.hidden = true;
  box.innerHTML = '';
  if (!series || !onKeyissue) return;
  const lib = await load();
  const found = plexMatches(lib, series);
  if (!found.length) return;
  box.innerHTML = `<strong>🎬 We've got it on Plex</strong> <span class="muted">(watch on the TV)</span><div class="plex-list">${found.map((x) =>
    `<a href="${esc(link(lib.mid, x))}" target="_blank" rel="noopener">${esc(x.title)}${x.year ? ` (${x.year})` : ''}${x.kind === 'TV' ? ' · TV' : ''}</a>`).join('')}</div>`;
  box.hidden = false;
}
