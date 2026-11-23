#!/data/data/com.termux/files/usr/bin/bash
# Installation du pont SPIN ARENA dans Termux (une seule fois).
# Utilisation : bash install-termux.sh
set -e
DEST="$HOME/spin-bridge"
command -v node >/dev/null 2>&1 || pkg install -y nodejs
mkdir -p "$DEST"
cp -r "$(dirname "$0")"/. "$DEST"/
cd "$DEST"
npm install --omit=dev --no-audit --no-fund
# Commande « spin-bridge » disponible partout dans Termux.
cat > "$PREFIX/bin/spin-bridge" <<'EOS'
#!/data/data/com.termux/files/usr/bin/bash
termux-wake-lock 2>/dev/null || true
cd "$HOME/spin-bridge" && exec node spin-bridge.mjs "$@"
EOS
chmod +x "$PREFIX/bin/spin-bridge"
echo ""
echo "✅ Pont installé. Lance-le avec : spin-bridge ton_pseudo_tiktok"
echo "   (le pseudo est mémorisé : ensuite « spin-bridge » suffit ; « spin-bridge --test » pour un essai sans live)"
