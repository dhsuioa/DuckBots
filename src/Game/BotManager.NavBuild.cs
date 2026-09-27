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
            if (Level.CheckPoint<Spikes>(p) != null) return NavMap.Deadly;
            if (Level.CheckPoint<Saws>(p) != null) return NavMap.Deadly;
            Icicles ice = Level.CheckPoint<Icicles>(p);
            if (ice != null && ice.kill) return NavMap.Deadly;
            // Лава и вода — частицы Fluid, а не блоки. Воду смертельной считать
            // неправильно, но утка в ней беспомощна, так что для маршрута это тоже нет.
            if (Level.CheckPoint<Fluid>(p) != null) return NavMap.Deadly;

            Spring sp = Level.CheckPoint<Spring>(p);
            if (sp != null)
            {
                if (sp is SpringDown) return NavMap.Empty;      // вниз пинает, маршруту бесполезна
                if (sp is SpringLeft) return sp.flipHorizontal ? NavMap.SpringRight : NavMap.SpringLeft;
                if (sp is SpringRight) return sp.flipHorizontal ? NavMap.SpringLeft : NavMap.SpringRight;
                return NavMap.SpringUp;                         // прямая и диагональные
            }

            Block b = Level.CheckPoint<Block>(p);
            if (b != null)
            {
                // Дверь — это Block, но утка проходит её насквозь. Незапертая дверь
                // в карте должна быть пустотой, иначе на замковых и пирамидных уровнях
                // половина арены оказывается недостижимой.
                Door door = b as Door;
                if (door != null) return door.locked ? NavMap.Solid : NavMap.Empty;
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
            int w = (int)((br.x - tl.x) / NavCell) + 2;
            int h = (int)((br.y - tl.y) / NavCell) + 2;
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
                        new Vec2(tl.x + (x + 0.5f) * NavCell, tl.y + (y + 0.5f) * NavCell));

            // Лежащая мина с выдернутой чекой — нажимная ловушка: наступил и сошёл —
            // взрыв, таймера у неё нет. Для маршрута это клетка смерти, как шипы: её
            // обходят, а не бегут от неё в стену, как на forest10_8.
            foreach (Thing t in Level.current.things[typeof(Mine)])
            {
                Mine mine = t as Mine;
                if (mine == null || mine._pin || mine.owner != null || mine.removeFromLevel) continue;
                int mx = (int)Math.Floor((mine.x - tl.x) / NavCell);
                int my = (int)Math.Floor((mine.y - tl.y) / NavCell);
                if (mx >= 0 && my >= 0 && mx < w && my < h) cells[my * w + mx] = NavMap.Deadly;
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
            nav.Build(cells, w, h, NavCell, tl.x, tl.y);

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
                    + ", телепортов " + links);
                DumpMap(nav, "первая сборка");
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
                        .Append(CellName(was[i])).Append('→').Append(CellName(now[i]));
            }
            if (changed == 0) return;
            Log("мир: карта изменилась клеток=" + changed + " запись=f" + frame);
            Detail("мир: изменения" + list + (changed > 40 ? " …" : ""));
            DumpMap(nav, "после изменений");
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
                case NavMap.SpringRight: return "пружина";
                default: return "пусто";
            }
        }

        private static readonly List<string> _mapRows = new List<string>();

        private static void DumpMap(NavMap nav, string why)
        {
            Detail("карта: " + why + " уровень=" + MapName(Level.current) + " клеток=" + nav.width + "x"
                + nav.height + " узлов=" + nav.nodeCount + " начало=" + (int)nav.originX + ","
                + (int)nav.originY + " клетка=16 — # стена, = платформа, ^ смерть, S < > пружины,"
                + " o узел, T телепорт, . пусто");
            _mapRows.Clear();
            nav.Render(_mapRows);
            for (int i = 0; i < _mapRows.Count; i++) Detail("карта: " + _mapRows[i]);
        }
    }
}
