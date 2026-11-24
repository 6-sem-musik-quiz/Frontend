// Drag and drop for HandOfCards (SCRUM-148), with Pointer Events, so mouse and touch work the same.
//
// Blazor owns the state; this script only moves the "?" card under the pointer and tells .NET
// which gap ("slot") it is over: DragStarted(), HotSlot(slot) when it changes, Dropped(slot).
// slot is 0..n (n = cards in the hand), or -1 when the pointer is not over the hand.
//
// Elements (set by HandOfCards.razor):
//   [data-drag]   can be grabbed (the "?" above the hand, or the placed "?" in the hand)
//   [data-ghost]  the card that follows the pointer (the "?" above the hand, shown while dragging)
//   [data-hand]   the hand; [data-card] its real cards (not the placed "?")

export function attach(root, dotnet) {
    let drag = null;

    function onDown(e) {
        const grabbed = e.target.closest("[data-drag]");
        if (!grabbed || !root.contains(grabbed) || e.button > 0) return;
        e.preventDefault();
        root.setPointerCapture(e.pointerId);
        drag = { id: e.pointerId, base: null, centers: null, hand: null, hot: -1 };
        dotnet.invokeMethodAsync("DragStarted");
    }

    function onMove(e) {
        if (!drag || e.pointerId !== drag.id) return;
        const ghost = root.querySelector("[data-ghost]");
        if (!ghost) return; // Blazor has not shown it yet

        if (!drag.base) {
            // Measure once, after Blazor took the placed card out of the hand: the gaps' positions
            // without the opened gap, so the target does not jump while the cards slide aside.
            ghost.style.transform = "";
            const g = ghost.getBoundingClientRect();
            drag.base = { x: g.left + g.width / 2, y: g.top + g.height / 2 };
            const hand = root.querySelector("[data-hand]");
            drag.hand = hand.getBoundingClientRect();
            drag.centers = [...hand.querySelectorAll("[data-card]")].map(c => {
                const r = c.getBoundingClientRect();
                return r.left + r.width / 2;
            });
        }

        ghost.style.transform = `translate(${e.clientX - drag.base.x}px, ${e.clientY - drag.base.y}px) rotate(6deg)`;

        const hot = slotAt(e.clientX, e.clientY);
        if (hot !== drag.hot) {
            drag.hot = hot;
            dotnet.invokeMethodAsync("HotSlot", hot);
        }
    }

    function slotAt(x, y) {
        const h = drag.hand;
        if (y < h.top - 40 || y > h.bottom + 40 || x < h.left - 60 || x > h.right + 60) return -1;
        let slot = 0;
        for (const center of drag.centers) if (x > center) slot++;
        return slot;
    }

    function finish(slot) {
        const ghost = root.querySelector("[data-ghost]");
        if (ghost) ghost.style.transform = "";
        try { root.releasePointerCapture(drag.id); } catch { /* already released */ }
        drag = null;
        dotnet.invokeMethodAsync("Dropped", slot);
    }

    function onUp(e) {
        if (drag && e.pointerId === drag.id) finish(drag.hot);
    }

    function onCancel(e) {
        if (drag && e.pointerId === drag.id) finish(-1);
    }

    root.addEventListener("pointerdown", onDown);
    root.addEventListener("pointermove", onMove);
    root.addEventListener("pointerup", onUp);
    root.addEventListener("pointercancel", onCancel);

    return {
        dispose() {
            root.removeEventListener("pointerdown", onDown);
            root.removeEventListener("pointermove", onMove);
            root.removeEventListener("pointerup", onUp);
            root.removeEventListener("pointercancel", onCancel);
        }
    };
}
