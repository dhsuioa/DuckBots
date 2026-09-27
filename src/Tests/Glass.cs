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

            // --- ребро «стекло» ---
            // Под потолком стекло не перепрыгнуть: единственный путь — разбить его.
            string[] roofed = {
                "##########",
                "....|.....",
                "....|.....",
                "##########",
            };
            nav = new NavMap();
            nav.Build(Cells(roofed), roofed[0].Length, roofed.Length, 16f, 0f, 0f, GlassCells(roofed));
            float sx, sy;
            int edge;
            if (!nav.NextStep(3 * 16 + 8f, 2 * 16 + 12f, 7 * 16 + 8f, 2 * 16 + 12f, out sx, out sy, out edge))
                return "стекло: маршрута сквозь стекло нет";
            if (edge != NavMap.Smash || sx != 5 * 16 + 8f)
                return "стекло: у стекла первый шаг — ребро " + edge + " в x=" + sx + ", а не «стекло» за него";

            // Без потолка стекло высотой в две клетки перепрыгивается, и прыжок дешевле:
            // разбег и подкат стоят SmashExtra лишнего пути.
            nav = new NavMap();
            nav.Build(Cells(room), room[0].Length, room.Length, 16f, 0f, 0f, GlassCells(room));
            if (!nav.NextStep(3 * 16 + 8f, 2 * 16 + 12f, 7 * 16 + 8f, 2 * 16 + 12f, out sx, out sy, out edge))
                return "стекло: через стекло без потолка маршрута нет";
            if (edge != NavMap.Jump) return "стекло: стекло можно перепрыгнуть, а маршрут выбрал ребро " + edge;

            // --- удар по стеклу: подкат ---
            // Бот без ствола вплотную к стеклу справа. Шагом стекло не разбить, а
            // подкатом человек 25.09 разбивал его на скорости от 2.2: отойти на разбег
            // и подкатиться, пока стекло ещё в SlideGap.
            SimDuck d = new SimDuck();
            d.grounded = true;
            float x = GlassX - GlassReach;
            int facing = 1;
            BotState s = new BotState();
            bool slid = false, step = true;
            for (int f = 0; f < 90 && !slid; f++)
            {
                float gap = GlassX - GlassReach - x, hs = d.hs;
                ushort m = GlassFrame(f, ref step, ref d, ref x, ref facing, ref s, false, true);
                if ((m & BotInput.SHOOT) != 0) return "стекло: без ствола жмёт SHOOT";
                if ((m & BotInput.DOWN) == 0) continue;
                if (gap > SlideGap || hs < 2.2f)
                    return "стекло: подкат с зазора " + gap + " на скорости " + hs + " — так стекло не разбить";
                if (s.how != HowSmashSlide) return "причина: подкат к стеклу записан как «" + s.how + "»";
                slid = true;
            }
            if (!slid) return "стекло: за полторы секунды у стекла бот так и не подкатился";

            // --- удар по стеклу: выстрел ---
            // Со стволом в 4 пикселях от стекла: вплотную ствол упирается и задирается
            // вверх, поэтому сначала отойти, потом развернуться и стрелять. Разбили —
            // дальше обычный шаг, без выстрелов и подката.
            d = new SimDuck();
            d.grounded = true;
            x = GlassX - GlassReach - 4f;
            facing = 1;
            s = new BotState();
            bool fired = false;
            step = true;
            for (int f = 0; f < 60 && !fired; f++)
            {
                float gap = GlassX - GlassReach - x;
                bool faced = facing > 0;
                ushort m = GlassFrame(f, ref step, ref d, ref x, ref facing, ref s, true, true);
                if ((m & BotInput.DOWN) != 0) return "стекло: со стволом жмёт DOWN, а не стреляет";
                if ((m & BotInput.SHOOT) == 0) continue;
                if (gap < ShootMin) return "стекло: стреляет с зазора " + gap + " — ствол упрётся и задерётся";
                if (!faced) return "стекло: стреляет, стоя к стеклу спиной";
                if (s.how != HowSmashShoot) return "причина: выстрел в стекло записан как «" + s.how + "»";
                fired = true;
            }
            if (!fired) return "стекло: со стволом за секунду так и не выстрелил в стекло";
            for (int f = 60; f < 65; f++)
            {
                ushort m = GlassFrame(f, ref step, ref d, ref x, ref facing, ref s, true, false);
                if ((m & (BotInput.SHOOT | BotInput.DOWN)) != 0) return "стекло: разбито, а бот всё бьёт по нему";
                if ((m & BotInput.RIGHT) == 0) return "стекло: разбито, а бот не идёт дальше";
            }

            return null;
        }

        private const float GlassX = 200f;   // центр стекла в прогонке

        // Один кадр у стекла справа: обстановка, решение, физика. Стекло, пока цело,
        // останавливает утку. Маршрут, как в игре, пересчитывается раз в десять кадров:
        // из клетки у стекла шаг — «стекло» за него, а отошедшему на клетку — «шаг»
        // к стеклу. Программа удара обязана пережить эту смену.
        private static ushort GlassFrame(int f, ref bool smashStep, ref SimDuck d, ref float x, ref int facing,
                                         ref BotState s, bool gun, bool alive)
        {
            if (f % 10 == 0) smashStep = x >= GlassX - 24f;   // бот в клетке у стекла
            BotView v = new BotView();
            v.hasEnemy = true; v.ex = GlassX + 200f - x; v.grounded = true;
            v.holding = gun ? 1 : 0; v.glassGun = gun;
            v.hs = d.hs; v.speedAbs = Math.Abs(d.hs); v.facingRight = facing > 0;
            v.hasGlass = alive; v.glassDx = GlassX - x;
            v.hasEnemyStep = true;
            v.enemyStepX = (smashStep ? GlassX + 16f : GlassX - 16f) - x;
            v.enemyStepEdge = smashStep ? NavMap.Smash : NavMap.Walk;
            ushort m = Decide(ref v, ref s);
            int dir = (m & BotInput.RIGHT) != 0 ? 1 : ((m & BotInput.LEFT) != 0 ? -1 : 0);
            if (dir != 0) facing = dir;
            DuckPhysics.Controls(ref d, dir, false);
            x += d.hs;
            if (alive && x > GlassX - GlassReach) { x = GlassX - GlassReach; d.hs = 0f; }
            return m;
        }

        private static bool[] GlassCells(string[] rows)
        {
            int w = rows[0].Length;
            bool[] g = new bool[w * rows.Length];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < w; x++)
                    g[y * w + x] = rows[y][x] == '|';
            return g;
        }
    }
}
