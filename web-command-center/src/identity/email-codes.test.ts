import assert from 'node:assert/strict';
import test from 'node:test';
import { EmailAuthError, EmailCodeService, VerificationEmail } from './email-codes';

function setup() {
    const now = { value: 1_000_000 };
    const sent: VerificationEmail[] = [];
    const service = new EmailCodeService({
        now: () => now.value, generateCode: () => '123456',
        deliver: async (message) => { sent.push(message); },
    });
    const request = { email: 'Player@Example.COM', purpose: 'register' as const, ip: 'test-ip' };
    return { now, sent, service, request };
}

test('email proofs normalize addresses, expire, and can be consumed only once', async () => {
    const { service, request, sent, now } = setup();
    const ticket = await service.request(request);
    assert.equal(sent[0].email, 'player@example.com');
    const input = { ...request, ...ticket, code: '123456' };
    assert.equal(service.consume(input), 'player@example.com');
    assert.throws(() => service.consume(input), /email_code_invalid/);
    now.value += 60_001;
    const expired = await service.request(request);
    now.value += 600_000;
    assert.throws(() => service.consume({ ...input, ...expired }), /email_code_invalid/);
});

test('five wrong guesses invalidate a proof; purpose, email and account cannot be substituted', async () => {
    const { service, request } = setup();
    const ticket = await service.request({ ...request, purpose: 'bind-email', principal: 'owner' });
    const input = { ...request, ...ticket, code: '123456', purpose: 'bind-email' as const, principal: 'owner' };
    assert.throws(() => service.consume({ ...input, purpose: 'register' }), /email_code_invalid/);
    assert.throws(() => service.consume({ ...input, email: 'other@example.com' }), /email_code_invalid/);
    assert.throws(() => service.consume({ ...input, principal: 'attacker' }), /email_code_invalid/);
    assert.throws(() => service.consume({ ...input, code: '000000' }), /email_code_invalid/);
    assert.throws(() => service.consume({ ...input, code: '000000' }), /email_code_invalid/);
    assert.throws(() => service.consume(input), /email_code_invalid/);
});

test('sending is reserved before awaiting delivery and failed SMTP creates no usable proof', async () => {
    let release!: () => void;
    let received: VerificationEmail | undefined;
    const service = new EmailCodeService({ deliver: async (message) => {
        received = message;
        await new Promise<void>((resolve) => { release = resolve; });
    } });
    const input = { email: 'player@example.com', purpose: 'register' as const, ip: 'ip' };
    const pending = service.request(input);
    await assert.rejects(service.request(input), (error: EmailAuthError) => error.status === 429);
    release();
    const proof = await pending;
    assert.equal(service.consume({ ...input, ...proof, code: received!.code }), input.email);
    const failed = new EmailCodeService({ deliver: async () => { throw new Error('private SMTP details'); } });
    await assert.rejects(failed.request(input), (error: EmailAuthError) => error.message === 'email_delivery_failed' && error.status === 503);
    await assert.rejects(new EmailCodeService().request(input), /email_service_unavailable/);
});

test('resending replaces the old proof; IP and mailbox quotas also apply across purposes', async () => {
    const { service, request, now } = setup();
    const first = await service.request(request);
    await assert.rejects(service.request({ ...request, purpose: 'reset-password' }), /email_rate_limited/);
    now.value += 60_001;
    const second = await service.request(request);
    assert.throws(() => service.consume({ ...request, ...first, code: '123456' }), /email_code_invalid/);
    assert.equal(service.consume({ ...request, ...second, code: '123456' }), 'player@example.com');
});

test('unknown recovery accounts receive a challenge-shaped response without a usable proof or email', async () => {
    const { service, sent, request } = setup();
    const ticket = await service.request({ ...request, purpose: 'reset-password', eligible: false });
    assert.equal(typeof ticket.challengeId, 'string');
    assert.equal(sent.length, 0);
    assert.throws(() => service.consume({ ...request, ...ticket, purpose: 'reset-password', code: '123456' }), /email_code_invalid/);
});
