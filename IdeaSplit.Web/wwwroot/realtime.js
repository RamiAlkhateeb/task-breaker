// Supabase Realtime: hears about changes to this user's rows as they happen, so another device's edit shows up
// within seconds instead of on the next sync. Row-level security limits events to the signed-in user's rows.
// Needs the tables in the supabase_realtime publication; without that it simply never hears anything.
window.nxtTaskRealtime = (() => {
    const tables = ["projects", "tasks", "routines"];
    let socket = null, config = null, heartbeat = null, retryTimer = null, retryDelay = 1000, ref = 0, stopped = true;

    const send = message => { if (socket && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(message)); };
    const nextRef = () => String(++ref);

    function open() {
        if (!config) return;
        stopped = false;
        const url = config.url.replace(/^http/, "ws") + "/realtime/v1/websocket?apikey=" + encodeURIComponent(config.key) + "&vsn=1.0.0";
        try { socket = new WebSocket(url); } catch { scheduleRetry(); return; }
        socket.onopen = () => {
            retryDelay = 1000;
            // One channel per table, so a table that isn't set up for Realtime doesn't stop the others.
            for (const table of tables) {
                const joinRef = nextRef();
                send({
                    topic: `realtime:nxttask-${table}`, event: "phx_join", ref: joinRef, join_ref: joinRef,
                    payload: {
                        config: { broadcast: { self: false }, presence: { key: "" }, postgres_changes: [{ event: "*", schema: "public", table }] },
                        access_token: config.token
                    }
                });
            }
            heartbeat = setInterval(() => send({ topic: "phoenix", event: "heartbeat", payload: {}, ref: nextRef() }), 25000);
        };
        socket.onmessage = event => {
            let message;
            try { message = JSON.parse(event.data); } catch { return; }
            if (message.event === "postgres_changes" && config) config.dotnet.invokeMethodAsync("OnRemoteChange").catch(() => {});
        };
        socket.onclose = () => {
            clearInterval(heartbeat);
            socket = null;
            if (!stopped) scheduleRetry();
        };
    }

    function scheduleRetry() {
        clearTimeout(retryTimer);
        retryTimer = setTimeout(open, retryDelay);
        retryDelay = Math.min(retryDelay * 2, 60000);
    }

    return {
        connect(url, key, token, dotnet) {
            this.disconnect();
            config = { url, key, token, dotnet };
            open();
        },
        setToken(token) {
            if (!config || config.token === token) return;
            config.token = token;
            for (const table of tables) send({ topic: `realtime:nxttask-${table}`, event: "access_token", payload: { access_token: token }, ref: nextRef() });
        },
        disconnect() {
            stopped = true;
            clearTimeout(retryTimer);
            clearInterval(heartbeat);
            if (socket) socket.close();
            socket = null;
            config = null;
        }
    };
})();
