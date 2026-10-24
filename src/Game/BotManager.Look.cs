using System;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        // Один раз: без нового делегата каждый кадр.
        private static readonly Predicate<Thing> _freeGun = IsFreeGun;
        private static readonly Predicate<Thing> _liveBox = IsLiveBox;
        private static readonly Predicate<Thing> _armedExplosive = IsArmedExplosive;
        private static readonly Predicate<Thing> _freePresent = IsFreePresent;

        private const float FootProbe = 14f;    // шаг вперёд от центра утки
        private const float GlassSee = 64f;
        private const float ThreatRange = 200f; // дальше прицел врага не повод ложиться
        private const float FootDepth = 34f;    // глубже — уже яма, а не ступенька
        // Ручка силы для этапа 3: сколько ждать линии на позиции и на сколько её потом забыть.
        private const int SpotPatience = 120, SpotOffFrames = 180;
        private const float KickVs = -5.5f;     // своим прыжком (−4.9) быстрее вверх не взлететь

        private static bool IsFreeGun(Thing t)
        {
            Gun g = t as Gun;
            return g != null && Usable(g) && g.owner == null && !g.removeFromLevel && g.visible && !Skipped(t);
        }

        // Предикат создан один раз и о боте не знает: цели бота выставляются перед поиском.
        private static SkipList _skipFor;
        private const int SkipFrames = 180;

        private static bool Skipped(Thing t) { return _skipFor != null && _skipFor.Contains(t); }

        // isFatal игра ставит сама: у музыкальных и прочих несмертельных он false. SuicidePistol
        // стреляет назад. Сеткомёт — оружие, только если на карте есть пропасть.
        private static bool Usable(Gun g)
        {
            if (g.ammo <= 0 || g is SuicidePistol || Unskilled(g)) return false;
            if (g is NetGun) return _nav != null && _nav.hasAbyssEdge;
            return g.isFatal;
        }

        // Своей логики для них пока нет: луч захвата обездвиживает стрелка, пультом водят машинку,
        // магнит тянет металл.
        private static bool Unskilled(Gun g)
        {
            return g is MindControlRay || g is RCController || g is MagnetGun;
        }

        // Общего предка у холодного оружия нет.
        private static bool IsMelee(Gun g)
        {
            return g is Sword || g is EnergyScimitar || g is OldEnergyScimi || g is TapedSword
                || g is Chainsaw || g is SledgeHammer || g is BoxingGloves;
        }

        // Взрыв, огонь или луч: стекло вблизи таким не бьют.
        private static bool Blasts(Gun g)
        {
            return Explodes(g) || g is FlameThrower || g is LaserRifle;
        }

        // Рвётся там, куда попал (римская свеча — тоже FlareGun).
        public static bool Explodes(Gun g)
        {
            return g is Bazooka || g is GrenadeLauncher || g is GrenadeCannon || g is FlareGun;
        }

        public static float GunReach(Gun g)
        {
            if (g == null || g.ammoType == null || IsMelee(g) || g is NetGun
                || g is Grenade || g is Mine || g is GoodBook) return BotBrain.RangeX;
            if (g is FlameThrower) return BotBrain.FlameReach;
            if (g is FlareGun) return BotBrain.FlareReach;
            AmmoType a = g.ammoType;
            return BotBrain.FireReach(a.range, a.rangeVariation, a.accuracy, a.affectedByGravity);
        }

        private static bool GlassGun(Gun g)
        {
            return g != null && Usable(g) && !IsMelee(g) && !(g is Grenade) && !(g is Mine)
                && !(g is NetGun) && !Blasts(g);
        }

        // Целое стекло без решётки пуле не стена: толщина окна 0.3, пуля его бьёт и летит дальше.
        private static bool Wall(Vec2 a, Vec2 b, bool glassGun)
        {
            if (!glassGun) return Level.CheckLine<Block>(a, b) != null;
            foreach (Block k in Level.CheckLineAll<Block>(a, b))
            {
                Window w = k as Window;
                if (w == null || w.bars.value) return true;
            }
            return false;
        }

        private static bool IsFreePresent(Thing t)
        {
            Present p = t as Present;
            return p != null && p.owner == null && !p.removeFromLevel && p.visible && !Skipped(t);
        }

        private static bool IsLiveBox(Thing t)
        {
            ItemBox b = t as ItemBox;
            return b != null && !b._hit && !b.removeFromLevel && !Skipped(t);
        }

        // Бежать только от гранаты с выдернутой чекой. Мина — нажимная ловушка: её клетку обходит
        // маршрут, а _clicked у мины в руках не сбрасывается.
        private static bool IsArmedExplosive(Thing t)
        {
            Grenade g = t as Grenade;
            return g != null && !g._pin && !g.removeFromLevel;
        }

        private static bool Footing(float x, float yFrom)
        {
            Vec2 a = new Vec2(x, yFrom);
            Vec2 b = new Vec2(x, yFrom + FootDepth);
            if (Level.CheckLine<Spikes>(a, b) != null) return false;   // шипы — не опора
            if (Level.CheckLine<Block>(a, b) != null) return true;
            if (Level.CheckLine<AutoPlatform>(a, b) != null) return true;
            return Level.CheckLine<Platform>(a, b) != null;
        }

        // Здесь обстановка превращается в числа; дальше Decide работает без игровых типов.
        private static BotView Look(Bot b, Duck me)
        {
            BotView v = new BotView();
            EnsureNav();
            v.facingRight = me.offDir > 0;
            v.grounded = me.grounded;
            v.speedAbs = Math.Abs(me.hSpeed);
            v.hs = me.hSpeed;
            v.netted = me.inNet;
            v.ragdoll = me.ragdoll != null;
            // Прыжок по программе — из центра узла, откуда считан маршрут: тормозя, бот проскакивает в соседнюю клетку.
            if (!float.IsNaN(b.routeStartX)) v.alignDx = b.routeStartX - me.x;
            else if (_nav != null)
                v.alignDx = (float)Math.Floor((me.x - _nav.originX) / NavCell) * NavCell
                    + _nav.originX + NavCell / 2f - me.x;

            // Обрывы с обеих сторон: направление выбирает Decide. Без карты любой обрыв — пропасть.
            if (me.grounded)
            {
                float foot = me.bottom - 2f;
                v.gapLeft = !Footing(me.x - FootProbe, foot);
                v.gapRight = !Footing(me.x + FootProbe, foot);
                v.abyssLeft = v.gapLeft
                    && (_nav == null || !_nav.SafeBelow(me.x - FootProbe, me.bottom + 2f));
                v.abyssRight = v.gapRight
                    && (_nav == null || !_nav.SafeBelow(me.x + FootProbe, me.bottom + 2f));
                Vec2 under = new Vec2(me.x, me.bottom + 3f);
                v.platformBelow = Level.CheckPoint<Block>(under) == null
                    && (Level.CheckPoint<AutoPlatform>(under) != null
                        || Level.CheckPoint<Platform>(under) != null);
                v.abyssBelow = v.platformBelow
                    && (_nav == null || !_nav.SafeBelow(me.x, me.bottom + 3f + NavCell));
            }

            Window near = null;
            foreach (Thing t in Level.current.things[typeof(Window)])
            {
                Window w = t as Window;
                if (w == null || w.floor || w.bars.value || w.destroyed || w.removeFromLevel) continue;
                if (w.bottom <= me.top || w.top >= me.bottom) continue;
                float wdx = w.x - me.x;
                if (Math.Abs(wdx) > GlassSee || (near != null && Math.Abs(wdx) >= Math.Abs(v.glassDx))) continue;
                near = w;
                v.glassDx = wdx;
            }
            v.hasGlass = near != null;
            if (b.glass != null && b.glass != near && (b.glass.destroyed || b.glass.removeFromLevel))
                Log(BotName(b) + ": стекло разбито в " + (int)b.glass.x + "," + (int)b.glass.y
                    + " — " + (b.state.how ?? "-"));
            string how = b.state.how;
            b.glass = how == BotBrain.HowSmashShoot || how == BotBrain.HowSmashSlide || how == BotBrain.HowSmashBack
                ? near : null;

            v.sliding = me.sliding;
            Duck shooter = Aiming(me);
            if (shooter != null)
            {
                v.threatened = true;
                v.threatDx = shooter.x - me.x;
            }

            // С сеткомётом и с пойманным на руках враг в сетке — не цель.
            bool netOrCarry = me.holdObject is NetGun || me.holdObject is TrappedDuck;
            Duck enemy = NearestEnemy(me, netOrCarry);
            if (enemy != null)
            {
                v.hasEnemy = true;
                v.ex = enemy.x - me.x;
                v.ey = enemy.y - me.y;
                // Луч — в сторону врага, а не по offDir: стоящий спиной иначе не узнает, что хватит развернуться.
                bool through = GlassGun(me.holdObject as Gun);
                bool wall = Wall(me.position, enemy.position, through);
                v.canShootUp = v.ey < -BotBrain.SameLevelY && v.ey > -BotBrain.HoverReach
                    && Math.Abs(v.ex) < BotBrain.HoverAimX && !wall;
                v.canShoot = Math.Abs(v.ey) < BotBrain.SameLevelY
                    && Math.Abs(v.ex) < GunReach(me.holdObject as Gun) && !wall
                    && Level.CheckRay<Duck>(me.position + new Vec2(v.ex > 0f ? 10f : -10f, 0f),
                                            enemy.position) == enemy;
                v.glassLine = v.canShoot && through && Level.CheckLine<Block>(me.position, enemy.position) != null;
            }

            Gun held = me.holdObject as Gun;
            if (held != null)
            {
                Grenade gr = held as Grenade;
                Mine mn = held as Mine;
                v.explosiveInHand = gr != null || mn != null;
                v.armedInHand = (gr != null && !gr._pin) || (mn != null && mn._armed);
                v.holding = Usable(held) ? 1 : 2;
                v.meleeInHand = IsMelee(held);
                v.netGun = held is NetGun && v.holding == 1;
                v.blastInHand = v.holding == 1 && Explodes(held);
                v.holdFire = v.holding == 1 && held is FlameThrower;
                v.glassGun = GlassGun(held);

                // Гранатомёту нужна не линия на одной высоте, а дуга без стены.
                GrenadeLauncher gl = held as GrenadeLauncher;
                if (gl != null && v.holding == 1)
                {
                    v.lobber = true;
                    v.lobAim = gl._fireAngle;
                    v.lobAiming = gl._aiming;
                    v.lobCooling = gl._cooldown > 0f;
                    v.lobAngle = -1;
                    v.canShootUp = false;
                    v.canShoot = false;
                    if (enemy != null)
                    {
                        Vec2 pivot = gl.position;
                        v.lobAngle = BotBrain.LobAngle(enemy.x - pivot.x, enemy.y - pivot.y, BotBrain.LobBarrel,
                            (x0, y0, x1, y1) => Level.CheckLine<Block>(new Vec2(pivot.x + x0, pivot.y + y0),
                                                                       new Vec2(pivot.x + x1, pivot.y + y1)) != null);
                        v.canShoot = v.lobAngle >= 0;
                    }
                }
            }
            else if (me.holdObject != null)
            {
                v.holding = 3;
                v.presentInHand = me.holdObject is Present;
                TrappedDuck trap = me.holdObject as TrappedDuck;
                v.carrying = trap != null && trap.captureDuck != null && trap.captureDuck != me;
            }

            // Цель держится между пересчётами маршрута, но каждый кадр проверяется: ствол могли
            // подобрать, ящик — вскрыть. С сеткомётом цель тоже ищем: пойманного и смертельный ствол.
            b.skip.Tick();
            bool wantPickup = !v.carrying && (v.holding != 1 || v.netGun);
            if (b.pickup != null && (!wantPickup || !StillFree(b.pickup)))
            {
                if (wantPickup) b.routeWait = 0;
                b.pickup = null;
                b.pickupNode = -1;
                b.hasPickupStep = false;
            }

            // Своя взведённая в руках — не опасность. Порог с запасом: на границе радиуса режим
            // менялся бы каждый кадр.
            Thing bomb = Level.current.NearestThingFilter<Gun>(
                me.position, _armedExplosive, b.danger != null ? BotBrain.DangerR + 20f : BotBrain.DangerR);
            b.danger = null;
            if (bomb != null && bomb != me.holdObject)
            {
                v.danger = true;
                v.dx = bomb.x - me.x;
                b.danger = bomb;
            }

            // Закрытая дверь для карты — проход, но линию огня заслоняет: простоял на позиции без
            // линии дольше SpotPatience — забыть позицию и идти к врагу, толчок откроет дверь.
            if (b.atSpot && !v.canShoot && !v.canShootUp) b.spotWait++;
            else b.spotWait = 0;
            if (b.spotWait > SpotPatience)
            {
                b.spotWait = 0;
                b.spotOff = SpotOffFrames;
                b.atSpot = false;
                b.routeWait = 0;
                Log(BotName(b) + ": на позиции " + SpotPatience / 60 + "с нет линии, иду к врагу — " + Context(b, me));
            }
            if (b.spotOff > 0) b.spotOff--;

            // Толчок пружины: в воздухе скорость вверх резко выросла.
            bool kicked = !me.grounded && me.vSpeed < KickVs && b.prevVs - me.vSpeed > 3f;
            b.prevVs = me.vSpeed;
            v.kicked = kicked;
            if (kicked && _nav != null)
            {
                b.flightStart = _nav.NodeNear(me.x, me.bottom - 4f);
                b.routeWait = 0;
            }
            else if (me.grounded) b.flightStart = -1;

            Route(b, me, enemy, wantPickup, v.carrying, v.netGun);
            Thing pickup = b.pickup;
            if (pickup != null)
            {
                v.hasPickup = true;
                v.pickupIsBox = pickup is ItemBox;
                v.pickupIsTrap = pickup is TrappedDuck;
                v.px = pickup.x - me.x;
                v.py = pickup.y - me.y;
            }
            v.hasThrowStep = v.carrying && b.hasThrowStep;
            v.throwStepX = b.throwStepX - me.x;
            v.throwStepY = b.throwStepY - me.y;
            v.throwStepEdge = b.throwStepEdge;
            v.throwStepProg = b.throwStepProg;
            v.atThrowSpot = v.carrying && b.atThrowSpot;
            v.throwDir = b.throwDir;
            v.hasEnemyStep = b.hasEnemyStep;
            v.enemyStepX = b.enemyStepX - me.x;
            v.enemyStepY = b.enemyStepY - me.y;
            v.enemyStepEdge = b.enemyStepEdge;
            v.enemyStepProg = b.enemyStepProg;
            v.hasPickupStep = b.hasPickupStep && v.hasPickup;
            v.pickupStepX = b.pickupStepX - me.x;
            v.pickupStepY = b.pickupStepY - me.y;
            v.pickupStepEdge = b.pickupStepEdge;
            v.pickupStepProg = b.pickupStepProg;
            v.atSpot = b.atSpot;

            v.breakout = b.breakoutFrames > 0;
            return v;
        }

        private static bool TrapFree(TrappedDuck trap)
        {
            Duck captive = trap.captureDuck;
            return trap.owner == null && trap.visible && !trap.removeFromLevel
                && captive != null && !captive.dead && captive._trapped == trap;
        }

        private static bool StillFree(Thing t)
        {
            if (t.removeFromLevel || t.owner != null) return false;
            ItemBox box = t as ItemBox;
            if (box != null) return !box._hit;
            TrappedDuck trap = t as TrappedDuck;
            if (trap != null) return TrapFree(trap);
            Gun g = t as Gun;
            return g == null || Usable(g);
        }

        private const int BreakoutFrames = 120;

        // Сторож смотрит не на причину, а на следствие: идёт к цели, а с места не сдвинулся. Смещение
        // меряется по земле: прыжок на месте поднимает утку на 60 px.
        private static void Watch(Bot b, Duck d)
        {
            if (b.breakoutFrames > 0) { b.breakoutFrames--; b.anchorFrames = 0; return; }

            // Ожидание на позиции и пустая арена — не залипание.
            int mode = b.state.mode;
            bool going = mode == BotBrain.GetGun || mode == BotBrain.Carry
                || (mode == BotBrain.Chase && b.view.hasEnemy && b.state.why != BotBrain.WhyAim);
            bool moved = d.grounded
                && Math.Abs(d.x - b.anchorX) + Math.Abs(d.bottom - b.anchorY) > 32f;
            bool frozen = d.immobilized;
            if (!going || moved || frozen)
            {
                b.anchorX = d.x;
                b.anchorY = d.bottom;
            }
            if (!BotBrain.Watchdog(ref b.anchorFrames, going, moved, frozen)) return;

            b.breakoutFrames = BreakoutFrames;
            _watchdogFired++;
            bool forPickup = mode == BotBrain.GetGun && b.pickup != null;
            Log(BotName(b) + ": сторож застрял=4с x=" + (int)d.x + " y=" + (int)d.bottom + " "
                + Context(b, d) + " — " + Reason(b.state)
                + (forPickup ? "; бросаю цель, ухожу в сторону" : "; ухожу в сторону")
                + " запись=f" + frame);
            RequestDump(d, "сторож");
            if (forPickup)
            {
                b.skip.Add(b.pickup, SkipFrames * 2);
                b.pickup = null;
                b.pickupNode = -1;
                b.routeWait = 0;
            }
        }

        // Команды не смотрим: в versus все, кроме себя, — враги.
        private static Duck NearestEnemy(Duck me, bool skipNetted)
        {
            Duck best = null;
            float bestDist = 0f;
            foreach (Thing t in Level.current.things[typeof(Duck)])
            {
                Duck d = t as Duck;
                if (d == null || d == me || d.dead || d.destroyed || d.removeFromLevel) continue;
                if (skipNetted && d.inNet) continue;
                float ddx = d.x - me.x;
                float ddy = d.y - me.y;
                float dist = ddx * ddx + ddy * ddy;
                if (best == null || dist < bestDist) { best = d; bestDist = dist; }
            }
            return best;
        }

        // Кто целится в бота: ствол по прямой, смотрит на него, та же высота, чистая линия.
        private static Duck Aiming(Duck me)
        {
            Duck best = null;
            float bestDx = 0f;
            foreach (Thing t in Level.current.things[typeof(Duck)])
            {
                Duck e = t as Duck;
                if (e == null || e == me || e.dead || e.destroyed || e.removeFromLevel || e.ragdoll != null) continue;
                Gun g = e.holdObject as Gun;
                if (g == null || !Usable(g) || IsMelee(g) || g is Grenade || g is Mine || g is NetGun) continue;
                float dx = me.x - e.x;
                if (Math.Abs(dx) > ThreatRange || Math.Abs(me.y - e.y) >= BotBrain.SameLevelY) continue;
                if ((dx > 0f) != (e.offDir > 0)) continue;
                if (Wall(e.position, me.position, GlassGun(g))) continue;
                if (Level.CheckRay<Duck>(e.position + new Vec2(dx > 0f ? 10f : -10f, 0f), me.position) != me) continue;
                if (best != null && Math.Abs(dx) >= Math.Abs(bestDx)) continue;
                best = e;
                bestDx = dx;
            }
            return best;
        }

        public static Bot Find(Duck d)
        {
            for (int i = 0; i < bots.Count; i++)
                if (bots[i].profile != null && bots[i].profile.duck == d) return bots[i];
            return null;
        }
    }
}
