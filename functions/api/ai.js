// POST /api/ai: AI for signed-in NxtTask users who have no Gemini key, through Cloudflare Workers AI.
//
// Body: { prompt, schema }, where schema is the Gemini response_schema the app already builds.
// Answers { result: <JSON matching the schema> }, or { error } with 401 (sign in), 429 (daily limit),
// 400 (bad request) or 502/503.
//
// Pages project bindings (Settings > Bindings): Workers AI as "AI", and a KV namespace as "AI_LIMITS"
// for the per-user daily count. Optional variables: AI_MODEL (tried first), AI_DAILY_LIMIT.
//
// The Cloudflare account's free Workers AI allowance is shared with its other apps (e.g. the Makdous photo
// worker); once it is used up for the day every model fails with error 4006, answered here as 429 "daily_allowance".

// The larger model writes better plans; the small one is the fallback (same pair as the Makdous worker).
const DEFAULT_MODELS = ["@cf/meta/llama-3.3-70b-instruct-fp8-fast", "@cf/meta/llama-3.1-8b-instruct"];
const DEFAULT_DAILY_LIMIT = 20;
const MAX_PROMPT_CHARS = 16000;
const SYSTEM_PROMPT =
    "You are the planning assistant of a to-do app. Answer only with JSON that matches the given schema. " +
    "Write all user-facing text in the same language as the user's own text.";

export async function onRequestPost({ request, env }) {
    if (!env.AI || !env.AI_LIMITS) return reply(503, { error: "The built-in AI isn't set up for this site yet." });

    const token = (request.headers.get("Authorization") || "").replace(/^Bearer\s+/i, "").trim();
    const user = token ? await signedInUser(token, request, env) : null;
    if (!user) return reply(401, { error: "Sign in to use the built-in AI." });

    let body;
    try { body = await request.json(); } catch { return reply(400, { error: "Invalid request." }); }
    const prompt = typeof body?.prompt === "string" ? body.prompt : "";
    if (!prompt.trim() || prompt.length > MAX_PROMPT_CHARS) return reply(400, { error: "The request is empty or too long." });
    const schema = toJsonSchema(body.schema);
    if (!schema) return reply(400, { error: "Invalid schema." });

    // Count the request before running it, so parallel requests can't slip past the limit; refund on failure.
    const limit = Number(env.AI_DAILY_LIMIT) || DEFAULT_DAILY_LIMIT;
    const key = `${user.id}:${new Date().toISOString().slice(0, 10)}`;
    const used = Number(await env.AI_LIMITS.get(key)) || 0;
    if (used >= limit) return reply(429, { error: `Daily limit of ${limit} reached.` });
    await env.AI_LIMITS.put(key, String(used + 1), { expirationTtl: 2 * 24 * 60 * 60 });

    const models = env.AI_MODEL ? [env.AI_MODEL, ...DEFAULT_MODELS.filter(model => model !== env.AI_MODEL)] : DEFAULT_MODELS;
    const messages = [
        { role: "system", content: `${SYSTEM_PROMPT} The JSON must match this JSON Schema: ${JSON.stringify(schema)}` },
        { role: "user", content: prompt }
    ];
    let lastError = "no answer";
    for (const model of models) {
        // JSON mode first; models that don't support it are asked plainly and the JSON is read from the text.
        for (const jsonMode of [true, false]) {
            try {
                const answer = await env.AI.run(model, {
                    messages,
                    max_tokens: 2048,
                    ...(jsonMode ? { response_format: { type: "json_schema", json_schema: schema } } : {})
                });
                const result = readJson(answer?.response);
                if (result) return reply(200, { result, remaining: limit - used - 1 });
                lastError = "the answer wasn't JSON";
            } catch (error) {
                if (isAllowanceUsedUp(error)) {
                    await refund(env, key, used);
                    return reply(429, { error: "daily_allowance", detail: "The built-in AI has used up today's allowance." });
                }
                lastError = String(error?.message || error).slice(0, 200);
            }
        }
    }
    await refund(env, key, used);
    return reply(502, { error: `The model didn't answer: ${lastError}` });
}

const isAllowanceUsedUp = error => /4006|allocation|neurons|quota/i.test(String(error?.message || error));

const refund = (env, key, used) => env.AI_LIMITS.put(key, String(used), { expirationTtl: 2 * 24 * 60 * 60 });

/** The model's answer as an object: already parsed (JSON mode), or the first {...} in its text. */
export function readJson(response) {
    if (response && typeof response === "object") return response;
    const found = String(response ?? "").match(/\{[\s\S]*\}/);
    if (!found) return null;
    try {
        const parsed = JSON.parse(found[0]);
        return parsed && typeof parsed === "object" ? parsed : null;
    } catch {
        return null;
    }
}

/** Asks Supabase who the token belongs to (it rejects expired or forged tokens). Supabase's public values come from the site's appsettings.json. */
async function signedInUser(token, request, env) {
    try {
        const settings = await (await env.ASSETS.fetch(new URL("/appsettings.json", request.url))).json();
        const { Url: url, PublishableKey: key } = settings.Supabase || {};
        if (!url || !key) return null;
        const response = await fetch(`${url.replace(/\/$/, "")}/auth/v1/user`, { headers: { apikey: key, Authorization: `Bearer ${token}` } });
        if (!response.ok) return null;
        const user = await response.json();
        return user?.id ? { id: user.id } : null;
    } catch {
        return null;
    }
}

/** Gemini's response_schema (type: "OBJECT", ...) to standard JSON Schema (type: "object", ...). */
export function toJsonSchema(schema) {
    if (!schema || typeof schema !== "object" || Array.isArray(schema)) return null;
    const out = {};
    for (const [name, value] of Object.entries(schema)) {
        if (name === "type" && typeof value === "string") out.type = value.toLowerCase();
        else if (name === "properties" && value && typeof value === "object")
            out.properties = Object.fromEntries(Object.entries(value).map(([key, child]) => [key, toJsonSchema(child)]));
        else if (name === "items") out.items = toJsonSchema(value);
        else if (["required", "enum", "description"].includes(name)) out[name] = value;
    }
    if (out.type === "object" && !out.required && out.properties) out.required = Object.keys(out.properties);
    return out;
}

function reply(status, body) {
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store" } });
}
