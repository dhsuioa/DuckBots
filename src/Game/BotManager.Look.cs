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
        // --- глаза бота ---

        // Предикаты создаются один раз: в горячем пути нельзя рожать делегат каждый кадр.
        private static readonly Predicate<Thing> _freeGun = IsFreeGun;
        private static readonly Predicate<Thing> _liveBox = IsLiveBox;
        private static readonly Predicate<Thing> _armedExplosive = IsArmedExplosive;
        private static readonly Predicate<Thing> _freePresent = IsFreePresent;

        private const float FootProbe = 14f;   // на шаг вперёд от центра утки
        private const float GlassSee = 64f;    // стекло дальше этого для удара не в счёт
        private const float ThreatRange = 200f; // дальше этого прицел врага не повод ложиться
        private const float FootDepth = 34f;   // глубже — уже яма, а не ступенька
        // Ручка силы для этапа 3: сколько ждать линии на позиции и на сколько её забыть.
        private const int SpotPatience = 120, SpotOffFrames = 180;
        private const float KickVs = -5.5f;    // быстрее вверх своим прыжком (−4.9) не взлететь

        // isFatal игра ставит сама: у саксофона, трубы, тромбона и кейтара он false.
        // Отдельного списка музыкальных стволов заводить не надо, игра его уже знает.
        private static bool IsFreeGun(Thing t)
        {
            Gun g = t as Gun;
            return g != null && Usable(g) && g.owner == null && !g.removeFromLevel && g.visible && !Skipped(t);
        }

        // Недостижимые цели текущего бота. Статическое поле, потому что предикат для
        // NearestThingFilter создан один раз и о боте ничего не знает; Look выставляет
        // его перед поиском и сбрасывает после.
        private static SkipList _skipFor;
        private const int SkipFrames = 180;

        private static bool Skipped(Thing t) { return _skipFor != null && _skipFor.Contains(t); }

        // Годится ли ствол в бой. isFatal игра ставит сама: саксофон, водомёт, сетемёт
        // и прочее несмертельное помечены false. SuicidePistol смертелен, но стреляет
        // назад (barrelAngleDegrees = 180, immediatelyDeadly) — в руках бота это
        // самоубийство, а не оружие.
        private static bool Usable(Gun g)
        {
            if (g.ammo <= 0 || g is SuicidePistol || Unskilled(g)) return false;
            // Сеткомёт несмертелен, но там, где есть пропасть, это оружие: поймать
            // врага сеткой, поднять и бросить за край.
            if (g is NetGun) return _nav != null && _nav.hasAbyssEdge;
            return g.isFatal;
        }

        // Особое оружие, для которого у бота ещё нет своей логики. Игра считает его
        // смертельным, и бот брал его как ствол: луч захвата обездвиживает самого стрелка,
        // пультом водят машинку, магнит только притягивает металл — бот стоял с ними без
        // толку. Пока не брать, логику для каждого — отдельно. GoodBook бот использует сам.
        private static bool Unskilled(Gun g)
        {
            return g is MindControlRay || g is RCController || g is MagnetGun;
        }

        // Общего предка у холодного оружия нет, все они просто Gun. Список взят из
        // каталога мира — там же видно, что их «дальность» в исходниках это хвост
        // шаблона, а не длина удара.
        private static bool IsMelee(Gun g)
        {
            return g is Sword || g is EnergyScimitar || g is OldEnergyScimi || g is TapedSword
                || g is Chainsaw || g is SledgeHammer || g is BoxingGloves;
        }

        // Бьёт не пулей по прямой: взрыв, огонь или луч под углом. Стекло в двух шагах
        // таким не разбивают — достанется себе.
        private static bool Blasts(Gun g)
        {
            return Explodes(g) || g is FlameThrower || g is LaserRifle;
        }

        // Рвётся там, куда попало: ракета, граната, ракетница и римская свеча (она —
        // FlareGun). Ближе BotBrain.BlastSafe взрыв достаёт самого стрелка.
        public static bool Explodes(Gun g)
        {
            return g is Bazooka || g is GrenadeLauncher || g is GrenadeCannon || g is FlareGun;
        }

        // Дальность выстрела из того, что в руках (BotBrain.FireReach). Холодное, сеткомёт,
        // гранаты, мины и Библия живут по своим дистанциям — им прежняя RangeX.
        public static float GunReach(Gun g)
        {
            if (g == null || g.ammoType == null || IsMelee(g) || g is NetGun
                || g is Grenade || g is Mine || g is GoodBook) return BotBrain.RangeX;
            if (g is FlameThrower) return BotBrain.FlameReach;
            if (g is FlareGun) return BotBrain.FlareReach;
            AmmoType a = g.ammoType;
            return BotBrain.FireReach(a.range, a.rangeVariation, a.accuracy, a.affectedByGravity);
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

        // От чего бежать: от гранаты с выдернутой чекой — это таймер на две секунды.
        // Мина — не таймер, а нажимная ловушка: лежащая взрывается, только если на
        // неё наступить и сойти, и её клетку обходит маршрут. Бежать от мины незачем:
        // сначала боты по десять секунд «убегали» от лежащей мины в стену, потом —
        // от мины в руках игрока (её _clicked, раз выставленный, не сбрасывается).
        private static bool IsArmedExplosive(Thing t)
        {
            Grenade g = t as Grenade;
            return g != null && !g._pin && !g.removeFromLevel;
        }

        // Есть ли опора под точкой. Смотрим и сплошные блоки, и односторонние платформы:
        // для бота, решающего «шагнуть или прыгнуть», разницы между ними нет.
        private static bool Footing(float x, float yFrom)
        {
            Vec2 a = new Vec2(x, yFrom);
            Vec2 b = new Vec2(x, yFrom + FootDepth);
            // Шипы — не опора, а смерть: считаем их ямой, чтобы бот перепрыгивал.
            if (Level.CheckLine<Spikes>(a, b) != null) return false;
            if (Level.CheckLine<Block>(a, b) != null) return true;
            if (Level.CheckLine<AutoPlatform>(a, b) != null) return true;
            return Level.CheckLine<Platform>(a, b) != null;
        }

        // Единственное место, где мозг соприкасается с игрой: здесь обстановка
        // превращается в числа, а дальше Decide работает уже без игровых типов.
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
            // Прыжок по программе начинается из центра узла, откуда считан маршрут, — так
            // его прогоняла карта. Не из центра своей клетки: тормозя на полном ходу,
            // бот проскакивает в соседнюю и выполнил бы там программу чужого узла.
            if (!float.IsNaN(b.routeStartX)) v.alignDx = b.routeStartX - me.x;
            else if (_nav != null)
                v.alignDx = (float)Math.Floor((me.x - _nav.originX) / NavCell) * NavCell
                    + _nav.originX + NavCell / 2f - me.x;

            // Обрывы щупаем с обеих сторон: направление движения выбирает Decide,
            // а мерить по offDir значит опаздывать на кадр ровно в момент разворота.
            if (me.grounded)
            {
                float foot = me.bottom - 2f;
                v.gapLeft = !Footing(me.x - FootProbe, foot);
                v.gapRight = !Footing(me.x + FootProbe, foot);
                // Пропасть — обрыв, под которым до низа карты нет ни пола, ни платформы.
                // Без карты любой обрыв считаем пропастью: осторожность дешевле смерти.
                v.abyssLeft = v.gapLeft
                    && (_nav == null || !_nav.SafeBelow(me.x - FootProbe, me.bottom + 2f));
                v.abyssRight = v.gapRight
                    && (_nav == null || !_nav.SafeBelow(me.x + FootProbe, me.bottom + 2f));
                // Односторонняя платформа под ногами, а сплошного пола нет — можно вниз.
                Vec2 under = new Vec2(me.x, me.bottom + 3f);
                v.platformBelow = Level.CheckPoint<Block>(under) == null
                    && (Level.CheckPoint<AutoPlatform>(under) != null
                        || Level.CheckPoint<Platform>(under) != null);
                // Под платформой смотрим со следующей клетки: сама платформа не в счёт.
                v.abyssBelow = v.platformBelow
                    && (_nav == null || !_nav.SafeBelow(me.x, me.bottom + 3f + NavCell));
            }

            // Целое вертикальное стекло по сторонам на высоте утки. Маршрут ведёт сквозь
            // стекло ребром «стекло», а мозгу нужно знать, далеко ли оно и цело ли ещё.
            // Разбилось то, по которому бот бил в прошлом кадре, — строка в журнал: по ней
            // видно, что умение работает, и каким способом.
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

            // С сеткомётом или с пойманным на руках враг в сетке — не цель: второй сеткой
            // его не поймать, а тот, что на руках, и так наш. Со смертельным стволом —
            // цель: пуля убивает и пойманного.
            bool netOrCarry = me.holdObject is NetGun || me.holdObject is TrappedDuck;
            Duck enemy = NearestEnemy(me, netOrCarry);
            if (enemy != null)
            {
                v.hasEnemy = true;
                v.ex = enemy.x - me.x;
                v.ey = enemy.y - me.y;
                // Пара проверок из ванильного AIStateFindTarget: луч до утки и стена на пути.
                // Луч пускаем в сторону врага, а не по offDir: бот, стоящий спиной, иначе
                // никогда не узнает, что достаточно развернуться.
                v.canShootUp = v.ey < -BotBrain.SameLevelY && v.ey > -BotBrain.HoverReach
                    && Math.Abs(v.ex) < BotBrain.HoverAimX
                    && Level.CheckLine<Block>(me.position, enemy.position) == null;
                v.canShoot = Math.Abs(v.ey) < BotBrain.SameLevelY
                    && Math.Abs(v.ex) < GunReach(me.holdObject as Gun)
                    && Level.CheckLine<Block>(me.position, enemy.position) == null
                    && Level.CheckRay<Duck>(me.position + new Vec2(v.ex > 0f ? 10f : -10f, 0f),
                                            enemy.position) == enemy;
            }

            Gun held = me.holdObject as Gun;
            if (held != null)
            {
                Grenade gr = held as Grenade;
                Mine mn = held as Mine;
                v.explosiveInHand = gr != null || mn != null;
                v.armedInHand = (gr != null && !gr._pin) || (mn != null && mn._armed);
                // Саксофон и пустой ствол одинаково бесполезны, разбирать их незачем.
                v.holding = Usable(held) ? 1 : 2;
                v.meleeInHand = IsMelee(held);
                v.netGun = held is NetGun && v.holding == 1;
                v.blastInHand = v.holding == 1 && Explodes(held);
                v.holdFire = v.holding == 1 && held is FlameThrower;
                v.glassGun = v.holding == 1 && !v.meleeInHand && !v.explosiveInHand
                    && !(held is NetGun) && !Blasts(held);
            }
            else if (me.holdObject != null)
            {
                v.holding = 3;
                v.presentInHand = me.holdObject is Present;
                TrappedDuck trap = me.holdObject as TrappedDuck;
                v.carrying = trap != null && trap.captureDuck != null && trap.captureDuck != me;
            }

            // Цель подбора выбирает Route по длине маршрута, раз в десять кадров. Между
            // пересчётами она держится, но каждый кадр проверяется: ствол могли
            // подобрать, ящик — вскрыть. Тогда цель забывается и выбирается заново
            // на первом же кадре на земле. Без отсечки по дальности: списки уровня
            // глобальные, тумана войны в игре нет, стоит ли бежать через полкарты —
            // решает длина маршрута.
            b.skip.Tick();
            // С сеткомётом тоже ищем: пойманного врага и смертельный ствол получше.
            bool wantPickup = !v.carrying && (v.holding != 1 || v.netGun);
            if (b.pickup != null && (!wantPickup || !StillFree(b.pickup)))
            {
                if (wantPickup) b.routeWait = 0;
                b.pickup = null;
                b.pickupNode = -1;
                b.hasPickupStep = false;
            }

            // Своя взведённая граната в руках опасностью не считается: её надо бросать,
            // а не убегать от собственных рук. Этим занимается первая ветка Decide.
            // Порог с запасом: убегающий бот держит опасность до DangerR + 20, иначе на
            // границе радиуса он менял режим каждый кадр (FLEE↔UNSTUCK на snow15_8).
            Thing bomb = Level.current.NearestThingFilter<Gun>(
                me.position, _armedExplosive, b.danger != null ? BotBrain.DangerR + 20f : BotBrain.DangerR);
            b.danger = null;
            if (bomb != null && bomb != me.holdObject)
            {
                v.danger = true;
                v.dx = bomb.x - me.x;
                b.danger = bomb;
            }

            // Позицию для выстрела Route выбирает по клеткам карты, а дверь для карты —
            // проход: утка проходит её насквозь. Закрытая дверь при этом заслоняет линию
            // огня, и 20.10 на industrial01 два бота стояли по разные стороны двери и
            // ждали линии, пока раунд не закончили руками. Сторож этого не видит: он
            // считает застревание по пути к цели, а не ожидание. Простоял на позиции без
            // линии дольше SpotPatience — позицию на время забыть и идти к врагу по
            // маршруту: толчок открывает дверь.
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

            // Толчок пружины: в воздухе скорость вверх резко выросла. Свой прыжок даёт −4.9
            // и только с земли, пружина — до −22 (повторные отскоки под блоком −6…−7).
            // Запоминаем узел над пружиной: маршрут в полёте посчитаем один раз оттуда, и
            // следующим шагом будет ребро полёта с неё (Launch) — к его цели бот и рулит.
            bool kicked = !me.grounded && me.vSpeed < KickVs && b.prevVs - me.vSpeed > 3f;
            b.prevVs = me.vSpeed;
            v.kicked = kicked;
            if (kicked && _nav != null)
            {
                b.flightStart = _nav.NodeNear(me.x, me.bottom - 4f);
                b.routeWait = 0;
            }
            else if (me.grounded) b.flightStart = -1;

            // Маршрут вокруг ям и через этажи знает только граф; прямое направление
            // приводит бота в стену. Считаем его не каждый кадр: цель за десять кадров
            // далеко не уедет.
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

        // Враг в сетке, которого можно поднять: сетка на месте, в ней живой враг,
        // её никто не держит.
        private static bool TrapFree(TrappedDuck trap)
        {
            Duck captive = trap.captureDuck;
            return trap.owner == null && trap.visible && !trap.removeFromLevel
                && captive != null && !captive.dead && captive._trapped == trap;
        }

        // Цель всё ещё лежит и годится: её не подобрали, ящик не вскрыли.
        private static bool StillFree(Thing t)
        {
            if (t.removeFromLevel || t.owner != null) return false;
            ItemBox box = t as ItemBox;
            if (box != null) return !box._hit;
            // Пойманный вырвался — сетка исчезла или в ней уже никого.
            TrappedDuck trap = t as TrappedDuck;
            if (trap != null) return TrapFree(trap);
            Gun g = t as Gun;
            return g == null || Usable(g);
        }

        private const int BreakoutFrames = 120;   // столько уходить в сторону

        // Сторож залипания. Боты порой прыгали на месте, пока раунд не заканчивали
        // руками: маршрут вёл туда, куда на деле не попасть, и бот повторял одно и то же
        // движение. Причин у этого может быть несколько, и видно их не все, поэтому
        // сторож смотрит не на причину, а на следствие: идёт к цели, а с места не
        // сдвинулся. Тогда — строка в лог с тем, куда он пытался, цель забыть,
        // и пару секунд уходить в сторону. Смещение меряется только по земле:
        // прыжок на месте поднимает утку на 60 пикселей и иначе сбивал бы счёт.
        private static void Watch(Bot b, Duck d)
        {
            if (b.breakoutFrames > 0) { b.breakoutFrames--; b.anchorFrames = 0; return; }

            int mode = b.state.mode;
            // Идёт к цели — значит должен сдвигаться. Стоять, когда врагов не осталось
            // или бот ждёт линию огня на позиции, — не залипание.
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

        // Уток на арене не больше восьми, поэтому перебор дешевле любых ухищрений.
        // Команды не смотрим: в обычном versus все, кроме себя, — враги.
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

        // Кто целится в бота: в руках ствол, стреляющий по прямой, смотрит в его сторону,
        // та же высота (пуля летит горизонтально), в пределах выстрела и чистая линия —
        // та же пара проверок, что мозг делает для своего выстрела, только наоборот.
        // Ближайший такой стрелок — повод уйти подкатом.
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
                if ((dx > 0f) != (e.offDir > 0)) continue;                   // смотрит не на бота
                if (Level.CheckLine<Block>(e.position, me.position) != null) continue;
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
