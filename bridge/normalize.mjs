// Conversion des événements de TikTok-Live-Connector (format v2 « protobuf » ou ancien format aplati)
// vers le format simple attendu par SPIN ARENA. Aucune donnée n'est inventée : un champ absent reste vide.

const str = (v) => (v === undefined || v === null ? '' : String(v));

function firstUrl(img) {
  if (!img) return '';
  if (typeof img === 'string') return img;
  const list = img.urlList || img.url_list || img.url || [];
  if (Array.isArray(list)) {
    // Préférence pour une image classique (jpeg/webp) plutôt que le format heic, illisible par Unity.
    const ok = list.find((u) => typeof u === 'string' && !/\.heic/i.test(u));
    return ok || str(list[0]);
  }
  return '';
}

export function normalizeUser(d) {
  const u = (d && (d.user || d.userDetails)) || d || {};
  const userId = str(u.userId || u.id || d?.userId);
  const uniqueId = str(u.uniqueId || u.displayId || d?.uniqueId);
  const nickname = str(u.nickname || d?.nickname);
  const avatarUrl =
    str(u.profilePictureUrl || d?.profilePictureUrl) ||
    firstUrl(u.avatarThumb) || firstUrl(u.avatarMedium) || firstUrl(u.profilePicture) || firstUrl(u.avatarLarge);
  return { userId, uniqueId, nickname, avatarUrl };
}

function msgIdOf(d) {
  return str(d?.common?.msgId || d?.msgId || d?.event?.msgId || d?.common?.msg_id);
}

export function normalizeChat(d) {
  return { type: 'chat', msgId: msgIdOf(d), user: normalizeUser(d), comment: str(d?.comment ?? d?.content) };
}

export function normalizeGift(d) {
  const gift = d?.gift || {};
  const details = d?.giftDetails || {};
  const giftType = Number(gift.type ?? details.giftType ?? d?.giftType ?? 0);
  const repeatEnd = d?.repeatEnd === true || Number(d?.repeatEnd) === 1;
  return {
    type: 'gift',
    msgId: msgIdOf(d),
    user: normalizeUser(d),
    giftId: str(d?.giftId || gift.id),
    giftName: str(gift.name || details.giftName || d?.giftName || d?.extendedGiftInfo?.name),
    diamondCount: Number(gift.diamondCount ?? details.diamondCount ?? d?.diamondCount ?? 0) || 0,
    repeatCount: Math.max(1, Number(d?.repeatCount ?? d?.comboCount ?? 1) || 1),
    repeatEnd,
    // giftType 1 = cadeau enchaînable (série) ; les autres arrivent en un seul message.
    streakable: giftType === 1,
    groupId: str(d?.groupId),
    giftImageUrl: firstUrl(gift.image) || firstUrl(gift.icon) || str(details?.giftImage?.giftPictureUrl || d?.giftPictureUrl),
  };
}

export function normalizeLike(d) {
  return { type: 'like', msgId: msgIdOf(d), user: normalizeUser(d), likeCount: Math.max(1, Number(d?.count ?? d?.likeCount ?? 1) || 1) };
}

export function normalizeSimple(type, d) {
  return { type, msgId: msgIdOf(d), user: normalizeUser(d) };
}

/** Clé anti-doublon (les reconnexions peuvent renvoyer des messages déjà reçus). */
export function dedupKey(ev) {
  if (!ev.msgId) return '';
  return ev.type === 'gift' ? `${ev.msgId}|${ev.repeatCount}|${ev.repeatEnd ? 1 : 0}` : `${ev.type}|${ev.msgId}`;
}
