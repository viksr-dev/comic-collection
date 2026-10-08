// Camera barcode scanner using zxing-wasm, which (unlike the browser's built-in
// detector) can read the small 5-digit add-on next to a comic's UPC.

const ZX = globalThis.ZXingWASM;
ZX.prepareZXingModule({
  overrides: {
    locateFile: (path, prefix) =>
      path.endsWith('.wasm') ? new URL(`vendor/${path}`, document.baseURI).href : prefix + path,
  },
});

const READ_OPTIONS = {
  formats: ['EANUPC'],
  eanAddOnSymbol: 'Read',
  tryHarder: true,
  maxNumberOfSymbols: 1,
};

// How long to keep looking for the add-on once the main barcode has been read.
const ADDON_GRACE_MS = 2500;
const FRAME_INTERVAL_MS = 120;
const REPEAT_GAP_MS = 4000;

export class Scanner {
  constructor(video) {
    this.video = video;
    this.canvas = document.createElement('canvas');
    this.ctx = this.canvas.getContext('2d', { willReadFrequently: true });
    this.stream = null;
    this.running = false;
  }

  // continuous: keep scanning after each read (for scanning a stack of comics),
  // ignoring the same barcode for a few seconds so it isn't counted twice.
  async start(onResult, onBaseOnly, { continuous = false } = {}) {
    this.stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: {
        facingMode: { ideal: 'environment' },
        width: { ideal: 1920 },
        height: { ideal: 1080 },
      },
    });
    this.video.srcObject = this.stream;
    await this.video.play();
    this.track = this.stream.getVideoTracks()[0];
    try {
      await this.track.applyConstraints({ advanced: [{ focusMode: 'continuous' }] });
    } catch {}

    this.running = true;
    this.paused = false;
    let baseRead = null;
    let baseSince = 0;
    const recent = new Map();
    // Treat a code as just read, e.g. after a question about it is answered
    // while the comic is still in front of the camera.
    this.markSeen = (digits) => {
      recent.set(digits, performance.now());
      recent.set(digits.slice(0, 13), performance.now());
    };
    const finish = (digits, gotAddon) => {
      baseRead = null;
      if (!continuous) this.stop();
      else {
        // Remember the full code and the main barcode on its own, since the
        // next frames may only catch the main part.
        recent.set(digits, performance.now());
        recent.set(digits.slice(0, 13), performance.now());
      }
      onResult(digits, gotAddon);
    };
    const seenRecently = (digits) => performance.now() - (recent.get(digits) ?? -1e9) < REPEAT_GAP_MS;

    const loop = async () => {
      if (!this.running) return;
      const started = performance.now();
      if (this.paused) {
        this.timer = setTimeout(loop, FRAME_INTERVAL_MS);
        return;
      }
      try {
        const text = await this.readFrame();
        if (text) {
          const digits = text.replace(/\D/g, '');
          if (seenRecently(digits)) {
            // Same comic still in front of the camera.
          } else if (digits.length > 13) {
            finish(digits, true);
          } else if (!baseRead || baseRead !== digits) {
            baseRead = digits;
            baseSince = started;
            onBaseOnly?.(digits);
          } else if (started - baseSince > ADDON_GRACE_MS) {
            finish(digits, false);
          }
          if (!this.running) return;
        }
      } catch (err) {
        console.warn('scan error', err);
      }
      const wait = Math.max(0, FRAME_INTERVAL_MS - (performance.now() - started));
      this.timer = setTimeout(loop, wait);
    };
    loop();
  }

  async readFrame() {
    const { videoWidth: w, videoHeight: h } = this.video;
    if (!w || !h) return null;
    // Scan a horizontal band through the middle of the picture, scaled down so
    // each frame decodes quickly.
    const bandH = Math.round(h * 0.6);
    const scale = Math.min(1, 1280 / w);
    this.canvas.width = Math.round(w * scale);
    this.canvas.height = Math.round(bandH * scale);
    this.ctx.drawImage(this.video, 0, (h - bandH) / 2, w, bandH, 0, 0, this.canvas.width, this.canvas.height);
    const image = this.ctx.getImageData(0, 0, this.canvas.width, this.canvas.height);
    const results = await ZX.readBarcodes(image, READ_OPTIONS);
    return results.find((r) => r.isValid)?.text || null;
  }

  get torchSupported() {
    return !!this.track?.getCapabilities?.().torch;
  }

  async setTorch(on) {
    await this.track.applyConstraints({ advanced: [{ torch: on }] });
  }

  stop() {
    this.running = false;
    clearTimeout(this.timer);
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
    this.video.srcObject = null;
  }
}

// Decode a still photo, used as a fallback when live scanning struggles.
export async function readImageFile(file) {
  const results = await ZX.readBarcodes(file, READ_OPTIONS);
  return results.find((r) => r.isValid)?.text || null;
}
