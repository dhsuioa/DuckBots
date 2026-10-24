using System;
using System.Collections.Generic;
using System.Reflection;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        private static void DumpBots()
        {
            try
            {
                Detail("мод: уровень=" + Level.current.GetType().Name);
                for (int i = 0; i < bots.Count; i++)
                {
                    Bot b = bots[i];
                    Profile p = b.profile;
                    Duck d = p == null ? null : p.duck;
                    string line = "  бот " + (b.slot + 1) + " [" + LevelName(b)
                        + "] (" + (p == null ? "-" : p.name) + "): команда="
                        + (p == null || p.team == null ? "нет" : p.team.name)
                        + ", утка=" + (d == null ? "нет" : "есть");
                    if (d != null)
                        line += ", позиция=" + (int)d.x + "," + (int)d.y
                              + ", камера_y=" + (int)d.cameraPosition.y
                              + ", мёртв=" + d.dead
                              + ", обездвижен=" + d.immobilized
                              + ", в_руках=" + (d.holdObject == null ? "нет" : d.holdObject.GetType().Name);
                    if (b.box != null)
                        line += ", готов=" + b.box.ready + ", активна=" + b.box.playerActive;
                    Detail("мод:" + line);
                }
            }
            catch (Exception e)
            {
                Log("мод: DumpBots упал: " + e.Message);
            }
        }

        // Журнал движений: строка на каждое движение утки (прыжок, сход с края, провал сквозь
        // платформу, пружина, телепорт, подкат), сверенное с картой. «В графе НЕТ» у движения
        // человека — место, где бот видит карту неверно. У человека пишется всё, у ботов — промахи
        // и смерти; в конце раунда — сводка.
        private static readonly Dictionary<Duck, MoveTracer> _tracers = new Dictionary<Duck, MoveTracer>();
        private static Level _traceLevel;
        private static int _roundStart;
        private static int _humanMoves, _humanNoPath, _humanDetour, _botJumps, _botMisses;
        private static int _watchdogFired, _botDeaths, _botFalls, _botDrops;
        private static bool _boomLast;

        private static void TraceAll()
        {
            if (!(Level.current is GameLevel)) return;

            // Начало взрыва — кадр, когда ExplosionPart появились после кадра без них.
            bool boom = false;
            foreach (Thing t in Level.current.things[typeof(ExplosionPart)])
            {
                if (!_boomLast) Log("мир: взрыв x=" + (int)t.x + " y=" + (int)t.y);
                boom = true;
                break;
            }
            _boomLast = boom;

            foreach (Thing t in Level.current.things[typeof(Duck)])
            {
                Duck d = t as Duck;
                if (d == null || d.removeFromLevel) continue;
                MoveTracer tr;
                if (!_tracers.TryGetValue(d, out tr))
                {
                    tr = new MoveTracer();
                    tr.duck = d;
                    tr.bot = Find(d);
                    tr.who = tr.bot != null ? BotName(tr.bot) : "игрок";
                    _tracers[d] = tr;
                }
                try
                {
                    Trace(tr);
                    // Событие из мозга сбрасывается здесь, когда кадр уже лёг в самописец.
                    if (tr.dumpWhy != null)
                    {
                        Dump(tr, tr.dumpWhy, MoveTracer.RecLen);
                        tr.dumpWhy = null;
                    }
                }
                catch (Exception e) { Log("мод: журнал упал на " + tr.who + ": " + e.Message); }
            }
        }

        private static void Trace(MoveTracer tr)
        {
            Duck d = tr.duck;
            if (d.dead)
            {
                if (!tr.dead) { tr.dead = true; LogDeath(tr); tr.air = false; }   // строка смерти пишет «полёт»
                return;
            }

            // В рэгдолле объект утки унесён на 5000 px вверх: ни полёт, ни телепорт по нему не считаются.
            bool doll = d.ragdoll != null;
            if (doll != tr.ragdoll)
            {
                tr.ragdoll = doll;
                Log(tr.who + ": " + (doll ? "рэгдолл x=" + (int)d.ragdoll.x + " y=" + (int)d.ragdoll.y
                                         : "встал x=" + (int)d.x + " y=" + (int)d.y));
                tr.air = false;
                tr.seen = false;
            }
            Record(tr, d);
            if (doll) return;

            InputProfile ip = d.inputProfile;
            bool jumpDown = ip != null && ip.Down("JUMP");
            bool downDown = ip != null && ip.Down("DOWN");

            // Телепорт: за кадр далеко, а скорость этого не объясняет.
            if (tr.seen)
            {
                float jump = Math.Abs(d.x - tr.lastX) + Math.Abs(d.y - tr.lastY);
                float speed = Math.Abs(d.hSpeed) + Math.Abs(d.vSpeed);
                if (jump > 48f && jump > speed * 3f)
                {
                    Log(tr.who + ": телепорт из " + (int)tr.lastX + "," + (int)tr.lastY
                        + " в " + (int)d.x + "," + (int)d.y + " — "
                        + GraphWord(tr.lastX, tr.lastBottom - 4f, d.x, d.bottom - 4f));
                    tr.air = false;
                }
            }

            TraceHands(tr, d);
            TraceSlide(tr, d);

            if (!tr.air)
            {
                // Только с земли: иначе падение на спавн записалось бы сходом с края.
                if (tr.lastGrounded && !d.grounded) StartAir(tr, d, jumpDown || tr.lastJumpDown, downDown);
            }
            else
            {
                tr.airFrames++;
                if (d.bottom < tr.apexY) tr.apexY = d.bottom;
                if (jumpDown && d.vSpeed < 0f) tr.jumpHeld++;
                if (!tr.throughPlatform && d.vSpeed < 0f && PlatformAt(d.position)) tr.throughPlatform = true;
                if (d.grounded) EndAir(tr, d);
                else if (tr.airFrames > 400) tr.air = false;   // лиана или вода — не полёт
            }

            tr.seen = true;
            tr.lastX = d.x;
            tr.lastY = d.y;
            tr.lastBottom = d.bottom;
            tr.lastJumpDown = jumpDown;
            tr.lastGrounded = d.grounded;
        }

        private static bool PlatformAt(Vec2 p)
        {
            return Level.CheckPoint<AutoPlatform>(p) != null || Level.CheckPoint<Platform>(p) != null;
        }

        private static void StartAir(MoveTracer tr, Duck d, bool jumpPressed, bool downHeld)
        {
            tr.air = true;
            tr.airFrames = 0;
            tr.jumpHeld = jumpPressed ? 1 : 0;
            // Отрыв замечен уже в полёте: точка отрыва — прошлый кадр.
            tr.fromX = tr.lastX;
            tr.fromY = tr.lastBottom;
            tr.apexY = Math.Min(tr.lastBottom, d.bottom);
            tr.startVSpeed = d.vSpeed;
            tr.jumpPressed = jumpPressed;
            tr.downHeld = downHeld;
            tr.throughPlatform = false;

            tr.routed = false;
            tr.startHow = null;
            Bot b = tr.bot;
            if (b != null)
            {
                // Кадр отрыва в самописце предпоследний: в последнем бот уже в полёте и решает заново.
                // Маршрутным считается только прыжок, начатый ребром маршрута.
                string how = tr.recCount >= 2
                    ? tr.rec[(tr.recHead - 2 + MoveTracer.RecLen) % MoveTracer.RecLen].how : b.state.how;
                bool jumping = b.state.jumpHold > 0 || b.state.progAt > 0;
                tr.startHow = jumping ? b.state.jumpBy : how;
                if (jumping && b.state.jumpBy == BotBrain.HowRouteJump)
                {
                    tr.routed = true;
                    tr.routeToX = tr.lastX + b.state.jumpDX;
                    tr.routeToY = tr.lastY + b.state.jumpDY + NavCell / 2f;   // узел — центр клетки, утка стоит на её низе
                    tr.routeEdge = NavMap.Jump;
                    tr.routeProg = b.state.progAt > 0 ? b.state.prog : 0;
                }
            }
        }

        private static string AirKind(MoveTracer tr)
        {
            if (tr.startVSpeed < -8f) return "пружина";   // прыжок даёт −4.9, пружина −22
            if (tr.jumpPressed && tr.startVSpeed < 0f) return "прыжок";
            if (tr.downHeld) return "провал сквозь платформу";
            if (tr.startVSpeed >= -0.5f) return "сход с края";
            return "подброс";
        }

        private static void EndAir(MoveTracer tr, Duck d)
        {
            tr.air = false;
            float up = tr.fromY - tr.apexY;
            float rise = tr.fromY - d.bottom;
            float side = d.x - tr.fromX;
            string kind = AirKind(tr);

            if (tr.bot != null)
            {
                if (kind == "прыжок") _botJumps++;
                if (kind == "пружина")
                    Log(tr.who + ": пружина из " + (int)tr.fromX + "," + (int)tr.fromY
                        + " в " + (int)d.x + "," + (int)d.bottom);
                LogBotMiss(tr, d, up);
                // Падение — не свой прыжок, а сход или подброс, и заметно ниже; по маршруту это спуск.
                if ((kind == "сход с края" || kind == "подброс") && -rise > 48f)
                {
                    bool meant = tr.startHow == BotBrain.HowRoute || tr.startHow == BotBrain.HowRouteDrop
                        || tr.startHow == BotBrain.HowDropGuess;
                    Log(tr.who + ": " + (meant ? "спуск" : "падение") + " вниз=" + (int)-rise
                        + " вбок=" + (int)side + " из " + (int)tr.fromX + "," + (int)tr.fromY
                        + " как=" + (tr.startHow ?? "-") + (meant ? "" : " запись=f" + frame));
                    if (!meant)
                    {
                        _botDrops++;
                        Dump(tr, "падение", tr.airFrames + 30);
                    }
                }
                return;
            }

            if (tr.airFrames < 4 && Math.Abs(rise) < 8f && Math.Abs(side) < 8f) return;

            int edge = NavMap.Walk;
            int known = _nav == null ? NavMap.OffMap
                : _nav.MoveKnown(tr.fromX, tr.fromY - 4f, d.x, d.bottom - 4f, out edge);
            _humanMoves++;
            if (known == NavMap.NoPath) _humanNoPath++;
            if (known == NavMap.Detour) _humanDetour++;

            string line = tr.who + ": " + kind;
            if (kind == "прыжок") line += ", держал " + tr.jumpHeld + " кадр.";
            line += ", вверх " + (int)up + ", итог " + (rise >= 0f ? "+" : "") + (int)rise
                  + ", вбок " + (int)side;
            if (tr.throughPlatform) line += ", сквозь платформу";
            line += ", из " + (int)tr.fromX + "," + (int)tr.fromY + " — " + KnownWord(known, edge);
            Log(line);
        }

        // Промах: прыжок ребра маршрута кончился не в том узле.
        private static void LogBotMiss(MoveTracer tr, Duck d, float up)
        {
            if (!tr.routed) return;
            float missX = d.x - tr.routeToX;
            float missY = d.bottom - tr.routeToY;
            if (Math.Abs(missX) <= 12f && Math.Abs(missY) <= 8f) return;
            _botMisses++;
            Log(tr.who + ": промах ребро=" + EdgeName(tr.routeEdge)
                + (tr.routeProg != 0 ? " прог={" + NavMap.ProgWord(tr.routeProg) + "}" : " прог=нет")
                + " надо_вверх=" + (int)(tr.fromY - tr.routeToY) + " поднялся=" + (int)up
                + " цель_x=" + (int)(tr.routeToX - tr.fromX) + " сел_x=" + (int)(d.x - tr.fromX)
                + " мимо_x=" + (int)missX + " ниже_на=" + (int)missY
                + " из " + (int)tr.fromX + "," + (int)tr.fromY
                + " кадров=" + tr.airFrames + " запись=f" + frame);
            Dump(tr, "промах", tr.airFrames + 30);
        }

        private static string EdgeName(int edge)
        {
            if (edge == NavMap.Jump) return "прыжок";
            if (edge == NavMap.Launch) return "пружина";
            if (edge == NavMap.Drop) return "вниз";
            if (edge == NavMap.Smash) return "стекло";
            return "шаг";
        }

        private static string GraphWord(float fromX, float fromY, float toX, float toY)
        {
            if (_nav == null) return "карты нет";
            int edge;
            int known = _nav.MoveKnown(fromX, fromY, toX, toY, out edge);
            return KnownWord(known, edge);
        }

        private static string KnownWord(int known, int edge)
        {
            if (_nav == null) return "карты нет";
            if (known == NavMap.KnownEdge) return "в графе есть (" + EdgeName(edge) + ")";
            if (known == NavMap.Detour) return "в графе только обходом";
            if (known == NavMap.NoPath) return "в графе НЕТ";
            return "вне графа";
        }

        private static void TraceHands(MoveTracer tr, Duck d)
        {
            Holdable now = d.holdObject;
            if (now == tr.held) return;
            if (tr.held != null) Log(tr.who + ": бросил " + tr.held.GetType().Name);
            if (now != null)
            {
                Gun g = now as Gun;
                Log(tr.who + ": взял " + now.GetType().Name + (g != null ? " (" + g.ammo + ")" : "")
                    + " в " + (int)d.x + "," + (int)d.y
                    + (g != null ? " бьёт_на=" + (int)GunReach(g) : ""));
            }
            tr.held = now;
        }

        private static void TraceSlide(MoveTracer tr, Duck d)
        {
            if (d.sliding)
            {
                if (!tr.sliding)
                {
                    tr.sliding = true;
                    tr.slideFrames = 0;
                    tr.slideSpeed = 0f;
                    tr.slideWindow = Level.CheckCircle<Window>(d.position, 64f);
                }
                tr.slideFrames++;
                if (Math.Abs(d.hSpeed) > tr.slideSpeed) tr.slideSpeed = Math.Abs(d.hSpeed);
                return;
            }
            if (!tr.sliding) return;
            tr.sliding = false;
            if (tr.slideFrames < 5) return;
            bool broke = tr.slideWindow != null
                && (tr.slideWindow.destroyed || tr.slideWindow.removeFromLevel);
            Log(tr.who + ": подкат " + tr.slideFrames + " кадр., скорость до "
                + tr.slideSpeed.ToString("0.0") + (broke ? ", стекло разбито" : "")
                + " в " + (int)d.x + "," + (int)d.y);
        }

        // Тип смерти — из Duck.Kill, место — из самописца: объект мёртвой утки игра уже унесла
        // на 5000 px вверх, а упавшую перед этим ещё и на 500 вниз.
        private static void LogDeath(MoveTracer tr)
        {
            Duck d = tr.duck;
            string by = null;
            string cause;
            try { cause = Cause(tr.killType, d, out by); }
            catch (Exception e) { cause = "не прочиталась(" + e.Message + ")"; }
            if (by == null && d.killedByProfile != null)
                by = d.killedByProfile == d.profile ? "сам" : WhoIs(d.killedByProfile);

            string where = "";
            if (tr.recCount > 0)
            {
                RecFrame f = tr.rec[(tr.recHead + MoveTracer.RecLen - 1) % MoveTracer.RecLen];
                where = " x=" + (int)f.x + " y=" + (int)f.y + " hs=" + F2(f.hs) + " vs=" + F2(f.vs)
                    + " земля=" + (f.grounded ? "да" : "нет")
                    + (f.mode >= 0 ? " режим=" + BotBrain.modeNames[f.mode] + " шаг=" + StepWord(f)
                        + " — " + (f.why ?? "-") + (f.how != null ? "; " + f.how : "") : "");
            }
            Log(tr.who + ": смерть причина=" + cause + (by != null ? " кто=" + by : "")
                + " дно=" + (int)Level.current.lowestPoint + " низ_карты=" + (int)Level.current.bottomRight.y
                + " полёт=" + (tr.air ? "да" : "нет") + " запись=f" + frame + where);
            Dump(tr, "смерть", MoveTracer.RecLen);
            if (tr.bot != null)
            {
                _botDeaths++;
                if (tr.killType is DTFall) _botFalls++;
            }
        }

        private static string Cause(DestroyType t, Duck d, out string by)
        {
            by = null;
            if (t == null) return "неизвестно";
            Profile p = t.responsibleProfile;
            if (p != null) by = p == d.profile ? "сам" : WhoIs(p);
            if (t is DTFall) return "падение";
            DTShot shot = t as DTShot;
            if (shot != null)
            {
                Thing from = shot.bulletFiredFrom;
                // У осколков владельца нет: их выпускает сама граната, стрелок — дальше по цепочке.
                Thing src = Origin(from);
                if (by == null) by = OwnerOf(src, d);
                string gun = src == null ? "?" : src.GetType().Name;
                if (from is Grenade || from is Mine || from is Bullet) return "взрыв:" + gun;
                Bullet bullet = shot.bullet;
                return "пуля:" + gun + "/"
                    + (bullet != null && bullet.ammo != null ? bullet.ammo.GetType().Name : "?");
            }
            string what = t.thing == null ? "" : ":" + t.thing.GetType().Name;
            if (t is DTImpale) return "насадился" + what;
            if (t is DTIncinerate)
            {
                // Владельца нет ни у огня, ни у вспышки.
                Thing src = Origin(t.thing);
                if (by == null) by = OwnerOf(src, d);
                return "огонь" + what + (src != null && src != t.thing ? "/" + src.GetType().Name : "");
            }
            if (t is DTCrush) return "раздавлен" + what;
            if (t is DTImpact) return "удар" + what;
            if (t is DTRocketExplosion) return "ракета" + what;
            if (t is DTElectrocute) return "ток" + what;
            return t.GetType().Name + what;
        }

        private static FieldInfo _flareOwner;

        // Чем выпущено: осколок — гранатой, граната — гранатомётом, огонь — огнемётом или вспышкой,
        // вспышка — ракетницей (Flare._owner).
        private static Thing Origin(Thing t)
        {
            for (int i = 0; i < 8 && t != null; i++)
            {
                Thing next = null;
                if (t is Bullet) next = ((Bullet)t).firedFrom;
                else if (t is SmallFire) next = ((SmallFire)t).firedFrom;
                else if (t is Flare)
                {
                    _flareOwner = _flareOwner ?? Priv(typeof(Flare), "_owner");
                    next = _flareOwner.GetValue(t) as Thing;
                }
                if (next == null) return t;
                t = next;
            }
            return t;
        }

        private static string OwnerOf(Thing src, Duck d)
        {
            Profile p = src == null ? null : src.responsibleProfile;
            return p == null ? null : p == d.profile ? "сам" : WhoIs(p);
        }

        // Kill зовут и для неуязвимой утки: запоминаем только убийство, после которого она мертва.
        public static void OnKilled(Duck d, DestroyType type)
        {
            MoveTracer tr;
            if (d != null && d.dead && _tracers.TryGetValue(d, out tr) && tr.killType == null)
                tr.killType = type;
        }

        private static string WhoIs(Profile p)
        {
            for (int i = 0; i < bots.Count; i++)
                if (bots[i].profile == p) return "бот " + (bots[i].slot + 1);
            return "игрок";
        }

        // displayName в первом кадре раунда бросает NullReferenceException: данные уровня ещё не загружены.
        private static string MapName(Level lvl)
        {
            GameLevel gl = lvl as GameLevel;
            if (gl == null) return "?";
            try
            {
                string name = "" + gl.displayName;
                return name.Length > 0 ? "«" + name + "»" : "?";
            }
            catch (Exception) { return "?"; }
        }

        private static void RoundSummary()
        {
            if (!(_traceLevel is GameLevel)) return;
            Log("мир: раунд р=" + round + " карта=" + MapName(_traceLevel)
                + " длился=" + (frame - _roundStart) / 60 + "с"
                + " игрок: движений=" + _humanMoves + " граф_не_знает=" + _humanNoPath
                + " обходом=" + _humanDetour
                + "; боты: прыжков=" + _botJumps + " промахов=" + _botMisses
                + " сторож=" + _watchdogFired + " смертей=" + _botDeaths
                + " в_пропасть=" + _botFalls + " падений=" + _botDrops);
            _humanMoves = _humanNoPath = _humanDetour = _botJumps = _botMisses = 0;
            _watchdogFired = _botDeaths = _botFalls = _botDrops = 0;
        }

        private static string RouteWord(BotView v)
        {
            bool routed = v.hasPickupStep || v.hasEnemyStep;
            if (!routed) return "нет";
            bool pick = v.hasPickupStep;
            int edge = pick ? v.pickupStepEdge : v.enemyStepEdge;
            return EdgeName(edge) + "(" + (int)(pick ? v.pickupStepX : v.enemyStepX)
                + "," + (int)(pick ? v.pickupStepY : v.enemyStepY) + ")";
        }

        private static void LogBrain()
        {
            for (int i = 0; i < bots.Count; i++)
            {
                Bot b = bots[i];
                Duck d = b.profile == null ? null : b.profile.duck;
                if (d == null || d.dead) continue;
                BotView v = b.view;
                Detail(BotName(b) + ": состояние режим=" + BotBrain.modeNames[b.state.mode] + " "
                    + Context(b, d) + " x=" + (int)d.x + " y=" + (int)d.bottom
                    + (v.gapLeft ? (v.abyssLeft ? " пропасть<" : " обрыв<") : "")
                    + (v.gapRight ? (v.abyssRight ? " пропасть>" : " обрыв>") : "")
                    + (v.platformBelow ? " платформа" : "")
                    + " — " + Reason(b.state));
            }
        }

        private static string Context(Bot b, Duck d)
        {
            BotView v = b.view;
            string s = "враг=" + (v.hasEnemy ? (int)v.ex + "," + (int)v.ey : "нет")
                + " линия=" + (v.glassLine ? "сквозь_стекло" : v.canShoot ? "да" : v.canShootUp ? "вверх" : "нет")
                + " в_руках=" + (d.holdObject == null ? "нет" : Describe(d.holdObject));
            if (v.hasPickup && b.pickup != null)
                s += " цель=" + Describe(b.pickup) + "(" + (int)v.px + "," + (int)v.py + ")";
            if (v.danger)
                s += " опасность=" + (b.danger != null ? b.danger.GetType().Name : "?") + "(" + (int)v.dx + ")";
            return s + " шаг=" + RouteWord(v);
        }

        private static string Reason(BotState s)
        {
            return (s.why ?? "-") + (s.how != null ? "; " + s.how : "");
        }

        private static string Describe(Thing t)
        {
            TrappedDuck trap = t as TrappedDuck;
            if (trap != null && trap.captureDuck != null) return "пойманный(" + WhoIs(trap.captureDuck.profile) + ")";
            Gun g = t as Gun;
            return t.GetType().Name + (g != null ? "(" + g.ammo + ")" : "");
        }

        private static string BotName(Bot b) { return "бот " + (b.slot + 1); }
    }
}
