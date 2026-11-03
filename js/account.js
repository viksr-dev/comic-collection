// Who is using the app, when it's opened from keyissue.app (where the family signs in at
// home.keyissue.app). Each person gets their own collection, digital comics and settings on
// the phone. The family's owner keeps the storage the app used before sign-in, so nothing moves.
// Anywhere else (like GitHub Pages) there's no sign-in and everything works as before.

const HOME = 'https://home.keyissue.app';
const USER_KEY = 'keyissueUser';

export const onKeyissue = /(^|\.)keyissue\.app$/.test(location.hostname);

function cachedUser() {
  try { return JSON.parse(localStorage.getItem(USER_KEY) || 'null'); } catch { return null; }
}

async function signedInUser() {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), 5000);
  try {
    const res = await fetch(`${HOME}/api/me`, { credentials: 'include', cache: 'no-store', signal: ctl.signal });
    if (!res.ok) throw new Error(`me ${res.status}`);
    return (await res.json()).user || null;
  } finally {
    clearTimeout(timer);
  }
}

export const signInUrl = () => `${HOME}/?return=${encodeURIComponent(location.href)}`;

let current = null;
if (onKeyissue) {
  try {
    current = await signedInUser();
    if (!current) {
      try { localStorage.removeItem(USER_KEY); } catch {}
      location.replace(signInUrl());
      await new Promise(() => {}); // stop here while the browser goes to sign in
    }
    try { localStorage.setItem(USER_KEY, JSON.stringify(current)); } catch {}
  } catch {
    // Offline (in a shop, say): carry on as whoever signed in last on this phone.
    current = cachedUser();
  }
}

/** The signed-in person ({ id, name, role }), or null when there's no sign-in. */
export const user = current;

/** '' for the family's owner (and with no sign-in), otherwise the person's id. */
export const scope = current && current.role !== 'owner' ? String(current.id) : '';

/** A storage name (localStorage key or IndexedDB name) of the signed-in person's own. */
export const scoped = (name) => (scope ? `${name}@${scope}` : name);
