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
        // Маршрут пересчитываем не каждый кадр: цель за десять кадров далеко не уедет.
        // Один поиск от узла бота (Дейкстра по всей карте) даёт и дорогу к врагу,
        // и длины до всех целей подбора, поэтому цель выбирается по длине маршрута.
        private static void Route(Bot b, Duck me, Duck enemy, bool wantPickup, bool carrying, bool netHolder)
        {
            // В полёте маршрут не пересчитывается: у утки в воздухе нет клетки, на
            // которой она стоит, маршрут не строился, и мозг откатывался на запасной
            // вариант — лететь напрямую к цели. Журнал поймал это 176 раз подряд: бот
            // прыгал на уступ вправо, в воздухе поворачивал к стволу влево и падал
            // под него. Начатый прыжок держит ту цель, что была при отрыве.
            //
            // Но счётчик до пересчёта в полёте обнуляется: первый же кадр после
            // приземления считает маршрут заново. Раньше счётчик шёл только по кадрам
            // на земле, а бот, прыгающий по ребру «прыжок», стоит на земле два-три
            // кадра за цикл — шаг устаревал на секунды, и бот прыгал на месте
            // (подпись «прыжок(0,N)» в строках сторожа 24.09).
            //
            // Исключение — полёт с пружины (см. kicked в Look): его цель решается уже в
            // воздухе, поэтому маршрут считается один раз из узла над пружиной.
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
                // Карты нет или бот вне её — цель по прямой, как раньше.
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
            b.atSpot = false;

            // Враг там, куда отсюда дороги нет, а сам спуститься может: на level_8 верх карты
            // снизу недостижим (подъём 64 пикселя, прыжок даёт 57). Раньше бот шёл к нему по
            // прямой — вставал под ним и прыгал в полку (23.10, скриншот человека). Теперь
            // засада: цель — место, где враг войдёт в нашу часть карты по пути к боту.
            bool wasAmbush = b.ambush;
            b.ambush = false;
            float aimX = enemy == null ? 0f : enemy.x, aimFeet = enemy == null ? 0f : enemy.bottom;
            int arrival = -1;
            if (enemyNode >= 0 && _nav.RouteLength(enemyNode) >= float.MaxValue)
                arrival = _nav.Arrival(start, enemyNode);
            if (arrival >= 0)
            {
                b.ambush = true;
                enemyNode = arrival;
                b.atSpot = arrival == start;
                aimX = b.ambushX = _nav.CellX(arrival);
                aimFeet = _nav.CellY(arrival) + NavCell / 2f;
                to = "засада: место спуска";
            }

            // Со стволом идти не к врагу, а на позицию для выстрела: та же высота,
            // дистанция, чистая линия. Раньше все шли в точку врага и сходились там;
            // вооружённые боты 157 раз из 181 гнались за врагом на другой высоте.
            float range = FireRange(me);
            if (enemyNode >= 0 && range > 0f && b.spotOff == 0)   // spotOff — см. Look
            {
                // Со взрывчаткой позиция не ближе BlastSafe: вплотную взрыв достаёт стрелка.
                Gun held = me.holdObject as Gun;
                float near = held != null && Explodes(held) ? BotBrain.BlastSafe : range < 100f ? 16f : 32f;
                int spot = _nav.FiringNode(aimX, aimFeet, near, range);
                if (spot >= 0)
                {
                    enemyNode = spot;
                    b.atSpot = spot == start;
                    to = b.ambush ? "засада: позиция для выстрела" : "позиция для выстрела";
                }
                else if (b.ambush)
                {
                    // Стрелять к месту спуска неоткуда — ждать, где стоим, лицом к нему: идти
                    // под полку, с которой он спрыгнет, — та самая прежняя ошибка.
                    enemyNode = start;
                    b.atSpot = true;
                    to = "засада: жду на месте";
                }
            }
            if (b.ambush && !wasAmbush)
                Log(BotName(b) + ": засада — до врага нет дороги, спустится в " + _nav.CellCol(arrival) + ","
                    + _nav.CellRow(arrival) + ", цель=" + to + " — " + Context(b, me));
            b.hasEnemyStep = _nav.FirstStep(start, enemyNode,
                out b.enemyStepX, out b.enemyStepY, out b.enemyStepEdge, out b.enemyStepProg);
            int goal = enemyNode;

            // Несём пойманного — путь к ближайшему краю над пропастью.
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

        // С какого расстояния бот бьёт тем, что в руках: ствол — издалека,
        // граната — вплотную, чтобы долетела. Ноль — идти к самому врагу: холодное
        // оружие бьёт в упор, а с пустыми руками стрелять нечем.
        private static float FireRange(Duck me)
        {
            Gun g = me.holdObject as Gun;
            if (g == null || !Usable(g) || IsMelee(g)) return 0f;
            if (g is Grenade || g is Mine) return BotBrain.ThrowRange - 6f;
            // Сетка летит дугой (hSpeed 10, vSpeed −1.5) и к 150 пикселям опускается
            // до уровня ствола — стрелять ближе.
            if (g is NetGun) return 112f;
            return GunReach(g) - 22f;
        }

        // Кандидат на подбор и почему он годится или отброшен.
        private struct Candidate
        {
            public Thing thing;
            public int kind;        // KindTrap … KindBox — чем меньше, тем лучше
            public int node;
            public string status;   // null — годен
        }

        // Враг в сетке лучше всего: поднять и бросить в пропасть — убийство. Дальше
        // ствол и холодное; сеткомёт — только если смертельного не достать; гранату
        // бот бросает лишь вплотную, а в матче 24.09 боты то и дело брали её вместо
        // ствола. В конце подарок и ящик — их ещё надо открыть.
        private const int KindTrap = BotBrain.KindTrap, KindGun = BotBrain.KindGun, KindNet = BotBrain.KindNet,
            KindBomb = BotBrain.KindBomb, KindPresent = BotBrain.KindPresent, KindBox = BotBrain.KindBox;

        private static readonly List<Candidate> _cands = new List<Candidate>();
        private static bool _canThrow;   // из узла бота достижим край над пропастью

        // Кандидаты — пойманные враги, свободные стволы, подарки и живые ящики, у
        // каждого статус. Сначала вид, внутри вида — кратчайший маршрут, а не
        // ближайший по прямой: ствол за стеной «рядом» только на карте. С сеткомётом
        // в руках годятся только пойманный враг и смертельный ствол. Смена цели —
        // строка в журнал со всеми кандидатами.
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

        // Невидимое в цели не годится. В сети ящик создаёт содержимое заранее и держит
        // его внутри себя невидимым и неактивным (ItemBox.UpdateContainedObject), чтобы
        // разослать клиентам. 20.10 боты шли к такому стволу — в точку ящика, на него, —
        // и ни разу не выбрали сам ящик: ствол важнее ящика, а под ящик и бьют.
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
            // Фиолетовый — не ящик с оружием. Он запоминает копию ствола, с которым его ударили,
            // за профилем утки (Level.core._storedItems) и выдаёт этой утке свежую копию —
            // раз за раунд с каждого блока. Тому, кто ничего не клал, он не даёт ничего, а
            // 20.10 боты выбирали его целью 7 раз за матч и били впустую. Класть и забирать
            // бот пока не умеет — это отдельный пункт «Очереди».
            else if (t is PurpleBlock) c.status = "фиолетовый: бот пока не умеет им пользоваться";
            else if (b.skip.Contains(t)) c.status = "забыт сторожем";
            else
            {
                // Ящик вскрывают снизу, поэтому идти надо под него, а не к нему.
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

        // Без карты — ближайшая цель по прямой, как было до выбора по маршруту.
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

        // Маршрут целиком — в подробный журнал, когда он изменился: узлы клетками
        // с видами рёбер, цена, с какого звена новый путь разошёлся со старым,
        // и стоит ли бот в той клетке, откуда карта считает маршрут. «своя_клетка=нет» —
        // бот стоит на том, чего карта не видит: выступ, тонкий блок, ящик, утка.
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
