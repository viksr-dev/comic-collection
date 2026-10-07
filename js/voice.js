// "Batman 404 near mint" → { series: 'Batman', number: '404', condition: 'Near Mint' }.
// Uses the phone's own speech recognition where the browser offers it.

const CONDITIONS = ['Near Mint', 'Very Fine', 'Very Good', 'Mint', 'Fine', 'Good', 'Fair', 'Poor'];
const SHORT = { nm: 'Near Mint', vf: 'Very Fine', vg: 'Very Good', fn: 'Fine', gd: 'Good' };
const WORDS = {
  zero: 0, oh: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10,
  eleven: 11, twelve: 12, thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16, seventeen: 17, eighteen: 18,
  nineteen: 19, twenty: 20, thirty: 30, forty: 40, fifty: 50, sixty: 60, seventy: 70, eighty: 80, ninety: 90,
};

// "four hundred and four" → 404, "twenty one" → 21. Only used when the phone
// writes the number as words, which is rare.
function wordsToNumber(words) {
  let total = 0;
  let current = 0;
  for (const w of words) {
    if (w === 'and') continue;
    if (w === 'hundred') current = (current || 1) * 100;
    else if (w in WORDS) current += WORDS[w];
    else return null;
  }
  return total + current;
}

const titleCase = (s) => s.replace(/\b([a-z])/g, (m, c) => c.toUpperCase());

export function parseSpoken(text) {
  let rest = ` ${String(text || '').toLowerCase().replace(/[.,!?]/g, ' ')} `.replace(/\s+/g, ' ');
  const result = { series: '', number: '', condition: '', volume: '' };

  for (const c of CONDITIONS) {
    const re = new RegExp(` (in )?${c.toLowerCase()}( condition)? `);
    if (re.test(rest)) {
      result.condition = c;
      rest = rest.replace(re, ' ');
      break;
    }
  }
  if (!result.condition) {
    const m = rest.match(/ (nm|vf|vg|fn|gd) /);
    if (m) {
      result.condition = SHORT[m[1]];
      rest = rest.replace(m[0], ' ');
    }
  }

  const vol = rest.match(/ (?:volume|vol) (\d+|[a-z]+) /);
  if (vol) {
    result.volume = /^\d+$/.test(vol[1]) ? vol[1] : String(wordsToNumber([vol[1]]) ?? '');
    rest = rest.replace(vol[0], ' ');
  }

  // "number 404", "issue 404", "#404", or just the last number said.
  const words = rest.trim().split(' ').filter(Boolean);
  let at = -1;
  for (let i = words.length - 1; i >= 0; i--) {
    if (/^#?\d+[a-z]?$/.test(words[i])) { at = i; break; }
  }
  if (at >= 0) {
    result.number = words[at].replace('#', '').toUpperCase();
    words.splice(at, 1);
  } else {
    // Number said as words after "issue" or "number", e.g. "saga issue four".
    // (Without that word, "Fantastic Four" would lose its four.)
    const said = words.map((w) => /^(number|issue|no)$/.test(w)).lastIndexOf(true);
    const n = said >= 0 ? wordsToNumber(words.slice(said + 1)) : null;
    if (n !== null && said + 1 < words.length) {
      result.number = String(n);
      words.splice(said + 1);
    }
  }
  while (words.length && /^(number|issue|no|#)$/.test(words[words.length - 1])) words.pop();
  result.series = titleCase(words.join(' ').replace(/ ?#$/, '').trim());
  return result;
}

export function speechSupported() {
  return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
}

// Listens once and resolves with what was said (or '' if nothing was heard).
export function listen(onStart) {
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  return new Promise((resolve, reject) => {
    const rec = new Recognition();
    rec.lang = 'en-NZ';
    rec.interimResults = false;
    rec.maxAlternatives = 1;
    let heard = '';
    rec.onresult = (e) => { heard = e.results[0]?.[0]?.transcript || ''; };
    rec.onerror = (e) => (e.error === 'no-speech' || e.error === 'aborted' ? resolve('') : reject(new Error(e.error)));
    rec.onend = () => resolve(heard);
    rec.start();
    onStart?.(rec);
  });
}
