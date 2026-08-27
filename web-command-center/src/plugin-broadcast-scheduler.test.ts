import assert from 'node:assert/strict';
import test from 'node:test';
import { PluginStateBroadcastScheduler } from './plugin-broadcast-scheduler';

test('high-frequency requests share one trailing broadcast per window', () => {
    let broadcasts = 0;
    const scheduled: Array<() => void> = [];
    const scheduler = new PluginStateBroadcastScheduler(
        () => { broadcasts += 1; },
        500,
        (callback) => {
            scheduled.push(callback);
            return scheduled.length;
        },
        () => undefined,
    );

    scheduler.request();
    scheduler.request();
    scheduler.request();

    assert.equal(scheduled.length, 1);
    assert.equal(broadcasts, 0);
    scheduled.shift()?.();
    assert.equal(broadcasts, 1);

    scheduler.request();
    assert.equal(scheduled.length, 1);
});

test('critical events flush a pending window immediately without a later duplicate', () => {
    let broadcasts = 0;
    let pending: (() => void) | undefined;
    let cancelled = false;
    const scheduler = new PluginStateBroadcastScheduler(
        () => { broadcasts += 1; },
        500,
        (callback) => {
            pending = callback;
            return 7;
        },
        () => { cancelled = true; },
    );

    scheduler.request();
    scheduler.flush();

    assert.equal(broadcasts, 1);
    assert.equal(cancelled, true);
    pending?.();
    assert.equal(broadcasts, 1);
});
