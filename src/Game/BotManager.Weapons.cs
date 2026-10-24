using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        // isFatal игра ставит сама: у музыкальных и прочих несмертельных он false. SuicidePistol
        // стреляет назад. Сеткомёт — оружие, только если на карте есть пропасть.
        private static bool Usable(Gun g)
        {
            if (g.ammo <= 0 || g is SuicidePistol || Unskilled(g)) return false;
            if (g is NetGun) return _nav != null && _nav.hasAbyssEdge;
            return g.isFatal;
        }

        // Своей логики для них пока нет: луч захвата обездвиживает стрелка, пультом водят машинку,
        // магнит тянет металл.
        private static bool Unskilled(Gun g)
        {
            return g is MindControlRay || g is RCController || g is MagnetGun;
        }

        // Общего предка у холодного оружия нет.
        private static bool IsMelee(Gun g)
        {
            return g is Sword || g is EnergyScimitar || g is OldEnergyScimi || g is TapedSword
                || g is Chainsaw || g is SledgeHammer || g is BoxingGloves;
        }

        // Взрыв, огонь или луч: стекло вблизи таким не бьют.
        private static bool Blasts(Gun g)
        {
            return Explodes(g) || g is FlameThrower || g is LaserRifle;
        }

        // Рвётся там, куда попал (римская свеча — тоже FlareGun).
        public static bool Explodes(Gun g)
        {
            return g is Bazooka || g is GrenadeLauncher || g is GrenadeCannon || g is FlareGun;
        }

        public static float GunReach(Gun g)
        {
            if (g == null || g.ammoType == null || IsMelee(g) || g is NetGun
                || g is Grenade || g is Mine || g is GoodBook) return BotBrain.RangeX;
            if (g is FlameThrower) return BotBrain.FlameReach;
            if (g is FlareGun) return BotBrain.FlareReach;
            AmmoType a = g.ammoType;
            return BotBrain.FireReach(a.range, a.rangeVariation, a.accuracy, a.affectedByGravity);
        }

        private static bool GlassGun(Gun g)
        {
            return g != null && Usable(g) && !IsMelee(g) && !(g is Grenade) && !(g is Mine)
                && !(g is NetGun) && !Blasts(g);
        }

        // Ноль — идти к самому врагу: холодное бьёт в упор, пустыми руками стрелять нечем.
        private static float FireRange(Duck me)
        {
            Gun g = me.holdObject as Gun;
            if (g == null || !Usable(g) || IsMelee(g)) return 0f;
            if (g is Grenade || g is Mine) return BotBrain.ThrowRange - 6f;
            if (g is NetGun) return 112f;   // сетка летит дугой и к 150 px опускается ниже ствола
            return GunReach(g) - 22f;
        }
    }
}
