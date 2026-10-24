using System;

namespace DuckBots
{
    // Карта проходимости и поиск маршрута. Свой граф, потому что PathNode для A* игры в
    // ванильных картах нет. Игровых типов здесь нет: клетки приходят числами.
    public partial class NavMap
    {
        public const byte Empty = 0, Solid = 1, Platform = 2, Deadly = 3;
        public const byte SpringUp = 4, SpringLeft = 5, SpringRight = 6;
        public const byte SpringDiagLeft = 7, SpringDiagRight = 8;
        public const int Walk = 0, Jump = 1, Drop = 2, Launch = 3, Smash = 4;

        public const float SmashExtra = 96f;   // ручка силы для этапа 3: цена удара по стеклу в пикселях пути

        // Короб целей пружины: она даёт vSpeed −22, это 1210 px подъёма.
        private const int LaunchRise = 12;
        private const int LaunchSide = 10;

        private byte[] _cells;
        private bool[] _glass;     // стена, которую можно разбить
        private int _w, _h;
        private float _cell, _originX, _originY;
        private int[] _node;
        private int[] _nodeCell;
        private int _nodes;
        private int[] _abyssDir;
        private int _abyssEdges;

        private int[] _linkFrom = new int[0];
        private int[] _linkTo = new int[0];
        private int _links;

        private float[] _g;
        private int[] _from;
        private int[] _fromEdge, _fromProg;
        private bool[] _closed, _opened;
        private int[] _open;
        private int _openCount;
        private readonly int[] _nb = new int[1024];
        private readonly int[] _nbEdge = new int[1024];
        private readonly int[] _nbProg = new int[1024];
        private readonly int[] _nbFrames = new int[1024];
        private int[][] _adj, _adjEdge, _adjProg;

        public int nodeCount { get { return _nodes; } }
        public int width { get { return _w; } }
        public int height { get { return _h; } }
        public byte[] cells { get { return _cells; } }
        public float originX { get { return _originX; } }
        public float originY { get { return _originY; } }
        public int CellCol(int node) { return _nodeCell[node] % _w; }
        public int CellRow(int node) { return _nodeCell[node] / _w; }

        public void Build(byte[] cells, int w, int h, float cellSize, float originX, float originY,
                          bool[] glass = null)
        {
            _cells = cells; _glass = glass; _w = w; _h = h; _cell = cellSize;
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

        public void AddLink(float fromX, float fromY, float toX, float toY)
        {
            int a = NodeNear(fromX, fromY), b = NodeNear(toX, toY);
            if (a < 0 || b < 0 || a == b || _links >= _linkFrom.Length) return;
            _linkFrom[_links] = a;
            _linkTo[_links] = b;
            _links++;
        }

        // Под картой — пропасть. За боками и верхом — воздух: настоящая стена, если есть, лежит внутри.
        private byte At(int x, int y)
        {
            if (y >= _h) return Deadly;
            if (x < 0 || x >= _w || y < 0) return Empty;
            return _cells[y * _w + x];
        }

        // Платформу пролетают снизу, пружину — насквозь.
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
            return below == Solid || below == Platform || IsSpring(below);
        }

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

        private bool IsGlass(int x, int y)
        {
            return _glass != null && x >= 0 && y >= 0 && x < _w && y < _h && _glass[y * _w + x];
        }

        public static int CellOf(float world, float origin, float cell) { return (int)Math.Floor((world - origin) / cell); }
        public int Col(float wx) { return CellOf(wx, _originX, _cell); }
        public int Row(float wy) { return CellOf(wy, _originY, _cell); }

        // Где встанет утка, вошедшая в столбец x на высоте y; −1 — стена или смерть по дороге.
        private int LandingBelow(int x, int y)
        {
            for (int cy = y; cy < _h; cy++)
            {
                byte c = At(x, cy);
                if (c == Solid || c == Deadly) return -1;
                int node = NodeAt(x, cy);
                if (node >= 0) return node;
            }
            return -1;
        }

        public float CellX(int node) { return _originX + (_nodeCell[node] % _w + 0.5f) * _cell; }
        public float CellY(int node) { return _originY + (_nodeCell[node] / _w + 0.5f) * _cell; }

