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
                if (wantPickup) ChoosePickup(b, me, netHolder, false);
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
                ChoosePickup(b, me, netHolder, true);
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
            public float len;       // длина маршрута, без карты — по прямой
            public string status;   // null — годен
        }

        private static readonly List<Candidate> _cands = new List<Candidate>();
        private static bool _canThrow;

        // Одно правило и с картой, и без неё (бот вне карты): лучший вид цели, внутри вида — ближе.
        private static void ChoosePickup(Bot b, Duck me, bool netHolder, bool routed)
        {
            _cands.Clear();
            int side;
            _canThrow = routed && _nav.NearestAbyssEdge(out side) >= 0;
            foreach (Thing t in Level.current.things[typeof(TrappedDuck)])
            {
                TrappedDuck trap = t as TrappedDuck;
                if (trap != null && trap.captureDuck != me && TrapFree(trap)) AddCandidate(b, me, t, BotBrain.KindTrap, netHolder, routed);
            }
            foreach (Thing t in Level.current.things[typeof(Gun)])
                AddCandidate(b, me, t, t is Grenade || t is Mine ? BotBrain.KindBomb : t is NetGun ? BotBrain.KindNet : BotBrain.KindGun,
                    netHolder, routed);
            foreach (Thing t in Level.current.things[typeof(Present)]) AddCandidate(b, me, t, BotBrain.KindPresent, netHolder, routed);
            foreach (Thing t in Level.current.things[typeof(ItemBox)]) AddCandidate(b, me, t, BotBrain.KindBox, netHolder, routed);

            int best = -1;
            for (int i = 0; i < _cands.Count; i++)
            {
                Candidate c = _cands[i];
                if (c.status != null) continue;
                if (best < 0 || BotBrain.BetterPickup(c.kind, c.len, _cands[best].kind, _cands[best].len)) best = i;
            }
            Thing chosen = best < 0 ? null : _cands[best].thing;
            if (chosen != b.pickup) LogCandidates(b, me, best, routed);
            b.pickup = chosen;
            b.pickupNode = best < 0 ? -1 : _cands[best].node;
        }

        // Только видимое: в сети ящик держит содержимое заранее, невидимым (ItemBox.UpdateContainedObject).
        private static void AddCandidate(Bot b, Duck me, Thing t, int kind, bool netHolder, bool routed)
        {
            Holdable h = t as Holdable;
            if (t.removeFromLevel || !t.visible || (h != null && (h.owner != null || !h.canPickUp))) return;
            Candidate c = new Candidate();
            c.thing = t;
            c.kind = kind;
            c.node = -1;
            Gun g = t as Gun;
            if (netHolder && kind > BotBrain.KindGun) c.status = "в руках сеткомёт";
            else if (g != null && g.ammo <= 0) c.status = "пустой";
            else if ((kind == BotBrain.KindTrap || kind == BotBrain.KindNet) && !_canThrow) c.status = "некуда бросить: нет пропасти";
            else if (g != null && !g.isFatal && kind != BotBrain.KindNet) c.status = "несмертельный";
            else if (g is SuicidePistol) c.status = "стреляет назад";
            else if (g != null && Unskilled(g)) c.status = "особое: бот пока не умеет";
            else if (kind == BotBrain.KindBox && ((ItemBox)t)._hit) c.status = "ящик пуст";
            // Фиолетовый выдаёт копию ствола только тому, кто его туда положил (Level.core._storedItems).
            else if (t is PurpleBlock) c.status = "фиолетовый: бот пока не умеет им пользоваться";
            else if (b.skip.Contains(t)) c.status = "забыт сторожем";
            else if (!routed)
            {
                float dx = t.x - me.x, dy = t.y - me.y;
                c.len = (float)Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                // Ящик бьют снизу: идти под него.
                c.node = kind == BotBrain.KindBox ? _nav.NodeBelow(t.x, t.y, 4) : _nav.NodeNear(t.x, t.y);
                if (c.node < 0) c.status = kind == BotBrain.KindBox ? "нет места под ящиком" : "вне карты";
                else if ((c.len = _nav.RouteLength(c.node)) >= float.MaxValue) c.status = "нет дороги";
            }
            _cands.Add(c);
        }

        private static void LogCandidates(Bot b, Duck me, int best, bool routed)
        {
            int usable = 0;
            for (int i = 0; i < _cands.Count; i++) if (_cands[i].status == null) usable++;
            Log(BotName(b) + ": цель=" + (best < 0 ? "нет" : Describe(_cands[best].thing)
                    + " путь=" + (int)_cands[best].len) + (routed ? "" : " по прямой: карты нет")
                + " кандидатов=" + _cands.Count + " годных=" + usable
                + " было=" + (b.pickup == null ? "нет" : b.pickup.GetType().Name));
            for (int i = 0; i < _cands.Count && DetailOn; i++)
            {
                Candidate c = _cands[i];
                float dx = c.thing.x - me.x, dy = c.thing.y - me.y;
                Detail(BotName(b) + ": кандидат " + Describe(c.thing) + " x=" + (int)c.thing.x
                    + " y=" + (int)c.thing.y + " по_прямой=" + (int)Math.Sqrt(dx * dx + dy * dy)
                    + (c.status != null ? " отброшен — " + c.status
                        : " путь=" + (int)c.len + (i == best ? " ВЫБРАН" : "")));
            }
        }

        private static readonly int[] _pathBuf = new int[512];

        // «своя_клетка=нет» — бот стоит на том, чего карта не видит: выступ, тонкий блок, ящик, утка.
        private static void LogPath(Bot b, Duck me, int start, int goal, string to)
        {
            if (!DetailOn) return;
            int n = _nav.PathTo(goal, _pathBuf);
            int same = 0;
            while (same < n && same < b.pathLen && _pathBuf[same] == b.path[same]) same++;
            if (same == n && n == b.pathLen) return;

            int col = _nav.Col(me.x), row = _nav.Row(me.bottom - 4f);
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
