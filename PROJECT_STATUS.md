# SPIN ARENA — état du projet

Dernière mise à jour : 23 novembre 2026 — version 0.1 (phase 1 + bases des phases 2 à 4)

## Vérifié

| Quoi | Comment | Résultat |
|---|---|---|
| Noyau du jeu (règles de manche, combat, éléments, commandes, anti-abus, cadeaux, joueurs, progression) | `dotnet run --project tests/Core.Tests` | 180 / 180 ✅ |
| Pont Termux : conversion des événements (formats v1 et v2 de la bibliothèque) | `npm test` dans `bridge/` | 6 / 6 ✅ |
| Pont Termux : serveur WebSocket et mode test | lancement réel + client WebSocket | ✅ messages reçus |
| Compilation des scripts Unity | vérification locale contre les API Unity (hors module URP) | ✅ 0 erreur |
| Compilation Unity complète | GitHub Actions, build n°3 | ✅ APK 28 Mo + version Windows |
| Banc d'essai (Windows, sans carte graphique) | 3 manches complètes en mode démo (19, 33 et 44 toupies) | ✅ 3/3 manches, 0 erreur, 229 dons simulés exécutés, 95 éliminations (39 éjections) |

## Pas encore vérifié

- Fluidité réelle : le serveur de test n'a pas de carte graphique (3 images/s, non représentatif) → à mesurer sur le S23 Ultra.
- Connexion à un vrai live TikTok (photos, commentaires, cadeaux).

## Prochaines étapes

1. Essai de l'APK sur le téléphone en mode démo.
2. Régler la physique et les effets d'après les captures et mesures du banc d'essai.
3. Essai sur le téléphone en mode démo, puis avec le pont en mode `--test`, puis en live.
4. Phase 5 : effets élémentaires complets (brûlure, gel, poison, racines, absorption).

## Historique

- v0.2 — 21 cadeaux avec leur image et leur action, tapotements (vitesse, rotation, taille), photo de profil posée sur la toupie, retrait des commandes (sauf !join) et du fil d'action, 18 bruitages + 2 musiques ElevenLabs (musique dynamique), APK construit seulement sur demande.

- v0.1 — architecture, noyau testé, arène électrique, 42 toupies, physique, IA, battle royale, HUD, administration, pont Termux, compilation cloud, banc d'essai.
