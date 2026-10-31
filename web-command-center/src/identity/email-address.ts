export function normalizeEmail(raw: unknown): string {
    if (typeof raw !== 'string') throw new Error('email_invalid');
    const email = raw.trim().toLowerCase();
    if (email.length > 254 || !/^[a-z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?\.[a-z]{2,63}$/.test(email)) {
        throw new Error('email_invalid');
    }
    const [local, domain] = email.split('@');
    if (local.startsWith('.') || local.endsWith('.') || local.includes('..') ||
        domain.split('.').some((part) => !part || part.length > 63 || part.startsWith('-') || part.endsWith('-'))) {
        throw new Error('email_invalid');
    }
    return email;
}
