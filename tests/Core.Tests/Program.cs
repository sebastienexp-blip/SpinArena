using System;
using System.Collections.Generic;
using System.Linq;
using SpinArena.Core.Battle;
using SpinArena.Core.Combat;
using SpinArena.Core.Commands;
using SpinArena.Core.Elements;
using SpinArena.Core.Live;
using SpinArena.Core.Players;
using SpinArena.Core.Progression;
using SpinArena.Core.Tops;

// Tests du noyau de SPIN ARENA (logique pure, sans Unity). Lancer : dotnet run --project tests/Core.Tests
static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new List<string>();

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Failures.Add(name); }
    }

    static void Test(string group, Action body)
    {
        try { body(); }
        catch (Exception e) { _fail++; Failures.Add(group + " : exception " + e.GetType().Name + " " + e.Message); }
    }

    static int Main()
    {
        Test("éléments", Elements);
        Test("catalogue", Catalog);
        Test("combat", Combat);
        Test("manche", Round);
        Test("manche-temps", RoundTimeout);
        Test("manche-tardifs", RoundLateJoin);
        Test("commandes", Commands);
        Test("anti-abus", RateLimit);
        Test("cadeaux-séries", GiftStreaks);
        Test("cadeaux-débit", GiftThrottle);
        Test("cadeaux-règles", GiftRules);
        Test("joueurs", Players);
        Test("progression", Progression);
        Test("cadeaux-images-actions", GiftWiring);
        Test("déblocages-séries-primes", Unlocks);
        Test("retour-en-jeu", Revive);

        Console.WriteLine($"Tests du noyau : {_pass} réussis, {_fail} échoués");
        foreach (var f in Failures) Console.WriteLine("  ÉCHEC : " + f);
        return _fail == 0 ? 0 : 1;
    }

    static void Elements()
    {
        var all = (Element[])Enum.GetValues(typeof(Element));
        foreach (var a in all)
        {
            int strong = all.Count(d => ElementChart.Affinity(a, d) > 0);
            int weak = all.Count(d => ElementChart.Affinity(a, d) < 0);
            Check(strong == 2 && weak == 2, $"{a} : 2 forces et 2 faiblesses (trouvé {strong}/{weak})");
            foreach (var d in all) Check(ElementChart.Affinity(a, d) == -ElementChart.Affinity(d, a), $"symétrie {a}/{d}");
        }
        Check(ElementChart.Affinity(Element.Fire, Element.Nature) > 0, "Feu bat Nature");
        Check(ElementChart.Affinity(Element.Ice, Element.Electricity) > 0, "Glace bat Électricité");
        Check(ElementChart.Affinity(Element.Darkness, Element.Light) > 0, "Ténèbres bat Lumière");
        Check(ElementChart.Affinity(Element.Light, Element.Fire) > 0, "Lumière bat Feu");
        Check(ElementChart.Affinity(Element.Fire, Element.Electricity) == 0, "Feu/Électricité neutre");
    }

    static void Catalog()
    {
        var c = TopCatalog.CreateDefault();
        Check(c.Count == 42, "42 toupies par défaut");
        Check(c.All.Select(t => t.name).Distinct().Count() == 42, "noms uniques");
        Check(c.All.Select(t => t.id).Distinct().Count() == 42, "identifiants uniques");
        foreach (Rarity r in Enum.GetValues(typeof(Rarity))) Check(c.ByRarity(r).Count() == 6, "6 toupies " + r);
        Check(c.All.All(t => t.power is >= 1 and <= 100 && t.speed is >= 1 and <= 100 && t.defense is >= 1 and <= 100 && t.stability is >= 1 and <= 100), "stats dans 1..100");
        Check(c.ByRarity(Rarity.Divine).Min(t => t.TotalStats) > c.ByRarity(Rarity.Common).Max(t => t.TotalStats), "divin > commun");
        var classes = c.All.Select(t => t.Class).Distinct().Count();
        Check(classes >= 3, "au moins 3 styles de combat (" + classes + ")");
        Check(c.StarterFor("12345").id == c.StarterFor("12345").id, "toupie de départ stable");
        Check(c.StarterFor("12345").rarity == Rarity.Common, "toupie de départ commune");
        bool threw = false;
        try { c.Add(new TopDefinition { id = "fire_0" }); } catch (ArgumentException) { threw = true; }
        Check(threw, "refus d'un identifiant en double");
        c.Add(new TopDefinition { id = "custom_1", name = "Test", power = 500, speed = -3 });
        Check(c.Get("custom_1").power == 100 && c.Get("custom_1").speed == 1, "stats ajoutées bornées");
    }

    static void Combat()
    {
        var c = TopCatalog.CreateDefault();
        var fire = CombatStats.From(c.Get("fire_3"), 1);
        var nature = CombatStats.From(c.Get("nature_3"), 1);
        float slow = CombatMath.ImpactDamage(fire, nature, 0.5f);
        Check(slow == 0f, "frôlement sans dégâts");
        float hit = CombatMath.ImpactDamage(fire, nature, 8f);
        float back = CombatMath.ImpactDamage(nature, fire, 8f);
        Check(hit > 0 && hit > back, "avantage élémentaire Feu sur Nature");
        var shielded = nature; shielded.damageReduction = 5f;
        float capped = CombatMath.ImpactDamage(fire, shielded, 8f);
        Check(capped > 0 && Math.Abs(capped - hit * (1 - CombatMath.MaxReduction)) < 0.01f, "réduction plafonnée à 60 %");
        Check(CombatMath.ImpactDamage(fire, nature, 1000f) == CombatMath.ImpactDamage(fire, nature, 30f), "vitesse plafonnée");
        var lv200 = CombatStats.From(c.Get("fire_3"), 200);
        Check(lv200.power <= fire.power * 1.151f, "bonus de niveau ≤ 15 %");
        Check(CombatMath.SpinDecayPerSecond(nature, 4f) > CombatMath.SpinDecayPerSecond(nature, 1f), "mort subite accélère la perte");
        // Durée de vie sans combat : entre 1,5 et 5 minutes (manche lisible).
        foreach (var t in c.All)
        {
            var s = CombatStats.From(t, 1);
            float life = t.MaxSpin / CombatMath.SpinDecayPerSecond(s, 1f);
            Check(life > 90 && life < 400, $"autonomie {t.name} = {life:0}s");
        }
    }

    static BattleRound NewRound(int players, out List<string> ids)
    {
        var r = new BattleRound(new RoundSettings { lobbySeconds = 5, countdownSeconds = 3, maxFightSeconds = 60, victorySeconds = 4, minParticipants = 2, maxParticipants = 50 });
        ids = Enumerable.Range(0, players).Select(i => "p" + i).ToList();
        foreach (var id in ids) r.RequestJoin(id);
        return r;
    }

    static void Round()
    {
        var r = NewRound(50, out var ids);
        Check(r.Participants.Count == 50, "50 inscrits");
        Check(r.RequestJoin("p0") == BattleRound.JoinResult.AlreadyIn, "pas de double inscription");
        Check(r.RequestJoin("extra") == BattleRound.JoinResult.Queued, "51e joueur mis en file");
        string winner = "?"; int elimEvents = 0;
        r.WinnerDecided += w => winner = w;
        r.ParticipantEliminated += _ => elimEvents++;
        r.Tick(5.01f); Check(r.Phase == RoundPhase.Countdown, "lobby → compte à rebours");
        Check(!r.Eliminate("p1", EliminationReason.RingOut), "pas d'élimination hors combat");
        r.Tick(3.01f); Check(r.Phase == RoundPhase.Fighting, "compte à rebours → combat");
        for (int i = 1; i < 50; i++) r.Eliminate("p" + i, i % 2 == 0 ? EliminationReason.RingOut : EliminationReason.OutOfSpin, "p0");
        Check(!r.Eliminate("p1", EliminationReason.RingOut), "élimination en double ignorée");
        Check(r.Phase == RoundPhase.Victory && winner == "p0", "dernier survivant gagne");
        Check(elimEvents == 49, "49 éliminations");
        Check(r.Get("p0").placement == 1 && r.Get("p1").placement == 50 && r.Get("p49").placement == 2, "classement final");
        Check(r.Get("p0").eliminations == 49, "éliminations attribuées");
        r.Tick(4.01f);
        Check(r.Phase == RoundPhase.Lobby && r.RoundNumber == 2, "nouvelle manche");
        Check(r.Participants.Count == 50 && r.Get("extra") != null, "réinscription auto + file d'attente (limite 50)");

        var lone = new BattleRound(new RoundSettings { lobbySeconds = 1, minParticipants = 2 });
        lone.RequestJoin("solo");
        lone.Tick(10f);
        Check(lone.Phase == RoundPhase.Lobby, "attente d'au moins 2 joueurs");
        var paused = NewRound(3, out _); paused.Paused = true; paused.Tick(100f);
        Check(paused.Phase == RoundPhase.Lobby, "pause respectée");
    }

    static void RoundTimeout()
    {
        var r = NewRound(4, out var ids);
        r.StandingProvider = () => new[] { "p2", "p0", "p3", "p1" };
        bool sd = false; string winner = null;
        r.SuddenDeathStarted += () => sd = true;
        r.WinnerDecided += w => winner = w;
        r.Tick(5.01f); r.Tick(3.01f);
        r.Tick(61f);
        Check(sd && r.SuddenDeath && r.DecayFactor > 1f, "mort subite après le temps réglementaire");
        r.Tick(61f);
        Check(r.Phase == RoundPhase.Victory && winner == "p2", "départage par le classement après la mort subite");
        Check(r.Get("p1").placement == 4 && r.Get("p0").placement == 2, "placements du départage");
    }

    static void RoundLateJoin()
    {
        var r = NewRound(2, out _);
        r.Tick(5.01f); r.Tick(3.01f); r.Tick(5f);
        Check(r.RequestJoin("late") == BattleRound.JoinResult.Joined, "entrée tardive autorisée au début");
        r.Tick(6f);
        r.Settings.joinAnytime = false;
        Check(r.RequestJoin("later") == BattleRound.JoinResult.Queued, "sans « à tout moment » : manche suivante");
        Check(r.RequestJoin("later") == BattleRound.JoinResult.AlreadyQueued, "pas de double file");
        r.Settings.joinAnytime = true;
        r.Tick(55f);
        Check(r.SuddenDeath && r.RequestJoin("anytime") == BattleRound.JoinResult.Joined, "à tout moment : entrée même en mort subite");
    }

    static void Commands()
    {
        Check(ChatCommandParser.Parse("!join") == ChatCommand.Join, "!join");
        Check(ChatCommandParser.Parse("  !JOIN let's go") == ChatCommand.Join, "!JOIN + texte");
        Check(ChatCommandParser.Parse("！join") == ChatCommand.Join, "point d'exclamation pleine chasse");
        Check(ChatCommandParser.Parse("!Spécial") == ChatCommand.Special, "accents ignorés");
        Check(ChatCommandParser.Parse("!attaque!!!") == ChatCommand.Attack, "!attaque!!!");
        Check(ChatCommandParser.Parse("! bouclier") == ChatCommand.Shield, "espace après !");
        Check(ChatCommandParser.Parse("join") == ChatCommand.None, "sans ! ignoré");
        Check(ChatCommandParser.Parse("!joinx") == ChatCommand.None, "commande inconnue");
        Check(ChatCommandParser.Parse("") == ChatCommand.None, "vide");
        Check(ChatCommandParser.Parse("!profil") == ChatCommand.Profile && ChatCommandParser.Parse("!top") == ChatCommand.Top
              && ChatCommandParser.Parse("!toupie") == ChatCommand.MyTop && ChatCommandParser.Parse("!stats") == ChatCommand.Stats
              && ChatCommandParser.Parse("!help") == ChatCommand.Help && ChatCommandParser.Parse("!turbo") == ChatCommand.Turbo, "toutes les commandes");
    }

    static void RateLimit()
    {
        var l = new CommandRateLimiter(new CommandLimits { combatCooldown = 8, burstMax = 3, burstWindow = 30, globalInfoPerMinute = 2, infoCooldown = 20 });
        Check(l.Check("a", ChatCommand.Attack, 0) == CommandVerdict.Accepted, "1re attaque");
        Check(l.Check("a", ChatCommand.Attack, 3) == CommandVerdict.Cooldown, "recharge");
        Check(l.Check("b", ChatCommand.Attack, 3) == CommandVerdict.Accepted, "autre joueur non bloqué");
        Check(l.Check("a", ChatCommand.Attack, 8.5) == CommandVerdict.Accepted, "après recharge");
        Check(l.Check("a", ChatCommand.Shield, 9) == CommandVerdict.Accepted, "autre commande");
        Check(l.Check("a", ChatCommand.Turbo, 10) == CommandVerdict.Spam, "rafale bloquée");
        Check(l.Check("a", ChatCommand.Turbo, 41) == CommandVerdict.Accepted, "rafale libérée");
        Check(l.Check("x", ChatCommand.Stats, 50) == CommandVerdict.Accepted && l.Check("y", ChatCommand.Stats, 51) == CommandVerdict.Accepted, "infos");
        Check(l.Check("z", ChatCommand.Stats, 52) == CommandVerdict.GlobalLimit, "limite globale d'affichage");
        l.Prune(10000);
    }

    static LiveEvent Gift(string msg, string user, string name, int diamonds, int repeat, bool end, bool streak = true) =>
        new LiveEvent { type = LiveEventType.Gift, msgId = msg, user = new LiveUser { userId = user, nickname = user }, giftId = name, giftName = name, diamondCount = diamonds, repeatCount = repeat, repeatEnd = end, streakable = streak };

    static void GiftStreaks()
    {
        var g = new GiftProcessor(GiftSettings.CreateDefault());
        int total = 0;
        for (int i = 1; i <= 10; i++) total += g.Receive(Gift("m1", "u1", "Rose", 1, i, false), 0);
        total += g.Receive(Gift("m1", "u1", "Rose", 1, 10, true), 0);
        Check(total == 10, "série de 10 roses = 10 (trouvé " + total + ")");
        Check(g.Receive(Gift("m1", "u1", "Rose", 1, 10, true), 0) == 0 && g.Duplicates >= 1, "doublon ignoré");
        Check(g.Receive(Gift("m2", "u1", "Rose", 1, 1, false), 1) == 1, "nouvelle série repart de 1");
        Check(g.Receive(Gift("m3", "u2", "Lion", 29999, 3, true, false), 1) == 3, "cadeau non enchaînable ×3");
        Check(g.Receive(Gift("m4", "u2", "Rose", 1, 7, true), 2) == 7, "série vue seulement à la fin");
        // Événement sans identifiant de message : compté quand même.
        Check(g.Receive(Gift(null, "u3", "Rose", 1, 1, true, false), 3) == 1, "sans identifiant");
    }

    static void GiftThrottle()
    {
        var s = GiftSettings.CreateDefault();
        s.maxActivationsPerSecond = 2; s.maxActivationsPerGift = 5;
        var g = new GiftProcessor(s);
        g.Receive(Gift("big", "u1", "Rose", 1, 100, true, false), 0);
        Check(g.PendingCount == 5, "100 roses → 5 activations (trouvé " + g.PendingCount + ")");
        var first = g.Dequeue(0);
        Check(first.Count == 2, "débit global 2/s");
        Check(first.Sum(a => a.count) == 40, "intensité conservée");
        var all = new List<GiftActivation>(first);
        for (double t = 0.5; t < 10; t += 0.5) all.AddRange(g.Dequeue(t));
        Check(all.Count == 5 && all.Sum(a => a.count) == 100 && g.PendingCount == 0, "aucun cadeau perdu");

        var g2 = new GiftProcessor(GiftSettings.CreateDefault());
        g2.Receive(Gift("a", "u9", "Galaxy", 1000, 1, true, false), 0);
        g2.Receive(Gift("b", "u9", "Galaxy", 1000, 1, true, false), 0);
        var d = g2.Dequeue(0);
        Check(d.Count == 1 && d[0].tier == GiftTier.Legendary, "événement légendaire : 1 à la fois par spectateur");
        Check(g2.Dequeue(30).Count == 0 && g2.Dequeue(61).Count == 1, "recharge de 60 s respectée, sans perte");
        Check(g2.History.Count >= 2, "historique");
    }

    static void GiftRules()
    {
        var g = new GiftProcessor(GiftSettings.CreateDefault());
        Check(g.FindRule("rose", 1).tier == GiftTier.Collective, "Rose (casse ignorée)");
        Check(g.FindRule("Donut", 30).tier == GiftTier.Power, "30 diamants : pouvoir");
        Check(g.FindRule("Inconnu", 299).tier == GiftTier.SpecialAttack, "299 : attaque spéciale");
        Check(g.FindRule("Lion", 29999).tier == GiftTier.Legendary, "Lion : légendaire");
    }

    static void Players()
    {
        var cat = TopCatalog.CreateDefault();
        var reg = new PlayerRegistry(cat);
        var u = new LiveUser { userId = "42", uniqueId = "seb", nickname = "Seb", avatarUrl = "https://exemple/a.jpg" };
        var p = reg.GetOrCreate(u, 1000, out bool created);
        Check(created && p.selectedTopId != null && p.ownedTops.Count == 1, "création avec toupie de départ");
        var p2 = reg.GetOrCreate(new LiveUser { userId = "42", nickname = "Seb2" }, 2000, out created);
        Check(!created && ReferenceEquals(p, p2) && p2.nickname == "Seb2" && p2.avatarUrl == "https://exemple/a.jpg", "mise à jour sans effacer la photo");
        p.ownedTops.Add("inexistante"); p.selectedTopId = "inexistante";
        reg.Load(new[] { p });
        Check(reg.Get("42").selectedTopId != "inexistante" && !reg.Get("42").ownedTops.Contains("inexistante"), "toupie inconnue retirée au chargement");
        var b = reg.GetOrCreate(new LiveUser { uniqueId = "sans_id" }, 1, out _);
        Check(b.id == "sans_id", "clé de secours = @pseudo");
        p.wins = 3; b.wins = 1;
        Check(reg.Leaderboard(10)[0].id == "42" && reg.RankOf("sans_id") == 2, "classement");
    }

    /// <summary>
    /// Vérifie que chaque cadeau configuré a la bonne image, une action qui existe et qui est programmée dans le jeu,
    /// et que le fichier de configuration et les valeurs par défaut du code disent la même chose.
    /// </summary>
    static void GiftWiring()
    {
        string root = FindRoot();
        var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/SpinArena/Resources/Config/game_config.json")));
        var rules = json.RootElement.GetProperty("gifts").GetProperty("rules").EnumerateArray().ToList();
        var defaults = GiftSettings.CreateDefault().rules;
        Check(rules.Count == defaults.Count, $"config et code : même nombre de règles ({rules.Count}/{defaults.Count})");
        string runSource = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/SpinArena/Scripts/Runtime/Live/GiftManager.cs"));
        string giftDir = System.IO.Path.Combine(root, "Assets/SpinArena/Resources/Gifts");
        var usedImages = new HashSet<string>();
        for (int i = 0; i < rules.Count; i++)
        {
            string name = rules[i].GetProperty("giftName").GetString();
            string effect = rules[i].GetProperty("effect").GetString();
            int tier = rules[i].GetProperty("tier").GetInt32();
            var d = defaults[Math.Min(i, defaults.Count - 1)];
            Check(d.giftName == (string.IsNullOrEmpty(name) ? null : name) || (string.IsNullOrEmpty(d.giftName) && string.IsNullOrEmpty(name)), $"règle {i} : même cadeau ({name} / {d.giftName})");
            Check(d.effect == effect && (int)d.tier == tier, $"{name} : même action et niveau dans la config et le code");
            Check(GiftEffects.IsKnown(effect), $"{name} : action « {effect} » connue");
            Check(runSource.Contains("case \"" + effect + "\":"), $"action « {effect} » programmée dans GiftManager");
            if (string.IsNullOrEmpty(name)) continue;
            string slug = name.Trim().ToLowerInvariant().Replace(' ', '_');
            if (slug == "donut") slug = "doughnut";
            usedImages.Add(slug);
            Check(System.IO.File.Exists(System.IO.Path.Combine(giftDir, slug + ".png")), $"{name} : image {slug}.png présente");
            // Chaîne complète : un événement « cadeau » avec ce nom donne bien cette action et retrouve la bonne image.
            var g = new GiftProcessor(GiftSettings.CreateDefault());
            g.Receive(new LiveEvent { type = LiveEventType.Gift, msgId = "w" + i, user = new LiveUser { userId = "u" }, giftName = name.ToUpperInvariant(), diamondCount = 1, repeatCount = 1, repeatEnd = true }, 0);
            var a = g.Dequeue(0).FirstOrDefault();
            Check(a != null && a.effect == effect && a.ruleName == name, $"{name} : événement → action {effect} (trouvé {a?.effect}, image de {a?.ruleName})");
        }
        foreach (var f in System.IO.Directory.GetFiles(giftDir, "*.png"))
            Check(usedImages.Contains(System.IO.Path.GetFileNameWithoutExtension(f)), "image sans cadeau : " + System.IO.Path.GetFileName(f));
        Check(GiftProcessor.NormalizeName("Finger-Heart ") == GiftProcessor.NormalizeName("finger heart"), "noms comparés sans espaces ni tirets");
        Check(GiftEffects.EffectIds.Length == GiftEffects.EffectNames.Length, "chaque action a un nom affiché");
    }

    static void Unlocks()
    {
        var cat = TopCatalog.CreateDefault();
        var reg = new PlayerRegistry(cat);
        var p = reg.GetOrCreate(new LiveUser { userId = "unlock" }, 0, out _);
        var start = cat.Get(p.selectedTopId);
        Check(TopUnlocks.Apply(p, cat).Count == 0, "niveau 1 : rien de nouveau");
        p.level = 15;
        var got = TopUnlocks.Apply(p, cat);
        Check(got.Count == 2 && got.All(t => t.element == start.element), "niveau 15 : peu commun + rare du même élément");
        Check(cat.Get(p.selectedTopId).rarity == Rarity.Rare, "la meilleure toupie est équipée");
        Check(TopUnlocks.Apply(p, cat).Count == 0, "pas de doublon");
        p.level = 200; TopUnlocks.Apply(p, cat);
        Check(p.ownedTops.Count == 7 && cat.Get(p.selectedTopId).rarity == Rarity.Divine, "niveau 200 : collection complète de l'élément");
        Check(TopUnlocks.MaxRarityFor(4) == Rarity.Common && TopUnlocks.MaxRarityFor(5) == Rarity.Uncommon && TopUnlocks.MaxRarityFor(120) == Rarity.Divine, "paliers de rareté");

        var w = new PlayerProfile { id = "w" };
        var rs = new RewardSettings();
        var o1 = ProgressionRules.Apply(w, new RoundResult { placement = 1, participants = 10 }, rs);
        var o2 = ProgressionRules.Apply(w, new RoundResult { placement = 1, participants = 10 }, rs);
        Check(w.winStreak == 2 && o2.xpGained == o1.xpGained + rs.xpPerStreak, "série de victoires et bonus");
        ProgressionRules.Apply(w, new RoundResult { placement = 4, participants = 10 }, rs);
        Check(w.winStreak == 0 && w.bestWinStreak == 2, "série remise à zéro, record gardé");
        var b = new PlayerProfile { id = "b" };
        var ob = ProgressionRules.Apply(b, new RoundResult { placement = 5, participants = 10, bounties = 1 }, rs);
        var b2 = new PlayerProfile { id = "b2" };
        var ob2 = ProgressionRules.Apply(b2, new RoundResult { placement = 5, participants = 10 }, rs);
        Check(ob.xpGained - ob2.xpGained == rs.xpBounty && b.bountiesClaimed == 1, "prime : XP bonus");
    }

    static void Revive()
    {
        var r = NewRound(3, out _);
        Check(!r.Revive("p0"), "pas de retour hors combat");
        r.Tick(5.01f); r.Tick(3.01f);
        Check(!r.Revive("p0"), "pas de retour pour un joueur vivant");
        r.Eliminate("p1", EliminationReason.RingOut, "p0");
        Check(r.Revive("p1") && r.Get("p1").alive && r.AliveCount == 3, "retour en jeu");
        r.Eliminate("p1", EliminationReason.RingOut);
        Check(!r.Revive("p1"), "une seule fois par manche");
        r.Eliminate("p2", EliminationReason.OutOfSpin);
        Check(r.Phase == RoundPhase.Victory && r.WinnerId == "p0" && r.Get("p1").placement == 3, "classement après un retour");
    }

    static string FindRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
        return dir?.FullName ?? ".";
    }

    static void Progression()
    {
        Check(LevelCurve.XpToNext(1) < LevelCurve.XpToNext(100), "courbe croissante");
        Check(LevelCurve.XpToNext(200) == long.MaxValue, "niveau 200 = maximum");
        var p = new PlayerProfile { id = "x" };
        var o = ProgressionRules.Apply(p, new RoundResult { playerId = "x", placement = 1, eliminations = 3, survivedSeconds = 90, participants = 30 }, new RewardSettings());
        Check(p.wins == 1 && p.gamesPlayed == 1 && p.coins == o.coinsGained && o.xpGained > 300, "récompenses de victoire");
        Check(p.level >= 2, "montée de niveau");
        ProgressionRules.AddXp(p, long.MaxValue / 4);
        Check(p.level == 200 && p.xp == 0, "plafond 200");
        // Un joueur gratuit qui participe régulièrement progresse : ~10 manches par live.
        var f = new PlayerProfile { id = "f" };
        for (int i = 0; i < 10; i++) ProgressionRules.Apply(f, new RoundResult { placement = 20, eliminations = 1, survivedSeconds = 60, participants = 40 }, new RewardSettings());
        Check(f.level >= 4, "progression gratuite sur un live (niveau " + f.level + ")");
    }
}
