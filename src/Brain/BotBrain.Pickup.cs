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
        public static bool BetterPickup(int kind, float len, int bestKind, float bestLen)
        {
            int tier = kind == KindBox ? KindGun : kind;
            int bestTier = bestKind == KindBox ? KindGun : bestKind;
            if (tier != bestTier) return tier < bestTier;
            return len + (kind == KindBox ? BoxExtra : 0f) < bestLen + (bestKind == KindBox ? BoxExtra : 0f);
        }
    }
}
