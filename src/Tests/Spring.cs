using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Диагональная пружина бросает вверх и вбок только в свою сторону.
        private static string SpringTest()
        {
            string[] rows = {
                "..........",
                "..........",
                ".=......=.",
                "..........",
                "..........",
                "..........",
                "...../....",
                "##########",
            };
            NavMap nav = new NavMap();
            nav.Build(Cells(rows), rows[0].Length, rows.Length, 16f, 0f, 0f);
            int edge;
            // Узел над пружиной — (5,5), полки — (1,1) и (8,1).
            if (nav.MoveKnown(5 * 16 + 8f, 5 * 16 + 8f, 8 * 16 + 8f, 1 * 16 + 8f, out edge) != NavMap.KnownEdge)
                return "пружина: правая диагональная не бросает на полку справа";
            if (nav.MoveKnown(5 * 16 + 8f, 5 * 16 + 8f, 1 * 16 + 8f, 1 * 16 + 8f, out edge) == NavMap.KnownEdge)
                return "пружина: правая диагональная «бросает» влево — граф считает её вертикальной";
            return SpringFlightTest();
        }

        // Подбросила пружина под блоком: бросить программу прыжка и рулить к цели полёта.
        private static string SpringFlightTest()
        {
            BotView v = new BotView();
            BotState s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.ey = -100f; v.holding = 1;
            v.grounded = false; v.facingRight = false;
            v.hasEnemyStep = true; v.enemyStepX = 80f; v.enemyStepY = -40f; v.enemyStepEdge = NavMap.Launch;
            s.prog = NavMap.Prog(1, 4, 0, 16, 0);
            s.progAt = 31;
            v.kicked = true;
            ushort m = Decide(ref v, ref s);
            if (s.progAt != 0) return "пружина: подбросило, а бот доигрывает программу прыжка";
            if ((m & BotInput.RIGHT) == 0) return "пружина: в полёте с пружины бот не рулит к цели (кнопки " + m + ")";
            if (s.how != HowSpringFlight) return "причина: полёт с пружины записан как «" + s.how + "»";

            // Следующий кадр, толчка уже нет: рулить дальше.
            v.kicked = false;
            m = Decide(ref v, ref s);
            if ((m & BotInput.RIGHT) == 0 || s.progAt != 0) return "пружина: на втором кадре полёта бот бросил руль";
            return null;
        }
    }
}
