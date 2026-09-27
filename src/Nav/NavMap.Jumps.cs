using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DuckGame;
using HarmonyLib;

namespace DuckBots
{
    public partial class NavMap
    {
        // --- прыжки покадровой симуляцией ---
        //
        // Сначала дальность прыжка считалась таблицей «на глаз», потом формулой, а дуга
        // проверялась ломаной линией. Журнал 24.09 показал, чего это стоит: 182 промаха
        // за 20 раундов — 60 раз бот сел ниже цели, 61 выше (дуга прошла сквозь
        // платформу, и он сел на неё сверху), 57 недолётов, и четыре смерти на space12_8:
        // средний прыжок не дотянул до края блока. Формула знала полный прыжок на полной
        // скорости, а бот прыгал другим.
        //
        // Теперь ребро «прыжок» — это программа нажатий: направление в кадры
        // dirFrom..dirTo, сколько кадров держать прыжок, с какого кадра махать крыльями.
        // Карта прогоняет каждую программу из каждого узла той же физикой, что у игры
        // (DuckPhysics, сверена с самописцем), со столкновениями по клеткам, и где утка
        // сядет — туда ребро. Бот встаёт в центр клетки, гасит скорость и проигрывает
        // программу дословно. Упакована в int, чтобы ехать в BotView без мусора.
        public const int Forever = 63;   // dirTo: жать направление до приземления

        public static int Prog(int dir, int hold, int dirFrom, int dirTo, int hover)
        {
            return (dir + 1) | hold << 2 | dirFrom << 8 | dirTo << 14 | hover << 20;
        }

        public static int ProgDir(int p) { return (p & 3) - 1; }
        public static int ProgHold(int p) { return p >> 2 & 63; }
        public static int ProgHover(int p) { return p >> 20 & 63; }   // 0 — без взмахов

        // Что жать программой на кадре f от начала прыжка.
        public static int ProgDirAt(int p, int f)
        {
            int from = p >> 8 & 63, to = p >> 14 & 63;
            return f >= from && (to == Forever || f < to) ? ProgDir(p) : 0;
        }

        public static bool ProgJumpAt(int p, int f)
        {
            int hover = ProgHover(p);
            return f < ProgHold(p) || (hover > 0 && f >= hover);
        }

        // Программа словами — для журнала: «h20 влево 0-8 взмахи 21».
        public static string ProgWord(int p)
        {
            if (p == 0) return "";
            int d = ProgDir(p), from = p >> 8 & 63, to = p >> 14 & 63, hover = ProgHover(p);
            string s = "h" + ProgHold(p) + (d < 0 ? " влево" : d > 0 ? " вправо" : " вверх");
            if (d != 0 && (from > 0 || to != Forever)) s += " " + from + "-" + (to == Forever ? "…" : to.ToString());
            if (hover > 0) s += " взмахи " + hover;
            return s;
        }

        // Набор программ: пять длительностей удержания (ступенька, этаж, до вершины),
        // направление сразу и до конца, коротким толчком или с задержкой (сначала вверх,
        // потом вбок — заскочить на уступ рядом, не ударившись в его бок), прямо вверх
        // сквозь платформу и со взмахами после вершины — это дальше всего.
        private static readonly int[] _progs = BuildProgs();

        private static int[] BuildProgs()
        {
            List<int> p = new List<int>();
            int[] holds = { 4, 9, 14, 20, 28 };
            for (int i = 0; i < holds.Length; i++) p.Add(Prog(0, holds[i], 0, Forever, 0));
            for (int d = -1; d <= 1; d += 2)
            {
                for (int i = 0; i < holds.Length; i++)
                {
                    p.Add(Prog(d, holds[i], 0, Forever, 0));
                    p.Add(Prog(d, holds[i], 0, 8, 0));      // ~20 пикселей вбок
                    p.Add(Prog(d, holds[i], 0, 16, 0));     // ~50: без него между толчком и
                    p.Add(Prog(d, holds[i], 8, Forever, 0)); // «до конца» дыра, небольшие площадки не взять
                }
                p.Add(Prog(d, 28, 0, Forever, 29));
                p.Add(Prog(d, 28, 8, Forever, 29));
                p.Add(Prog(d, 14, 0, Forever, 16));
            }
            return p.ToArray();
        }

        private int Col(float wx) { return (int)Math.Floor((wx - _originX) / _cell); }
        private int Row(float wy) { return (int)Math.Floor((wy - _originY) / _cell); }

        // Столбец клеток cx в полосе от top до bottom (в пикселях) — есть ли стена.
        private bool SolidIn(int cx, float top, float bottom)
        {
            for (int cy = Row(top); cy <= Row(bottom); cy++)
                if (At(cx, cy) == Solid) return true;
            return false;
        }

