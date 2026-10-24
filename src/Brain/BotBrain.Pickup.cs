using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Виды цели подбора: меньше — лучше, внутри вида — короче маршрут.
        public const int KindTrap = 0, KindGun = 1, KindNet = 2, KindBomb = 3, KindPresent = 4, KindBox = 5;

        // Ручка силы для этапа 3: надбавка к пути до ящика за удар снизу и лотерею.
        public const float BoxExtra = 96f;

        public const float ReachMargin = 16f, DuckHalfHeight = 10f;

        // Огнемёт выпускает не пули, а огонь (SmallFire): он падает в 35–85 px от ствола.
        public const float FlameReach = 60f;

        // Ракетница стреляет вспышкой (Flare, ±11.5° наугад): на 100 px попадает в ~60% выстрелов.
        public const float FlareReach = 100f;

        // Дальность — у патрона, за вычетом разброса и запаса на дуло. Но не дальше, чем
        // попадает разброс: пуля уходит на ±15°·(1 − accuracy), а утку высотой 20 px
        // задевает, пока d·tg(разброса) ≤ 2·10. Пулям с гравитацией — RangeX.
        public static float FireReach(float range, float rangeVariation, float accuracy, bool gravity)
        {
            if (gravity || range <= 0f) return RangeX;
            float reach = range - rangeVariation - ReachMargin;
            float spread = (1f - accuracy) * 15f;
            if (spread > 0f)
            {
                float half = 2f * DuckHalfHeight / (float)Math.Tan(spread * Math.PI / 180.0);
                if (half < reach) reach = half;
            }
            return reach;
        }

        // Ящик в одном ряду со стволом, только дальше на BoxExtra.
        public static bool BetterPickup(int kind, float len, int bestKind, float bestLen)
        {
            int tier = kind == KindBox ? KindGun : kind;
            int bestTier = bestKind == KindBox ? KindGun : bestKind;
            if (tier != bestTier) return tier < bestTier;
            return len + (kind == KindBox ? BoxExtra : 0f) < bestLen + (bestKind == KindBox ? BoxExtra : 0f);
        }
    }
}
