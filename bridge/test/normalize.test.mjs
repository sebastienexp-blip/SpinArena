// Tests du format des événements (sans réseau). Lancer : npm test
import assert from 'node:assert/strict';
import { normalizeChat, normalizeGift, normalizeLike, dedupKey } from '../normalize.mjs';

let n = 0;
const t = (name, fn) => { fn(); n++; };

// Format v2 (protobuf, tiktok-live-connector 2.5)
t('chat v2', () => {
  const e = normalizeChat({ common: { msgId: '123' }, user: { id: '42', displayId: 'seb', nickname: 'Seb', avatarThumb: { urlList: ['https://x/a.heic', 'https://x/a.webp'] } }, content: '!join' });
  assert.equal(e.comment, '!join');
  assert.equal(e.user.userId, '42');
  assert.equal(e.user.uniqueId, 'seb');
  assert.equal(e.user.avatarUrl, 'https://x/a.webp'); // seule image disponible
  assert.equal(e.msgId, '123');
});

t('photo : JPEG préféré au WebP et au HEIC', () => {
  const e = normalizeChat({ user: { id: '1', avatarThumb: { urlList: ['https://p16/a~100x100.webp?x=1', 'https://p16/a~100x100.heic?x=1', 'https://p16/a~100x100.jpeg?x=1'] } }, content: 'salut' });
  assert.equal(e.user.avatarUrl, 'https://p16/a~100x100.jpeg?x=1');
});

t('photo : JPEG trouvé dans une autre taille', () => {
  const e = normalizeChat({ user: { id: '1', avatarThumb: { urlList: ['https://p16/t.webp'] }, avatarMedium: { urlList: ['https://p16/m.jpeg'] } }, content: 'x' });
  assert.equal(e.user.avatarUrl, 'https://p16/m.jpeg');
});

t('gift v2 série', () => {
  const e = normalizeGift({ common: { msgId: '9' }, user: { id: '1', displayId: 'a' }, giftId: '5655', repeatCount: 4, repeatEnd: 0, groupId: 'g', gift: { name: 'Rose', diamondCount: 1, type: 1, image: { urlList: ['https://x/rose.png'] } } });
  assert.equal(e.giftName, 'Rose');
  assert.equal(e.diamondCount, 1);
  assert.equal(e.repeatCount, 4);
  assert.equal(e.repeatEnd, false);
  assert.equal(e.streakable, true);
  assert.equal(e.giftImageUrl, 'https://x/rose.png');
  assert.equal(dedupKey(e), '9|4|0');
});

// Ancien format aplati (v1)
t('gift v1', () => {
  const e = normalizeGift({ msgId: '7', userId: '5', uniqueId: 'bob', nickname: 'Bob', profilePictureUrl: 'https://x/b.jpg', giftId: 6064, giftName: 'Lion', diamondCount: 29999, giftType: 2, repeatCount: 1, repeatEnd: true });
  assert.equal(e.user.uniqueId, 'bob');
  assert.equal(e.user.avatarUrl, 'https://x/b.jpg');
  assert.equal(e.streakable, false);
  assert.equal(e.repeatEnd, true);
  assert.equal(e.giftId, '6064');
});

t('chat v1', () => {
  const e = normalizeChat({ msgId: '8', userId: '5', uniqueId: 'bob', comment: '!attaque' });
  assert.equal(e.comment, '!attaque');
  assert.equal(e.user.userId, '5');
});

t('like', () => {
  assert.equal(normalizeLike({ user: { id: '1' }, count: 15 }).likeCount, 15);
  assert.equal(normalizeLike({ userId: '1' }).likeCount, 1);
});

t('champs absents restent vides', () => {
  const e = normalizeChat({ user: {}, content: 'salut' });
  assert.equal(e.user.userId, '');
  assert.equal(e.user.avatarUrl, '');
  assert.equal(dedupKey(e), '');
});

console.log(`Tests du pont : ${n} réussis`);
