using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Стекло: узел рядом, маршрут сквозь него, удар подкатом и выстрелом. '|' — стекло: для физики стена, для маршрута ребро «стекло».
        private static string GlassTest()
        {
            // Узел у стекла: NodeNear берёт клетку со своей стороны.
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

            // Под потолком стекло не перепрыгнуть: первый шаг — ребро «стекло».
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

            // Без потолка стекло перепрыгивают: прыжок дешевле SmashExtra.
            nav = new NavMap();
            nav.Build(Cells(room), room[0].Length, room.Length, 16f, 0f, 0f, GlassCells(room));
            if (!nav.NextStep(3 * 16 + 8f, 2 * 16 + 12f, 7 * 16 + 8f, 2 * 16 + 12f, out sx, out sy, out edge))
                return "стекло: через стекло без потолка маршрута нет";
            if (edge != NavMap.Jump) return "стекло: стекло можно перепрыгнуть, а маршрут выбрал ребро " + edge;

            // Без ствола: отойти на разбег и подкатиться, пока стекло в SlideGap.
            SimDuck d = new SimDuck();
            d.grounded = true;
            float x = GlassX - GlassReach;
            int facing = 1;
            BotState s = new BotState();
            bool slid = false, step = true;
            float stepX = 0f;
            for (int f = 0; f < 90 && !slid; f++)
            {
                float gap = GlassX - GlassReach - x, hs = d.hs;
                ushort m = GlassFrame(f, ref step, ref stepX, ref d, ref x, ref facing, ref s, false, true);
                if ((m & BotInput.SHOOT) != 0) return "стекло: без ствола жмёт SHOOT";
                if ((m & BotInput.DOWN) == 0) continue;
                if (gap > SlideGap || hs < 2.2f)
                    return "стекло: подкат с зазора " + gap + " на скорости " + hs + " — так стекло не разбить";
                if (s.how != HowSmashSlide) return "причина: подкат к стеклу записан как «" + s.how + "»";
                slid = true;
            }
            if (!slid) return "стекло: за полторы секунды у стекла бот так и не подкатился";

            // Со стволом вплотную: отойти, развернуться, выстрелить; разбито — идти дальше.
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
                ushort m = GlassFrame(f, ref step, ref stepX, ref d, ref x, ref facing, ref s, true, true);
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
                ushort m = GlassFrame(f, ref step, ref stepX, ref d, ref x, ref facing, ref s, true, false);
                if ((m & (BotInput.SHOOT | BotInput.DOWN)) != 0) return "стекло: разбито, а бот всё бьёт по нему";
                if ((m & BotInput.RIGHT) == 0) return "стекло: разбито, а бот не идёт дальше";
            }

            // Враг за стеклом: стрелять сквозь него, но не вплотную.
            BotView g = new BotView();
            s = new BotState();
            g.hasEnemy = true; g.ex = 40f; g.canShoot = true; g.glassLine = true; g.holding = 1; g.glassGun = true;
            g.facingRight = true; g.grounded = true; g.hasGlass = true; g.glassDx = 30f;
            fired = false;
            for (int f = 0; f < 20; f++) if ((Decide(ref g, ref s) & BotInput.SHOOT) != 0) fired = true;
            if (!fired) return "стекло: враг за стеклом, а бот не стреляет сквозь него";
            g.glassDx = GlassReach + 4f;
            s = new BotState();
            for (int f = 0; f < 20; f++)
            {
                ushort m = Decide(ref g, ref s);
                if ((m & BotInput.SHOOT) != 0) return "стекло: стреляет вплотную к стеклу — ствол задерётся";
                if ((m & BotInput.LEFT) == 0 || (m & BotInput.STRAFE) == 0)
                    return "стекло: вплотную к стеклу не отходит лицом к врагу";
            }
            if (s.why != WhyGlassBack) return "причина: отход от стекла записан как «" + s.why + "»";

            return null;
        }

        private const float GlassX = 200f;

        // Кадр у стекла справа. Маршрут, как в игре, считается раз в 10 кадров от клетки бота, и шаг ведёт к центру следующей клетки: бот то встаёт в него, то проскакивает.
        private static ushort GlassFrame(int f, ref bool smashStep, ref float stepX, ref SimDuck d, ref float x,
                                         ref int facing, ref BotState s, bool gun, bool alive)
        {
            if (f % 10 == 0)
            {
                int k = (int)Math.Round((GlassX - 16f - x) / 16f);
                if (k < 0) k = 0;
                smashStep = k == 0;
                stepX = smashStep ? GlassX + 16f : GlassX - 16f - 16f * (k - 1);
            }
            BotView v = new BotView();
            v.hasEnemy = true; v.ex = GlassX + 200f - x; v.grounded = true;
            v.holding = gun ? 1 : 0; v.glassGun = gun;
            v.hs = d.hs; v.speedAbs = Math.Abs(d.hs); v.facingRight = facing > 0;
            v.hasGlass = alive; v.glassDx = GlassX - x;
            v.hasEnemyStep = true;
            v.enemyStepX = stepX - x;
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
