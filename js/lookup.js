// Comic details come from Metron (metron.cloud) via a small relay, because
// Metron's API can't be called directly from a web page. See relay/README.md.

const RELAY_KEY = 'relayUrl';

export function getRelayUrl() {
  try {
    return (localStorage.getItem(RELAY_KEY) || '').replace(/\/+$/, '');
  } catch {
    return '';
  }
}

export function setRelayUrl(url) {
  localStorage.setItem(RELAY_KEY, url.trim());
}

export const hasRelay = () => !!getRelayUrl();

async function call(path) {
  const base = getRelayUrl();
  if (!base) throw new Error('Comic lookup is not set up yet (see Settings).');
  const res = await fetch(base + path);
  if (res.status === 429) throw new Error('Too many lookups in a short time. Wait a minute and try again.');
  if (!res.ok) throw new Error(`Lookup failed (${res.status}).`);
  return res.json();
}

export async function lookupBarcode(parsed) {
  const data = await call(`/upc/${encodeURIComponent(parsed.full)}?issue=${encodeURIComponent(parsed.issue || '')}`);
  return data.results || [];
}

export async function searchTitle(series, number) {
  const q = new URLSearchParams({ series });
  if (number) q.set('number', number);
  const data = await call(`/search?${q}`);
  return data.results || [];
}

export async function issueDetails(metronId) {
  const data = await call(`/issue/${encodeURIComponent(metronId)}`);
  return data.result || null;
}

// Leaves comics (as CSV text) in the relay's mailbox for the desktop app to collect.
// Plain text keeps it a simple request, so the browser doesn't need to ask first.
export async function sendToComputer(code, csv) {
  const base = getRelayUrl();
  if (!base) throw new Error('Comic lookup is not set up yet (see Settings).');
  const res = await fetch(`${base}/inbox/${code}`, { method: 'POST', headers: { 'Content-Type': 'text/plain;charset=UTF-8' }, body: csv });
  if (res.status === 501) throw new Error('Your relay needs its mailbox set up first (see the setup steps).');
  if (!res.ok) throw new Error(`Sending failed (${res.status}).`);
}

export async function testRelay(url) {
  const res = await fetch(url.replace(/\/+$/, '') + '/ping');
  if (!res.ok) throw new Error(`Relay answered ${res.status}`);
  const data = await res.json();
  if (!data.ok) throw new Error(data.error || 'Relay could not reach Metron');
  return data;
}
