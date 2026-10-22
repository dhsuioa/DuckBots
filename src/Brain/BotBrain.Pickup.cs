using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Виды цели подбора: пойманный враг, ствол или холодное, сеткомёт, взрывчатка,
        // подарок, ящик. Чем меньше, тем лучше; внутри вида — короче маршрут.
        public const int KindTrap = 0, KindGun = 1, KindNet = 2, KindBomb = 3, KindPresent = 4, KindBox = 5;

        // Ручка силы для этапа 3: во сколько пикселей пути бот ценит удар по ящику снизу
        // и лотерею содержимого. Ящик — тот же ствол, только дальше на эту надбавку.
        public const float BoxExtra = 96f;

        // Лучше ли цель (kind, len) нынешней лучшей (bestKind, bestLen). Ящик стоит в одном
        // ряду со стволом: раньше вид решал раньше длины пути, и бот бежал к дальнему стволу
        // мимо ящика рядом — в сборке к выпуску 20.10 в 11 случаях из 32 ящик был ближе
        // даже с надбавкой.
        // С какого расстояния стрелять из ствола. Дальность — у патрона (AmmoType.range),
        // за вычетом её разброса и запаса на то, что пуля вылетает из ствола, а не из
        // центра утки. Но не дальше, чем попадает разброс: игра поворачивает пулю на
        // случайный угол до ±15°·(1 − accuracy) (Bullet), и утку высотой ~20 px пуля
        // находит хотя бы в половине выстрелов, пока d·tg(разброса) ≤ 2·10. Раньше на
        // всё была одна RangeX = 150: снайперка ждала, пока подойдёт, а мушкетон бил мимо.
        // Пули с гравитацией летят дугой — для них пока прежние RangeX.
        public const float ReachMargin = 16f, DuckHalfHeight = 10f;

        // Огнемёт пуль не выпускает: из ствола летит огонь (SmallFire) со скоростью 2–3.5,
        // трением 0.03 и гравитацией 0.07 за кадр и с высоты ствола падает на пол в 35–85
        // пикселях от него. Патрон у огнемёта для вида (AT9mm), по нему выходило 234.
        public const float FlameReach = 60f;

        // Ракетница тоже стреляет не своим патроном, а вспышкой (Flare): 14 px за кадр,
        // угол ±11.5° наугад и случайные толчки по вертикали. По прикидке она попадает
        // в утку в 84% выстрелов на 60 px, в 59% на 100 и в 25% на прежних 234, а
        // зарядов всего два.
        public const float FlareReach = 100f;

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

        public static bool BetterPickup(int kind, float len, int bestKind, float bestLen)
        {
            int tier = kind == KindBox ? KindGun : kind;
            int bestTier = bestKind == KindBox ? KindGun : bestKind;
            if (tier != bestTier) return tier < bestTier;
            return len + (kind == KindBox ? BoxExtra : 0f) < bestLen + (bestKind == KindBox ? BoxExtra : 0f);
        }
    }
}
