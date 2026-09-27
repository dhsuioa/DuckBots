using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Стекло: узел рядом с ним, маршрут сквозь него и удар по нему. Карты — как
        // в SelfTest, '|' — стекло: для физики стена, для маршрута ребро «стекло».
        private static string GlassTest()
        {
            // --- узел у стекла ---
            // city11 25.09: бот стоял вплотную к стеклу справа, центр его — в колонке
            // стекла, и NodeNear при равенстве клеток брал узел слева, за стеклом.
            // Маршрут считался с чужой стороны, и бот 20 секунд упирался в стекло.
            string[] room = {
                "..........",
                "....|.....",
                "....|.....",
                "##########",
            };
            NavMap nav = new NavMap();
            nav.Build(Cells(room), room[0].Length, room.Length, 16f, 0f, 0f);
            int right = nav.NodeNear(4 * 16 + 12f, 2 * 16 + 12f);
            int left = nav.NodeNear(4 * 16 + 4f, 2 * 16 + 12f);
            if (right < 0 || nav.CellCol(right) != 5) return "стекло: справа от стекла NodeNear берёт узел за ним";
            if (left < 0 || nav.CellCol(left) != 3) return "стекло: слева от стекла NodeNear берёт узел за ним";

            return null;
        }
    }
}
