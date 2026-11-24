using System;
using System.Collections.Generic;
using SpinArena.Core.Players;

namespace SpinArena.Core.Progression
{
    [Serializable]
    public sealed class RewardSettings
    {
        public int xpParticipation = 40;
        public int xpPerElimination = 25;
        public int xpPerSurvivedSecond = 1;
        public int xpWin = 200;
        public int xpTop3 = 60;
        public int coinsParticipation = 10;
        public int coinsPerElimination = 5;
        public int coinsWin = 100;
        public int xpBounty = 60;          // élimination du joueur qui porte la prime
        public int xpPerStreak = 25;       // bonus par victoire d'affilée (à partir de la 2e)
    }

    public sealed class RoundResult
    {
        public string playerId;
        public int placement;       // 1 = vainqueur
        public int eliminations;
        public float survivedSeconds;
        public int participants;
        public int bounties;
    }

    public sealed class RewardOutcome
    {
        public long xpGained;
        public long coinsGained;
        public int levelsGained;
        public int newLevel;
        public int winStreak;
        public System.Collections.Generic.List<string> unlockedTops = new System.Collections.Generic.List<string>();
    }

    /// <summary>200 niveaux, courbe d'expérience croissante. Les récompenses viennent du jeu, jamais des cadeaux.</summary>
    public static class LevelCurve
    {
        public const int MaxLevel = 200;

        /// <summary>XP nécessaire pour passer du niveau n au niveau n+1.</summary>
        public static long XpToNext(int level)
        {
            if (level >= MaxLevel) return long.MaxValue;
            if (level < 1) level = 1;
            return (long)Math.Round(100 + 35 * Math.Pow(level, 1.45));
        }

        public static long TotalXpForLevel(int level)
        {
            long t = 0;
            for (int i = 1; i < Math.Min(level, MaxLevel); i++) t += XpToNext(i);
            return t;
        }
    }

    public static class ProgressionRules
    {
        public static RewardOutcome Apply(PlayerProfile p, RoundResult r, RewardSettings s)
        {
            s = s ?? new RewardSettings();
            long xp = s.xpParticipation
                      + s.xpPerElimination * (long)r.eliminations
                      + s.xpPerSurvivedSecond * (long)Math.Min(r.survivedSeconds, 300f);
            long coins = s.coinsParticipation + s.coinsPerElimination * (long)r.eliminations;
            if (r.placement == 1) { xp += s.xpWin; coins += s.coinsWin; }
            else if (r.placement > 0 && r.placement <= 3) xp += s.xpTop3;
            xp += s.xpBounty * (long)Math.Max(0, r.bounties);

            p.gamesPlayed++;
            p.eliminations += r.eliminations;
            p.bountiesClaimed += Math.Max(0, r.bounties);
            if (r.placement == 1)
            {
                p.wins++;
                p.winStreak++;
                if (p.winStreak > p.bestWinStreak) p.bestWinStreak = p.winStreak;
                if (p.winStreak >= 2) xp += s.xpPerStreak * (long)(p.winStreak - 1);
            }
            else p.winStreak = 0;
            if (r.placement > 0 && (p.bestPlacement == 0 || r.placement < p.bestPlacement)) p.bestPlacement = r.placement;
            p.coins += coins;
            p.seasonPoints += (int)Math.Max(0, r.participants - r.placement + 1) + r.eliminations * 2;

            int before = p.level;
            AddXp(p, xp);
            return new RewardOutcome { xpGained = xp, coinsGained = coins, levelsGained = p.level - before, newLevel = p.level, winStreak = p.winStreak };
        }

        public static void AddXp(PlayerProfile p, long xp)
        {
            if (xp <= 0) return;
            p.totalXp += xp;
            if (p.level >= LevelCurve.MaxLevel) { p.level = LevelCurve.MaxLevel; p.xp = 0; return; }
            p.xp += xp;
            while (p.level < LevelCurve.MaxLevel && p.xp >= LevelCurve.XpToNext(p.level))
            {
                p.xp -= LevelCurve.XpToNext(p.level);
                p.level++;
            }
            if (p.level >= LevelCurve.MaxLevel) { p.level = LevelCurve.MaxLevel; p.xp = 0; }
        }
    }
}
