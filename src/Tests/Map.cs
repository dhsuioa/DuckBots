using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Карта: узлы, рёбра и поиск на выдуманных картах.
        private static string MapTest()
        {
            // Пол с ямой, полка на три клетки выше и недосягаемая полка под потолком.
            string[] arena = {
                "....................",
                ".................==.",
                "....................",
                "....................",
                "......===...........",
                "....................",
                "....................",
                "####..##############" };
            NavMap nav = new NavMap();
            nav.Build(Cells(arena), 20, 8, 16f, 0f, 0f);
            if (nav.nodeCount == 0) return "карта: ни одного узла на арене с полом";

            float sx, sy;
            int edge;
            if (!nav.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "карта: не нашла дорогу через яму";
            if (sx <= 24f) return "карта: первый шаг в другую сторону от цели";

            if (!nav.NextStep(24f, 104f, 120f, 56f, out sx, out sy, out edge))
                return "карта: не нашла дорогу на полку";

            if (nav.NextStep(24f, 104f, 280f, 24f, out sx, out sy, out edge))
                return "карта: выдумала маршрут туда, куда не запрыгнуть";

            int known;
            if (nav.MoveKnown(56f, 104f, 104f, 104f, out known) != NavMap.KnownEdge || known != NavMap.Jump)
                return "сверка: прыжок через яму граф должен знать прямым ребром";
            if (nav.MoveKnown(24f, 104f, 248f, 104f, out known) != NavMap.Detour)
                return "сверка: дальняя точка того же пола достижима, но не одним движением";
            if (nav.MoveKnown(24f, 104f, 280f, 24f, out known) != NavMap.NoPath)
                return "сверка: полка под потолком должна быть «пути нет»";

            // Подъём на 3 клетки со сдвигом на 3: человек так прыгает.
            string[] ledge = {
                "............",
                "............",
                "............",
                "............",
                ".......#####",
                ".......#####",
                ".......#####",
                "############" };
            NavMap onLedge = new NavMap();
            onLedge.Build(Cells(ledge), 12, 8, 16f, 0f, 0f);
            if (onLedge.MoveKnown(72f, 104f, 120f, 56f, out known) != NavMap.KnownEdge)
                return "дальность: подъём на 3 клетки со сдвигом на 3 игрок делает, а граф не знает";

            // Прыжок вниз на 7 и вбок на 11 над пропастью.
            string[] chasm = {
                "........................",
                "........................",
                "####....................",
                "........................",
                "........................",
                "........................",
                "........................",
                "........................",
                "........................",
                "..............##########",
                "........................" };
            NavMap overChasm = new NavMap();
            overChasm.Build(Cells(chasm), 24, 11, 16f, 0f, 0f);
            if (overChasm.MoveKnown(56f, 24f, 232f, 136f, out known) != NavMap.KnownEdge)
                return "дальность: прыжок вниз на 7 и вбок на 11 игрок делает, а граф не знает";

            // Под картой пропасть, а не пол.
            string[] abyss = {
                "........",
                "........",
                "###.....",
                "........",
                "........" };
            NavMap overAbyss = new NavMap();
            overAbyss.Build(Cells(abyss), 8, 5, 16f, 0f, 0f);
            if (overAbyss.nodeCount != 3)
                return "пропасть: под картой невидимый пол, узлов " + overAbyss.nodeCount + " вместо 3";

            // Платформа ловит падающую утку: вниз сквозь неё — только ребром «вниз», с края мимо — можно.
            string[] shelf = {
                "........",
                "........",
                "..===...",
                "........",
                "........",
                "########" };
            NavMap onShelf = new NavMap();
            onShelf.Build(Cells(shelf), 8, 6, 16f, 0f, 0f);
            if (onShelf.MoveKnown(56f, 24f, 72f, 72f, out known) == NavMap.KnownEdge && known == NavMap.Jump)
                return "платформа: граф прыгает вниз сквозь платформу, на которой стоит";
            if (onShelf.MoveKnown(72f, 24f, 120f, 72f, out known) != NavMap.KnownEdge)
                return "платформа: спрыгнуть с края мимо платформы граф должен уметь";

            // Шипы перепрыгивают, а не проходят.
            string[] spiky = {
                "............",
                "............",
                "............",
                "............",
                "............",
                "............",
                ".....^......",
                "############" };
            NavMap nav2 = new NavMap();
            nav2.Build(Cells(spiky), 12, 8, 16f, 0f, 0f);
            if (!nav2.NextStep(72f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "шипы: не нашла дорогу мимо шипов";
            if (edge != NavMap.Jump) return "шипы: собирается пройти по ним ногами";

            // Полка в семи клетках: прыжком не попасть, пружиной — да.
            string[] withSpring = {
                "............",
                "............",
                "......===...",
                "............",
                "............",
                "............",
                "............",
                "............",
                ".....S......",
                "############" };
            string[] noSpring = {
                "............",
                "............",
                "......===...",
                "............",
                "............",
                "............",
                "............",
                "............",
                "............",
                "############" };

            NavMap sprung = new NavMap();
            sprung.Build(Cells(withSpring), 12, 10, 16f, 0f, 0f);
            NavMap plain = new NavMap();
            plain.Build(Cells(noSpring), 12, 10, 16f, 0f, 0f);

            if (plain.NextStep(24f, 136f, 104f, 24f, out sx, out sy, out edge))
                return "пружина: без неё маршрут на полку не должен находиться вовсе";
            if (!sprung.NextStep(24f, 136f, 104f, 24f, out sx, out sy, out edge))
                return "пружина: с ней маршрут на полку не нашёлся";

            // Стена в пять клеток: маршрута нет, с телепортом — есть.
            string[] split = {
                "............",
                "............",
                ".....##.....",
                ".....##.....",
                ".....##.....",
                ".....##.....",
                ".....##.....",
                "############" };
            NavMap walled = new NavMap();
            walled.Build(Cells(split), 12, 8, 16f, 0f, 0f);
            if (walled.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "телепорт: через стену в пять клеток маршрута быть не должно";

            NavMap linked = new NavMap();
            linked.Build(Cells(split), 12, 8, 16f, 0f, 0f);
            linked.AddLink(40f, 104f, 152f, 104f);
            if (!linked.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "телепорт: пара связана, а маршрут всё равно не нашёлся";

            // Длина маршрута: за стеной дороги нет, через телепорт — есть; путь и первый шаг совпадают.
            walled.SearchAll(walled.NodeNear(24f, 104f));
            if (walled.RouteLength(walled.NodeNear(168f, 104f)) < float.MaxValue)
                return "длина маршрута: за стеной в пять клеток дорога нашлась";
            linked.SearchAll(linked.NodeNear(24f, 104f));
            if (linked.RouteLength(linked.NodeNear(168f, 104f)) >= float.MaxValue)
                return "длина маршрута: через телепорт дорога не нашлась";
            int from = nav.NodeNear(24f, 104f), to = nav.NodeNear(120f, 56f);
            nav.SearchAll(from);
            int[] path = new int[64];
            int pathLen = nav.PathTo(to, path);
            if (pathLen < 2 || path[0] != from || path[pathLen - 1] != to)
                return "путь: не ведёт от старта к цели";
            float fx, fy;
            int fe;
            int fprog;
            if (!nav.FirstStep(from, to, out fx, out fy, out fe, out fprog)
                || fx != nav.CellX(path[1]) || fy != nav.CellY(path[1]))
                return "путь: первый шаг не совпадает с путём";

            // Под обрывом пропасти пола нет, над полом — есть.
            if (overAbyss.SafeBelow(88f, 40f)) return "пропасть: под обрывом нашёлся пол, которого нет";
            if (!overAbyss.SafeBelow(24f, 8f)) return "пропасть: над полом пол не найден";

            // Маршрут к ящику ведёт под него, а не на крышу.
            string[] boxRoom = {
                "............",
                "............",
                ".....#......",
                "............",
                "............",
                "............",
                "############" };
            NavMap boxMap = new NavMap();
            boxMap.Build(Cells(boxRoom), 12, 7, 16f, 0f, 0f);
            int under = boxMap.NodeBelow(88f, 40f, 4);
            if (under < 0) return "ящик: места под ящиком не нашлось";
            if (boxMap.CellCol(under) != 5 || boxMap.CellRow(under) != 5)
                return "ящик: цель не под ящиком, а в клетке " + boxMap.CellCol(under) + "," + boxMap.CellRow(under);

            // Позиция для выстрела — на высоте врага и на дистанции.
            string[] duel = {
                "................",
                "................",
                "................",
                "..=========.....",
                "................",
                "................",
                "################" };
            NavMap duelMap = new NavMap();
            duelMap.Build(Cells(duel), 16, 7, 16f, 0f, 0f);
            duelMap.SearchAll(duelMap.NodeNear(24f, 88f));
            int spot = duelMap.FiringNode(152f, 48f, 32f, 128f);
            if (spot < 0) return "позиция: на платформе врага места для выстрела не нашлось";
            if (duelMap.CellRow(spot) != 2) return "позиция: не на высоте врага, а в строке " + duelMap.CellRow(spot);
            if (Math.Abs(duelMap.CellCol(spot) - 9) < 2) return "позиция: вплотную к врагу, колонка " + duelMap.CellCol(spot);

            return null;
        }
    }
}
