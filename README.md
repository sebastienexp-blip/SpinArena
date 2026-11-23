# SPIN ARENA — TikTok Battle Royale

Jeu de combat de toupies fantastiques en 3D (Unity 6, URP) où tous les spectateurs d'un live TikTok font combattre leur toupie en même temps. Dernier survivant gagnant.

- Architecture, modules, stratégie TikTok, planning : [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- État du projet : [PROJECT_STATUS.md](PROJECT_STATUS.md)

## Télécharger

| Quoi | Où |
|---|---|
| Jeu Android | release **apk-derniere** → `SpinArena.apk` |
| Pont TikTok pour Termux | release **pont-termux** → `spin-bridge-termux.zip` |
| Résultats du banc d'essai | release **labo** → `labo.zip` |

## Jouer en live

1. Termux : `spin-bridge ton_pseudo` (la première fois : dézipper le pont puis `bash install-termux.sh`).
2. Lancer TikTok LIVE en mode Jeu mobile, puis ouvrir Spin Arena.
3. Les spectateurs écrivent `!join`.

Options (10 onglets) : 3 tapotements rapides dans le coin en haut à gauche.

## Développement

- Tests du noyau : `dotnet run --project tests/Core.Tests`
- Tests du pont : `cd bridge && npm install && npm test`
- Construction : automatique sur GitHub à chaque modification (secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`).
