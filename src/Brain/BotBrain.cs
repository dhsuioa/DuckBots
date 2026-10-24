using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        public const int Chase = 0, Fight = 1, GetGun = 2, Flee = 3, Drop = 4, Unstuck = 5, Netted = 6, Carry = 7, Ragdoll = 8;
        public static readonly string[] modeNames = { "CHASE", "FIGHT", "GETGUN", "FLEE", "DROP", "UNSTUCK", "NETTED", "CARRY", "RAGDOLL" };

        public const float RangeX = 150f;      // дальность, когда своей у оружия нет
        public const float SameLevelY = 16f;   // ствол бьёт только горизонтально
        public const float KeepDist = 20f;
        public const float BlastSafe = 56f;    // взрыв убивает в 30 px и сыплет осколки ещё на 20 (ATMissile.DestroyRadius)
        public const float MeleeReach = 22f;
        public const float BoxDist = 8f;
        public const float ThrowRange = 70f;
        public const float DropBelow = 24f;
        public const float VertStep = 16f;
        public const int WanderFrames = 45;
        public const float DangerR = 75f;
        public const float HoverReach = 90f;
        public const float HoverAimX = 16f;
        public const int ShootHold = 3, ShootGap = 8;
        public const int GrabGap = 12;
        // Высота прыжка — длина удержания: Released(Jump) режет скорость вверх вдвое.
        public const int JumpTap = 4, JumpMid = 14, JumpFull = 28;
        public const int JumpRest = 2;
        public const int StuckFrames = 12;
        public const int StuckLimit = 240;
        public const float BoxReach = 80f;
        public const float GrabR = 16f;        // игра берёт в круге 18 вокруг точки на 4 ниже центра (Duck.TryGrab)
        public const float FarJump = 48f;      // дальше — до упора: дальность рёбер граф считает по полному прыжку
        public const float AlignTol = 2f;

        // Причины решений: why — ветка Think, how — способ хода. Их печатает журнал и сверяет самопроверка.
        public const string WhyThrow = "бросаю взведённую";
        public const string WhyFlee = "бегу от взрывчатки";
        public const string WhyArm = "взвожу: враг вплотную";
        public const string WhyCarry = "несу взрывчатку к врагу";
        public const string WhyShootUp = "стреляю вверх взмахами";
        public const string WhyTurn = "разворот к врагу";
        public const string WhyMeleeGo = "подхожу с холодным";
        public const string WhyMeleeHit = "бью вплотную";
        public const string WhyShoot = "стреляю: линия огня";
        public const string WhyBackOff = "стреляю и отступаю: враг вплотную";
        public const string WhyLob = "гранатомёт: поднимаю ствол под дугу";
        public const string WhyGlassBack = "отхожу от стекла, чтобы стрелять сквозь него";
        public const string WhyBlastBack = "отхожу: взрывчаткой вплотную нельзя";
        public const string WhyBreakout = "сторож: ухожу в сторону";
        public const string WhyPresent = "открываю подарок";
        public const string WhyJunk = "выбрасываю негодное";
        public const string WhyGun = "иду к стволу";
        public const string WhyGrab = "подбираю";
        public const string WhyBox = "иду к ящику";
        public const string WhyHitBox = "бью ящик снизу";
        public const string WhyNoEnemy = "врагов нет";
        public const string WhyStack = "расходимся: враг сверху или снизу";
        public const string WhyChase = "иду к врагу";
        public const string WhyAim = "на позиции: жду линию огня";
        public const string WhyNetted = "в сетке: рвусь наружу";
        public const string WhyRagdoll = "в рэгдолле: встаю прыжком";
        public const string WhyCarryTrap = "несу пойманного к пропасти";
        public const string WhyThrowTrap = "бросаю пойманного в пропасть";
        public const string WhyTakeTrap = "иду за пойманным";
        public const string WhyFreeHands = "освобождаю руки для пойманного";
        public const string WhyDropNet = "бросаю сеткомёт: есть ствол получше";

        public const string HowRoute = "по маршруту";
        public const string HowRouteJump = "прыжок по маршруту";
        public const string HowRouteDrop = "вниз сквозь платформу по маршруту";
        public const string HowSpring = "к пружине по маршруту";
        public const string HowSpringFlight = "лечу с пружины: рулю к цели полёта";
        public const string HowDirect = "напрямик: маршрута нет";
        public const string HowDropGuess = "вниз сквозь платформу наугад";
        public const string HowWander = "брожу: цель над или под, маршрута нет";
        public const string HowSidestep = "шаг в сторону";
        public const string HowGapJump = "обрыв: прыжок наугад";
        public const string HowStuckJump = "упёрся: прыжок";
        public const string HowGuessJump = "цель выше: прыжок наугад";
        public const string HowAbyss = "стою: впереди пропасть";
        public const string HowHere = "на месте";
        public const string HowStepOff = "схожу с опоры, которой нет на карте";
        public const string HowAlign = "встаю в центр клетки перед прыжком";

        // Чистая функция: на входе числа, на выходе маска кнопок. Начатый прыжок держится
        // поверх решения ветки: высоту задаёт удержание уже в воздухе, где ни одно условие
        // «пора прыгать» не выполняется.
        public static ushort Decide(ref BotView v, ref BotState s)
        {
            if (s.jumpRest > 0) s.jumpRest--;
            s.how = null;
            // Программа прыжка кончается только приземлением, а пружина под блоком ловит раньше
            // земли: подбросило — программу бросить, дальше рулит ребро полёта (Launch).
            if (v.kicked) { s.progAt = 0; s.jumpHold = 0; s.stuckFrames = 0; }
            ushort m = Think(ref v, ref s);
            m = Dodge(m, ref v, ref s);
            if (s.jumpHold > 0)
            {
                m |= BotInput.JUMP;
                if (--s.jumpHold == 0) s.jumpRest = JumpRest;
            }
            // Пока идёт программа прыжка ребра, направление и прыжок жмёт только она — как её прогнала карта.
            if (s.progAt > 0)
            {
                int f = s.progAt - 1;
                if ((f > 1 && v.grounded) || f > 240)
                {
                    s.progAt = 0;
                    s.jumpRest = JumpRest;
                }
                else
                {
                    m &= unchecked((ushort)~(BotInput.LEFT | BotInput.RIGHT | BotInput.JUMP | BotInput.DOWN));
                    int dir = NavMap.ProgDirAt(s.prog, f);
                    if (dir > 0) m |= BotInput.RIGHT;
                    else if (dir < 0) m |= BotInput.LEFT;
                    if (NavMap.ProgJumpAt(s.prog, f)) m |= BotInput.JUMP;
                    s.progAt++;
                }
            }
            return m;
        }

        // Pressed ловит фронт: перед новым прыжком кнопка должна побыть отпущенной.
        private static void StartProgram(ref BotState s, int prog, float dx, float dy)
        {
            if (s.jumpHold > 0 || s.jumpRest > 0 || s.progAt > 0) return;
            s.prog = prog;
            s.progAt = 1;
            s.jumpBy = HowRouteJump;
            s.jumpDX = dx;
            s.jumpDY = dy;
        }

        // Встать в центр клетки: желаемая скорость — по расстоянию, трение (0.25) и встречное
        // нажатие (ещё 0.55) гасят лишнее.
        private static ushort Align(float cx, float hs)
        {
            float want = cx * 0.3f;
            if (want > 2.85f) want = 2.85f;
            if (want < -2.85f) want = -2.85f;
            if (hs < want - 0.25f) return BotInput.RIGHT;
            if (hs > want + 0.25f) return BotInput.LEFT;
            return 0;
        }

        private static void StartJump(ref BotState s, int hold, bool grounded, string by, float dx, float dy)
        {
            if (!grounded || s.jumpHold > 0 || s.jumpRest > 0) return;
            s.jumpHold = hold;
            s.jumpBy = by;
            s.jumpDX = dx;
            s.jumpDY = dy;
        }

        private static bool InGrabReach(float px, float py)
        {
            float dy = py - 4f;
            return px * px + dy * dy < GrabR * GrabR;
        }

        private static bool Abyss(ref BotView v, int dir)
        {
            return dir > 0 ? v.abyssRight : dir < 0 && v.abyssLeft;
        }

        // Обездвиженная утка (заставка раунда, утрамбовка мушкета) не застряла.
        public static bool Watchdog(ref int frames, bool going, bool moved, bool frozen)
        {
            if (!going || moved || frozen) { frames = 0; return false; }
            if (++frames < StuckLimit) return false;
            frames = 0;
            return true;
        }

        private static ushort Think(ref BotView v, ref BotState s)
        {
            // Из рэгдолла игра поднимает утку по новому нажатию JUMP, когда тело на земле
            // (Ragdoll.UpdateInput). Координаты утки при этом на 5000 выше — остальной обзор врёт.
            if (v.ragdoll)
            {
                s.mode = Ragdoll;
                s.why = WhyRagdoll;
                s.jumpHold = 0;
                s.progAt = 0;
                return Pulse(ref s.jumpTimer, 1, 1) ? BotInput.JUMP : (ushort)0;
            }

            // Каждое нажатие прыжка срезает время сетки.
            if (v.netted)
            {
                s.mode = Netted;
                s.why = WhyNetted;
                s.jumpHold = 0;
                s.progAt = 0;
                return Pulse(ref s.jumpTimer, 1, 1) ? BotInput.JUMP : (ushort)0;
            }

            // GRAB бросает туда, куда смотрит утка.
            if (v.armedInHand)
            {
                s.mode = Fight;
                s.why = WhyThrow;
                if (v.hasEnemy)
                {
                    bool toRight = v.ex > 0f;
                    if (toRight != v.facingRight) return toRight ? BotInput.RIGHT : BotInput.LEFT;
                }
                return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
            }

            if (v.danger)
            {
                s.mode = Flee;
                s.why = WhyFlee;
                return MoveTo(v.dx > 0f ? -100f : 100f, 0f, NavMap.Walk, false, 0, ref v, ref s);
            }

            if (v.explosiveInHand)
            {
                if (v.hasEnemy && v.canShoot && Math.Abs(v.ex) < ThrowRange)
                {
                    s.mode = Fight;
                    s.why = WhyArm;
                    bool toRight = v.ex > 0f;
                    if (toRight != v.facingRight) return toRight ? BotInput.RIGHT : BotInput.LEFT;
                    return Pulse(ref s.shootTimer, 1, ShootGap) ? BotInput.SHOOT : (ushort)0;
                }
                s.mode = Chase;
                s.why = v.hasEnemy ? WhyCarry : WhyNoEnemy;
                if (!v.hasEnemy) return 0;
                return GoToEnemy(ref v, ref s);
            }

            // Упавший за край в сетке гибнет, убийство засчитывается поймавшему. Бросок с зажатым
            // направлением в 2.5 раза сильнее (Duck.ThrowItem), но жать его можно только в кадр
            // броска, иначе утка шагнёт с края сама.
            if (v.carrying)
            {
                s.mode = Carry;
                if (v.atThrowSpot)
                {
                    s.why = WhyThrowTrap;
                    ushort toward = v.throwDir > 0 ? BotInput.RIGHT : BotInput.LEFT;
                    if ((v.throwDir > 0) != v.facingRight) return toward;
                    return Pulse(ref s.grabTimer, 1, GrabGap) ? (ushort)(toward | BotInput.GRAB) : (ushort)0;
                }
                s.why = WhyCarryTrap;
                if (v.hasThrowStep)
                    return MoveTo(v.throwStepX, v.throwStepY, v.throwStepEdge, true, v.throwStepProg, ref v, ref s);
                s.how = HowHere;
                return 0;
            }

            // Угол прицела в игре не выбирается, но на взмахах ствол сам смотрит вверх. Взмах — новое
            // нажатие прыжка в воздухе: подпрыгнуть, отпустить, зажать снова. Огонь огнемёта падает на стрелка.
            if (v.holding == 1 && v.canShootUp && !(v.blastInHand && -v.ey < BlastSafe) && !v.holdFire)
            {
                s.mode = Fight;
                s.why = WhyShootUp;
                ushort up = 0;
                if (v.grounded) StartJump(ref s, 3, true, WhyShootUp, 0f, 0f);
                else if (s.jumpHold == 0 && s.jumpRest == 0) up |= BotInput.JUMP;
                if (Pulse(ref s.shootTimer, ShootHold, ShootGap)) up |= BotInput.SHOOT;
                return up;
            }

            if (v.holding == 1 && v.canShoot)
            {
                s.mode = Fight;
                bool targetRight = v.ex > 0f;
                if (targetRight != v.facingRight)
                {
                    s.why = WhyTurn;
                    return targetRight ? BotInput.RIGHT : BotInput.LEFT;
                }

                // Холодное для игры — Gun с патронами, но бьёт только вплотную.
                if (v.meleeInHand)
                {
                    if (Math.Abs(v.ex) > MeleeReach)
                    {
                        s.why = WhyMeleeGo;
                        return MoveTo(v.ex, v.ey, NavMap.Walk, false, 0, ref v, ref s);
                    }
                    s.why = WhyMeleeHit;
                    return Pulse(ref s.shootTimer, ShootHold, ShootGap) ? BotInput.SHOOT : (ushort)0;
                }

                // STRAFE держит утку лицом к врагу: без него шаг назад разворачивает её спиной.
                if (v.blastInHand && Math.Abs(v.ex) < BlastSafe)
                {
                    s.why = WhyBlastBack;
                    if (Abyss(ref v, targetRight ? -1 : 1)) return 0;
                    return (ushort)(BotInput.STRAFE | (targetRight ? BotInput.LEFT : BotInput.RIGHT));
                }

                if (v.glassLine && v.hasGlass && (v.glassDx > 0f) == targetRight
                    && Math.Abs(v.glassDx) - GlassReach < ShootMin && !Abyss(ref v, targetRight ? -1 : 1))
                {
                    s.why = WhyGlassBack;
                    return (ushort)(BotInput.STRAFE | (targetRight ? BotInput.LEFT : BotInput.RIGHT));
                }

                ushort m = 0;
                s.why = WhyShoot;
                if (Math.Abs(v.ex) < KeepDist && !Abyss(ref v, targetRight ? -1 : 1))
                {
                    m |= (ushort)(BotInput.STRAFE | (targetRight ? BotInput.LEFT : BotInput.RIGHT));
                    s.why = WhyBackOff;
                }
                // Нажатие ловится по фронту, а остывающий ствол его не слышит: до подъёма ствола — импульсы.
                if (v.lobber)
                {
                    s.why = WhyLob;
                    if (v.lobCooling) return m;
                    if (!v.lobAiming) return Pulse(ref s.shootTimer, 1, 1) ? (ushort)(m | BotInput.SHOOT) : m;
                    return v.lobAim >= v.lobAngle ? m : (ushort)(m | BotInput.SHOOT);
                }

                if (v.holdFire || Pulse(ref s.shootTimer, ShootHold, ShootGap)) m |= BotInput.SHOOT;
                return m;
            }

            // GRAB с чем-то в руках бросает это, а не поднимает.
            if (v.hasPickup && v.pickupIsTrap)
            {
                s.mode = Carry;
                if (v.holding != 0)
                {
                    s.why = WhyFreeHands;
                    return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
                }
                s.why = WhyTakeTrap;
                ushort take = GoToPickup(ref v, ref s);
                if (InGrabReach(v.px, v.py) && Pulse(ref s.grabTimer, 1, GrabGap)) take |= BotInput.GRAB;
                return take;
            }

            // С сеткомётом целью бывает только пойманный или смертельный ствол.
            if (v.netGun && v.hasPickup)
            {
                s.mode = Drop;
                s.why = WhyDropNet;
                return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
            }

            if (v.breakout)
            {
                s.mode = Unstuck;
                s.why = WhyBreakout;
                if (s.breakDir == 0) s.breakDir = v.facingRight ? -1 : 1;
                if (Abyss(ref v, s.breakDir)) s.breakDir = -s.breakDir;
                return MoveTo(s.breakDir * 100f, 0f, NavMap.Walk, false, 0, ref v, ref s);
            }
            s.breakDir = 0;

            if (v.presentInHand)
            {
                s.mode = GetGun;
                s.why = WhyPresent;
                return Pulse(ref s.shootTimer, 1, ShootGap) ? BotInput.SHOOT : (ushort)0;
            }

            if (v.holding == 2 || v.holding == 3)
            {
                s.mode = Drop;
                s.why = WhyJunk;
                return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
            }

            if (v.holding != 1 && v.hasPickup)
            {
                s.mode = GetGun;
                s.why = v.pickupIsBox ? WhyBox : WhyGun;
                ushort m = GoToPickup(ref v, ref s);
                if (v.pickupIsBox)
                {
                    if (Math.Abs(v.px) < BoxDist && v.py < -VertStep && v.py > -BoxReach)
                    {
                        s.why = WhyHitBox;
                        StartJump(ref s, JumpFull, v.grounded, WhyHitBox, v.px, v.py);
                    }
                }
                else if (InGrabReach(v.px, v.py) && Pulse(ref s.grabTimer, 1, GrabGap))
                {
                    m |= BotInput.GRAB;
                    s.why = WhyGrab;
                }
                return m;
            }

            s.mode = Chase;
            if (!v.hasEnemy) { s.why = WhyNoEnemy; return 0; }
            // Утки друг на друге: выстрела нет, маршрута тоже (одна клетка) — разойтись.
            if (Math.Abs(v.ex) < HoverAimX && Math.Abs(v.ey) < DropBelow)
            {
                s.why = WhyStack;
                return Sidestep(ref v, ref s);
            }
            if (v.holding == 1 && v.atSpot)
            {
                s.why = WhyAim;
                bool right = v.ex > 0f;
                if (right != v.facingRight) return right ? BotInput.RIGHT : BotInput.LEFT;
                s.how = HowHere;
                return 0;
            }
            s.why = WhyChase;
            return GoToEnemy(ref v, ref s);
        }

        private static ushort Sidestep(ref BotView v, ref BotState s)
        {
            if (s.wanderFrames <= 0)
            {
                s.wanderFrames = WanderFrames;
                s.wanderDir = s.wanderDir == 0 ? (v.facingRight ? 1 : -1) : -s.wanderDir;
            }
            s.wanderFrames--;
            if (Abyss(ref v, s.wanderDir)) s.wanderDir = -s.wanderDir;
            if (Abyss(ref v, s.wanderDir)) { s.how = HowAbyss; return 0; }
            s.how = HowSidestep;
            return s.wanderDir > 0 ? BotInput.RIGHT : BotInput.LEFT;
        }

        // Есть маршрут — шаг говорит, что делать: пройти, прыгнуть, провалиться. Нет — догадки:
        // прыжок при застревании и блуждание вдоль платформы.
        private static ushort MoveTo(float dx, float dy, int edge, bool routed, int prog,
                                     ref BotView v, ref BotState s)
        {
            ushort smash;
            if (Smash(dx, edge, routed, ref v, ref s, out smash)) return smash;

            bool wantUp = dy < -VertStep;
            bool wantDown = dy > DropBelow;

            // Программу прыжка проигрывают из центра клетки и с места — так её прогнала карта.
            if (routed && edge == NavMap.Jump && prog != 0 && v.grounded && s.progAt == 0)
            {
                s.stuckFrames = 0;
                if (Math.Abs(v.alignDx) > AlignTol || Math.Abs(v.hs) > 0.3f)
                {
                    s.how = HowAlign;
                    return Align(v.alignDx, v.hs);
                }
                s.how = HowRouteJump;
                StartProgram(ref s, prog, dx, dy);
                return 0;
            }

            // Сквозь платформу игра роняет по JUMP в воздухе при зажатом DOWN.
            bool dropHere = routed
                ? edge == NavMap.Drop
                : wantDown && v.platformBelow && !v.abyssBelow;
            if (dropHere && v.grounded)
            {
                s.how = routed ? HowRouteDrop : HowDropGuess;
                s.stuckFrames = 0;
                ushort drop = BotInput.DOWN;
                if (Pulse(ref s.jumpTimer, 2, 6)) drop |= BotInput.JUMP;
                if (dx > 1f) drop |= BotInput.RIGHT;
                else if (dx < -1f) drop |= BotInput.LEFT;
                return drop;
            }

            int dir = dx > 1f ? 1 : (dx < -1f ? -1 : 0);
            bool jumpEdge = routed && edge == NavMap.Jump && v.grounded;
            if (routed && edge == NavMap.Launch) jumpEdge = false;

            // Шаг ровно под ногами, а мы стоим: опора, которой нет на карте (Nubber, край тайла).
            // Сойти туда, где обрыв, но не в пропасть.
            if (routed && dir == 0 && wantDown && v.grounded && !jumpEdge)
            {
                int side = v.gapLeft == v.gapRight ? 0 : (v.gapRight ? 1 : -1);
                if (side == 0 || Abyss(ref v, side))
                {
                    if (s.wanderFrames <= 0)
                    {
                        s.wanderFrames = WanderFrames;
                        s.wanderDir = s.wanderDir == 0 ? (v.facingRight ? 1 : -1) : -s.wanderDir;
                    }
                    s.wanderFrames--;
                    if (Abyss(ref v, s.wanderDir)) s.wanderDir = -s.wanderDir;
                    side = s.wanderDir;
                }
                s.stuckFrames = 0;
                if (Abyss(ref v, side)) { s.how = HowAbyss; return 0; }
                s.how = HowStepOff;
                return side > 0 ? BotInput.RIGHT : BotInput.LEFT;
            }

            if (!routed && dir == 0 && (wantUp || wantDown))
            {
                if (s.wanderFrames <= 0)
                {
                    s.wanderFrames = WanderFrames;
                    s.wanderDir = s.wanderDir == 0 ? (v.facingRight ? 1 : -1) : -s.wanderDir;
                }
                dir = s.wanderDir;
                s.how = HowWander;
            }
            else s.wanderFrames = 0;
            if (s.wanderFrames > 0) s.wanderFrames--;

            if (!routed && Abyss(ref v, dir))
            {
                if (s.how == HowWander) { s.wanderDir = -s.wanderDir; dir = s.wanderDir; }
                if (Abyss(ref v, dir))
                {
                    s.how = HowAbyss;
                    s.stuckFrames = 0;
                    return 0;
                }
            }

            if (dir == 0 && !jumpEdge)
            {
                s.stuckFrames = 0;
                if (s.how == null) s.how = HowHere;
                return 0;
            }

            ushort m = 0;
            if (dir > 0) m |= BotInput.RIGHT;
            else if (dir < 0) m |= BotInput.LEFT;
            if (s.how == null)
                s.how = !routed ? HowDirect
                      : edge == NavMap.Launch ? (v.grounded ? HowSpring : HowSpringFlight)
                      : jumpEdge ? HowRouteJump : HowRoute;

            if (v.grounded && v.speedAbs < 0.2f) s.stuckFrames++;
            else s.stuckFrames = 0;

            // Обрыв на спуске — дорога. С маршрутом кнопки задаёт ребро: «шаг» с края — сойти вниз.
            bool gap = (dir > 0 ? v.gapRight : v.gapLeft) && !wantDown;
            bool stuck = s.stuckFrames > StuckFrames;
            bool guessJump = !routed && wantUp && v.grounded;
            if (jumpEdge)
                StartJump(ref s, JumpHold(dx, dy, gap), v.grounded, HowRouteJump, dx, dy);
            else if (!wantDown && ((gap && !routed) || stuck || guessJump))
            {
                s.how = stuck ? HowStuckJump : gap ? HowGapJump : HowGuessJump;
                StartJump(ref s, JumpHold(dx, dy, gap), v.grounded, s.how, dx, dy);
            }
            return m;
        }

        // При JumpSpeed −4.9 и gravity 0.2 вершина на 24.5 кадре: «до упора» — 28 кадров.
        private static int JumpHold(float dx, float dy, bool overGap)
        {
            if (overGap) return JumpFull;
            if (Math.Abs(dx) > FarJump) return JumpFull;
            float rise = -dy;
            if (rise > 40f) return JumpFull;
            if (rise > 24f) return JumpMid;
            if (rise > 6f) return JumpTap;
            return JumpFull;
        }

        private static ushort GoToEnemy(ref BotView v, ref BotState s)
        {
            if (v.hasEnemyStep)
                return MoveTo(v.enemyStepX, v.enemyStepY, v.enemyStepEdge, true, v.enemyStepProg, ref v, ref s);
            return MoveTo(v.ex, v.ey, NavMap.Walk, false, 0, ref v, ref s);
        }

        private static ushort GoToPickup(ref BotView v, ref BotState s)
        {
            if (v.hasPickupStep)
                return MoveTo(v.pickupStepX, v.pickupStepY, v.pickupStepEdge, true, v.pickupStepProg, ref v, ref s);
            return MoveTo(v.px, v.py, NavMap.Walk, false, 0, ref v, ref s);
        }

        // Кнопку не держат: Pressed ловит фронт, а после выстрела игра взводит _resetAction, пока SHOOT не отпустят.
        private static bool Pulse(ref int timer, int hold, int gap)
        {
            bool on = timer < hold;
            timer++;
            if (timer >= hold + gap) timer = 0;
            return on;
        }
    }
}
