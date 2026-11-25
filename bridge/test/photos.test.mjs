// Tests des photos (sans réseau). Lancer : npm test
import assert from 'node:assert/strict';
import { normalizeChat } from '../normalize.mjs';
import { isAllowedPhotoUrl, localPhotoUrl, sniff, webpToPng } from '../photos.mjs';

// Petite image WebP 2×2 (rouge) encodée en base64.
const WEBP = Buffer.from('UklGRjwAAABXRUJQVlA4IDAAAADQAQCdASoCAAIAAUAmJaACdLoB+AADsAD+8ut//NgVzXPv9//S4P0uD9Lg/9KQAAA=', 'base64');

const e = normalizeChat({ user: { id: '1', profilePicture: { urls: ['https://p16-sign.tiktokcdn.com/a.webp'] } }, content: 'x' });
assert.equal(e.user.avatarUrl, 'https://p16-sign.tiktokcdn.com/a.webp', 'profilePicture.urls lu (format du quiz)');
assert.ok(isAllowedPhotoUrl('https://p16-sign-va.tiktokcdn.com/x.webp'));
assert.ok(!isAllowedPhotoUrl('http://p16-sign-va.tiktokcdn.com/x.webp'), 'https seulement');
assert.ok(!isAllowedPhotoUrl('https://exemple.com/x.webp'), 'serveurs TikTok seulement');
assert.equal(localPhotoUrl('https://p16.tiktokcdn.com/a.webp?x=1&y=2', 8787), 'http://127.0.0.1:8787/photo?u=https%3A%2F%2Fp16.tiktokcdn.com%2Fa.webp%3Fx%3D1%26y%3D2');
assert.equal(sniff(Buffer.from([0xff, 0xd8, 0xff, 0xe0])), 'jpeg');
if (WEBP.length) {
  assert.equal(sniff(WEBP), 'webp');
  const png = await webpToPng(WEBP);
  assert.equal(sniff(png), 'png', 'WebP converti en PNG');
}
console.log(`photos : ${WEBP.length ? 7 : 6} vérifications OK`);
