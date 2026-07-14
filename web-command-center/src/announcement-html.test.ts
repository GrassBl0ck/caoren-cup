import assert from 'node:assert/strict';
import test from 'node:test';
import { sanitizeAnnouncementHtml } from './announcement-html';

test('announcement HTML keeps safe formatting and removes executable content', () => {
    const result = sanitizeAnnouncementHtml(
        '<script>alert(1)</script><p onclick="bad()"><strong>安全内容</strong></p>',
    );
    assert.equal(result, '<p><strong>安全内容</strong></p>');
});

test('announcement links allow safe protocols and reject javascript URLs', () => {
    assert.equal(
        sanitizeAnnouncementHtml('<a href="https://example.com">规则</a>'),
        '<a href="https://example.com" target="_blank" rel="noopener noreferrer">规则</a>',
    );
    assert.equal(sanitizeAnnouncementHtml('<a href="javascript:alert(1)">危险</a>'), '<a>危险</a>');
});

test('announcement link sanitization is idempotent for URL query entities', () => {
    const deeplyEncodedAmpersand = `&${'amp;'.repeat(12)}`;
    const inputs = [
        '<a href="https://example.com/rules?a=1&b=2&c=3">HTTPS</a>',
        '<a href="/rules?a=1&amp;b=2">相对地址</a>',
        '<a href="mailto:test@example.com?subject=草人杯&amp;body=欢迎">邮件</a>',
        '<a href="https://example.com/rules?a=1&amp;b=2">已有实体</a>',
        `<a href="https://example.com/rules?a=1${deeplyEncodedAmpersand}b=2">深层实体</a>`,
    ];

    for (const input of inputs) {
        const once = sanitizeAnnouncementHtml(input);
        assert.equal(sanitizeAnnouncementHtml(once), once, input);
        assert.equal(once.includes('&amp;amp;'), false, input);
    }
});

test('announcement links reject entity-obfuscated dangerous protocols', () => {
    const dangerous = [
        'jav&#x61;script:alert(1)',
        'javascript&#58;alert(1)',
        'java&amp;#x73;cript:alert(1)',
        '&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert(1)',
    ];
    for (const href of dangerous) {
        assert.equal(sanitizeAnnouncementHtml(`<a href="${href}">危险</a>`), '<a>危险</a>', href);
    }
});

test('announcement HTML applies the requested length limit before sanitizing', () => {
    assert.equal(sanitizeAnnouncementHtml('<p>abcdef</p>', 5), '<p>ab');
});
