// Sound effects (SCRUM-161 "Lydeffekter", first used by the lobby in SCRUM-162). Called from SoundEffects.cs.
//
// The sounds are synthesized with the Web Audio API instead of loaded from files: short 8-bit
// triangle/sine bleeps plus filtered noise for cards on the table, which fits the retro card-table
// theme and needs no assets or licences.
//
// This is the shared player for every sound effect. Volume and mute live in localStorage, so the
// settings menu (SCRUM-166) only has to call setVolume / setMuted; the player reads them on every play.

const VOLUME_KEY = "slopster.sound.volume";
const MUTED_KEY = "slopster.sound.muted";
const DEFAULT_VOLUME = 0.6;
// The sounds are mixed at half strength, so even 100 % on the volume slider stays below the song.
const MASTER = 0.5;

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
// Triangle by default: rounder than the square wave, which sounded shrill.
function tone(c, out, { type = "triangle", freq, to, at = 0, dur, gain = 0.18 }) {
    const t = c.currentTime + at;
    const osc = c.createOscillator();
    const env = c.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (to) osc.frequency.exponentialRampToValueAtTime(to, t + dur);
    env.gain.setValueAtTime(0.0001, t);
    env.gain.exponentialRampToValueAtTime(gain, t + 0.008);
    env.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(env).connect(out);
    osc.start(t);
    osc.stop(t + dur + 0.03);
}

// A short burst of filtered noise: a card laid on the table, a riffle, a stamp's click.
// Around 1.1 kHz it sounds like cardboard rather than a sharp click.
function swish(c, out, { at = 0, dur, freq = 1100, q = 0.8, gain = 0.25 }) {
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
    src.stop(t + dur + 0.03);
}

const SOUNDS = {
    // A card is laid on the table, then a friendly rising two-note bleep.
    lobbyJoin(c, out) {
        swish(c, out, { dur: 0.06, gain: 0.3 });
        tone(c, out, { freq: 392, at: 0.05, dur: 0.1 });
        tone(c, out, { freq: 523, at: 0.13, dur: 0.16 });
    },
    // The opposite of join: two soft falling notes and no card.
    lobbyLeave(c, out) {
        tone(c, out, { freq: 440, dur: 0.09, gain: 0.14 });
        tone(c, out, { freq: 330, at: 0.08, dur: 0.14, gain: 0.11 });
    },
    // Ready: a stamp (a low thump and a click) and a small blip.
    ready(c, out) {
        tone(c, out, { type: "sine", freq: 130, to: 55, dur: 0.12, gain: 0.35 });
        swish(c, out, { dur: 0.03, freq: 1800, q: 1.5, gain: 0.18 });
        tone(c, out, { freq: 659, at: 0.07, dur: 0.08, gain: 0.1 });
    },
    // Not ready: a light falling click, clearly different from the stamp.
    unready(c, out) {
        swish(c, out, { dur: 0.02, freq: 1500, q: 1.5, gain: 0.12 });
        tone(c, out, { freq: 523, to: 330, at: 0.01, dur: 0.12, gain: 0.13 });
    },
    // The last seat is taken: a discreet chime.
    lobbyFull(c, out) {
        tone(c, out, { type: "sine", freq: 880, dur: 0.35, gain: 0.09 });
        tone(c, out, { type: "sine", freq: 1175, at: 0.1, dur: 0.4, gain: 0.07 });
    },
    // Everyone is ready: a rising arpeggio.
    allReady(c, out) {
        [392, 494, 587, 784].forEach((freq, i) => tone(c, out, { freq, at: i * 0.08, dur: 0.12, gain: 0.13 }));
    },
    // The game starts: the deck is shuffled, then a short fanfare leads into round one.
    gameStart(c, out) {
        for (let i = 0; i < 8; i++) swish(c, out, { at: i * 0.05, dur: 0.04, freq: 1000 + (i % 2) * 300, gain: 0.22 });
        tone(c, out, { freq: 523, at: 0.45, dur: 0.12, gain: 0.13 });
        tone(c, out, { freq: 784, at: 0.56, dur: 0.3, gain: 0.13 });
        tone(c, out, { type: "sine", freq: 262, at: 0.56, dur: 0.3, gain: 0.12 });
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

    // A low-pass filter over each sound takes off the sharp treble.
    const out = c.createGain();
    const soften = c.createBiquadFilter();
    soften.type = "lowpass";
    soften.frequency.value = 2600;
    soften.Q.value = 0.5;
    out.gain.value = volume * MASTER;
    out.connect(soften).connect(c.destination);
    sound(c, out);
    return true;
}
