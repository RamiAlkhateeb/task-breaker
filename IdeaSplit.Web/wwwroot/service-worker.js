// Lets NxtTask open offline. Pages, styles and scripts come from the network when it is available (so a new
// version shows up straight away) and from the cache otherwise. Build files whose names carry a content hash
// never change, so they are served from the cache first. Other sites (Supabase, Gemini) are never cached.
const CACHE = "nxttask-v1";
const FINGERPRINTED = /\/_framework\/(.+)\.([a-z0-9]{10})\.(wasm|js|dat)$/;

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
self.addEventListener("message", event => { if (event.data && event.data.type === "SKIP_WAITING") self.skipWaiting(); });

self.addEventListener("fetch", event => {
    const request = event.request;
    if (request.method !== "GET") return;
    const url = new URL(request.url);
    if (url.origin !== self.location.origin) return;
    // Update checks must always ask the server.
    if (url.pathname.endsWith("/version.json")) return;
    event.respondWith(FINGERPRINTED.test(url.pathname) ? cacheFirst(request, url) : networkFirst(request));
});

async function cacheFirst(request, url) {
    const cache = await caches.open(CACHE);
    const hit = await cache.match(request);
    if (hit) return hit;
    const response = await fetch(request);
    if (response.ok) {
        await cache.put(request, response.clone());
        await dropOlderVersions(cache, url);
    }
    return response;
}

async function networkFirst(request) {
    const cache = await caches.open(CACHE);
    // Every page of the app is index.html; Blazor routes inside it.
    const key = request.mode === "navigate" ? new URL("index.html", self.registration.scope).href : request;
    try {
        const response = await fetch(request);
        if (response.ok) await cache.put(key, response.clone());
        return response;
    } catch (error) {
        const hit = await cache.match(key);
        if (hit) return hit;
        throw error;
    }
}

// A new build replaces e.g. IdeaSplit.Web.abc123.wasm with IdeaSplit.Web.def456.wasm; forget the old one.
async function dropOlderVersions(cache, url) {
    const [, name, hash, ext] = url.pathname.match(FINGERPRINTED);
    for (const request of await cache.keys()) {
        const match = new URL(request.url).pathname.match(FINGERPRINTED);
        if (match && match[1] === name && match[3] === ext && match[2] !== hash) await cache.delete(request);
    }
}
