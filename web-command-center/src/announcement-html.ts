const DEFAULT_MAXIMUM_LENGTH = 12_000;

const ALLOWED_TAGS = new Set([
    'a', 'b', 'blockquote', 'br', 'code', 'div', 'em', 'h2', 'h3', 'hr', 'i',
    'li', 'ol', 'p', 'pre', 's', 'span', 'strong', 'u', 'ul',
]);

const escapeText = (value: unknown) => String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

const URL_NAMED_ENTITIES: Record<string, string> = {
    amp: '&',
    apos: "'",
    colon: ':',
    gt: '>',
    lt: '<',
    newline: '\n',
    quot: '"',
    tab: '\t',
};

const decodeUrlEntitiesOnce = (value: string) => value.replace(
    /&#(?:x([0-9a-f]+)|([0-9]+));?|&(amp|apos|colon|gt|lt|newline|quot|tab);/gi,
    (_entity, hexadecimal: string | undefined, decimal: string | undefined, named: string | undefined) => {
        if (named) return URL_NAMED_ENTITIES[named.toLowerCase()];
        const codePoint = Number.parseInt(hexadecimal || decimal || '', hexadecimal ? 16 : 10);
        if (!Number.isFinite(codePoint)
            || codePoint <= 0
            || codePoint > 0x10FFFF
            || (codePoint >= 0xD800 && codePoint <= 0xDFFF)) return '\uFFFD';
        return String.fromCodePoint(codePoint);
    },
);

const normalizeUrlEntities = (value: string) => {
    let normalized = value;
    for (;;) {
        const decoded = decodeUrlEntitiesOnce(normalized);
        if (decoded === normalized) break;
        normalized = decoded;
    }
    return normalized;
};

const sanitizeUrl = (value: string) => {
    const trimmed = normalizeUrlEntities(value).trim();
    if (!trimmed) return '';
    if (/[\u0000-\u001F\u007F]/.test(trimmed)) return '';
    if (/^(https?:|mailto:)/i.test(trimmed)) return escapeText(trimmed);
    if (/^[#/]/.test(trimmed)) return escapeText(trimmed);
    return '';
};

export const sanitizeAnnouncementHtml = (
    rawHtml: unknown,
    maximumLength = DEFAULT_MAXIMUM_LENGTH,
) => {
    let html = String(rawHtml ?? '').slice(0, maximumLength);
    html = html
        .replace(/<!--[\s\S]*?-->/g, '')
        .replace(/<\s*(script|style|iframe|object|embed|meta|link)[^>]*>[\s\S]*?<\s*\/\s*\1\s*>/gi, '')
        .replace(/<\s*\/?\s*(script|style|iframe|object|embed|meta|link)[^>]*>/gi, '');

    return html.replace(/<[^>]*>/g, (tag) => {
        const match = tag.match(/^<\s*(\/?)\s*([a-zA-Z0-9]+)([^>]*)>/);
        if (!match) return '';
        const isClosing = match[1] === '/';
        const tagName = match[2].toLowerCase();
        const attributes = match[3] || '';
        if (!ALLOWED_TAGS.has(tagName)) return '';
        if (isClosing) return tagName === 'br' || tagName === 'hr' ? '' : `</${tagName}>`;
        if (tagName === 'br' || tagName === 'hr') return `<${tagName}>`;
        if (tagName === 'a') {
            const href = attributes.match(/\shref\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))/i);
            const safeHref = sanitizeUrl(href?.[1] || href?.[2] || href?.[3] || '');
            return safeHref
                ? `<a href="${safeHref}" target="_blank" rel="noopener noreferrer">`
                : '<a>';
        }
        return `<${tagName}>`;
    });
};
