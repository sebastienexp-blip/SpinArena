# SPIN ARENA — architecture technique

Battle royale de toupies pour les lives TikTok. Le jeu tourne **sur le téléphone de l'animateur** (APK Android, paysage 1920×1080) et est diffusé en mode « Jeu mobile » de TikTok. Les commentaires et les cadeaux arrivent par un **pont Termux** qui tourne sur le même téléphone.

```
 TikTok LIVE ──(bibliothèque non officielle)──▶ Pont Termux (Node.js) ──ws://127.0.0.1:8787──▶ Jeu Unity (APK)
                                                 spin-bridge.mjs                             BridgeLiveConnector
```

## 1. Choix de plateforme

| Besoin du cahier des charges | Décision | Pourquoi |
|---|---|---|
| Jeu PC pendant le live | **APK Android** (S23 Ultra) | Pas de PC ; le live part du téléphone en mode Jeu mobile |
| Unity 3D + C# | Unity **6000.0.32f1**, URP 17, C# 9 | Même version que les autres projets : licence et compilation cloud déjà éprouvées |
| Compilation | **GitHub Actions** (game-ci) | Tout se fait depuis GitHub, sans PC |
| Test sans téléphone | Version Windows + **banc d'essai** automatique | Mesures et captures d'écran sur serveur avant chaque installation |

## 2. Modules

Le code est découpé en trois assemblages :

- **SpinArena.Core** — logique pure, sans Unity, testée automatiquement (`tests/Core.Tests`, 180 vérifications).
- **SpinArena.Runtime** — tout ce qui touche Unity (physique, rendu, interface, réseau).
- **SpinArena.Editor** — configuration du projet et construction de l'APK.

| Module demandé | Implémentation | Assemblage | État |
|---|---|---|---|
| GameManager | `GameManager` : crée et relie tous les systèmes | Runtime | ✅ |
| BattleRoyaleManager | `BattleRound` (règles) + `BattleRoyaleManager` (moteur) | Core + Runtime | ✅ |
| PlayerManager | `PlayerRegistry` + `PlayerManager` | Core + Runtime | ✅ |
| TopManager | pool de toupies dans `BattleRoyaleManager`, `TopController`, `TopVisualFactory`, `TopCatalog` | Core + Runtime | ✅ base |
| PhysicsManager | réglages physiques dans `GameManager`, `CombatMath` | Core + Runtime | ✅ |
| AbilityManager | `AbilityManager` : commandes de combat, 6 capacités élémentaires, effets des cadeaux | Runtime | ✅ base (phase 5 : enrichir) |
| ArenaManager | `ArenaManager` : arène électrique procédurale | Runtime | ✅ 1 arène sur 6 |
| GiftManager | `GiftProcessor` (anti-doublons, séries, file, limites, historique) + `GiftManager` | Core + Runtime | ✅ |
| TikTokConnector | `ILiveConnector` + `BridgeLiveConnector` + pont Termux | Core + Runtime + bridge | ✅ (à valider sur un vrai live) |
| ChatCommandManager | `ChatCommandParser`, `CommandRateLimiter`, `ChatCommandManager` | Core + Runtime | ✅ |
| RankingManager | classement dans `PlayerRegistry` (`!top`) | Core | ✅ base (saisons : phase 9) |
| ProgressionManager | `LevelCurve`, `ProgressionRules` (200 niveaux) | Core | ✅ base (missions, succès : phase 9) |
| InventoryManager | `PlayerProfile.ownedTops` | Core | ⏳ phase 9 |
| SaveManager | sauvegarde locale atomique dans `PlayerManager` | Runtime | ✅ local (serveur : phase 9) |
| UIManager | `HudManager`, `AdminPanel`, `AvatarCache`, `UiFactory` | Runtime | ✅ base |
| EventManager | pluie de météores dans `AbilityManager` | Runtime | ⏳ boss et autres événements : phase 10 |

**Remplacer le connecteur** : le jeu ne connaît que l'interface `ILiveConnector`. Un futur accès officiel, un autre pont ou un service payant se branchent en écrivant une seule classe.

**Configuration sans recompiler** : `Assets/SpinArena/Resources/Config/game_config.json` (durées, joueurs max, anti-abus, règles des cadeaux, récompenses, arène, effets). L'administration enregistre ses changements dans une copie sur le téléphone.

## 3. Dépendances

| Dépendance | Rôle | Coût |
|---|---|---|
| Unity 6000.0.32f1 + URP 17.0.3 + UGUI | moteur, rendu, interface | licence Unity Personal existante |
| game-ci/unity-builder (GitHub Actions) | compilation APK / Windows | gratuit (minutes Actions) |
| Node.js (Termux) | exécution du pont | gratuit |
| tiktok-live-connector 2.5 | lecture du live (non officiel) | gratuit |
| ws 8 | WebSocket local pont → jeu | gratuit |

Aucune ressource payante, aucun modèle 3D externe à ce stade.

## 4. Ressources 3D

