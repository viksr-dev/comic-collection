// Notifications and wishlist price alerts, when the app is opened from keyissue.app.
//
// The server (keyissue-apps, apps/comics) keeps each person's wishlist with the most they'd pay,
// searches eBay for those comics every few hours and sends a notification when one is listed at or
// under that price. The wishlist and prices come from the Comic Catalog app on the computer; a price
// set here on the phone wins (0 turns alerts off for that comic).

async function call(method, path, body) {
  const res = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw Object.assign(new Error(data.error || `Failed (${res.status})`), { status: res.status });
  return data;
}

/** Same as the server's: "The Amazing Spider-Man", "#0300" -> "amazingspiderman#300". */
export function watchKey(series, issue) {
  const s = String(series || '').toLowerCase().replace(/^the\s+/, '').replace(/[^a-z0-9]/g, '');
  const i = String(issue || '').toLowerCase().replace(/^#/, '').replace(/^0+(?=\d)/, '').replace(/[^a-z0-9.]/g, '');
  return s && i ? `${s}#${i}` : '';
}

/** { watches: [{ key, series, issue, pcMax, phoneMax, max, checkedAt, error, deals: [{ title, price, url }] }] } */
export const getAlerts = () => call('GET', '/api/alerts');

/** Passes on the computer's wishlist ([{ series, issue, maxUsd }]). */
export const sendWishlist = (wishlist) => call('PUT', '/api/alerts', { wishlist });

/** A price set on this phone: a number, 0 for no alerts, or null to use the computer's. */
export const setPrice = (series, issue, maxUsd) => call('POST', '/api/alerts/price', { series, issue, maxUsd });

// ---------- notifications ----------

const iPhone = () => /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
const standalone = () => matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;

/** 'unsupported' | 'install' (iPhone, not opened from the Home Screen) | 'denied' | 'on' | 'off' */
export async function notificationState() {
  if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
    return iPhone() && !standalone() ? 'install' : 'unsupported';
  }
  if (Notification.permission === 'denied') return 'denied';
  const reg = await navigator.serviceWorker.ready;
  const sub = await reg.pushManager.getSubscription();
  if (!sub || Notification.permission !== 'granted') return 'off';
  // This browser may have been turned on by someone else in the family: only "on" if it's ours.
  const { subscribed } = await call('GET', `/api/push?endpoint=${encodeURIComponent(sub.endpoint)}`).catch(() => ({ subscribed: true }));
  return subscribed ? 'on' : 'off';
}

function keyBytes(b64url) {
  const s = b64url.replace(/-/g, '+').replace(/_/g, '/');
  const bin = atob(s + '='.repeat((4 - (s.length % 4)) % 4));
  return Uint8Array.from(bin, (c) => c.charCodeAt(0));
}

/** Asks for permission and turns notifications on for this browser. Returns the new state. */
export async function turnOnNotifications() {
  const { publicKey } = await call('GET', '/api/push');
  if (!publicKey) throw Object.assign(new Error("Notifications aren't set up on the server yet."), { status: 503 });
  if ((await Notification.requestPermission()) !== 'granted') return 'denied';
  const reg = await navigator.serviceWorker.ready;
  let sub = await reg.pushManager.getSubscription();
  const key = keyBytes(publicKey);
  const same = sub && sub.options && sub.options.applicationServerKey
    && new Uint8Array(sub.options.applicationServerKey).every((b, i) => b === key[i]);
  if (sub && !same) {
    await sub.unsubscribe();
    sub = null;
  }
  if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
  await call('POST', '/api/push', { subscription: sub.toJSON() });
  return 'on';
}

export async function turnOffNotifications() {
  const reg = await navigator.serviceWorker.ready;
  const sub = await reg.pushManager.getSubscription();
  if (sub) {
    await call('DELETE', '/api/push', { endpoint: sub.endpoint }).catch(() => {});
    await sub.unsubscribe();
  }
  return 'off';
}
