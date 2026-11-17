// Swipe a task row: right to complete it, left to move it to tomorrow (rows with data-swipe-left).
// Rows carry data-swipe-id; the visible part that slides is the row's .swipe-surface child.
window.nxtTaskSwipe = (() => {
    const threshold = 80;

    function attach(container, dotnet) {
        if (!container || container._nxtSwipe) return;
        let row = null, surface = null, startX = 0, startY = 0, dx = 0, horizontal = null;

        const reset = () => {
            if (surface) {
                surface.style.transition = "transform .2s ease";
                surface.style.transform = "";
            }
            if (row) { delete row.dataset.swiping; row.classList.remove("swipe-ready"); }
            row = surface = null; horizontal = null; dx = 0;
        };

        const start = e => {
            const target = e.target;
            const candidate = target.closest("[data-swipe-id]");
            if (!candidate || !container.contains(candidate) || target.closest("input, button, a, textarea")) return;
            row = candidate;
            surface = row.querySelector(".swipe-surface") || row;
            startX = e.touches[0].clientX; startY = e.touches[0].clientY; dx = 0; horizontal = null;
            surface.style.transition = "none";
        };

        const move = e => {
            if (!row) return;
            const x = e.touches[0].clientX - startX, y = e.touches[0].clientY - startY;
            if (horizontal === null) {
                if (Math.abs(x) > 10 && Math.abs(x) > Math.abs(y) * 1.5) horizontal = true;
                else if (Math.abs(y) > 10) { horizontal = false; reset(); return; }
                else return;
            }
            dx = x;
            if (dx < 0 && !("swipeLeft" in row.dataset)) dx = 0;
            if (dx > 0 && !("swipeRight" in row.dataset)) dx = 0;
            surface.style.transform = `translateX(${dx}px)`;
            row.dataset.swiping = dx > 0 ? "right" : dx < 0 ? "left" : "";
            row.classList.toggle("swipe-ready", Math.abs(dx) >= threshold);
            if (e.cancelable) e.preventDefault();
        };

        const end = () => {
            if (!row) return;
            const id = row.dataset.swipeId, direction = dx > 0 ? "right" : "left", done = horizontal && Math.abs(dx) >= threshold;
            reset();
            if (done) {
                if (navigator.vibrate) navigator.vibrate(12);
                dotnet.invokeMethodAsync("OnSwipe", id, direction);
            }
        };

        container.addEventListener("touchstart", start, { passive: true });
        container.addEventListener("touchmove", move, { passive: false });
        container.addEventListener("touchend", end);
        container.addEventListener("touchcancel", reset);
        container._nxtSwipe = { start, move, end, reset };
    }

    function detach(container) {
        const handlers = container && container._nxtSwipe;
        if (!handlers) return;
        container.removeEventListener("touchstart", handlers.start);
        container.removeEventListener("touchmove", handlers.move);
        container.removeEventListener("touchend", handlers.end);
        container.removeEventListener("touchcancel", handlers.reset);
        delete container._nxtSwipe;
    }

    // A short buzz where supported (Android); iOS ignores it.
    function tick() { if (navigator.vibrate) navigator.vibrate(10); }

    return { attach, detach, tick };
})();
