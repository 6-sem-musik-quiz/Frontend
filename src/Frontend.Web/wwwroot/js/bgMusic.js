// Quiet background music on the front page and in the lobby (SCRUM-167). Called from BackgroundMusic.cs.
//
// One looping <audio> element. Its volume is a small fraction (LEVEL) of the "Musik" setting, which is
// the round's song volume, so the menu music is always far below the game's song. "Musik" at 0 turns
// it off. It fades in and out instead of starting or stopping abruptly, and only starts after the
// first click or key press, because browsers block sound until then.

const MUSIC_KEY = "slopster.volume"; // GamePage.VolumeKey: the "Musik" setting
const DEFAULT_MUSIC = 0.8;           // the game page's default
const LEVEL = 0.12;                  // menu music = 12 % of "Musik": low, in the background

let audio = null;
let wanted = false;
let unlocked = false;
let fade = null;

function musicSetting() {
    try {
        const v = parseFloat(localStorage.getItem(MUSIC_KEY));
        return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : DEFAULT_MUSIC;
    } catch {
        return DEFAULT_MUSIC;
    }
}

function element() {
    if (!audio) {
        audio = new Audio("./music/menu.mp3");
        audio.loop = true;
        audio.preload = "none"; // nothing is downloaded until it is first played
        audio.volume = 0;
    }
    return audio;
}

function fadeTo(target, ms, done) {
    clearInterval(fade);
    const a = element();
    const start = a.volume, steps = Math.max(1, Math.round(ms / 50));
    let i = 0;
    fade = setInterval(() => {
        i++;
        a.volume = Math.min(1, Math.max(0, start + (target - start) * (i / steps)));
        if (i >= steps) {
            clearInterval(fade);
            if (done) done();
        }
    }, 50);
}

async function apply() {
    const target = musicSetting() * LEVEL;
    if (wanted && unlocked && target > 0) {
        const a = element();
        if (a.paused) {
            a.volume = 0;
            try { await a.play(); } catch { return; } // still blocked: the next click tries again
        }
        fadeTo(target, 1500);
    } else if (audio && !audio.paused) {
        fadeTo(0, 800, () => audio.pause());
    }
}

function unlock() {
    unlocked = true;
    window.removeEventListener("pointerdown", unlock, true);
    window.removeEventListener("keydown", unlock, true);
    apply();
}
window.addEventListener("pointerdown", unlock, true);
window.addEventListener("keydown", unlock, true);

// true on the pages with menu music, false elsewhere (the game has its own song).
export function setWanted(on) {
    wanted = !!on;
    apply();
}

// After the "Musik" setting changed.
export function refreshVolume() {
    apply();
}
