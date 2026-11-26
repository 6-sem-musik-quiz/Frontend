// Sound effects for the whole app (SCRUM-161 "Lydeffekter"). Called from SoundEffects.cs, hand.js and
// MainLayout (installUi).
//
// The sounds are recorded CC0 sounds from Kenney (kenney.nl: Casino Audio, Interface Sounds, UI Audio;
// see wwwroot/sounds/LICENSE-kenney.txt): cards, chips and soft clicks that fit the card-table theme.
// They were cut, levelled to the same peak and low-passed at 5.5 kHz so none of them is shrill.
//
// Volume and mute live in localStorage, so the settings menu (SCRUM-166) only has to call
// setVolume / setMuted; the player reads them on every play.

const VOLUME_KEY = "slopster.sound.volume";
const MUTED_KEY = "slopster.sound.muted";
const DEFAULT_VOLUME = 0.6;
// Effects are mixed at half strength, so even 100 % stays below the round's song.
const MASTER = 0.5;

// Per sound: how loud next to the others, and the shortest time between two plays of it.
// The game's sounds go above 1: they play on top of the round's song and would drown otherwise.
const SOUNDS = {
    // Lobby (SCRUM-162)
    lobbyJoin: { gain: 1, gap: 300 },
    lobbyLeave: { gain: 0.8, gap: 300 },
    ready: { gain: 1, gap: 250 },
    unready: { gain: 0.8, gap: 250 },
    lobbyFull: { gain: 0.7, gap: 1000 },
    allReady: { gain: 1, gap: 1000 },
    gameStart: { gain: 0.9, gap: 2000 },
    // Front page, buttons and navigation (SCRUM-163)
    click: { gain: 0.6, gap: 60 },
    hover: { gain: 0.15, gap: 80 },
    navigate: { gain: 0.6, gap: 400 },
    // Cards in the hand (SCRUM-164)
    // Dragging the card: short standard UI sounds (clicks, not card sounds), so they do not distract.
    cardPickup: { gain: 1.8, gap: 150 },
    cardMove: { gain: 1.2, gap: 90 },
    cardPlace: { gain: 1.8, gap: 150 },
    lockIn: { gain: 1.8, gap: 300 },
    // The round
    tick: { gain: 1.0, gap: 500 },
    timeUp: { gain: 1.5, gap: 1000 },
    // Results (SCRUM-165)
    correct: { gain: 1.8, gap: 500 },
    wrong: { gain: 1.6, gap: 500 },
    winner: { gain: 1.8, gap: 2000 },
    gameOver: { gain: 1.6, gap: 2000 },
};

// At most MAX_BURST sounds start within BURST_MS, so many events at once are not a wall of sound.
const BURST_MS = 500;
const MAX_BURST = 4;

let ctx = null;
const buffers = new Map();   // name -> Promise<AudioBuffer | null>
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

function load(name) {
    if (!buffers.has(name)) {
        const c = context();
        buffers.set(name, !c ? Promise.resolve(null) :
            fetch(`./sounds/${name}.mp3`)
                .then(r => r.ok ? r.arrayBuffer() : Promise.reject(r.status))
                .then(data => c.decodeAudioData(data))
                .catch(() => null));
    }
    return buffers.get(name);
}

// Browsers only allow audio after a click or key press; the first one unlocks the context and
// loads all sounds (about 170 kB), so the first real sound is not late.
function unlock() {
    if (!context()) return;
    Object.keys(SOUNDS).forEach(load);
    window.removeEventListener("pointerdown", unlock, true);
    window.removeEventListener("keydown", unlock, true);
}
window.addEventListener("pointerdown", unlock, true);
window.addEventListener("keydown", unlock, true);

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

export function getVolume() {
    return getSettings().volume;
}

export function setVolume(volume) {
    write(VOLUME_KEY, String(Math.min(1, Math.max(0, volume))));
}

export function getMuted() {
    return getSettings().muted;
}

export function setMuted(muted) {
    write(MUTED_KEY, muted ? "true" : "false");
}

// Plays one sound by name. Returns false when it was skipped (muted, throttled, unknown or no audio).
export function play(name) {
    const sound = SOUNDS[name];
    if (!sound) return false;

    const { volume, muted } = getSettings();
    if (muted || volume <= 0) return false;

    const now = performance.now();
    if (now - (lastPlayed.get(name) ?? -Infinity) < sound.gap) return false;
    recent = recent.filter(t => now - t < BURST_MS);
    if (recent.length >= MAX_BURST) return false;

    const c = context();
    if (!c || c.state !== "running") return false;

    lastPlayed.set(name, now);
    recent.push(now);

    load(name).then(buffer => {
        if (!buffer) return;
        const src = c.createBufferSource();
        const gain = c.createGain();
        src.buffer = buffer;
        gain.gain.value = volume * MASTER * sound.gain;
        src.connect(gain).connect(c.destination);
        src.start();
    });
    return true;
}

// ---- Hover and click on every button and link in the app ----
//
// One delegated listener for the whole page instead of code in every component. An element can
// opt out with data-sfx="none" (also on a parent, e.g. the game's hand, which has its own card
// sounds), or choose another click sound with data-sfx="<name>". Links make no click sound: the
// page change has its own sound (MainLayout). Form fields (dropdowns, checkboxes, text fields and
// their labels) make no hover sound, only buttons and links do.

const INTERACTIVE = "button, a[href], [role=button], .btn, summary, select, label[for], input[type=checkbox], input[type=radio]";
let installed = false;
let hovered = null;

function target(e) {
    const el = e.target instanceof Element ? e.target.closest(INTERACTIVE) : null;
    if (!el || el.matches(":disabled, [aria-disabled=true]")) return null;
    if (el.closest("[data-sfx=none]")) return null;
    return el;
}

const FORM_FIELD = "select, input, textarea, label";

export function installUi() {
    if (installed) return;
    installed = true;

    document.addEventListener("pointerover", e => {
        if (e.pointerType !== "mouse") return; // no hover sounds on touch
        const el = target(e);
        if (el === hovered) return;
        hovered = el;
        if (el && !el.matches(FORM_FIELD)) play("hover");
    });

    document.addEventListener("click", e => {
        const el = target(e);
        if (!el) return;
        const custom = el.closest("[data-sfx]")?.getAttribute("data-sfx");
        if (custom) play(custom);
        else if (!el.matches("a[href]")) play("click");
    });
}
