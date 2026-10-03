import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const script = readFileSync(new URL('../../src/TimeOps.Web/wwwroot/connection-storage.js', import.meta.url), 'utf8');
const credential = JSON.stringify({ Organization: 'org-test', PersonalAccessToken: 'pat-sintetico' });

function openApp(baseURI, localStorage = storage()) {
    const window = { localStorage };
    runInNewContext(script, { window, document: { baseURI }, URL });
    return window.timeOpsConnection;
}

function storage() {
    const values = new Map();
    return {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value),
        removeItem: key => values.delete(key)
    };
}

test('restores a saved connection when reopening the same app', () => {
    const localStorage = storage();
    const app = openApp('https://example.test/TimeOps/', localStorage);
    assert.equal(app.read(), null);
    app.save(credential);

    const reopened = openApp('https://example.test/TimeOps/', localStorage);
    assert.equal(reopened.read(), credential);
    reopened.remove();
    assert.equal(app.read(), null);
    reopened.remove();
    assert.equal(reopened.read(), null);
});

test('apps sharing an origin keep credentials isolated by base path', () => {
    const localStorage = storage();
    const first = openApp('https://example.test/TimeOps/', localStorage);
    const second = openApp('https://example.test/OtherApp/', localStorage);
    const root = openApp('https://example.test/', localStorage);
    first.save(credential);
    assert.equal(second.read(), null);
    assert.equal(root.read(), null);

    const otherCredential = JSON.stringify({ Organization: 'other-org', PersonalAccessToken: 'other-synthetic-token' });
    second.save(otherCredential);
    first.remove();
    assert.equal(first.read(), null);
    assert.equal(second.read(), otherCredential);
});

test('storage failures reach interop so the app can report them', () => {
    const error = new Error('Storage blocked');
    const app = openApp('https://example.test/TimeOps/', {
        getItem() { throw error; },
        setItem() { throw error; },
        removeItem() { throw error; }
    });

    for (const operation of [() => app.read(), () => app.save(credential), () => app.remove()]) {
        assert.throws(operation, thrown => thrown === error);
    }
});
