using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Диагональная пружина бросает вверх (vSpeed −22, как прямая) и вбок (hSpeed ±10).
        // На city02 28.09 граф считал её вертикальной и строил через неё маршрут влево, а
        // она кидала вправо: бот возвращался, снова вставал на неё — петля и сторож.
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
            // Узел над пружиной — (5,5); полки — (1,1) слева и (8,1) справа.
            if (nav.MoveKnown(5 * 16 + 8f, 5 * 16 + 8f, 8 * 16 + 8f, 1 * 16 + 8f, out edge) != NavMap.KnownEdge)
                return "пружина: правая диагональная не бросает на полку справа";
            if (nav.MoveKnown(5 * 16 + 8f, 5 * 16 + 8f, 1 * 16 + 8f, 1 * 16 + 8f, out edge) == NavMap.KnownEdge)
                return "пружина: правая диагональная «бросает» влево — граф считает её вертикальной";
            return SpringFlightTest();
        }

        // Пружина под блоком: 20.10 на forest13_8 четверо ботов скакали на пружинах, пока
        // раунд не закончили руками. Бот допрыгивал до пружины программой и доигрывал её
        // дальше — программа кончается только приземлением, а пружина ловила его раньше
        // земли: подбрасывала в блок над собой и ловила снова. Кнопок программа к тому
        // кадру уже не жала, и бот не рулил. Подбросило — программу бросить и рулить к
        // цели полёта (ребро Launch, его шаг считается в полёте).
        private static string SpringFlightTest()
        {
            BotView v = new BotView();
            BotState s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.ey = -100f; v.holding = 1;
            v.grounded = false; v.facingRight = false;
            v.hasEnemyStep = true; v.enemyStepX = 80f; v.enemyStepY = -40f; v.enemyStepEdge = NavMap.Launch;
            s.prog = NavMap.Prog(1, 4, 0, 16, 0);   // «h4 вправо 0-16», как в журнале forest13_8
            s.progAt = 31;                          // кадр 30: программа уже ничего не жмёт
            v.kicked = true;
            ushort m = Decide(ref v, ref s);
            if (s.progAt != 0) return "пружина: подбросило, а бот доигрывает программу прыжка";
            if ((m & BotInput.RIGHT) == 0) return "пружина: в полёте с пружины бот не рулит к цели (кнопки " + m + ")";
            if (s.how != HowSpringFlight) return "причина: полёт с пружины записан как «" + s.how + "»";

            // Следующий кадр, толчка уже нет: рулить дальше, программа не возвращается.
            v.kicked = false;
            m = Decide(ref v, ref s);
            if ((m & BotInput.RIGHT) == 0 || s.progAt != 0) return "пружина: на втором кадре полёта бот бросил руль";
            return null;
        }
    }
}
