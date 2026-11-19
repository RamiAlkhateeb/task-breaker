// node --test tests/functions/*.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { onRequestPost, toJsonSchema, readJson } from "../../functions/api/ai.js";

const schema = { type: "OBJECT", properties: { tasks: { type: "ARRAY", items: { type: "STRING" } } } };

function setup(run) {
    const kv = new Map();
    const env = {
        AI: { run },
        AI_LIMITS: { get: async key => kv.get(key) ?? null, put: async (key, value) => kv.set(key, value) },
        ASSETS: { fetch: async () => new Response(JSON.stringify({ Supabase: { Url: "https://x.supabase.co", PublishableKey: "pk" } })) }
    };
    globalThis.fetch = async (url, options) => options.headers.Authorization === "Bearer good"
        ? new Response(JSON.stringify({ id: "user-1" }))
        : new Response("{}", { status: 401 });
    const call = async (token = "good", body = { prompt: "Plan a party", schema }) => {
        const response = await onRequestPost({
            request: new Request("https://task-breaker.pages.dev/api/ai", { method: "POST", headers: token ? { Authorization: `Bearer ${token}` } : {}, body: JSON.stringify(body) }),
            env
        });
        return { status: response.status, body: await response.json() };
    };
    return { call, used: () => Number([...kv.values()][0] ?? 0) };
}

test("requests need a signed-in user", async () => {
    const { call } = setup(async () => ({ response: { tasks: ["a"] } }));
    assert.equal((await call(null)).status, 401);
    assert.equal((await call("forged")).status, 401);
});

test("answers with the model's JSON and counts the request", async () => {
    const { call, used } = setup(async () => ({ response: { tasks: ["Buy cake"] } }));
    const { status, body } = await call();
    assert.equal(status, 200);
    assert.deepEqual(body.result, { tasks: ["Buy cake"] });
    assert.equal(used(), 1);
});

test("stops at 20 requests a day", async () => {
    const { call } = setup(async () => ({ response: { tasks: ["a"] } }));
    for (let i = 0; i < 20; i++) assert.equal((await call()).status, 200);
    assert.equal((await call()).status, 429);
});

test("falls back to plain text and to the smaller model", async () => {
    const tried = [];
    const { call } = setup(async (model, input) => {
        tried.push(model);
        if (model.includes("70b")) throw new Error("overloaded");
        if (input.response_format) throw new Error("json mode not supported");
        return { response: 'Here you go: {"tasks":["x"]}' };
    });
    const { status, body } = await call();
    assert.equal(status, 200);
    assert.deepEqual(body.result, { tasks: ["x"] });
    assert.ok(tried.at(-1).includes("8b"));
});

test("a used-up account allowance is reported and not counted", async () => {
    const { call, used } = setup(async () => { throw new Error("4006: daily free allocation of neurons used up"); });
    const { status, body } = await call();
    assert.equal(status, 429);
    assert.equal(body.error, "daily_allowance");
    assert.equal(used(), 0);
});

test("Gemini schemas become JSON Schema", () => {
    assert.deepEqual(toJsonSchema({ type: "OBJECT", properties: { action: { type: "STRING", enum: ["add"] } } }),
        { type: "object", properties: { action: { type: "string", enum: ["add"] } }, required: ["action"] });
    assert.equal(readJson("no json"), null);
    assert.deepEqual(readJson('text {"a":1} text'), { a: 1 });
});
