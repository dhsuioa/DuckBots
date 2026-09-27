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
        // Следующая точка маршрута от одной мировой координаты к другой.
        public bool NextStep(float fromX, float fromY, float toX, float toY,
                             out float stepX, out float stepY, out int edge)
        {
            stepX = 0f; stepY = 0f; edge = Walk;
            if (_nodes == 0) return false;

            int start = NodeNear(fromX, fromY);
            int goal = NodeNear(toX, toY);
            if (start < 0 || goal < 0) return false;
            if (start == goal) return false;
            if (!Search(start, goal)) return false;
            int prog;
            return FirstStep(start, goal, out stepX, out stepY, out edge, out prog);
        }

        // Дейкстра от узла: длина маршрута до каждого узла сразу. Одним поиском
        // бот получает и дорогу к врагу, и длины до всех стволов, поэтому цель
        // выбирается по длине маршрута, а не по прямой.
        public void SearchAll(int start)
        {
            if (start >= 0 && start < _nodes) Search(start, -1);
        }

        // Длина маршрута до узла по последнему поиску; MaxValue — дороги нет.
        public float RouteLength(int node)
        {
            return node < 0 || node >= _nodes ? float.MaxValue : _g[node];
        }

        // Первый шаг к узлу по последнему поиску: куда, каким ребром и, если это
        // прыжок, по какой программе.
        public bool FirstStep(int start, int goal, out float stepX, out float stepY, out int edge, out int prog)
        {
            stepX = 0f; stepY = 0f; edge = Walk; prog = 0;
            if (goal < 0 || goal >= _nodes || goal == start || _from[goal] < 0) return false;
            int step = goal;
            while (_from[step] != start && _from[step] >= 0) step = _from[step];
            if (_from[step] != start) return false;
            stepX = CellX(step);
            stepY = CellY(step);
            edge = _fromEdge[step];
            prog = _fromProg[step];
            return true;
        }

        // Программа прыжка, которым пришли в узел по последнему поиску.
        public int ProgInto(int node) { return _fromProg[node]; }

        // Путь целиком по последнему поиску: узлы от старта до цели. Ноль — дороги
        // нет или путь не влез в буфер.
        public int PathTo(int goal, int[] into)
        {
            if (goal < 0 || goal >= _nodes || _g[goal] >= float.MaxValue) return 0;
            int n = 0;
            for (int at = goal; at >= 0; at = _from[at])
            {
                if (n >= into.Length) return 0;
                into[n++] = at;
            }
            Array.Reverse(into, 0, n);
            return n;
        }

        // Каким ребром пришли в узел по последнему поиску.
        public int EdgeInto(int node) { return _fromEdge[node]; }

        private bool Linked(int node)
        {
            for (int i = 0; i < _links; i++)
                if (_linkFrom[i] == node || _linkTo[i] == node) return true;
            return false;
        }

        // Карта картинкой для журнала: # стена, | стекло, = платформа, ^ смерть, S < >
        // пружины, o узел, T телепорт, . пусто. Сверху линейка колонок (десятки и
        // единицы), слева номер строки — по ним пиксели из любой строки журнала
        // переводятся в клетку: (x − начало) / 16.
        public void Render(List<string> into)
        {
            char[] line = new char[_w];
            for (int x = 0; x < _w; x++) line[x] = x % 10 == 0 ? (char)('0' + x / 10 % 10) : ' ';
            into.Add("    " + new string(line));
            for (int x = 0; x < _w; x++) line[x] = (char)('0' + x % 10);
            into.Add("    " + new string(line));
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    byte c = _cells[i];
                    char ch = '.';
                    if (c == Solid) ch = IsGlass(x, y) ? '|' : '#';
                    else if (c == Platform) ch = '=';
                    else if (c == Deadly) ch = '^';
                    else if (c == SpringUp) ch = 'S';
                    else if (c == SpringLeft) ch = '<';
                    else if (c == SpringRight) ch = '>';
                    else if (_node[i] >= 0) ch = Linked(_node[i]) ? 'T' : 'o';
                    line[x] = ch;
                }
                into.Add(y.ToString(CultureInfo.InvariantCulture).PadLeft(3) + " " + new string(line));
            }
        }

        // Что граф думает о движении, которое кто-то уже сделал: из точки отрыва
        // в точку приземления. Нужно журналу игрока — каждый ответ «пути нет» на
        // живое движение человека означает место, где бот неверно видит пространство.
        public const int OffMap = -1, NoPath = 0, KnownEdge = 1, Detour = 2;

        public int MoveKnown(float fromX, float fromY, float toX, float toY, out int edge)
        {
            edge = Walk;
            if (_nodes == 0) return OffMap;
            int start = NodeNear(fromX, fromY);
            int goal = NodeNear(toX, toY);
            if (start < 0 || goal < 0) return OffMap;
            if (start == goal) return KnownEdge;

            int count = Edges(start);
            for (int i = 0; i < count; i++)
                if (_nb[i] == goal) { edge = _nbEdge[i]; return KnownEdge; }
            return Search(start, goal) ? Detour : NoPath;
        }

        // A* от узла к узлу; путь остаётся в _from и _fromEdge. Без цели (goal < 0)
        // это Дейкстра по всей карте: эвристика ноль, раньше времени не выходим.
        // ponytail: открытый список — линейный поиск минимума. Узлов на арене около
        // тысячи, это микросекунды; куча понадобится, только если карты вырастут.
        private bool Search(int start, int goal)
        {
            for (int i = 0; i < _nodes; i++)
            {
                _closed[i] = false; _opened[i] = false;
                _g[i] = float.MaxValue; _from[i] = -1; _fromEdge[i] = Walk; _fromProg[i] = 0;
            }

            bool aimed = goal >= 0;
            float gx = aimed ? CellX(goal) : 0f, gy = aimed ? CellY(goal) : 0f;
            _g[start] = 0f;
            _opened[start] = true;
            _openCount = 0;
            _open[_openCount++] = start;

            while (_openCount > 0)
            {
                int bestAt = 0;
                float bestF = float.MaxValue;
                for (int i = 0; i < _openCount; i++)
                {
                    int node = _open[i];
                    float f = _g[node] + (aimed ? Heuristic(node, gx, gy) : 0f);
                    if (f < bestF) { bestF = f; bestAt = i; }
                }
                int current = _open[bestAt];
                _open[bestAt] = _open[--_openCount];
                _opened[current] = false;
                _closed[current] = true;

                if (current == goal) break;

                int count = Edges(current);
                for (int i = 0; i < count; i++)
                {
                    int next = _nb[i];
                    if (_closed[next]) continue;
                    // Прыжок дороже шага: при равной длине бот выберет пройти ногами.
                    // Прыжок со взмахами — ещё дороже: полёт длинный, ошибиться проще.
                    // Полёт пружиной дешевле всего — она делает работу за него.
                    // Стекло — дороже всего: отойти, разогнаться и подкатиться.
                    float extra = _nbEdge[i] == Jump ? (ProgHover(_nbProg[i]) > 0 ? _cell * 3f : _cell)
                                : _nbEdge[i] == Launch ? -_cell * 0.5f
                                : _nbEdge[i] == Smash ? SmashExtra : 0f;
                    float step = Dist(current, next) + extra;
                    if (step < 0f) step = 0f;
                    float g = _g[current] + step;
                    if (g >= _g[next]) continue;
                    _g[next] = g;
                    _from[next] = current;
                    _fromEdge[next] = _nbEdge[i];
                    _fromProg[next] = _nbProg[i];
                    if (!_opened[next] && _openCount < _open.Length)
                    {
                        _opened[next] = true;
                        _open[_openCount++] = next;
                    }
                }
            }

            return !aimed || _from[goal] >= 0;
        }

        private float Heuristic(int node, float gx, float gy)
        {
            float dx = CellX(node) - gx, dy = CellY(node) - gy;
            return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy);
        }

        private float Dist(int a, int b)
        {
            float dx = CellX(a) - CellX(b), dy = CellY(a) - CellY(b);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
