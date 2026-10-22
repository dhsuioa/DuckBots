using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Выбор цели подбора: ящик в одном ряду со стволом, но с надбавкой за удар и лотерею.
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
    }
}
