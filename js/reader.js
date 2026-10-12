// The comic reader: swipe or tap to turn pages (with a slide), pinch or
// double-tap the middle to zoom, or "webtoon" mode: one long strip you scroll.
// Pages are prepared a few ahead, so turning doesn't wait.
import { openComic } from './comicfile.js';
import { getFile, savePlace } from './shelf.js';

const $ = (sel, root = document) => root.querySelector(sel);
const SETTINGS_KEY = 'readerSettings';

function loadSettings() {
  try {
    return { mode: 'page', manga: false, ...JSON.parse(localStorage.getItem(SETTINGS_KEY) || '{}') };
  } catch {
    return { mode: 'page', manga: false };
  }
}

const saveSettings = (s) => localStorage.setItem(SETTINGS_KEY, JSON.stringify(s));

// Opens a book from the shelf. Resolves when the reader is closed.
export async function openReader(book, { onClose } = {}) {
  const blob = await getFile(book.id);
  if (!blob) throw new Error('The file for this comic is missing. Remove it and add it again.');
  const comic = await openComic(blob);
  return new Promise((resolve) => {
    const r = new Reader(book, comic, () => {
      onClose?.();
      resolve();
    });
    r.show();
  });
}

class Reader {
  constructor(book, comic, done) {
    this.book = book;
    this.comic = comic;
    this.count = comic.pageCount;
    this.done = done;
    this.settings = loadSettings();
    this.index = book.page >= book.pages - 1 ? 0 : Math.min(book.page || 0, this.count - 1);
    this.urls = new Map(); // page → object URL
    this.loading = new Map(); // page → promise
    this.order = []; // most recently used last
    this.zoom = { s: 1, x: 0, y: 0 };
    this.width = Math.min(1600, Math.round(window.innerWidth * (window.devicePixelRatio || 1)));
    this.build();
  }

  build() {
    const el = document.createElement('div');
    el.className = 'reader';
    el.innerHTML = `
      <div class="reader-pages"></div>
      <div class="reader-top">
        <button class="reader-btn" data-act="close" aria-label="Close">✕</button>
        <div class="reader-title"></div>
        <div class="reader-count"></div>
      </div>
      <div class="reader-bottom">
        <input class="reader-slider" type="range" min="1" step="1">
        <div class="reader-modes">
          <button class="reader-btn" data-act="page">Pages</button>
          <button class="reader-btn" data-act="webtoon">Webtoon</button>
          <button class="reader-btn" data-act="manga">Right to left</button>
        </div>
      </div>
      <div class="reader-hint"></div>`;
    this.el = el;
    this.pagesEl = $('.reader-pages', el);
    $('.reader-title', el).textContent = this.book.title;
    const slider = $('.reader-slider', el);
    slider.max = String(this.count);
    slider.addEventListener('input', () => ($('.reader-count', el).textContent = `${slider.value} / ${this.count}`));
    slider.addEventListener('change', () => this.jump(Number(slider.value) - 1));
    el.addEventListener('click', (e) => {
      const act = e.target.closest('[data-act]')?.dataset.act;
      if (!act) return;
      e.stopPropagation();
      if (act === 'close') this.close();
      if (act === 'page' || act === 'webtoon') this.setMode(act);
      if (act === 'manga') {
        this.settings.manga = !this.settings.manga;
        saveSettings(this.settings);
        this.render();
      }
    });
    this.onKey = (e) => {
      if (e.key === 'Escape') this.close();
      else if (this.settings.mode === 'webtoon') return;
      else if (e.key === 'ArrowRight') this.turnVisual(1);
      else if (e.key === 'ArrowLeft') this.turnVisual(-1);
    };
  }

  show() {
    document.body.appendChild(this.el);
    document.body.classList.add('reading');
    document.addEventListener('keydown', this.onKey);
    this.render();
    this.flashUi();
  }

  close() {
    this.savePlace();
    document.removeEventListener('keydown', this.onKey);
    document.body.classList.remove('reading');
    this.el.remove();
    this.observer?.disconnect();
    for (const url of this.urls.values()) URL.revokeObjectURL(url);
    this.urls.clear();
    this.comic.close();
    this.done();
  }

  // Shows the bars for a moment, then gets them out of the way.
  flashUi() {
    this.el.classList.remove('ui-hidden');
    clearTimeout(this.uiTimer);
    this.uiTimer = setTimeout(() => this.el.classList.add('ui-hidden'), 2500);
  }