        // Прогон программы из узла: где утка сядет. -1 — разбилась (смерть, пропасть,
        // пружина — после неё полёт не предсказать) или села туда, где стоять нельзя.
        // frames — кадров до приземления.
        // Опора — и в SupportMargin левее центра утки, и столько же правее, а не
        // где-нибудь под краем бокса: сетка по клеткам расходится с игрой на несколько
        // пикселей (на space12_8 настоящий блок был на 4 пикселя уже клетки), и посадку
        // на самую кромку граф обещать не должен. Там полный прыжок садился центром
        // в 0.7 пикселя от края.
        private const float SupportMargin = 3f;

        public int Simulate(int node, int prog, out int frames)
        {
            frames = 0;
            const float hw = DuckPhysics.HalfWidth, hh = DuckPhysics.Height;
            SimDuck d = new SimDuck();
            d.x = CellX(node);
            d.bottom = _originY + (_nodeCell[node] / _w + 1) * _cell;
            d.grounded = true;
            d.groundValid = 7;
            float mapBottom = _originY + _h * _cell;
            for (int f = 0; f < 240; f++)
            {
                DuckPhysics.Controls(ref d, ProgDirAt(prog, f), ProgJumpAt(prog, f));

                // По x: упёрлась в стену — скорость ноль, как в игре.
                float top = d.bottom - hh + 0.5f, low = d.bottom - 0.5f;
                if (d.hs != 0f)
                {
                    float nx = d.x + d.hs;
                    int cx = Col(d.hs > 0f ? nx + hw : nx - hw);
                    if (SolidIn(cx, top, low))
                    {
                        nx = d.hs > 0f ? _originX + cx * _cell - hw - 0.01f
                                       : _originX + (cx + 1) * _cell + hw + 0.01f;
                        d.hs = 0f;
                    }
                    d.x = nx;
                }

                // По y: падая — встать на то, чей верх ноги пересекли за кадр
                // (сквозь платформу снизу пролетают, сверху она ловит); взлетая —
                // стукнуться головой о стену.
                int c0 = Col(d.x - hw + 0.5f), c1 = Col(d.x + hw - 0.5f);
                int s0 = Col(d.x - SupportMargin), s1 = Col(d.x + SupportMargin);
                float nb = d.bottom + d.vs;
                if (d.vs > 0f)
                {
                    for (int cy = Row(d.bottom - 0.01f) + 1; cy <= Row(nb); cy++)
                    {
                        float rowTop = _originY + cy * _cell;
                        if (rowTop < d.bottom - 0.01f || rowTop > nb) continue;
                        // Смерть и пружина — под любой частью бокса.
                        bool any = false;
                        for (int cx = c0; cx <= c1; cx++)
                        {
                            byte c = At(cx, cy);
                            // На пружину сели — значит пришли в узел над ней, дальше
                            // полетит пружина (рёбра Launch).
                            if (IsSpring(c)) { frames = f + 1; return NodeAt(cx, cy - 1); }
                            if (c == Deadly) return -1;
                            if (Support(c)) any = true;
                        }
                        if (!any) continue;
                        // Опора под боксом есть, но центр ближе запаса к кромке — сядет
                        // или соскользнёт, не угадать; такую программу граф не берёт.
                        if (!Support(At(s0, cy)) || !Support(At(s1, cy))) return -1;
                        d.bottom = rowTop;
                        d.vs = 0f;
                        d.grounded = true;
                        frames = f + 1;
                        return Landing(d.x, d.bottom);
                    }
                }
                else if (d.vs < 0f)
                {
                    // Строки, чей низ голова пересекла за кадр, от ближней к дальней.
                    bool bumped = false;
                    for (int cy = Row(d.bottom - hh + 0.01f) - 1; cy >= Row(nb - hh) && !bumped; cy--)
                    {
                        float rowBottom = _originY + (cy + 1) * _cell;
                        if (rowBottom > d.bottom - hh + 0.01f || rowBottom < nb - hh) continue;
                        for (int cx = c0; cx <= c1; cx++)
                            if (At(cx, cy) == Solid)
                            {
                                nb = rowBottom + hh + 0.01f;
                                d.vs = 0f;
                                bumped = true;
                                break;
                            }
                    }
                }
                d.bottom = nb;

                // Смерть: бокс утки задел смертельную клетку или ноги ушли под карту.
                if (d.bottom > mapBottom) return -1;
                for (int cy = Row(d.bottom - hh + 0.5f); cy <= Row(d.bottom - 0.5f); cy++)
                    for (int cx = c0; cx <= c1; cx++)
                        if (At(cx, cy) == Deadly) return -1;
            }
            return -1;
        }

        private static bool Support(byte c) { return c == Solid || c == Platform; }

        // Узел под севшей уткой. Центр бывает над пустотой, а стоит она краем бокса
        // на соседней клетке — тогда узел там.
        private int Landing(float x, float bottom)
        {
            int row = Row(bottom - 4f);
            int node = NodeAt(Col(x), row);
            if (node < 0) node = NodeAt(Col(x - DuckPhysics.HalfWidth + 0.5f), row);
            if (node < 0) node = NodeAt(Col(x + DuckPhysics.HalfWidth - 0.5f), row);
            return node;
        }
    }
}
