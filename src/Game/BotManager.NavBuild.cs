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
    public static partial class BotManager
    {
        // --- карта проходимости ---

        private const float NavCell = 16f;   // шаг сетки уровня в Duck Game
        private const int RouteEvery = 10;   // кадров между пересчётами маршрута
        private const int NavRebuild = 180;  // кадров между перестройками карты

        private static NavMap _nav;
        private static Level _navLevel;
        private static int _navFrames;
        private static bool _navReported;    // о карте этого уровня в лог уже сказано

        // Из чего состоит клетка. Порядок проверок важен: смерть перебивает опору,
        // пружина перебивает пустоту, дверь перебивает стену.
        private static byte Classify(Vec2 p)
        {
            // Шипы и пилы — не здесь, а отдельным проходом в EnsureNav: центр клетки в них
            // не попадает.
            Icicles ice = Level.CheckPoint<Icicles>(p);
            if (ice != null && ice.kill) return NavMap.Deadly;
            // Лава и вода — частицы Fluid, а не блоки. Воду смертельной считать
            // неправильно, но утка в ней беспомощна, так что для маршрута это тоже нет.
            if (Level.CheckPoint<Fluid>(p) != null) return NavMap.Deadly;

            Spring sp = Level.CheckPoint<Spring>(p);
            if (sp != null)
            {
                // Вниз пинают — маршруту бесполезны (нижние диагональные — наследники SpringUpRight).
                if (sp is SpringDown || sp is SpringDownLeft || sp is SpringDownRight) return NavMap.Empty;
                if (sp is SpringLeft) return sp.flipHorizontal ? NavMap.SpringRight : NavMap.SpringLeft;
                if (sp is SpringRight) return sp.flipHorizontal ? NavMap.SpringLeft : NavMap.SpringRight;
                // Диагональные бросают вверх и вбок (hSpeed ±10). Раньше они считались
                // прямыми, и на city02 28.09 маршрут вёл через правую пружину влево —
                // бот улетал вправо, возвращался и вставал на неё снова. SpringUpLeft —
                // наследник SpringUpRight, поэтому проверяется первой.
                if (sp is SpringUpLeft) return sp.flipHorizontal ? NavMap.SpringDiagRight : NavMap.SpringDiagLeft;
                if (sp is SpringUpRight) return sp.flipHorizontal ? NavMap.SpringDiagLeft : NavMap.SpringDiagRight;
                return NavMap.SpringUp;
            }

            Block b = Level.CheckPoint<Block>(p);
            if (b != null)
            {
                // Дверь — это Block, но утка проходит её насквозь. Незапертая дверь
                // в карте должна быть пустотой, иначе на замковых и пирамидных уровнях
                // половина арены оказывается недостижимой.
                Door door = b as Door;
                if (door != null) return door.locked ? NavMap.Solid : NavMap.Empty;
                // Раздвижная дверь (и пирамидная) сама открывается, когда утка ближе 18
                // пикселей, — для маршрута это проход. Закрытой её видел момент перестройки
                // карты, и граф на случайных уровнях рвался надвое (27.09, р=9 и р=17).
                // Дверь на проводе (slideLocked) слушается кнопку, а выход аркады
                // (MetroidDoor) — свои условия: их оставляем как есть.
                VerticalDoor slide = b as VerticalDoor;
                if (slide != null && !(slide is MetroidDoor) && (!slide.slideLocked || slide.slideLockOpened))
                    return NavMap.Empty;
                if (!b.solid) return NavMap.Empty;
                return NavMap.Solid;
            }

            // Лифт — не платформа по типу, но стоят на нём так же. Где он сейчас, карта
            // узнаёт при очередной перестройке, поэтому маршрут через лифт приблизительный.
            if (Level.CheckPoint<AutoPlatform>(p) != null || Level.CheckPoint<Platform>(p) != null
                || Level.CheckPoint<Elevator>(p) != null)
                return NavMap.Platform;
            return NavMap.Empty;
        }

        // Строится раз в несколько секунд: около тысячи клеток по несколько запросов.
        // Это заметно дороже кадровой работы, но геометрия в Duck Game разрушаемая,
        // лифты ездят, двери открываются — статическая карта протухает на глазах.
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
            // Над верхними блоками — строка воздуха. Раньше карта начиналась с верхнего
            // края самых верхних блоков, крыша уровня в неё не попадала, и бот, забравшийся
            // наверх, оставался без узла и без маршрута: на office04_8 это восемь
            // срабатываний сторожа 25.09 и два 27.09.
            float oy = tl.y - NavCell;
            int w = (int)((br.x - tl.x) / NavCell) + 2;
            int h = (int)((br.y - tl.y) / NavCell) + 3;
            if (w < 4 || h < 4 || w * h > 20000)
            {
                // В первом кадре раунда границы уровня ещё не посчитаны и дают мусор
                // вроде -12497x-12497. Это не ошибка: кадром позже карта построится.
                // Сообщать стоит только о настоящем странном размере.
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

            // Лежащая мина с выдернутой чекой — нажимная ловушка: наступил и сошёл —
            // взрыв, таймера у неё нет. Для маршрута это клетка смерти, как шипы: её
            // обходят, а не бегут от неё в стену, как на forest10_8.
            foreach (Thing t in Level.current.things[typeof(Mine)])
            {
                Mine mine = t as Mine;
                if (mine == null || mine._pin || mine.owner != null || mine.removeFromLevel) continue;
                int mx = (int)Math.Floor((mine.x - tl.x) / NavCell);
                int my = (int)Math.Floor((mine.y - oy) / NavCell);
                if (mx >= 0 && my >= 0 && mx < w && my < h) cells[my * w + mx] = NavMap.Deadly;
            }

            // Шипы и пилы — полоска в 4–5 пикселей у стены (бокс 13x5 и 12x4), а центр
            // клетки от неё в трёх пикселях. Проверка по центру клетки их не видела: на
            // forest15 и forest09_8 карта ставила узлы прямо на шипы, и прыжок карты туда
            // садился — 22.10 боты гибли на шипах 9 раз за 163 раунда, человек ни разу.
            // Редактор ставит опасность в центр тайла со сдвигом editorOffset к стене,
            // так что центр тайла — position − editorOffset, в 8 пикселях от стены: при
            // любом сдвиге сетки меньше восьми это клетка перед стеной, а не сама стена.
            // ponytail: смерть — вся клетка, хотя убивают крайние 5 пикселей, а по шипам
            // можно ходить (убивает только падение на них с vSpeed > 1); мерить бокс утки
            // по самой полоске в Simulate, если граф на картах с шипами начнёт рваться.
            int hazards = 0;
            foreach (Type type in _hazards)
                foreach (Thing t in Level.current.things[type])
                {
                    Vec2 c = t.position - t.editorOffset;
                    int hx = (int)Math.Floor((c.x - tl.x) / NavCell);
                    int hy = (int)Math.Floor((c.y - oy) / NavCell);
                    if (hx < 0 || hy < 0 || hx >= w || hy >= h || cells[hy * w + hx] == NavMap.Solid) continue;
                    cells[hy * w + hx] = NavMap.Deadly;
                    hazards++;
                }

            // Стекло. Вертикальное окно — стойка 6 пикселей в центре тайла, а сетка карты
            // сдвинута от тайлов на 0–5 пикселей: начало карты — левый край самой левой
            // вещи уровня. Центр клетки попадает в стекло только при сдвиге до трёх (так
            // на city11 и office04_8), а при сдвиге 4–5 (forest13, office21_8 и другие
            // 25.09) стекла для карты не было вовсе, и маршрут шёл сквозь него. Поэтому
            // стекло размечается по боксу: для физики это стена, а маршрут может его
            // разбить. Решётку пуля и подкат не берут, горизонтальное стекло не мешает
            // ходить — их не трогаем.
            bool[] glass = new bool[w * h];
            int panes = 0, unseen = 0;
            foreach (Thing t in Level.current.things[typeof(Window)])
            {
                Window win = t as Window;
                if (win == null || win.floor || win.bars.value || win.destroyed || win.removeFromLevel) continue;
                panes++;
                bool seen = true;
                int x0 = (int)Math.Floor((win.left - tl.x) / NavCell), x1 = (int)Math.Floor((win.right - 1f - tl.x) / NavCell);
                int y0 = (int)Math.Floor((win.top - oy) / NavCell), y1 = (int)Math.Floor((win.bottom - 1f - oy) / NavCell);
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

            // Клетки те же — карта та же: прыжки теперь симулируются, и сборка стоит
            // десятки миллисекунд. Пересобираем только первый раз и когда уровень
            // изменился — взорвали пол, упала мина, поехал лифт.
            // ponytail: изменение одной клетки пересобирает всю карту; разбить сборку
            // по кадрам или считать заново только соседей изменённых клеток, если в
            // бою станут заметны заминки.
            if (sameLevel && _nav != null && _nav.width == w && _nav.height == h && SameCells(_nav.cells, cells))
                return;

            NavMap nav = new NavMap();
            nav.Build(cells, w, h, NavCell, tl.x, oy, glass);

            // Телепорты игра связывает сама: Teleporter.link — публичное поле с парой.
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
            // Первая удачная сборка на уровне — всегда в лог: по ней видно, что маршрут
            // вообще работает, а не молча откатился на догадки. Сама карта картинкой —
            // в подробный журнал: по ней видно, что вокруг любой точки из журнала.
            if (!_navReported)
            {
                _navReported = true;
                Log("мир: карта проходимости " + w + "x" + h + " клеток, узлов " + nav.nodeCount
                    + ", телепортов " + links + ", стёкол " + panes + ", из них мимо центров клеток " + unseen
                    + ", шипов и пил " + hazards);
                DumpMap(nav, "первая сборка");
                Detail("карта: предметы:" + MapThings(nav));
            }
            else if (before != null && before.width == w && before.height == h)
                LogMapChanges(before.cells, nav);
        }

        // Подклассы (Down, Left, Right) игра кладёт и под базовый тип.
        private static readonly Type[] _hazards = { typeof(Spikes), typeof(Saws) };

        private static bool SameCells(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Разрушенная геометрия, открытые двери, брошенные мины, лифты — по разнице
        // клеток между перестройками: игра о них не сообщает, а маршрут от них зависит.
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

        // Блок, который сейчас стоит в центре клетки: по нему видно, открылась ли дверь
        // или стена исчезла совсем — тогда блока нет, и дописывать нечего.
        private static string BlockAt(NavMap nav, int i)
        {
            Block b = Level.CheckPoint<Block>(new Vec2(nav.originX + (i % nav.width + 0.5f) * NavCell,
                                                       nav.originY + (i / nav.width + 0.5f) * NavCell));
            if (b == null) return "";
            Door door = b as Door;
            return "(" + b.GetType().Name + (b.solid ? "" : " несплошной")
                + (door != null && door.locked ? " заперта" : "") + ")";
        }

        // Что стоит на уровне и что карта видит не так, как утка: двери, ящики, трубы,
        // выступы и рампы. Клетка — по дампу карты рядом, так фантомный проём или
        // тонкий пол называются по имени, а не угадываются. Типы друг от друга не
        // наследуют, так что ничего не попадёт в список дважды.
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
                        .Append((int)Math.Floor((t.x - nav.originX) / NavCell)).Append(',')
                        .Append((int)Math.Floor((t.y - nav.originY) / NavCell));
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
