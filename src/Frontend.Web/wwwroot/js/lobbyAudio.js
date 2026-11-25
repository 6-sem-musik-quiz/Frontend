// Lobby sound effects (SCRUM-162). All sounds are synthesised with the Web Audio API — no audio files.
// Volume is set from the outside via setVolume(); callers read localStorage before the first call.
//
// Join sounds are throttled: if less than JOIN_THROTTLE_ms has passed since the last one,
// the call is silently ignored. This prevents a wall of sound when several players join at once.

let ctx = null;
let masterVolume = 0.8;
const JOIN_THROTTLE_MS = 400;
let lastJoinTime = -Infinity;

function audio() {
    if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
    if (ctx.state === "suspended") ctx.resume();
    return ctx;
}

// Plays a single oscillator tone. gainValue is relative to masterVolume.
function tone(frequency, type, durationSec, gainValue, startTime) {
    const a = audio();
    const o = a.createOscillator();
    const g = a.createGain();
    const vol = masterVolume * gainValue;
    o.type = type;
    o.frequency.value = frequency;
    o.connect(g);
    g.connect(a.destination);
    g.gain.setValueAtTime(vol, startTime);
    g.gain.exponentialRampToValueAtTime(0.0001, startTime + durationSec);
    o.start(startTime);
    o.stop(startTime + durationSec + 0.01);
}

// Friendly rising tri-tone (door-chime feel).
export function playPlayerJoined() {
    const now = Date.now();
    if (now - lastJoinTime < JOIN_THROTTLE_MS) return;
    lastJoinTime = now;
    const t = audio().currentTime;
    tone(523.25, "sine", 0.25, 0.35, t);        // C5
    tone(659.25, "sine", 0.25, 0.35, t + 0.08); // E5
    tone(783.99, "sine", 0.35, 0.35, t + 0.16); // G5
}

// Shorter, descending counterpart to join.
export function playPlayerLeft() {
    const t = audio().currentTime;
    tone(523.25, "sine", 0.2,  0.2, t);        // C5
    tone(392.00, "sine", 0.25, 0.2, t + 0.08); // G4
}

// Crisp "stamp approved" click – two short square pulses rising.
export function playPlayerReady() {
    const t = audio().currentTime;
    tone(880,    "square", 0.07, 0.18, t);
    tone(1046.5, "square", 0.1,  0.18, t + 0.07);
}

// Softer downward "un-stamp".
export function playPlayerNotReady() {
    const t = audio().currentTime;
    tone(523.25, "square", 0.08, 0.15, t);
    tone(392.00, "square", 0.1,  0.15, t + 0.07);
}

// Rising fanfare – four notes leading into the first round.
export function playAllReady() {
    const t = audio().currentTime;
    tone(523.25, "sine", 0.3, 0.3,  t);
    tone(659.25, "sine", 0.3, 0.3,  t + 0.1);
    tone(783.99, "sine", 0.3, 0.3,  t + 0.2);
    tone(1046.5, "sine", 0.5, 0.35, t + 0.3);
}

// Discreet two-note chime – last seat taken.
export function playLobbyFull() {
    const t = audio().currentTime;
    tone(440,    "sine", 0.15, 0.2, t);
    tone(554.37, "sine", 0.2,  0.2, t + 0.1);
}

export function setVolume(volume) {
    masterVolume = Math.min(1, Math.max(0, volume));
}
