// Tells the sync engine when the app comes back to the foreground or online, and when it is hidden
// (last chance to upload before iOS suspends a home-screen app).
window.nxtTaskSync = {
    register(dotnet) {
        document.addEventListener("visibilitychange", () =>
            dotnet.invokeMethodAsync(document.visibilityState === "visible" ? "OnVisible" : "OnHidden"));
        window.addEventListener("online", () => dotnet.invokeMethodAsync("OnVisible"));
    },
    // Removes the sign-in tokens Supabase puts in the address after a magic link.
    clearHash() {
        history.replaceState(history.state, "", location.pathname + location.search);
    }
};
