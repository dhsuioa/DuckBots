using System;
using System.Collections.Generic;
using System.Text;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        // Раз в RouteEvery кадров: один поиск от узла бота даёт и дорогу к врагу, и длины до всех
        // целей. В полёте маршрут не считается — у утки в воздухе нет клетки, — но счётчик
        // обнуляется: первый кадр на земле считает заново. Исключение — полёт с пружины:
        // один раз из узла над ней.
        private static void Route(Bot b, Duck me, Duck enemy, bool wantPickup, bool carrying, bool netHolder)
        {
            bool springFlight = b.flightStart >= 0 && me.ragdoll == null;
            if (!springFlight && (!me.grounded || me.ragdoll != null)) { b.routeWait = 0; return; }
            if (!springFlight && b.routeWait > 0) { b.routeWait--; return; }
            b.routeWait = RouteEvery;
            b.hasEnemyStep = false;
            b.hasPickupStep = false;

            int start = springFlight ? b.flightStart : _nav == null ? -1 : _nav.NodeNear(me.x, me.bottom - 4f);
            b.flightStart = -1;
            if (start < 0)
            {
                if (wantPickup)
                {
                    Thing near = NearestPickup(b, me);
                    if (near != b.pickup)
                        Log(BotName(b) + ": цель=" + (near == null ? "нет" : Describe(near))
                            + " по прямой: карты нет");
                    b.pickup = near;
                    b.pickupNode = -1;
                }
                return;
            }

            _nav.SearchAll(start);
            b.routeStartX = _nav.CellX(start);
            int enemyNode = enemy == null ? -1 : _nav.NodeNear(enemy.x, enemy.y);
            string to = enemy == null ? "нет" : "враг";
            // Со стволом — не в точку врага, а на позицию для выстрела, со взрывчаткой — не ближе BlastSafe.
            b.atSpot = false;
            float range = FireRange(me);
            if (enemyNode >= 0 && range > 0f && b.spotOff == 0)
            {
                Gun held = me.holdObject as Gun;
                float near = held != null && Explodes(held) ? BotBrain.BlastSafe : range < 100f ? 16f : 32f;
                int spot = _nav.FiringNode(enemy.x, enemy.bottom, near, range);
                if (spot >= 0)
                {
                    enemyNode = spot;
                    b.atSpot = spot == start;
                    to = "позиция для выстрела";
                }
            }
            b.hasEnemyStep = _nav.FirstStep(start, enemyNode,
                out b.enemyStepX, out b.enemyStepY, out b.enemyStepEdge, out b.enemyStepProg);
            int goal = enemyNode;

            b.hasThrowStep = false;
            b.atThrowSpot = false;
            if (carrying)
            {
                int dir;
                int edge = _nav.NearestAbyssEdge(out dir);
                if (edge >= 0)
                {
                    b.throwDir = dir;
                    b.atThrowSpot = edge == start;
                    b.hasThrowStep = _nav.FirstStep(start, edge,
                        out b.throwStepX, out b.throwStepY, out b.throwStepEdge, out b.throwStepProg);
                    goal = edge;
                    to = "край для броска";
                }
            }

            if (wantPickup)
            {
                ChoosePickup(b, me, netHolder);
                if (b.pickup != null)
                {
                    b.hasPickupStep = _nav.FirstStep(start, b.pickupNode,
                        out b.pickupStepX, out b.pickupStepY, out b.pickupStepEdge, out b.pickupStepProg);
                    goal = b.pickupNode;
                    to = b.pickup.GetType().Name;
                }
            }
            LogPath(b, me, start, goal, to);
        }

        private struct Candidate
        {
            public Thing thing;
            public int kind;
            public int node;
            public string status;   // null — годен
        }

        private const int KindTrap = BotBrain.KindTrap, KindGun = BotBrain.KindGun, KindNet = BotBrain.KindNet,
            KindBomb = BotBrain.KindBomb, KindPresent = BotBrain.KindPresent, KindBox = BotBrain.KindBox;

        private static readonly List<Candidate> _cands = new List<Candidate>();
        private static bool _canThrow;

        private static void ChoosePickup(Bot b, Duck me, bool netHolder)
        {
            _cands.Clear();
            int side;
            _canThrow = _nav.NearestAbyssEdge(out side) >= 0;
            foreach (Thing t in Level.current.things[typeof(TrappedDuck)])
            {
                TrappedDuck trap = t as TrappedDuck;
                if (trap != null && trap.captureDuck != me && TrapFree(trap)) AddCandidate(b, t, KindTrap, netHolder);
            }
            foreach (Thing t in Level.current.things[typeof(Gun)])
                AddCandidate(b, t, t is Grenade || t is Mine ? KindBomb : t is NetGun ? KindNet : KindGun, netHolder);
            foreach (Thing t in Level.current.things[typeof(Present)]) AddCandidate(b, t, KindPresent, netHolder);
            foreach (Thing t in Level.current.things[typeof(ItemBox)]) AddCandidate(b, t, KindBox, netHolder);

            int best = -1;
            for (int i = 0; i < _cands.Count; i++)
            {
                Candidate c = _cands[i];
                if (c.status != null) continue;
                if (best < 0 || BotBrain.BetterPickup(c.kind, _nav.RouteLength(c.node),
                                                      _cands[best].kind, _nav.RouteLength(_cands[best].node)))
                    best = i;
            }
            Thing chosen = best < 0 ? null : _cands[best].thing;
            if (chosen != b.pickup) LogCandidates(b, me, best);
            b.pickup = chosen;
            b.pickupNode = best < 0 ? -1 : _cands[best].node;
        }

        // Только видимое: в сети ящик держит содержимое заранее, невидимым (ItemBox.UpdateContainedObject).
        private static void AddCandidate(Bot b, Thing t, int kind, bool netHolder)
        {
            Holdable h = t as Holdable;
            if (t.removeFromLevel || !t.visible || (h != null && (h.owner != null || !h.canPickUp))) return;
            Candidate c = new Candidate();
            c.thing = t;
            c.kind = kind;
            c.node = -1;
            Gun g = t as Gun;
            if (netHolder && kind > KindGun) c.status = "в руках сеткомёт";
            else if (g != null && g.ammo <= 0) c.status = "пустой";
            else if ((kind == KindTrap || kind == KindNet) && !_canThrow) c.status = "некуда бросить: нет пропасти";
            else if (g != null && !g.isFatal && kind != KindNet) c.status = "несмертельный";
            else if (g is SuicidePistol) c.status = "стреляет назад";
            else if (g != null && Unskilled(g)) c.status = "особое: бот пока не умеет";
            else if (kind == KindBox && ((ItemBox)t)._hit) c.status = "ящик пуст";
            // Фиолетовый выдаёт копию ствола только тому, кто его туда положил (Level.core._storedItems).
            else if (t is PurpleBlock) c.status = "фиолетовый: бот пока не умеет им пользоваться";
            else if (b.skip.Contains(t)) c.status = "забыт сторожем";
            else
            {
                // Ящик бьют снизу: идти под него.
                c.node = kind == KindBox ? _nav.NodeBelow(t.x, t.y, 4) : _nav.NodeNear(t.x, t.y);
                if (c.node < 0) c.status = kind == KindBox ? "нет места под ящиком" : "вне карты";
                else if (_nav.RouteLength(c.node) >= float.MaxValue) c.status = "нет дороги";
            }
            _cands.Add(c);
        }

        private static void LogCandidates(Bot b, Duck me, int best)
        {
            int usable = 0;
            for (int i = 0; i < _cands.Count; i++) if (_cands[i].status == null) usable++;
            Log(BotName(b) + ": цель=" + (best < 0 ? "нет" : Describe(_cands[best].thing)
                    + " путь=" + (int)_nav.RouteLength(_cands[best].node))
                + " кандидатов=" + _cands.Count + " годных=" + usable
                + " было=" + (b.pickup == null ? "нет" : b.pickup.GetType().Name));
            for (int i = 0; i < _cands.Count; i++)
            {
                Candidate c = _cands[i];
                float dx = c.thing.x - me.x, dy = c.thing.y - me.y;
                Detail(BotName(b) + ": кандидат " + Describe(c.thing) + " x=" + (int)c.thing.x
                    + " y=" + (int)c.thing.y + " по_прямой=" + (int)Math.Sqrt(dx * dx + dy * dy)
                    + (c.status != null ? " отброшен — " + c.status
                        : " путь=" + (int)_nav.RouteLength(c.node) + (i == best ? " ВЫБРАН" : "")));
            }
        }

        private static Thing NearestPickup(Bot b, Duck me)
        {
            _skipFor = b.skip;
            Thing t = Level.current.NearestThingFilter<Gun>(me.position, _freeGun);
            if (t == null) t = Level.current.NearestThingFilter<Present>(me.position, _freePresent);
            if (t == null) t = Level.current.NearestThingFilter<ItemBox>(me.position, _liveBox);
            _skipFor = null;
            return t;
        }

        private static readonly int[] _pathBuf = new int[512];

        // «своя_клетка=нет» — бот стоит на том, чего карта не видит: выступ, тонкий блок, ящик, утка.
        private static void LogPath(Bot b, Duck me, int start, int goal, string to)
        {
            int n = _nav.PathTo(goal, _pathBuf);
            int same = 0;
            while (same < n && same < b.pathLen && _pathBuf[same] == b.path[same]) same++;
            if (same == n && n == b.pathLen) return;

            int col = (int)Math.Floor((me.x - _nav.originX) / NavCell);
            int row = (int)Math.Floor((me.bottom - 4f - _nav.originY) / NavCell);
            bool own = _nav.CellCol(start) == col && _nav.CellRow(start) == row;
            StringBuilder sb = new StringBuilder();
            sb.Append(BotName(b)).Append(": маршрут к=").Append(to).Append(" узлов=").Append(n)
              .Append(" цена=").Append(n > 0 ? "" + (int)_nav.RouteLength(goal) : "нет")
              .Append(" старт=").Append(_nav.CellCol(start)).Append(',').Append(_nav.CellRow(start))
              .Append(" своя_клетка=").Append(own ? "да" : "нет(" + col + "," + row + ")")
              .Append(" отличие_с=").Append(same).Append(" путь=");
            for (int i = 0; i < n; i++)
            {
                int node = _pathBuf[i];
                if (i > 0)
                {
                    sb.Append(' ').Append(EdgeName(_nav.EdgeInto(node)));
                    int prog = _nav.ProgInto(node);
                    if (prog != 0) sb.Append('{').Append(NavMap.ProgWord(prog)).Append('}');
                }
                sb.Append('(').Append(_nav.CellCol(node)).Append(',').Append(_nav.CellRow(node)).Append(')');
            }
            Detail(sb.ToString());
            Array.Copy(_pathBuf, b.path, n);
            b.pathLen = n;
        }
    }
}
