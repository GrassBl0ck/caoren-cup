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

test('announcement HTML applies the requested length limit before sanitizing', () => {
    assert.equal(sanitizeAnnouncementHtml('<p>abcdef</p>', 5), '<p>ab');
});