| Ressource | Aujourd'hui | Cible |
|---|---|---|
| 42 toupies (6 éléments × 7 raretés) | générées par code : pointe, disque, lames inclinées, anneau lumineux, couronne, emblème ; couleurs et forme propres à chaque toupie | modèles sculptés FBX originaux pour les raretés Épique → Divin, skins et évolutions |
| Arène électrique | stade en cuvette procédural, plaques métalliques et lignes lumineuses générées, murets, ouvertures d'éjection, pylônes, anneaux | 5 autres arènes (volcan, glace, forêt, ténèbres, céleste) avec pièges |
| Effets | particules mutualisées (étincelles, explosions, colonnes d'arrivée, ondes, confettis, météores), traînées, bloom | effets élémentaires dédiés |
| Interface | panneaux générés, police intégrée | identité graphique finale (logo, police, icônes) |

Le générateur de toupies n'est pas un « modèle temporaire » jeté plus tard : il garantit des toupies cohérentes pour toute la collection. Les modèles sculptés le remplaceront toupie par toupie.

## 5. Fonctionnement du battle royale

1. **Inscriptions** (25 s) : `!join` ajoute le spectateur ; sa toupie apparaît sur un cercle de lancement avec une colonne de lumière et une carte d'entrée (photo + pseudo + toupie). Les joueurs de la manche précédente encore actifs dans le chat sont réinscrits automatiquement ; ceux qui attendaient passent en priorité.
2. **Compte à rebours** (5 s) puis **lancement** : toutes les toupies partent en tournant autour du centre.
3. **Combat** autonome :
   - le sol en cuvette ramène les toupies vers le centre ; les toupies rapides glissent en orbite ;
   - chaque toupie a une IA selon sa classe (attaque, défense, endurance, équilibre) et évite le bord, surtout face aux ouvertures ;
   - chocs : dégâts = vitesse relative × puissance ÷ défense × affinité élémentaire (±25 %) ; projection selon puissance et stabilité ;
   - l'énergie baisse aussi naturellement (frottements) ; une toupie faible penche et oscille.
   - entrées tardives possibles pendant les 10 premières secondes, sinon manche suivante.
4. **Éliminations** : énergie à zéro (la toupie bascule) ou **éjection** hors de l'arène. L'élimination est créditée au dernier attaquant (6 s).
5. **Mort subite** après 150 s : l'énergie baisse 4× plus vite ; départage par énergie restante au bout d'une minute.
6. **Victoire** : plan sur le vainqueur, confettis, carte du champion ; XP et éclats pour tous les participants ; sauvegarde.

Commandes gratuites en combat : `!attaque` (+15 % de dégâts, 5 s), `!bouclier` (−25 % de dégâts, 4 s), `!turbo` (élan vers la cible), `!special` (capacité de l'élément). Temps de recharge par joueur et limite de rafale.

## 6. Stratégie d'intégration TikTok

- **Pas d'API officielle publique** pour lire les cadeaux et le chat d'un live dans un jeu tiers. On utilise **TikTok-Live-Connector** (non officiel, rétro-ingénierie, même bibliothèque que Sebrakon Quiz Live). Elle peut casser lors d'une mise à jour TikTok : le pont est isolé pour être mis à jour seul.
- Le pont accepte l'ancien et le nouveau format de la bibliothèque, ignore les doublons après reconnexion, ne coupe pas la connexion pour de petits incidents de décodage et se reconnecte seul (attente patiente si le live n'a pas commencé).
- **Photos de profil** : URL fournie par TikTok dans les événements ; téléchargées, découpées en rond, mises en cache (mémoire + disque, 7 jours). Sinon : avatar généré à partir de l'identifiant. Rien n'est inventé, aucune protection n'est contournée.
- **Cadeaux** : règles configurables (nom exact ou valeur) → 4 niveaux d'effet. Séries comptées sans double comptage, gros paquets découpés en quelques activations plus intenses, débit global limité, plafond par spectateur, rien n'est perdu (file d'attente), historique.
- **Équilibre** : un cadeau ne peut ni éliminer directement, ni rendre invulnérable (réduction plafonnée à 60 %), ni soigner plus de 8 % par activation. Les roses aident toutes les toupies. Les gros cadeaux déclenchent des événements qui touchent tout le monde au hasard.

## 7. Planning

| Phase | Contenu | État |
|---|---|---|
| 1 | Environnement, projet Unity, architecture, compilation cloud, tests | ✅ code prêt — en attente du dépôt GitHub |
| 2 | Arène 3D et premières toupies | ✅ arène électrique + 42 toupies générées |
| 3 | Physique de rotation et collisions | ✅ base, à régler après le banc d'essai |
| 4 | Battle royale à 50 toupies | ✅ base, à mesurer (images/s) |
| 5 | Éléments et pouvoirs | ◐ affinités + 6 capacités ; à enrichir (brûlure, poison, gel…) |
| 6 | Interface graphique | ◐ HUD complet ; animations et identité finale à faire |
| 7 | Profils et photos TikTok | ◐ code prêt, à valider en live |
| 8 | Commentaires et cadeaux | ◐ code prêt, à valider en live ; régie à distance à faire |
| 9 | Progression, inventaire, missions, saisons, serveur | ◐ niveaux, XP, éclats, sauvegarde locale |
| 10 | Boss, événements, 5 arènes | ⏳ (météores déjà faits) |
| 11 | Optimisation, tests longue durée, préparation des lives | ⏳ |
