using System;
using System.Collections.Generic;
using System.Text;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        private const float NavCell = 16f;   // шаг сетки уровня
        private const int RouteEvery = 10;
        private const int NavRebuild = 180;

        private static NavMap _nav;
        private static Level _navLevel;
        private static int _navFrames;
        private static bool _navReported;

        // Порядок важен: смерть перебивает опору, пружина — пустоту, дверь — стену.
        private static byte Classify(Vec2 p)
        {
            if (Level.CheckPoint<Spikes>(p) != null) return NavMap.Deadly;
            if (Level.CheckPoint<Saws>(p) != null) return NavMap.Deadly;
            Icicles ice = Level.CheckPoint<Icicles>(p);
            if (ice != null && ice.kill) return NavMap.Deadly;
            // Лава и вода — частицы Fluid; в воде утка беспомощна, для маршрута это тоже нет.
            if (Level.CheckPoint<Fluid>(p) != null) return NavMap.Deadly;

            Spring sp = Level.CheckPoint<Spring>(p);
            if (sp != null)
            {
                if (sp is SpringDown || sp is SpringDownLeft || sp is SpringDownRight) return NavMap.Empty;
                if (sp is SpringLeft) return sp.flipHorizontal ? NavMap.SpringRight : NavMap.SpringLeft;
                if (sp is SpringRight) return sp.flipHorizontal ? NavMap.SpringLeft : NavMap.SpringRight;
                // SpringUpLeft — наследник SpringUpRight (как и нижние диагональные), поэтому первая.
                if (sp is SpringUpLeft) return sp.flipHorizontal ? NavMap.SpringDiagRight : NavMap.SpringDiagLeft;
                if (sp is SpringUpRight) return sp.flipHorizontal ? NavMap.SpringDiagLeft : NavMap.SpringDiagRight;
                return NavMap.SpringUp;
            }

            Block b = Level.CheckPoint<Block>(p);
            if (b != null)
            {
                // Дверь — Block, но утка проходит её насквозь. Раздвижная и пирамидная открываются
                // сами, когда утка ближе 18 px; на проводе (slideLocked) и MetroidDoor — свои условия.
                Door door = b as Door;
                if (door != null) return door.locked ? NavMap.Solid : NavMap.Empty;
                VerticalDoor slide = b as VerticalDoor;
                if (slide != null && !(slide is MetroidDoor) && (!slide.slideLocked || slide.slideLockOpened))
                    return NavMap.Empty;
                if (!b.solid) return NavMap.Empty;
                return NavMap.Solid;
            }

            // Лифт стоит там, где его застала перестройка карты.
            if (Level.CheckPoint<AutoPlatform>(p) != null || Level.CheckPoint<Platform>(p) != null
                || Level.CheckPoint<Elevator>(p) != null)
                return NavMap.Platform;
            return NavMap.Empty;
        }

        private static void EnsureNav()
        {
            bool sameLevel = _navLevel == Level.current;
            if (sameLevel && _nav != null && ++_navFrames < NavRebuild) return;
            if (!sameLevel) _navReported = false;
            _navFrames = 0;
            _navLevel = Level.current;
            if (!(Level.current is GameLevel)) { _nav = null; return; }

            Vec2 tl = Level.current.topLeft;
            Vec2 br = Level.current.bottomRight;
            // Строка воздуха над верхними блоками: иначе на крыше уровня нет узлов.
            float oy = tl.y - NavCell;
            int w = (int)((br.x - tl.x) / NavCell) + 2;
            int h = (int)((br.y - tl.y) / NavCell) + 3;
            if (w < 4 || h < 4 || w * h > 20000)
            {
                // В первом кадре раунда границы уровня — мусор, кадром позже карта построится.
                if (w > 0 && h > 0 && !_navReported)
                {
                    Log("мир: карта проходимости не построена: размер " + w + "x" + h);
                    _navReported = true;
                }
                _nav = null;
                return;
            }

            byte[] cells = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    cells[y * w + x] = Classify(
                        new Vec2(tl.x + (x + 0.5f) * NavCell, oy + (y + 0.5f) * NavCell));

            // Мина с выдернутой чекой — нажимная ловушка: клетка смерти, её обходят.
            foreach (Thing t in Level.current.things[typeof(Mine)])
            {
                Mine mine = t as Mine;
                if (mine == null || mine._pin || mine.owner != null || mine.removeFromLevel) continue;
                int mx = NavMap.CellOf(mine.x, tl.x, NavCell), my = NavMap.CellOf(mine.y, oy, NavCell);
                if (mx >= 0 && my >= 0 && mx < w && my < h) cells[my * w + mx] = NavMap.Deadly;
            }

            // Стекло размечается по боксу: стойка 6 px в центре тайла, а сетка сдвинута от тайлов на
            // 0–5 px, и центр клетки в стекло попадает не всегда. Решётку и пол из стекла не трогаем.
            bool[] glass = new bool[w * h];
            int panes = 0, unseen = 0;
            foreach (Thing t in Level.current.things[typeof(Window)])
            {
                Window win = t as Window;
                if (win == null || win.floor || win.bars.value || win.destroyed || win.removeFromLevel) continue;
                panes++;
                bool seen = true;
                int x0 = NavMap.CellOf(win.left, tl.x, NavCell), x1 = NavMap.CellOf(win.right - 1f, tl.x, NavCell);
                int y0 = NavMap.CellOf(win.top, oy, NavCell), y1 = NavMap.CellOf(win.bottom - 1f, oy, NavCell);
                for (int gy = y0; gy <= y1; gy++)
                    for (int gx = x0; gx <= x1; gx++)
                    {
                        if (gx < 0 || gy < 0 || gx >= w || gy >= h) continue;
                        int i = gy * w + gx;
                        if (cells[i] != NavMap.Solid) seen = false;
                        cells[i] = NavMap.Solid;
                        glass[i] = true;
                    }
                if (!seen) unseen++;
            }

            // Клетки те же — карта та же: сборка с симуляцией прыжков стоит десятки мс.
            // ponytail: изменение одной клетки пересобирает всю карту; разбить сборку
            // по кадрам или считать заново только соседей изменённых клеток, если в
            // бою станут заметны заминки.
            if (sameLevel && _nav != null && _nav.width == w && _nav.height == h && SameCells(_nav.cells, cells))
                return;

            NavMap nav = new NavMap();
            nav.Build(cells, w, h, NavCell, tl.x, oy, glass);

            int links = 0;
            foreach (Thing t in Level.current.things[typeof(Teleporter)])
            {
                Teleporter tp = t as Teleporter;
                if (tp == null || tp.link == null || tp.noduck) continue;
                nav.AddLink(tp.x, tp.y, tp.link.x, tp.link.y);
                links++;
            }

            NavMap before = sameLevel ? _nav : null;
            _nav = nav;
            if (!_navReported)
            {
                _navReported = true;
                Log("мир: карта проходимости " + w + "x" + h + " клеток, узлов " + nav.nodeCount
                    + ", телепортов " + links + ", стёкол " + panes + ", из них мимо центров клеток " + unseen);
                DumpMap(nav, "первая сборка");
                Detail("карта: предметы:" + MapThings(nav));
            }
            else if (before != null && before.width == w && before.height == h)
                LogMapChanges(before.cells, nav);
        }

        private static bool SameCells(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void LogMapChanges(byte[] was, NavMap nav)
        {
            byte[] now = nav.cells;
            int changed = 0;
            StringBuilder list = new StringBuilder();
            for (int i = 0; i < now.Length; i++)
            {
                if (was[i] == now[i]) continue;
                if (++changed <= 40)
                    list.Append(' ').Append(i % nav.width).Append(',').Append(i / nav.width).Append(':')
                        .Append(CellName(was[i])).Append('→').Append(CellName(now[i])).Append(BlockAt(nav, i));
            }
            if (changed == 0) return;
            Log("мир: карта изменилась клеток=" + changed + " запись=f" + frame);
            Detail("мир: изменения" + list + (changed > 40 ? " …" : ""));
            DumpMap(nav, "после изменений");
        }

        private static string BlockAt(NavMap nav, int i)
        {
            Block b = Level.CheckPoint<Block>(new Vec2(nav.originX + (i % nav.width + 0.5f) * NavCell,
                                                       nav.originY + (i / nav.width + 0.5f) * NavCell));
            if (b == null) return "";
            Door door = b as Door;
            return "(" + b.GetType().Name + (b.solid ? "" : " несплошной")
                + (door != null && door.locked ? " заперта" : "") + ")";
        }

        // Что карта видит не так, как утка: двери, ящики, трубы, выступы, рампы. Друг от друга
        // эти типы не наследуют, дважды ничего не попадёт.
        private static readonly Type[] _mapThings = {
            typeof(Door), typeof(VerticalDoor), typeof(ItemBox), typeof(WireTrapDoor), typeof(PipeTileset),
            typeof(InvisibleBlock), typeof(Conveyor), typeof(Nubber), typeof(IceWedge), typeof(SnowPile),
            typeof(SnowDrift), typeof(Elevator) };

        private static string MapThings(NavMap nav)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Type type in _mapThings)
            {
                int n = 0;
                foreach (Thing t in Level.current.things[type])
                {
                    if (++n > 30) { sb.Append(" …"); break; }
                    sb.Append(' ').Append(t.GetType().Name).Append(' ')
                        .Append(nav.Col(t.x)).Append(',').Append(nav.Row(t.y));
                    Door door = t as Door;
                    if (door != null && door.locked) sb.Append(" заперта");
                    Block b = t as Block;
                    if (b != null && !b.solid) sb.Append(" несплошной");
                    sb.Append(';');
                }
            }
            return sb.Length == 0 ? " нет" : sb.ToString();
        }

        private static string CellName(byte c)
        {
            switch (c)
            {
                case NavMap.Solid: return "стена";
                case NavMap.Platform: return "платформа";
                case NavMap.Deadly: return "смерть";
                case NavMap.SpringUp:
                case NavMap.SpringLeft:
                case NavMap.SpringRight:
                case NavMap.SpringDiagLeft:
                case NavMap.SpringDiagRight: return "пружина";
                default: return "пусто";
            }
        }

        private static readonly List<string> _mapRows = new List<string>();

        private static void DumpMap(NavMap nav, string why)
        {
            Detail("карта: " + why + " уровень=" + MapName(Level.current) + " клеток=" + nav.width + "x"
                + nav.height + " узлов=" + nav.nodeCount + " начало=" + (int)nav.originX + ","
                + (int)nav.originY + " клетка=16 — # стена, | стекло, = платформа, ^ смерть, S < > \\ / пружины,"
                + " o узел, T телепорт, . пусто");
            _mapRows.Clear();
            nav.Render(_mapRows);
            for (int i = 0; i < _mapRows.Count; i++) Detail("карта: " + _mapRows[i]);
        }
    }
}
