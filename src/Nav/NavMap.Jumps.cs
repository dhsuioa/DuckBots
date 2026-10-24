using System;
using System.Collections.Generic;

namespace DuckBots
{
    public partial class NavMap
    {
        // Ребро «прыжок» — программа нажатий: направление в кадры dirFrom..dirTo, удержание hold,
        // взмахи с кадра hover. Карта прогоняет её физикой игры (DuckPhysics), бот проигрывает
        // дословно. Упакована в int, чтобы ехать в BotView без мусора.
        public const int Forever = 63;   // dirTo: жать направление до приземления
        public const int MaxFlight = 240;   // кадров: дольше полёт не ждём

        public static int Prog(int dir, int hold, int dirFrom, int dirTo, int hover)
        {
            return (dir + 1) | hold << 2 | dirFrom << 8 | dirTo << 14 | hover << 20;
        }

        public static int ProgDir(int p) { return (p & 3) - 1; }
        public static int ProgHold(int p) { return p >> 2 & 63; }
        public static int ProgHover(int p) { return p >> 20 & 63; }

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

        // Для журнала: «h20 влево 0-8 взмахи 21».
        public static string ProgWord(int p)
        {
            if (p == 0) return "";
            int d = ProgDir(p), from = p >> 8 & 63, to = p >> 14 & 63, hover = ProgHover(p);
            string s = "h" + ProgHold(p) + (d < 0 ? " влево" : d > 0 ? " вправо" : " вверх");
            if (d != 0 && (from > 0 || to != Forever)) s += " " + from + "-" + (to == Forever ? "…" : to.ToString());
            if (hover > 0) s += " взмахи " + hover;
            return s;
        }

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
                    p.Add(Prog(d, holds[i], 0, 8, 0));        // толчок: ~20 px вбок
                    p.Add(Prog(d, holds[i], 0, 16, 0));       // ~50 px
                    p.Add(Prog(d, holds[i], 8, Forever, 0));  // сначала вверх, потом вбок: на уступ рядом
                }
                p.Add(Prog(d, 28, 0, Forever, 29));
                p.Add(Prog(d, 28, 8, Forever, 29));
                p.Add(Prog(d, 14, 0, Forever, 16));
            }
            return p.ToArray();
        }

        private bool SolidIn(int cx, float top, float bottom)
        {
            for (int cy = Row(top); cy <= Row(bottom); cy++)
                if (At(cx, cy) == Solid) return true;
            return false;
        }

        // Опора нужна и на столько левее центра утки, и правее: сетка расходится с игрой на
        // несколько пикселей, и посадку на самую кромку граф не обещает.
        private const float SupportMargin = 3f;

        // Где утка сядет по программе; −1 — смерть, пропасть или туда, где стоять нельзя.
        // Села на пружину — узел над ней. frames — кадров до приземления.
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
            for (int f = 0; f < MaxFlight; f++)
            {
                DuckPhysics.Controls(ref d, ProgDirAt(prog, f), ProgJumpAt(prog, f));

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

                // Падая — встать на то, чей верх ноги пересекли за кадр; взлетая — стукнуться головой.
                int c0 = Col(d.x - hw + 0.5f), c1 = Col(d.x + hw - 0.5f);
                int s0 = Col(d.x - SupportMargin), s1 = Col(d.x + SupportMargin);
                float nb = d.bottom + d.vs;
                if (d.vs > 0f)
                {
                    for (int cy = Row(d.bottom - 0.01f) + 1; cy <= Row(nb); cy++)
                    {
                        float rowTop = _originY + cy * _cell;
                        if (rowTop < d.bottom - 0.01f || rowTop > nb) continue;
                        bool any = false;
                        for (int cx = c0; cx <= c1; cx++)
                        {
                            byte c = At(cx, cy);
                            if (IsSpring(c)) { frames = f + 1; return NodeAt(cx, cy - 1); }
                            if (c == Deadly) return -1;
                            if (Support(c)) any = true;
                        }
                        if (!any) continue;
                        if (!Support(At(s0, cy)) || !Support(At(s1, cy))) return -1;
                        d.bottom = rowTop;
                        d.vs = 0f;
                        d.grounded = true;
                        frames = f + 1;
                        return NodeUnderDuck(d.x, d.bottom);
                    }
                }
                else if (d.vs < 0f)
                {
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

                if (d.bottom > mapBottom) return -1;
                for (int cy = Row(d.bottom - hh + 0.5f); cy <= Row(d.bottom - 0.5f); cy++)
                    for (int cx = c0; cx <= c1; cx++)
                        if (At(cx, cy) == Deadly) return -1;
            }
            return -1;
        }

        private static bool Support(byte c) { return c == Solid || c == Platform; }

        // Узел под севшей уткой: центр бывает над пустотой, а стоит она краем бокса на соседней клетке.
        private int NodeUnderDuck(float x, float bottom)
        {
            int row = Row(bottom - 4f);
            int node = NodeAt(Col(x), row);
            if (node < 0) node = NodeAt(Col(x - DuckPhysics.HalfWidth + 0.5f), row);
            if (node < 0) node = NodeAt(Col(x + DuckPhysics.HalfWidth - 0.5f), row);
            return node;
        }
    }
}
