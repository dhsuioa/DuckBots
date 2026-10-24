using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Цель подбора: ящик в одном ряду со стволом, но дальше на BoxExtra.
        private static string PickupTest()
        {
            if (!BetterPickup(KindBox, 100f, KindGun, 300f))
                return "цель: ящик в 100 пикселях хуже ствола в 300 — бот побежит мимо ящика";
            if (BetterPickup(KindBox, 250f, KindGun, 300f))
                return "цель: ящик в 250 лучше ствола в 300, хотя надбавка " + BoxExtra + " делает его дальше";
            if (!BetterPickup(KindGun, 300f, KindBox, 250f))
                return "цель: сравнение несимметрично — ствол в 300 не лучше ящика в 250";
            if (!BetterPickup(KindTrap, 500f, KindBox, 50f))
                return "цель: пойманный враг должен быть важнее любого ящика";
            if (!BetterPickup(KindBox, 400f, KindPresent, 50f))
                return "цель: ящик должен стоять выше подарка, как ствол";
            if (!BetterPickup(KindGun, 300f, KindBomb, 50f))
                return "цель: ствол должен быть важнее гранаты";
            return null;
        }

        // Дальность по патрону: точный бьёт далеко, неточный ограничен разбросом, дуге — RangeX.
        private static string ReachTest()
        {
            float r = FireReach(1000f, 0f, 1f, false);
            if (Math.Abs(r - 984f) > 0.5f) return "дальность: снайперка (1000, точность 1) — " + r + ", ждали 984";
            r = FireReach(250f, 0f, 0.75f, false);
            if (Math.Abs(r - 234f) > 0.5f) return "дальность: пистолет (250, точность 0.75) — " + r + ", ждали 234";
            r = FireReach(115f, 10f, 0.6f, false);
            if (Math.Abs(r - 89f) > 0.5f) return "дальность: дробовик (115±10) — " + r + ", ждали 89";
            r = FireReach(600f, 0f, 0.3f, false);
            if (r > 120f || r < 95f) return "дальность: неточный ствол (600, точность 0.3) не ограничен разбросом — " + r;
            if (FireReach(4000f, 0f, 1f, true) != RangeX) return "дальность: пуля с гравитацией должна остаться на RangeX";
            return null;
        }
    }
}
