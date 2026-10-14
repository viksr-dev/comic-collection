// Search the way people type it: "daredevil 50", "walking dead 100", "spiderman",
// "#50a". Every word has to be found somewhere. A number has to be the issue
// number (any cover letter, leading zeros or not) or a whole number elsewhere,
// such as a volume year, so "daredevil 50" doesn't bring up #150 or #500.
// The computer app searches the same way (desktop/ComicCatalog/Data/WordSearch.vb).

const squash = (s) => s.replace(/[^a-z0-9]/g, '');
const NUMBER = /^0*(\d+)([a-z]{0,2})$/;

// The words of a search: lower case, without "#" and without "the" (typed or not).
// Words in quotes are found together: "i am gotham".
export function searchWords(query) {
  const words = [];
  for (const m of String(query || '').toLowerCase().matchAll(/"([^"]*)"?|[^\s,"]+/g)) {
    if (m[1] !== undefined) {
      const phrase = m[1].replace(/\s+/g, ' ').trim();
      if (phrase) words.push(phrase);
    } else {
      const w = m[0].replace(/^#+/, '');
      if (w && w !== 'the') words.push(w);
    }
  }
  return words;
}

// A comic's issue number as digits and cover letter: "050A" is { digits: '50', letter: 'a' }.
function issueOf(number, letter) {
  const issue = String(number ?? '').trim().toLowerCase().replace(/^#/, '').replace(/^0+(?=\d)/, '');
  const digits = issue.replace(/[a-z]+$/, '');
  return { issue, digits, letter: issue.slice(digits.length) || String(letter || '').toLowerCase() };
}

// Whether a number word is this issue: "50" is #50 (any cover), "50a" only cover A.
function isIssue(w, issue) {
  const m = NUMBER.exec(w);
  return !!m && m[1] === issue.digits && (!m[2] || m[2] === issue.letter);
}

// True when every word is found. text: the comic's words (series, volume, publisher…);
// number and letter: its issue number and cover letter, matched exactly.
export function wordsMatch(words, { text = [], number = '', letter = '' } = {}) {
  if (!words.length) return true;
  const fields = text.map((t) => String(t ?? '').toLowerCase()).filter(Boolean);
  const squashed = fields.map(squash);
  const issue = issueOf(number, letter);
  return words.every((w) => {
    const num = NUMBER.exec(w);
    if (num) {
      if (isIssue(w, issue)) return true;
      if (num[2]) return fields.some((f) => f.includes(w));
      const whole = new RegExp(`(^|[^0-9])${num[1]}($|[^0-9])`);
      if (fields.some((f) => whole.test(f))) return true;
      // Part of a barcode, typed with or without its space.
      return w.length >= 5 && fields.some((f) => f.replace(/\D/g, '').includes(w));
    }
    const sw = squash(w);
    return issue.issue.includes(w) || fields.some((f) => f.includes(w)) || (!!sw && squashed.some((f) => f.includes(sw)));
  });
}

// How well a comic that matches fits: 3 when the words are found together ("box 2" is Box 2, not
// issue #2 in another box), 2 when the last number typed is its issue number ("harley quinn vol 3 1"
// is #1), 1 when another number is, otherwise 0.
export function searchScore(words, { text = [], number = '', letter = '' } = {}) {
  if (words.length > 1) {
    const phrase = words.join(' ').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const together = new RegExp(`(^|[^a-z0-9])${phrase}($|[^a-z0-9])`);
    if (text.some((t) => together.test(String(t ?? '').toLowerCase()))) return 3;
  }
  const numbers = words.filter((w) => NUMBER.test(w));
  if (!numbers.length) return 0;
  const issue = issueOf(number, letter);
  if (isIssue(numbers[numbers.length - 1], issue)) return 2;
  return numbers.some((w) => isIssue(w, issue)) ? 1 : 0;
}