  toggleUi() {
    clearTimeout(this.uiTimer);
    this.el.classList.toggle('ui-hidden');
  }

  setMode(mode) {
    this.settings.mode = mode;
    saveSettings(this.settings);
    this.render();
    this.flashUi();
  }

  render() {
    const { mode, manga } = this.settings;
    this.el.querySelectorAll('[data-act=page],[data-act=webtoon]').forEach((b) => b.classList.toggle('on', b.dataset.act === mode));
    $('[data-act=manga]', this.el).classList.toggle('on', manga);
    $('[data-act=manga]', this.el).hidden = mode === 'webtoon';
    const slider = $('.reader-slider', this.el);
    slider.style.direction = manga && mode !== 'webtoon' ? 'rtl' : 'ltr';
    this.observer?.disconnect();
    this.pagesEl.innerHTML = '';
    this.pagesEl.className = `reader-pages ${mode}`;
    $('.reader-hint', this.el).textContent = mode === 'webtoon'
      ? 'Scroll down to read. Tap to show or hide the controls.'
      : 'Swipe or tap the sides to turn. Pinch or double-tap the middle to zoom.';
    if (mode === 'webtoon') this.buildStrip();
    else this.buildPager();
    this.showPlace();
  }

  showPlace() {
    $('.reader-count', this.el).textContent = `${this.index + 1} / ${this.count}`;
    $('.reader-slider', this.el).value = String(this.index + 1);
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.savePlace(), 700);
  }

  savePlace() {
    clearTimeout(this.saveTimer);
    savePlace(this.book.id, this.index, this.count).catch(() => {});
  }

  jump(i) {
    this.index = Math.max(0, Math.min(this.count - 1, i));
    if (this.settings.mode === 'webtoon') {
      const img = this.strip.children[this.index];
      if (img) this.strip.scrollTop = img.offsetTop;
    } else {
      this.fillSlots();
    }
    this.showPlace();
  }

  // ---------- getting pages ready ----------

  url(i) {
    if (i < 0 || i >= this.count) return Promise.resolve(null);
    if (this.urls.has(i)) {
      this.order = this.order.filter((n) => n !== i).concat(i);
      return Promise.resolve(this.urls.get(i));
    }
    if (this.loading.has(i)) return this.loading.get(i);
    const p = this.comic.getPage(i, this.width)
      .then((blob) => {
        const url = URL.createObjectURL(blob);
        this.urls.set(i, url);
        this.order.push(i);
        this.trim();
        return url;
      })
      .catch(() => null)
      .finally(() => this.loading.delete(i));
    this.loading.set(i, p);
    return p;
  }

  // Keeps memory down: pages far from where you are are let go.
  trim() {
    const max = this.settings.mode === 'webtoon' ? 24 : 8;
    while (this.order.length > max) {
      const i = this.order.shift();
      const url = this.urls.get(i);
      this.urls.delete(i);
      for (const img of this.el.querySelectorAll(`img[data-i="${i}"]`)) {
        if (img.src === url) {
          if (this.settings.mode === 'webtoon') {
            img.style.height = `${img.offsetHeight}px`;
            img.classList.add('pending');
          }
          img.removeAttribute('src');
          img.dataset.loaded = '';
        }
      }
      URL.revokeObjectURL(url);
    }
  }

  // ---------- page mode ----------

  buildPager() {
    const track = document.createElement('div');
    track.className = 'reader-track';
    this.slots = [0, 1, 2].map(() => {
      const slot = document.createElement('div');
      slot.className = 'reader-slot';
      const img = document.createElement('img');
      img.alt = '';
      img.draggable = false;
      slot.appendChild(img);
      track.appendChild(slot);
      return slot;
    });
    this.track = track;
    this.pagesEl.appendChild(track);
    this.placeSlots();
    this.fillSlots();
    this.attachTouch();
  }

  placeSlots() {
    this.slots.forEach((slot, k) => (slot.style.transform = `translateX(${(k - 1) * 100}%)`));
  }

  // The page shown left (-1), in the middle (0) or right (1) of the current one.
  pageAt(visual) {
    return this.index + (this.settings.manga ? -visual : visual);
  }

  async fillSlots() {
    this.resetZoom();
    const want = [-1, 0, 1].map((v) => this.pageAt(v));
    this.slots.forEach((slot, k) => this.fillSlot(slot, want[k]));
    // And a couple further on.
    for (const n of [this.index + 2, this.index - 2]) this.url(n);
  }

  async fillSlot(slot, i) {
    const img = slot.firstChild;
    img.dataset.i = String(i);
    slot.dataset.i = String(i);
    if (i < 0 || i >= this.count) {
      img.removeAttribute('src');
      slot.classList.add('blank');
      return;
    }
    slot.classList.remove('blank');
    const url = await this.url(i);
    if (slot.dataset.i === String(i) && url && img.src !== url) img.src = url;
  }

  // Turns one page left (-1) or right (1) as you see it, with a slide.
  turnVisual(dir, fromOffset = 0) {
    const target = this.pageAt(dir);
    if (target < 0 || target >= this.count) {
      this.slide(0);
      if (target >= this.count) this.showEnd();
      return;
    }
    const w = this.pagesEl.clientWidth;
    this.track.style.transition = 'transform .22s cubic-bezier(.2,.7,.3,1)';
    this.track.style.transform = `translateX(${-dir * w}px)`;
    const finish = () => {
      clearTimeout(timer);
      this.track.removeEventListener('transitionend', finish);
      this.track.style.transition = 'none';
      this.track.style.transform = 'translateX(0)';
      this.index = target;
      // The slot that's now in the middle already shows the right page.
      if (dir > 0) this.slots.push(this.slots.shift());
      else this.slots.unshift(this.slots.pop());
      this.placeSlots();
      this.fillSlots();
      this.showPlace();
    };
    const timer = setTimeout(finish, 320);
    this.track.addEventListener('transitionend', finish);
    void fromOffset;
  }

  slide(px, animate = true) {
    this.track.style.transition = animate ? 'transform .2s ease-out' : 'none';
    this.track.style.transform = `translateX(${px}px)`;
  }

  showEnd() {
    const hint = $('.reader-hint', this.el);
    hint.textContent = "That's the last page.";
    this.flashUi();
  }

  resetZoom() {
    this.zoom = { s: 1, x: 0, y: 0 };
    this.applyZoom();
  }

  applyZoom() {
    const img = this.slots?.[1]?.firstChild;
    if (!img) return;
    const { s, x, y } = this.zoom;
    img.style.transform = s === 1 ? '' : `translate(${x}px, ${y}px) scale(${s})`;
  }

  clampZoom() {
    const z = this.zoom;
    z.s = Math.max(1, Math.min(4, z.s));
    const w = this.pagesEl.clientWidth;
    const h = this.pagesEl.clientHeight;
    const maxX = (w * (z.s - 1)) / 2;
    const maxY = (h * (z.s - 1)) / 2;
    z.x = Math.max(-maxX, Math.min(maxX, z.x));
    z.y = Math.max(-maxY, Math.min(maxY, z.y));
    if (z.s === 1) z.x = z.y = 0;
  }

  attachTouch() {
    const area = this.pagesEl;
    let start = null;
    let pinch = null;
    let lastTap = 0;
    const dist = (t) => Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);

    area.addEventListener('touchstart', (e) => {
      if (e.touches.length === 2) {
        pinch = { d: dist(e.touches), s: this.zoom.s };
        start = null;
        return;
      }
      const t = e.touches[0];
      start = { x: t.clientX, y: t.clientY, time: Date.now(), zx: this.zoom.x, zy: this.zoom.y, moved: false };
    }, { passive: true });

    area.addEventListener('touchmove', (e) => {
      e.preventDefault();
      if (pinch && e.touches.length === 2) {
        this.zoom.s = pinch.s * (dist(e.touches) / pinch.d);
        this.clampZoom();
        this.applyZoom();
        return;
      }
      if (!start) return;
      const t = e.touches[0];
      const dx = t.clientX - start.x;
      const dy = t.clientY - start.y;
      if (Math.abs(dx) > 8 || Math.abs(dy) > 8) start.moved = true;
      if (this.zoom.s > 1) {
        this.zoom.x = start.zx + dx;
        this.zoom.y = start.zy + dy;
        this.clampZoom();
        this.applyZoom();
      } else if (start.moved) {
        this.slide(dx, false);
      }
    }, { passive: false });

    area.addEventListener('touchend', (e) => {
      if (pinch) {
        if (e.touches.length === 0) pinch = null;
        return;
      }
      if (!start) return;
      const t = e.changedTouches[0];
      const dx = t.clientX - start.x;
      const elapsed = Date.now() - start.time;
      const w = area.clientWidth;
      if (start.moved) {
        if (this.zoom.s === 1) {
          const fast = Math.abs(dx) > 30 && Math.abs(dx) / elapsed > 0.4;
          if (Math.abs(dx) > w * 0.22 || fast) this.turnVisual(dx < 0 ? 1 : -1);
          else this.slide(0);
        }
        start = null;
        return;
      }
      start = null;
      this.tap(t.clientX, t.clientY, lastTap);
      lastTap = Date.now();
    });

    // Mouse and trackpad (for a computer browser).
    area.addEventListener('click', (e) => {
      if (e.pointerType === 'touch' || this.touchSeen) return;
      this.tap(e.clientX, e.clientY, 0);
    });
    area.addEventListener('touchstart', () => (this.touchSeen = true), { once: true, passive: true });
  }

  tap(x, y, lastTap) {
    const rect = this.pagesEl.getBoundingClientRect();
    const third = rect.width / 3;
    const middle = x - rect.left > third && x - rect.left < 2 * third;
    if (middle) {
      if (Date.now() - lastTap < 320) {
        // Double tap: zoom in where you tapped, or back out.
        if (this.zoom.s > 1) this.resetZoom();
        else {
          this.zoom.s = 2.5;
          this.zoom.x = (rect.left + rect.width / 2 - x) * 1.5;
          this.zoom.y = (rect.top + rect.height / 2 - y) * 1.5;
          this.clampZoom();
          this.applyZoom();
        }
      }
      this.toggleUi();
      return;
    }
    if (this.zoom.s > 1) return;
    this.turnVisual(x - rect.left < third ? -1 : 1);
  }

  // ---------- webtoon mode ----------

  buildStrip() {
    const strip = document.createElement('div');
    strip.className = 'reader-strip';
    for (let i = 0; i < this.count; i++) {
      const img = document.createElement('img');
      img.alt = '';
      img.dataset.i = String(i);
      img.draggable = false;
      img.className = 'pending';
      img.addEventListener('load', () => {
        // A page above you that's a different height than guessed would make the strip jump.
        const before = img.offsetHeight;
        img.style.height = '';
        img.classList.remove('pending');
        const after = img.offsetHeight;
        if (img.offsetTop < strip.scrollTop && after !== before) strip.scrollTop += after - before;
      });
      strip.appendChild(img);
    }
    this.strip = strip;
    this.pagesEl.appendChild(strip);
    this.observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        const img = entry.target;
        const i = Number(img.dataset.i);
        if (img.dataset.loaded) continue;
        img.dataset.loaded = '1';
        this.url(i).then((url) => {
          if (url && img.isConnected) img.src = url;
        });
      }
    }, { root: strip, rootMargin: '150% 0px' });
    for (const img of strip.children) this.observer.observe(img);
    let ticking = false;
    strip.addEventListener('scroll', () => {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(() => {
        ticking = false;
        const at = strip.scrollTop + strip.clientHeight / 3;
        let i = this.index;
        // Pages are in order, so walk from the current one.
        while (i < this.count - 1 && strip.children[i + 1].offsetTop <= at) i++;
        while (i > 0 && strip.children[i].offsetTop > at) i--;
        if (i !== this.index) {
          this.index = i;
          this.showPlace();
        }
        if (strip.scrollTop + strip.clientHeight >= strip.scrollHeight - 4 && this.index !== this.count - 1) {
          this.index = this.count - 1;
          this.showPlace();
        }
        // Images let go of earlier are fetched again when you come back to them.
        for (const img of strip.children) {
          if (!img.dataset.loaded && Math.abs(img.offsetTop - strip.scrollTop) < strip.clientHeight * 2) {
            this.observer.unobserve(img);
            this.observer.observe(img);
          }
        }
      });
    }, { passive: true });
    strip.addEventListener('click', () => this.toggleUi());
    requestAnimationFrame(() => {
      const img = strip.children[this.index];
      if (img) strip.scrollTop = img.offsetTop;
    });
  }
}
