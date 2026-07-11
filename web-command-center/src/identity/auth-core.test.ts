import assert from 'node:assert/strict';
import test from 'node:test';
import {
    EphemeralTicketService,
    isDeviceAuthTransportAllowed,
    lastForwardedValue,
    socketLoginTicketMatchesSession,
} from './auth-core';

test('ephemeral ticket is single-use and expires after its TTL', () => {
    const now = { value: 1_000 };
    const tickets = new EphemeralTicketService<{ identityId: string }>({
        now: () => now.value,
        randomBytes: (size) => Buffer.alloc(size, 4),
    });
    const issued = tickets.issue({ identityId: 'identity-1' }, 30_000);

    assert.deepEqual(tickets.consume(issued.ticket), { identityId: 'identity-1' });
    assert.equal(tickets.consume(issued.ticket), undefined);

    const expired = tickets.issue({ identityId: 'identity-2' }, 30_000);
    now.value += 30_000;
    assert.equal(tickets.consume(expired.ticket), undefined);
});

test('device auth allows development HTTP but requires production HTTPS', () => {
    assert.equal(isDeviceAuthTransportAllowed({ production: false, secure: false, hostname: '127.0.0.1' }), true);
    assert.equal(isDeviceAuthTransportAllowed({ production: true, secure: false, hostname: '203.0.113.10' }), false);
    assert.equal(isDeviceAuthTransportAllowed({ production: true, secure: true, hostname: 'cup.example.com' }), true);
});

test('trusted proxy parsing uses the address nearest to the server', () => {
    assert.equal(lastForwardedValue('198.51.100.8, 203.0.113.24'), '203.0.113.24');
    assert.equal(lastForwardedValue(''), undefined);
});

test('socket login ticket must belong to the active session', () => {
    const ticket = { membershipId: 'member-1', sessionId: 'session-old' };
    assert.equal(socketLoginTicketMatchesSession(ticket, 'session-old'), true);
    assert.equal(socketLoginTicketMatchesSession(ticket, 'session-new'), false);
});
