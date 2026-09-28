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
            return null;
        }
    }
}
