const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const pdfPath = path.resolve(__dirname, '..', 'public', 'downloads', 'caoren-cup-full-rules.pdf');
assert.ok(fs.existsSync(pdfPath), '完整规则 PDF 不存在');
const pdf = fs.readFileSync(pdfPath);
assert.ok(pdf.length > 50_000, '完整规则 PDF 体积异常');
assert.equal(pdf.subarray(0, 5).toString('ascii'), '%PDF-', '下载资产不是 PDF');
console.log('rules PDF contract checks passed');
