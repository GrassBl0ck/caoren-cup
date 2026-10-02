import crypto from 'node:crypto';

export type SteamLoginMode = 'web' | 'desktop';
const PROVIDER = 'https://steamcommunity.com/openid/login';
const NAMESPACE = 'http://specs.openid.net/auth/2.0';
const FLOW_TTL_MS = 5 * 60 * 1000;
const requiredSigned = ['op_endpoint', 'return_to', 'response_nonce', 'assoc_handle', 'claimed_id', 'identity'];
const acceptedNonces = new Map<string, number>();

const digest = (value: string) => crypto.createHash('sha256').update(value).digest();

export const steamOpenIdUrl = (returnTo: string, realm: string): string => {
    const url = new URL(PROVIDER);
    for (const [key, value] of Object.entries({
        'openid.ns': NAMESPACE,
        'openid.mode': 'checkid_setup',
        'openid.return_to': returnTo,
        'openid.realm': realm,
        'openid.claimed_id': 'http://specs.openid.net/auth/2.0/identifier_select',
        'openid.identity': 'http://specs.openid.net/auth/2.0/identifier_select',
    })) url.searchParams.set(key, value);
    return url.toString();
};

export async function verifySteamOpenIdAssertion(
    query: URLSearchParams,
    expectedReturnTo: string,
    providerFetch: (url: string, init: RequestInit) => Promise<Pick<Response, 'ok' | 'text'>> = fetch,
): Promise<string | undefined> {
    const names = Array.from(query.keys());
    if (names.length !== new Set(names).size || names.some((name) => !name.startsWith('openid.'))) return undefined;
    const claimed = query.get('openid.claimed_id') || '';
    const match = /^https?:\/\/steamcommunity\.com\/openid\/id\/(7656119\d{10})$/.exec(claimed);
    const signed = new Set((query.get('openid.signed') || '').split(','));
    const nonce = query.get('openid.response_nonce') || '';
    if (!match || query.get('openid.ns') !== NAMESPACE || query.get('openid.mode') !== 'id_res' ||
        query.get('openid.op_endpoint') !== PROVIDER || query.get('openid.return_to') !== expectedReturnTo ||
        query.get('openid.identity') !== claimed || !requiredSigned.every((field) => signed.has(field)) ||
        !nonce || !query.get('openid.sig')) return undefined;
    const now = Date.now();
    for (const [key, expires] of acceptedNonces) if (expires <= now) acceptedNonces.delete(key);
    if (acceptedNonces.has(nonce)) return undefined;
    const body = new URLSearchParams(query);
    body.set('openid.mode', 'check_authentication');
    try {
        const response = await providerFetch(PROVIDER, {
            method: 'POST',
            headers: { 'content-type': 'application/x-www-form-urlencoded' },
            body,
            signal: AbortSignal.timeout(8_000),
        });
        if (!response.ok || !/^is_valid:true$/m.test(await response.text())) return undefined;
        if (acceptedNonces.has(nonce)) return undefined;
        acceptedNonces.set(nonce, now + 10 * 60 * 1000);
        return match[1];
    } catch {
        return undefined;
    }
}

interface Flow {
    mode: SteamLoginMode;
    secretHash: Buffer;
    expiresAt: number;
    steamId?: string;
    verified: boolean;
}

export class SteamOpenIdFlowStore {
    private flows = new Map<string, Flow>();

    start(mode: SteamLoginMode) {
        this.cleanup();
        const id = crypto.randomBytes(24).toString('base64url');
        const secret = crypto.randomBytes(32).toString('base64url');
        this.flows.set(id, { mode, secretHash: digest(secret), expiresAt: Date.now() + FLOW_TTL_MS, verified: false });
        return { id, secret, expiresAt: Date.now() + FLOW_TTL_MS };
    }

    mode(id: string): SteamLoginMode | undefined {
        this.cleanup();
        return this.flows.get(id)?.mode;
    }

    complete(id: string, steamId: string): SteamLoginMode | undefined {
        this.cleanup();
        const flow = this.flows.get(id);
        if (!flow || flow.verified || !/^7656119\d{10}$/.test(steamId)) return undefined;
        flow.steamId = steamId;
        flow.verified = true;
        return flow.mode;
    }

    consumeWeb(id: string, secret: string) { return this.consume(id, secret, 'web'); }
    consumeDesktop(id: string, secret: string) { return this.consume(id, secret, 'desktop'); }

    private consume(id: string, secret: string, mode: SteamLoginMode):
        { status: 'invalid' } | { status: 'pending' } | { status: 'complete'; steamId: string } {
        this.cleanup();
        const flow = this.flows.get(id);
        if (!flow || flow.mode !== mode || typeof secret !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(secret) ||
            !crypto.timingSafeEqual(flow.secretHash, digest(secret))) return { status: 'invalid' };
        if (!flow.verified || !flow.steamId) return { status: 'pending' };
        this.flows.delete(id);
        return { status: 'complete', steamId: flow.steamId };
    }

    private cleanup() {
        const now = Date.now();
        for (const [id, flow] of this.flows) if (flow.expiresAt <= now) this.flows.delete(id);
    }
}
