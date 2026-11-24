// Song playback for the game page (SCRUM-147, used by SCRUM-112). Called from GameAudio.cs.
//
// prefetch downloads a song into memory (a blob: URL), so the round can start for everyone at the
// same moment without waiting for the network. The CDN (media.munkcloud.dk) must allow CORS for
// the frontend's origin. One <audio> element is reused, so a click that unlocks autoplay keeps working.

let audio = null;

function player() {
    if (!audio) {
        audio = new Audio();
        audio.preload = "auto";
    }
    return audio;
}

// Returns a blob: URL, or null if the download failed.
export async function prefetch(url) {
    try {
        const response = await fetch(url, { mode: "cors" });
        if (!response.ok) return null;
        return URL.createObjectURL(await response.blob());
    } catch {
        return null;
    }
}

// Plays from offsetSeconds. Returns false if the browser blocked autoplay (NotAllowedError).
export async function play(source, offsetSeconds) {
    const a = player();
    a.pause();
    if (a.src !== source) a.src = source;
    await metadataLoaded(a);
    a.currentTime = Math.max(0, offsetSeconds);
    try {
        await a.play();
        return true;
    } catch (e) {
        if (e && e.name === "NotAllowedError") return false;
        throw e;
    }
}

export function stop() {
    if (audio) audio.pause();
}

export function revoke(url) {
    if (typeof url === "string" && url.startsWith("blob:")) URL.revokeObjectURL(url);
}

export function setVolume(volume) {
    player().volume = Math.min(1, Math.max(0, volume));
}

// currentTime can only be set once the duration is known.
function metadataLoaded(a) {
    if (a.readyState >= 1) return Promise.resolve();
    return new Promise((resolve, reject) => {
        const done = () => { cleanup(); resolve(); };
        const failed = () => { cleanup(); reject(new Error("The song could not be loaded.")); };
        const cleanup = () => {
            a.removeEventListener("loadedmetadata", done);
            a.removeEventListener("error", failed);
        };
        a.addEventListener("loadedmetadata", done);
        a.addEventListener("error", failed);
    });
}
