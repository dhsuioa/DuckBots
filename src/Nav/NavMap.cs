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
    // Карта проходимости и поиск маршрута. Своего навигационного графа игра не даёт:
    // её собственный A* ходит по объектам PathNode, которых в ванильных картах нет ни
    // одного. Поэтому граф строим сами из геометрии уровня.
    //
    // Никаких игровых типов здесь нет: клетки приходят числами, значит и граф, и A*
    // проверяются на выдуманной карте, не запуская игру.
    public partial class NavMap
    {
        public const byte Empty = 0, Solid = 1, Platform = 2, Deadly = 3;
        public const byte SpringUp = 4, SpringLeft = 5, SpringRight = 6;
        public const int Walk = 0, Jump = 1, Drop = 2, Launch = 3;

        // Пружина даёт vSpeed = -22 против -4.9 у прыжка, то есть 1210 пикселей
        // подъёма вместо шестидесяти. Ограничиваем разумным коробом: выше потолка
        // арены всё равно ничего нет, а граф от щедрости распухнет.
        private const int LaunchRise = 12;
        private const int LaunchSide = 10;

        private byte[] _cells;
        private int _w, _h;
        private float _cell, _originX, _originY;
        private int[] _node;       // клетка -> номер узла, -1 если стоять нельзя
        private int[] _nodeCell;   // номер узла -> клетка
        private int _nodes;
        private int[] _abyssDir;   // узел -> с какой стороны пропасть для броска (0 — нет)
        private int _abyssEdges;

        // Телепорты: пары узлов, между которыми игра переносит утку сама.
        private int[] _linkFrom = new int[0];
        private int[] _linkTo = new int[0];
        private int _links;

        // Рабочие массивы A* живут вместе с картой: перекладывать мусор каждый кадр
        // ради поиска пути — верный способ получить просадки на ровном месте.
        private float[] _g;
        private int[] _from;
        private int[] _fromEdge, _fromProg;
        private bool[] _closed, _opened;
        private int[] _open;
        private int _openCount;
        private readonly int[] _nb = new int[1024];
        private readonly int[] _nbEdge = new int[1024];
        private readonly int[] _nbProg = new int[1024];     // программа прыжка на ребре
        private readonly int[] _nbFrames = new int[1024];   // кадров полёта по ней
        private int[][] _adj, _adjEdge, _adjProg;   // соседи каждого узла, посчитанные при сборке

        public int nodeCount { get { return _nodes; } }
        public int width { get { return _w; } }
        public int height { get { return _h; } }
        public byte[] cells { get { return _cells; } }
        public float originX { get { return _originX; } }
        public float originY { get { return _originY; } }
        public int CellCol(int node) { return _nodeCell[node] % _w; }
        public int CellRow(int node) { return _nodeCell[node] / _w; }

        public void Build(byte[] cells, int w, int h, float cellSize, float originX, float originY)
        {
            _cells = cells; _w = w; _h = h; _cell = cellSize;
            _originX = originX; _originY = originY;
            _links = 0;

            _node = new int[w * h];
            for (int i = 0; i < _node.Length; i++) _node[i] = -1;
            _nodes = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Standable(x, y)) _node[y * w + x] = _nodes++;

            _nodeCell = new int[_nodes];
            for (int i = 0; i < _node.Length; i++)
                if (_node[i] >= 0) _nodeCell[_node[i]] = i;

            _abyssDir = new int[_nodes];
            _abyssEdges = 0;
            for (int i = 0; i < _nodes; i++)
            {
                _abyssDir[i] = FindAbyssDir(_nodeCell[i] % w, _nodeCell[i] / w);
                if (_abyssDir[i] != 0) _abyssEdges++;
            }

            _g = new float[_nodes];
            _from = new int[_nodes];
            _fromEdge = new int[_nodes];
            _fromProg = new int[_nodes];
            _closed = new bool[_nodes];
            _opened = new bool[_nodes];
            _open = new int[_nodes + 1];
            _linkFrom = new int[16];
            _linkTo = new int[16];

            // Соседей считаем один раз: дальше A* и сверка журнала только читают их.
            _adj = new int[_nodes][];
            _adjEdge = new int[_nodes][];
            _adjProg = new int[_nodes][];
            for (int i = 0; i < _nodes; i++)
            {
                int count = Neighbors(i);
                _adj[i] = new int[count];
                _adjEdge[i] = new int[count];
                _adjProg[i] = new int[count];
                Array.Copy(_nb, _adj[i], count);
                Array.Copy(_nbEdge, _adjEdge[i], count);
                Array.Copy(_nbProg, _adjProg[i], count);
            }
        }

        // Телепорт связывает два узла напрямую: Teleporter._link в игре публичный,
        // так что пару искать не надо, она уже известна.
        public void AddLink(float fromX, float fromY, float toX, float toY)
        {
            int a = NodeNear(fromX, fromY), b = NodeNear(toX, toY);
            if (a < 0 || b < 0 || a == b || _links >= _linkFrom.Length) return;
            _linkFrom[_links] = a;
            _linkTo[_links] = b;
            _links++;
        }

        private byte At(int x, int y)
        {
            // Под картой пропасть: в Duck Game падение вниз смертельно. Раньше здесь
            // тоже была стена, и нижний ряд карты становился невидимым полом.
            if (y >= _h) return Deadly;
            // За краями карты — открытый воздух: границы карты — это крайние блоки
            // уровня, а настоящая стена, если она есть, лежит внутри. Раньше бока и верх
            // считались стеной, и симуляция «отскакивала» от них: прыжок с полки у верха
            // обрезался, а прыжок к боку садился обратно, хотя в игре улетел бы за край.
            // Шагать туда наугад бот и так не станет — под этим воздухом пропасть.
            if (x < 0 || x >= _w || y < 0) return Empty;
            return _cells[y * _w + x];
        }

        // Платформа проходима снизу вверх, поэтому для полёта она пустая.
        // Пружина тоже не преграда: сквозь неё пролетают, от неё отталкиваются.
        private bool Fly(int x, int y)
        {
            byte c = At(x, y);
            return c == Empty || c == Platform || c >= SpringUp;
        }

        private static bool IsSpring(byte c) { return c >= SpringUp; }

        private bool Standable(int x, int y)
        {
            if (At(x, y) != Empty || At(x, y - 1) == Solid) return false;
            byte below = At(x, y + 1);
            // На пружине не стоят, но клетка над ней — законное место: туда приходят
            // ногами, и оттуда игра отправляет в полёт.
            return below == Solid || below == Platform || IsSpring(below);
        }

        // Пружина, которая сработает из этой клетки, и куда она бросит.
        private byte SpringAt(int x, int y)
        {
            byte below = At(x, y + 1);
            if (IsSpring(below)) return below;
            if (At(x - 1, y) == SpringRight) return SpringRight;
            if (At(x + 1, y) == SpringLeft) return SpringLeft;
            return Empty;
        }

        private int NodeAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return -1;
            return _node[y * _w + x];
        }

        public float CellX(int node) { return _originX + (_nodeCell[node] % _w + 0.5f) * _cell; }
        public float CellY(int node) { return _originY + (_nodeCell[node] / _w + 0.5f) * _cell; }

        // Дуга полёта пружиной — ломаной линией: вверх, потом прямо к цели.
        // ponytail: для пружин грубо — полёт от них не симулируется, потому что
        // скорость пружины (-22) уносит за пределы карты и сбивает всё прочее;
        // симулировать, если бот начнёт промахиваться мимо пружинных полок.
        private bool ArcClear(int x, int y, int tx, int ty)
        {
            int top = (ty < y ? ty : y) - 1;
            for (int cy = y - 1; cy >= top; cy--)
                if (!Fly(x, cy)) return false;       // над головой должно быть куда взлетать

            // Прыжок вниз: на спуске односторонняя платформа уже не пропускает, а ловит.
            // Иначе граф предлагал прыжок сквозь ту платформу, на которой бот стоит,
            // и бот прыгал на месте, садясь обратно на неё же.
            bool falling = ty > y;
            int steps = Math.Abs(tx - x) + Math.Abs(top - ty) + 1;
            for (int i = 1; i <= steps; i++)
            {
                int cx = x + (tx - x) * i / steps;
                int cy = top + (ty - 1 - top) * i / steps;
                if (!Fly(cx, cy)) return false;
                if (falling && At(cx, cy) == Platform) return false;
            }
            return true;
        }

        // Соседи узла, уже посчитанные при сборке, плюс телепорты, которые
        // добавляются после неё. Всё складывается в общий буфер _nb/_nbEdge.
        private int Edges(int node)
        {
            int[] adj = _adj[node], kind = _adjEdge[node];
            int n = adj.Length;
            Array.Copy(adj, _nb, n);
            Array.Copy(kind, _nbEdge, n);
            Array.Copy(_adjProg[node], _nbProg, n);
            // Телепорт переносит сам, идти никуда не надо.
            for (int i = 0; i < _links && n < _nb.Length; i++)
            {
                if (_linkFrom[i] == node) { _nb[n] = _linkTo[i]; _nbEdge[n] = Walk; _nbProg[n] = 0; n++; }
                else if (_linkTo[i] == node) { _nb[n] = _linkFrom[i]; _nbEdge[n] = Walk; _nbProg[n] = 0; n++; }
            }
            return n;
        }

        // Все движения из узла по геометрии: шаги, падения, провалы, прыжки, пружины.
        // Считается один раз на узел при сборке: с честной дальностью кандидатов на
        // прыжок стало несколько сотен, и пересчитывать их на каждом шаге A* дорого.
        private int Neighbors(int node)
        {
            int cell = _nodeCell[node];
            int x = cell % _w, y = cell / _w;
            int n = 0;

            // Встал на пружину — дальше решает она, а не бот. Поэтому из такой клетки
            // растут только полёты: пройти через пружину ногами всё равно не выйдет.
            byte spring = SpringAt(x, y);
            if (spring != Empty)
            {
                n += Launches(spring, x, y, n);
                return n;
            }

            for (int d = -1; d <= 1; d += 2)
            {
                // Шаг вбок.
                int side = NodeAt(x + d, y);
                if (side >= 0) { _nb[n] = side; _nbEdge[n] = Walk; _nbProg[n] = 0; n++; }
                else if (IsSpring(At(x + d, y)) && NodeAt(x + d, y - 1) >= 0)
                {
                    // Шагнул на пружину — стоишь на ней, дальше она бросит сама.
                    _nb[n] = NodeAt(x + d, y - 1); _nbEdge[n] = Walk; _nbProg[n] = 0; n++;
                }
                else if (At(x + d, y) == Empty)
                {
                    // Сбоку пусто — можно просто сойти с края и упасть.
                    for (int cy = y + 1; cy < _h; cy++)
                    {
                        if (At(x + d, cy) == Solid) break;
                        int landed = NodeAt(x + d, cy);
                        if (landed >= 0) { _nb[n] = landed; _nbEdge[n] = Walk; _nbProg[n] = 0; n++; break; }
                    }
                }
            }

            // Вниз сквозь одностороннюю платформу.
            if (At(x, y + 1) == Platform)
                for (int cy = y + 2; cy < _h; cy++)
                {
                    if (At(x, cy) == Solid) break;
                    int landed = NodeAt(x, cy);
                    if (landed >= 0) { _nb[n] = landed; _nbEdge[n] = Drop; _nbProg[n] = 0; n++; break; }
                }

            // Прыжки: каждая программа прогоняется той же физикой, что у игры, и где
            // утка сядет — туда ребро. Из нескольких программ в один узел остаётся та,
            // что короче по времени полёта: чем короче полёт, тем меньше ошибка.
            int first = n;
            for (int i = 0; i < _progs.Length; i++)
            {
                int frames;
                int target = Simulate(node, _progs[i], out frames);
                if (target < 0 || target == node) continue;
                int at = -1;
                for (int k = first; k < n; k++)
                    if (_nb[k] == target) { at = k; break; }
                if (at < 0)
                {
                    if (n >= _nb.Length) continue;
                    at = n++;
                    _nb[at] = target;
                    _nbEdge[at] = Jump;
                    _nbFrames[at] = int.MaxValue;
                }
                if (frames < _nbFrames[at])
                {
                    _nbProg[at] = _progs[i];
                    _nbFrames[at] = frames;
                }
            }
            return n;
        }

        // ponytail: диагональные пружины считаются вертикальными, а SpringDown
        // не даёт рёбер вовсе. Полноценно это значит считать баллистику с начальной
        // скоростью под углом — делать, когда станет видно, что бот мимо них ходит.
        private int Launches(byte spring, int x, int y, int at)
        {
            int n = 0;
            int fromX = -LaunchSide, toX = LaunchSide, fromY = -LaunchRise, toY = 2;
            if (spring == SpringLeft) { toX = -1; fromY = -2; }
            else if (spring == SpringRight) { fromX = 1; fromY = -2; }

            for (int dy = fromY; dy <= toY; dy++)
                for (int dx = fromX; dx <= toX; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int target = NodeAt(x + dx, y + dy);
                    if (target < 0 || at + n >= _nb.Length) continue;
                    if (!ArcClear(x, y, x + dx, y + dy)) continue;
                    _nb[at + n] = target; _nbEdge[at + n] = Launch; _nbProg[at + n] = 0; n++;
                }
            return n;
        }

        // Узел под точкой: утка стоит на чём-то, но её центр может попасть в клетку,
        // где стоять нельзя, поэтому ищем ближайшую подходящую вокруг. Расстояние
        // меряется от самой точки, а не от её клетки: на city11 25.09 бот стоял
        // вплотную к стеклу справа, центр его попал в колонку стекла, и при равенстве
        // клеток выигрывал узел слева — за стеклом, откуда маршрут и считался.
        public int NodeNear(float worldX, float worldY)
        {
            float fx = (worldX - _originX) / _cell, fy = (worldY - _originY) / _cell;
            int x = (int)fx;
            int y = (int)fy;
            int best = -1;
            float bestDist = 0f;
            for (int r = 0; r <= 3; r++)
            {
                for (int cy = y - r; cy <= y + r; cy++)
                    for (int cx = x - r; cx <= x + r; cx++)
                    {
                        int node = NodeAt(cx, cy);
                        if (node < 0) continue;
                        float ddx = cx + 0.5f - fx, ddy = cy + 0.5f - fy;
                        float dist = ddx * ddx + ddy * ddy;
                        if (best < 0 || dist < bestDist) { best = node; bestDist = dist; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        // Узел строго под точкой, не глубже maxCells клеток. Ящик вскрывают ударом
        // снизу, а NodeNear для сплошного ящика находил клетку на его крыше: бот
        // залезал на ящик и прыгал там на месте. Платформу под ящиком прыжок
        // пробивает снизу, стену — нет.
        public int NodeBelow(float worldX, float worldY, int maxCells)
        {
            int x = (int)Math.Floor((worldX - _originX) / _cell);
            int y = (int)Math.Floor((worldY - _originY) / _cell);
            if (x < 0 || x >= _w) return -1;
            for (int cy = y + 1; cy <= y + maxCells && cy < _h; cy++)
            {
                if (cy < 0) continue;
                int node = _node[cy * _w + x];
                if (node >= 0) return node;
                byte c = _cells[cy * _w + x];
                if (c == Solid || c == Deadly) return -1;
            }
            return -1;
        }

        // Край над пропастью: с какой стороны узла пропасть (+1 справа, −1 слева, 0 нет).
        // Отсюда бросают пойманного сеткой врага: бросок с зажатым направлением уносит
        // его на несколько клеток вбок, и он падает за низ карты. Поэтому пропасть —
        // это три клетки подряд, под которыми до низа ни пола, ни платформы, ни
        // пружины; за боком карты — тоже пусто: уровень там кончается.
        public int AbyssDir(int node) { return _abyssDir[node]; }

        public bool hasAbyssEdge { get { return _abyssEdges > 0; } }

        private int FindAbyssDir(int x, int y)
        {
            for (int d = 1; d >= -1; d -= 2)
            {
                if (At(x + d, y) == Solid) continue;                              // стена, бросать некуда
                if (NodeAt(x + d, y) >= 0) continue;                             // рядом можно стоять — не край
                bool open = true;
                for (int k = 1; k <= 3 && open; k++) open = NoFloorBelow(x + d * k, y);
                if (open) return d;
            }
            return 0;
        }

        private bool NoFloorBelow(int x, int y)
        {
            if (x < 0 || x >= _w) return true;
            for (int cy = y; cy < _h; cy++)
            {
                byte c = _cells[cy * _w + x];
                if (c == Solid || c == Platform || IsSpring(c)) return false;
            }
            return true;
        }

        // Ближайший по маршруту край над пропастью (по последнему поиску).
        public int NearestAbyssEdge(out int dir)
        {
            dir = 0;
            int best = -1;
            float bestLen = float.MaxValue;
            for (int n = 0; n < _nodes; n++)
            {
                if (_abyssDir[n] == 0 || _g[n] >= bestLen) continue;
                best = n;
                bestLen = _g[n];
                dir = _abyssDir[n];
            }
            return best;
        }

        // Позиция для выстрела: узел на той же высоте, что цель, в пределах дальности,
        // с чистой строкой клеток до цели — ствол в Duck Game бьёт только горизонтально,
        // а пулю останавливает только стена. Из таких узлов берётся ближайший по
        // маршруту (по последнему поиску). -1 — позиции нет или до неё не дойти.
        public int FiringNode(float targetX, float targetFeetY, float minDx, float maxDx)
        {
            int tx = (int)Math.Floor((targetX - _originX) / _cell);
            int ty = (int)Math.Floor((targetFeetY - 4f - _originY) / _cell);   // клетка, где стоят ноги цели
            if (tx < 0 || tx >= _w || ty < 0 || ty >= _h) return -1;
            int best = -1;
            float bestLen = float.MaxValue;
            for (int dir = -1; dir <= 1; dir += 2)
                for (int x = tx + dir; x >= 0 && x < _w; x += dir)
                {
                    if (_cells[ty * _w + x] == Solid) break;   // стена закрывает линию
                    float dist = Math.Abs(x - tx) * _cell;
                    if (dist > maxDx) break;
                    int node = _node[ty * _w + x];
                    if (dist < minDx || node < 0 || _g[node] >= bestLen) continue;
                    best = node;
                    bestLen = _g[node];
                }
            return best;
        }

        // Есть ли под точкой куда упасть и встать. Смотрим столбец клеток вниз до
        // низа карты: первая непустая клетка решает. Пол, платформа и пружина
        // ловят, смерть и низ карты (дальше пропасть) — нет.
        public bool SafeBelow(float worldX, float worldY)
        {
            int x = (int)Math.Floor((worldX - _originX) / _cell);
            int y = (int)Math.Floor((worldY - _originY) / _cell);
            if (x < 0 || x >= _w) return false;
            for (int cy = y < 0 ? 0 : y; cy < _h; cy++)
            {
                byte c = _cells[cy * _w + x];
                if (c == Deadly) return false;
                if (c != Empty) return true;
            }
            return false;
        }
    }
}
