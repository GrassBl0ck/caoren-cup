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

const sanitizeUrl = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed) return '';
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