        // Полёт пружиной ломаной линией: вверх, потом прямо к цели. На спуске платформа ловит.
        private bool ArcClear(int x, int y, int tx, int ty)
        {
            int top = (ty < y ? ty : y) - 1;
            for (int cy = y - 1; cy >= top; cy--)
                if (!Fly(x, cy)) return false;

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

        // Сосед в общий буфер _nb: возвращает новую длину.
        private int Add(int n, int node, int edge)
        {
            _nb[n] = node;
            _nbEdge[n] = edge;
            _nbProg[n] = 0;
            return n + 1;
        }

        private int Edges(int node)
        {
            int[] adj = _adj[node], kind = _adjEdge[node];
            int n = adj.Length;
            Array.Copy(adj, _nb, n);
            Array.Copy(kind, _nbEdge, n);
            Array.Copy(_adjProg[node], _nbProg, n);
            for (int i = 0; i < _links && n < _nb.Length; i++)
            {
                if (_linkFrom[i] == node) n = Add(n, _linkTo[i], Walk);
                else if (_linkTo[i] == node) n = Add(n, _linkFrom[i], Walk);
            }
            return n;
        }

        // Все движения из узла. Считаются один раз при сборке: кандидатов на прыжок сотни.
        private int Neighbors(int node)
        {
            int cell = _nodeCell[node];
            int x = cell % _w, y = cell / _w;
            int n = 0;

            // Встал на пружину — дальше решает она: из такой клетки растут только полёты.
            byte spring = SpringAt(x, y);
            if (spring != Empty) return Launches(spring, x, y, n);

            for (int d = -1; d <= 1; d += 2)
            {
                int side = NodeAt(x + d, y);
                if (side >= 0) n = Add(n, side, Walk);
                else if (IsSpring(At(x + d, y)) && NodeAt(x + d, y - 1) >= 0) n = Add(n, NodeAt(x + d, y - 1), Walk);
                else if (At(x + d, y) == Empty)
                {
                    for (int cy = y + 1; cy < _h; cy++)
                    {
                        if (At(x + d, cy) == Solid) break;
                        int landed = NodeAt(x + d, cy);
                        if (landed >= 0) { n = Add(n, landed, Walk); break; }
                    }
                }
                else if (IsGlass(x + d, y) && (IsGlass(x + d, y - 1) || At(x + d, y - 1) != Solid))
                {
                    // Ребро «стекло» ведёт туда, где утка встанет за проёмом.
                    int beyond = LandingBelow(x + 2 * d, y);
                    if (beyond >= 0) n = Add(n, beyond, Smash);
                }
            }

            if (At(x, y + 1) == Platform)
                for (int cy = y + 2; cy < _h; cy++)
                {
                    if (At(x, cy) == Solid) break;
                    int landed = NodeAt(x, cy);
                    if (landed >= 0) { n = Add(n, landed, Drop); break; }
                }

            // Каждая программа прыжка прогоняется физикой игры; в один узел остаётся та, что короче летит.
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

        // ponytail: полёт от пружины — короб целей, а не баллистика: прямая бросает
        // вверх в обе стороны, боковая — вбок, диагональная — вверх в свою сторону.
        // SpringDown рёбер не даёт. Считать баллистику, если бот начнёт мимо них летать.
        private int Launches(byte spring, int x, int y, int n)
        {
            int fromX = -LaunchSide, toX = LaunchSide, fromY = -LaunchRise, toY = 2;
            if (spring == SpringLeft) { toX = -1; fromY = -2; }
            else if (spring == SpringRight) { fromX = 1; fromY = -2; }
            else if (spring == SpringDiagLeft) toX = -1;
            else if (spring == SpringDiagRight) fromX = 1;

            for (int dy = fromY; dy <= toY; dy++)
                for (int dx = fromX; dx <= toX; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int target = NodeAt(x + dx, y + dy);
                    if (target < 0 || n >= _nb.Length) continue;
                    if (ArcClear(x, y, x + dx, y + dy)) n = Add(n, target, Launch);
                }
            return n;
        }

        // Ближайший узел. Расстояние — от самой точки, а не от её клетки: иначе у стекла при
        // равенстве клеток выигрывает узел за ним.
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

        // Узел строго под точкой — под ящик, который бьют снизу. Платформу прыжок пробивает, стену — нет.
        public int NodeBelow(float worldX, float worldY, int maxCells)
        {
            int x = Col(worldX), y = Row(worldY);
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

        // С какой стороны узла пропасть для броска пойманного (+1, −1, 0): три клетки подряд
        // без опоры до низа карты, бросок уносит на несколько клеток вбок.
        public int AbyssDir(int node) { return _abyssDir[node]; }

        public bool hasAbyssEdge { get { return _abyssEdges > 0; } }

        private int FindAbyssDir(int x, int y)
        {
            for (int d = 1; d >= -1; d -= 2)
            {
                if (At(x + d, y) == Solid) continue;
                if (NodeAt(x + d, y) >= 0) continue;
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

        // По последнему поиску.
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

        // Позиция для выстрела: узел на высоте цели в пределах дальности с чистой строкой клеток
        // до неё (ствол бьёт только горизонтально), ближайший по последнему поиску.
        public int FiringNode(float targetX, float targetFeetY, float minDx, float maxDx)
        {
            int tx = Col(targetX), ty = Row(targetFeetY - 4f);
            if (tx < 0 || tx >= _w || ty < 0 || ty >= _h) return -1;
            int best = -1;
            float bestLen = float.MaxValue;
            for (int dir = -1; dir <= 1; dir += 2)
                for (int x = tx + dir; x >= 0 && x < _w; x += dir)
                {
                    if (_cells[ty * _w + x] == Solid) break;
                    float dist = Math.Abs(x - tx) * _cell;
                    if (dist > maxDx) break;
                    int node = _node[ty * _w + x];
                    if (dist < minDx || node < 0 || _g[node] >= bestLen) continue;
                    best = node;
                    bestLen = _g[node];
                }
            return best;
        }

        // Есть ли под точкой куда упасть: первая непустая клетка вниз решает, низ карты — пропасть.
        public bool SafeBelow(float worldX, float worldY)
        {
            int x = Col(worldX), y = Row(worldY);
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
