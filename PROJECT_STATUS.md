# SPIN ARENA — état du projet

Dernière mise à jour : 23 novembre 2026 — version 0.1 (phase 1 + bases des phases 2 à 4)

## Vérifié

| Quoi | Comment | Résultat |
|---|---|---|
| Noyau du jeu (règles de manche, combat, éléments, commandes, anti-abus, cadeaux, joueurs, progression) | `dotnet run --project tests/Core.Tests` | 180 / 180 ✅ |
| Pont Termux : conversion des événements (formats v1 et v2 de la bibliothèque) | `npm test` dans `bridge/` | 6 / 6 ✅ |
| Pont Termux : serveur WebSocket et mode test | lancement réel + client WebSocket | ✅ messages reçus |
| Compilation des scripts Unity | vérification locale contre les API Unity (hors module URP) | ✅ 0 erreur |

## Pas encore vérifié

- Compilation Unity complète et APK : attend la création du dépôt GitHub et les secrets de licence.
- Rendu, physique et fluidité avec 50 toupies : banc d'essai automatique après la première compilation, puis essai sur le S23 Ultra.
- Connexion à un vrai live TikTok (photos, commentaires, cadeaux).

## Prochaines étapes

1. Créer le dépôt privé `SpinArena` et ses secrets → premier APK + banc d'essai.
2. Régler la physique et les effets d'après les captures et mesures du banc d'essai.
3. Essai sur le téléphone en mode démo, puis avec le pont en mode `--test`, puis en live.
4. Phase 5 : effets élémentaires complets (brûlure, gel, poison, racines, absorption).

## Historique

- v0.1 — architecture, noyau testé, arène électrique, 42 toupies, physique, IA, battle royale, HUD, administration, pont Termux, compilation cloud, banc d'essai.
