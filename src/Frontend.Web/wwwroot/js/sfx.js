// Sound effects (SCRUM-161 "Lydeffekter", first used by the lobby in SCRUM-162). Called from SoundEffects.cs.
//
// The sounds are synthesized with the Web Audio API instead of loaded from files: short 8-bit
// square/triangle bleeps plus filtered noise for cards on the table, which fits the retro card-table
// theme and needs no assets or licences.
//
// This is the shared player for every sound effect. Volume and mute live in localStorage, so the
// settings menu (SCRUM-166) only has to call setVolume / setMuted; the player reads them on every play.

const VOLUME_KEY = "slopster.sound.volume";
const MUTED_KEY = "slopster.sound.muted";
const DEFAULT_VOLUME = 0.6;

// The same sound again within this many ms is skipped, and at most MAX_BURST sounds start within
// BURST_MS, so many joins at once do not become a wall of sound.
const MIN_GAP_MS = 300;
const BURST_MS = 500;
const MAX_BURST = 3;

let ctx = null;
let noiseBuffer = null;
const lastPlayed = new Map();
let recent = [];

function context() {
    if (!ctx) {
        const AudioCtx = window.AudioContext || window.webkitAudioContext;
        if (!AudioCtx) return null;
        ctx = new AudioCtx();
    }
    if (ctx.state === "suspended") ctx.resume().catch(() => { });
    return ctx;
}

// Browsers only allow audio after a click or key press; the first one unlocks the context.
function unlock() {
    context();
    window.removeEventListener("pointerdown", unlock);
    window.removeEventListener("keydown", unlock);
}
window.addEventListener("pointerdown", unlock);
window.addEventListener("keydown", unlock);

function read(key) {
    try { return localStorage.getItem(key); } catch { return null; }
}

function write(key, value) {
    try { localStorage.setItem(key, value); } catch { /* private mode: setting lasts this page only */ }
}

export function getSettings() {
    const stored = parseFloat(read(VOLUME_KEY));
    const volume = Number.isFinite(stored) ? Math.min(1, Math.max(0, stored)) : DEFAULT_VOLUME;
    return { volume, muted: read(MUTED_KEY) === "true" };
}

export function setVolume(volume) {
    write(VOLUME_KEY, String(Math.min(1, Math.max(0, volume))));
}

export function setMuted(muted) {
    write(MUTED_KEY, muted ? "true" : "false");
}

function noise(c) {
    if (!noiseBuffer) {
        noiseBuffer = c.createBuffer(1, c.sampleRate * 0.5, c.sampleRate);
        const data = noiseBuffer.getChannelData(0);
        for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
    }
    return noiseBuffer;
}

// One note: wave type, frequency (optionally sliding to `to`), start offset and length in seconds.
function tone(c, out, { type = "square", freq, to, at = 0, dur, gain = 0.2 }) {
    const t = c.currentTime + at;
    const osc = c.createOscillator();
    const env = c.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (to) osc.frequency.exponentialRampToValueAtTime(to, t + dur);
    env.gain.setValueAtTime(0.0001, t);
    env.gain.exponentialRampToValueAtTime(gain, t + 0.005);
    env.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(env).connect(out);
    osc.start(t);
    osc.stop(t + dur + 0.02);
}

// A short burst of filtered noise: a card laid on the table, a riffle, a stamp's click.
function swish(c, out, { at = 0, dur, freq = 2000, q = 1, gain = 0.3 }) {
    const t = c.currentTime + at;
    const src = c.createBufferSource();
    const filter = c.createBiquadFilter();
    const env = c.createGain();
    src.buffer = noise(c);
    filter.type = "bandpass";
    filter.frequency.value = freq;
    filter.Q.value = q;
    env.gain.setValueAtTime(gain, t);
    env.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    src.connect(filter).connect(env).connect(out);
    src.start(t);
    src.stop(t + dur + 0.02);
}

const SOUNDS = {
    // A card is laid on the table, then a friendly rising two-note bleep.
    lobbyJoin(c, out) {
        swish(c, out, { dur: 0.07, freq: 1800, gain: 0.35 });
        tone(c, out, { freq: 659, at: 0.05, dur: 0.08 });
        tone(c, out, { freq: 880, at: 0.12, dur: 0.12 });
    },
    // The opposite of join: a softer, shorter falling bleep and no card.
    lobbyLeave(c, out) {
        tone(c, out, { type: "triangle", freq: 523, at: 0, dur: 0.07, gain: 0.18 });
        tone(c, out, { type: "triangle", freq: 349, at: 0.07, dur: 0.1, gain: 0.14 });
    },
    // Ready: a stamp (a low thump and a click) and a bright blip.
    ready(c, out) {
        tone(c, out, { freq: 150, to: 60, dur: 0.09, gain: 0.3 });
        swish(c, out, { dur: 0.03, freq: 3500, q: 2, gain: 0.25 });
        tone(c, out, { freq: 1047, at: 0.06, dur: 0.06, gain: 0.12 });
    },
    // Not ready: a light falling click, clearly different from the stamp.
    unready(c, out) {
        swish(c, out, { dur: 0.02, freq: 2500, q: 2, gain: 0.15 });
        tone(c, out, { type: "triangle", freq: 784, to: 392, at: 0.01, dur: 0.1, gain: 0.16 });
    },
    // The last seat is taken: a quiet two-note chime.
    lobbyFull(c, out) {
        tone(c, out, { type: "triangle", freq: 1319, dur: 0.25, gain: 0.1 });
        tone(c, out, { type: "triangle", freq: 1760, at: 0.09, dur: 0.3, gain: 0.08 });
    },
    // Everyone is ready: a rising arpeggio.
    allReady(c, out) {
        [523, 659, 784, 1047].forEach((freq, i) => tone(c, out, { freq, at: i * 0.07, dur: 0.09, gain: 0.15 }));
    },
    // The game starts: the deck is shuffled, then a short fanfare leads into round one.
    gameStart(c, out) {
        for (let i = 0; i < 8; i++) swish(c, out, { at: i * 0.045, dur: 0.035, freq: 2200 + (i % 2) * 600, gain: 0.25 });
        tone(c, out, { freq: 784, at: 0.42, dur: 0.1, gain: 0.15 });
        tone(c, out, { freq: 1047, at: 0.52, dur: 0.22, gain: 0.15 });
        tone(c, out, { type: "triangle", freq: 523, at: 0.52, dur: 0.22, gain: 0.12 });
    },
};

// Plays one sound by name. Returns false when it was skipped (muted, throttled, unknown or no audio).
export function play(name) {
    const sound = SOUNDS[name];
    if (!sound) return false;

    const { volume, muted } = getSettings();
    if (muted || volume <= 0) return false;

    const now = performance.now();
    if (now - (lastPlayed.get(name) ?? -Infinity) < MIN_GAP_MS) return false;
    recent = recent.filter(t => now - t < BURST_MS);
    if (recent.length >= MAX_BURST) return false;

    const c = context();
    if (!c) return false;

    lastPlayed.set(name, now);
    recent.push(now);

    const out = c.createGain();
    out.gain.value = volume;
    out.connect(c.destination);
    sound(c, out);
    return true;
}
