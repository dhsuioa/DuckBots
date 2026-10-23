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
    public static partial class BotBrain
    {
        public const int Chase = 0, Fight = 1, GetGun = 2, Flee = 3, Drop = 4, Unstuck = 5, Netted = 6, Carry = 7, Ragdoll = 8;
        public static readonly string[] modeNames = { "CHASE", "FIGHT", "GETGUN", "FLEE", "DROP", "UNSTUCK", "NETTED", "CARRY", "RAGDOLL" };

        // Эти числа и есть настройка поведения, их крутят по ощущениям от игры.
        public const float RangeX = 150f;      // дальше бот стрелять не пробует
        public const float SameLevelY = 16f;   // на одной высоте: целятся-то горизонтально
        public const float KeepDist = 20f;     // ближе не подходим, отходим назад
        // Ракета и граната убивают уток в 30 пикселях от взрыва и сыплют осколки ещё
        // на 20 (ATMissile.DestroyRadius): ближе этого взрывчаткой не стреляем.
        public const float BlastSafe = 56f;
        public const float MeleeReach = 22f;   // с мечом, наоборот, бить можно только отсюда
        public const float BoxDist = 8f;       // стоим под ящиком — можно бить снизу
        public const float ThrowRange = 70f;   // дальше граната не долетит
        public const float DropBelow = 24f;    // цель настолько ниже — стоит проваливаться
        public const float VertStep = 16f;     // цель настолько выше — пора прыгать, а не идти
        public const int WanderFrames = 45;    // столько кадров держим выбранное направление
        public const float DangerR = 75f;      // своя же брошенная граната улетает дальше сорока
        public const float HoverReach = 90f;   // выше взмахами уже не достать
        public const float HoverAimX = 16f;    // и только если враг почти над головой
        public const int ShootHold = 3, ShootGap = 8;
        public const int GrabGap = 12;
        // Высота прыжка задаётся длиной удержания: Released(Jump) режет скорость вдвое.
        // Через яму нужен полный прыжок, на уступ хватает короткого.
        // Три длительности удержания: ступенька, этаж, полный прыжок до вершины.
        public const int JumpTap = 4, JumpMid = 14, JumpFull = 28;
        public const int JumpRest = 2;   // кадров отпущенной кнопки между прыжками
        public const int StuckFrames = 12;
        public const int StuckLimit = 240;     // сторож: четыре секунды на пятачке — залип
        public const float BoxReach = 80f;     // ящик выше этого прыжком снизу не достать
        public const float GrabR = 16f;        // зона подбора: в игре круг 18 вокруг точки на 4 ниже центра
        public const float FarJump = 48f;      // дальше этого прыжок держим до упора при любом подъёме
        public const float AlignTol = 2f;      // прыжок по программе — не дальше этого от центра клетки
        // Стекло поперёк маршрута. Зазор меряется от бока утки до бока стекла.
        public const float GlassReach = DuckPhysics.HalfWidth + 3f;   // полуширины утки и стекла
        public const float RunUp = 32f;        // отход для разбега: к SlideGap утка уже на полной 2.85
        public const float SlideGap = 16f;     // отсюда подкат долетает до стекла
        public const float ShootMin = 16f;     // ближе ствол упирается в стекло и задирается вверх
        public const float ShootFrom = 24f;    // до этого зазора отходим, чтобы стрелять
        public const int BackFrames = 24;      // дольше не отходим: сзади стена
        public const int ShootFrames = 40;     // столько стреляем, дальше — подкатом
        public const int SmashTries = 3;       // столько неудач подряд, дальше — сторож
        // Подкат от выстрела. DodgeDelay — ручка силы для этапа 3: через сколько кадров
        // бот замечает, что в него целятся. Сильнейшему — сразу: бот-стрелок стреляет в
        // первый же кадр с линией, и 27.09 пять ботов из шести погибли ещё в разгоне.
        public const int DodgeDelay = 0;
        public const int DodgeAfter = 12;      // столько ещё лежать, когда целиться перестали
        public const int DodgeMax = 90;        // дольше не лежим: в лежачего тоже целятся
        public const int DodgeRest = 30;       // столько после этого не ложиться

        // Почему бот делает то, что делает: одна строка на ветку Think (why)
        // и одна на способ хода в MoveTo (how). Журнал печатает их при смене
        // режима и в каждом кадре самописца, самопроверка сверяет с веткой.
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
        public const string WhyAmbush = "засада: до врага нет дороги, жду, пока спустится";
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
        public const string HowSmashBack = "отхожу от стекла";
        public const string HowSmashShoot = "стреляю в стекло";
        public const string HowSmashSlide = "бью стекло подкатом";
        public const string HowDodgeRun = "разгон для подката: в меня целятся";
        public const string HowDodge = "лежу в подкате: в меня целятся";

        // Чистая функция: на входе числа, на выходе маска кнопок. Никаких игровых типов.
        //
        // Прыжок — действие на несколько кадров, а не решение одного кадра. Высота
        // задаётся тем, как долго кнопку держат уже в воздухе, а в воздухе ни одно
        // условие «пора прыгать» не выполняется: земли под ногами нет, обрывов не
        // видно, застревания нет. Раньше бот поэтому отпускал кнопку на первом же
        // кадре после отрыва, игра резала скорость вдвое, и любой прыжок выходил
        // высотой в одну клетку. Теперь начатый прыжок держится здесь, поверх
        // решения ветки, до конца.
        public static ushort Decide(ref BotView v, ref BotState s)
        {
            if (s.jumpRest > 0) s.jumpRest--;
            s.how = null;
            // Подбросила пружина — начатая программа прыжка больше не про этот полёт: она
            // кончается только приземлением, а пружина под блоком ловит бота раньше земли.
            // Дальше рулит ребро полёта с пружины (его шаг Route считает прямо в воздухе).
            if (v.kicked) { s.progAt = 0; s.jumpHold = 0; s.stuckFrames = 0; }
            ushort m = Think(ref v, ref s);
            m = Dodge(m, ref v, ref s);
            if (s.jumpHold > 0)
            {
                m |= BotInput.JUMP;
                if (--s.jumpHold == 0) s.jumpRest = JumpRest;
            }
            // Программа прыжка по ребру маршрута: пока идёт, она одна решает направление
            // и прыжок — ровно так, как её прогнала карта. Сели — программа кончилась.
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

        // Подкат от выстрела, поверх решения ветки. Утка в подкате — 11 пикселей высотой
        // против 22 стоя (анимация groundSlide), и пуля стоящего стрелка проходит над ней;
        // пригнуться на месте мало: присед — 16. Подкат начинается только на бегу,
        // |hs| > 1, а пригнувшись утка уже не разгоняется. Начатый подкат держится, пока
        // зажат DOWN: утка лежит, даже остановившись. Отсюда программа: в бота целятся —
        // разогнаться, если стоит, туда, куда он и так шёл, и нажать DOWN; лежать, пока
        // целятся, и ещё DodgeAfter кадров после. Дольше DodgeMax не лежать: в лежачего
        // попадает такой же лежачий, а бот без ствола пролежал бы весь раунд.
        private static ushort Dodge(ushort m, ref BotView v, ref BotState s)
        {
            s.threatFrames = v.threatened ? s.threatFrames + 1 : 0;
            if (s.dodgeRest > 0) s.dodgeRest--;
            bool aimed = s.threatFrames > DodgeDelay;
            // Только с земли; от взрывчатки бегут, а не ложатся.
            if (!v.grounded || s.mode == Flee || v.netted || v.ragdoll)
            {
                EndDodge(ref s);
                return m;
            }
            if (s.dodgeFrames == 0)
            {
                if (!aimed || s.dodgeRest > 0) return m;
                s.dodgeDir = 0;
            }
            s.dodgeFrames++;
            if (aimed) s.dodgeLeft = DodgeAfter;
            else if (--s.dodgeLeft <= 0) { EndDodge(ref s); return m; }
            if (s.dodgeFrames > DodgeMax) { EndDodge(ref s); s.dodgeRest = DodgeRest; return m; }

            // Стоим: разгон туда, куда бот уже катится, — разворот съедает 5–6 кадров, и
            // 28.09 боты гибли, тормозя перед подкатом; иначе туда, куда он и так шёл,
            // иначе к стрелку — не прочь от цели.
            bool down = v.sliding || Math.Abs(v.hs) > 1f;
            if (!down && s.dodgeDir == 0)
            {
                int dir = v.hs > 0.3f ? 1 : v.hs < -0.3f ? -1
                    : (m & BotInput.RIGHT) != 0 ? 1 : (m & BotInput.LEFT) != 0 ? -1 : (v.threatDx > 0f ? 1 : -1);
                if (Abyss(ref v, dir)) dir = -dir;
                if (Abyss(ref v, dir)) { EndDodge(ref s); s.dodgeRest = DodgeRest; return m; }   // разгоняться некуда
                s.dodgeDir = dir;
            }
            // Уклоняемся — не прыгаем: прыжок, начатый веткой в этом кадре, отменяется. И
            // лежачий бот не застрял, он не двигается нарочно: иначе MoveTo через дюжину
            // кадров командовал «упёрся: прыжок» и поднимал его под пулю.
            s.jumpHold = 0;
            s.progAt = 0;
            s.stuckFrames = 0;
            // Уже лежим или бежим достаточно быстро — DOWN. Направление оставляем ветке:
            // пригнувшись, утка не разгоняется, но поворачивается к врагу, чтобы стрелять.
            if (down)
            {
                s.how = HowDodge;
                return (ushort)((m & unchecked((ushort)~BotInput.JUMP)) | BotInput.DOWN);
            }
            s.how = HowDodgeRun;
            return (ushort)((m & unchecked((ushort)~(BotInput.LEFT | BotInput.RIGHT | BotInput.JUMP | BotInput.DOWN)))
                | (s.dodgeDir > 0 ? BotInput.RIGHT : BotInput.LEFT));
        }

        private static void EndDodge(ref BotState s)
        {
            s.dodgeFrames = 0;
            s.dodgeDir = 0;
            s.dodgeLeft = 0;
        }

        // Начать программу прыжка: кнопка прыжка перед этим должна побыть отпущенной,
        // иначе Pressed не поймает фронт.
        private static void StartProgram(ref BotState s, int prog, float dx, float dy)
        {
            if (s.jumpHold > 0 || s.jumpRest > 0 || s.progAt > 0) return;
            s.prog = prog;
            s.progAt = 1;
            s.jumpBy = HowRouteJump;
            s.jumpDX = dx;
            s.jumpDY = dy;
        }

        // Встать в центр клетки и остановиться: желаемая скорость — по расстоянию до
        // центра; жмём к ней, трение (0.25) и встречное нажатие (ещё 0.55) гасят лишнее.
        private static ushort Align(float cx, float hs)
        {
            float want = cx * 0.3f;
            if (want > 2.85f) want = 2.85f;
            if (want < -2.85f) want = -2.85f;
            if (hs < want - 0.25f) return BotInput.RIGHT;
            if (hs > want + 0.25f) return BotInput.LEFT;
            return 0;
        }

        // Начать прыжок можно только с земли, не посреди другого прыжка и после
        // хотя бы кадра с отпущенной кнопкой: Pressed ловит фронт, зажатая кнопка
        // второй раз не сработает. Кто начал прыжок и куда целился, запоминается:
        // журнал промахов считает только прыжки, начатые ребром маршрута.
        private static void StartJump(ref BotState s, int hold, bool grounded, string by, float dx, float dy)
        {
            if (!grounded || s.jumpHold > 0 || s.jumpRest > 0) return;
            s.jumpHold = hold;
            s.jumpBy = by;
            s.jumpDX = dx;
            s.jumpDY = dy;
        }

        // Подбор в игре — круг радиусом 18 вокруг точки на 4 пикселя ниже центра утки
        // (Duck.TryGrab). Ствол на полу под ногами лежит на 12 пикселей ниже центра,
        // и прежняя проверка «ближе 12 по обеим осям» его отсекала: боты стояли на нём.
        private static bool InGrabReach(float px, float py)
        {
            float dy = py - 4f;
            return px * px + dy * dy < GrabR * GrabR;
        }

        private static bool Abyss(ref BotView v, int dir)
        {
            return dir > 0 ? v.abyssRight : dir < 0 && v.abyssLeft;
        }

        // Сторож залипания, чистая часть: бот идёт к цели, а с места не сдвинулся —
        // считаем кадры, и на StuckLimit пора уходить в сторону. Обездвиженная утка
        // не застряла: это заставка раунда или утрамбовка мушкета. В логе 24.09
        // 145 срабатываний из 170 были заставкой — все боты разом через 4 с после
        // старта, а уход в сторону без маршрута уводил их в пропасть.
        public static bool Watchdog(ref int frames, bool going, bool moved, bool frozen)
        {
            if (!going || moved || frozen) { frames = 0; return false; }
            if (++frames < StuckLimit) return false;
            frames = 0;
            return true;
        }

        private static ushort Think(ref BotView v, ref BotState s)
        {
            // 0. Бот в рэгдолле. Сама утка не встаёт никогда: игра поднимает её по новому
            // нажатию JUMP, когда тело касается земли (Ragdoll.UpdateInput). Раньше боты
            // лежали, пока через 4 с прыжок не жал сторож, а мозг тем временем шёл к врагу
            // от объекта утки, унесённого на 5000 пикселей вверх. Поэтому ветка первая и
            // на остальной обзор не смотрит. Жать импульсами — Pressed ловит фронт.
            if (v.ragdoll)
            {
                s.mode = Ragdoll;
                s.why = WhyRagdoll;
                s.jumpHold = 0;
                s.progAt = 0;
                return Pulse(ref s.jumpTimer, 1, 1) ? BotInput.JUMP : (ushort)0;
            }

            // Бот в сетке. Пойманная утка — предмет, который враг может поднять и
            // сбросить в пропасть; вырывается она прыжками: каждое нажатие срезает время
            // сетки, на земле вчетверо сильнее. Жать импульсами — Pressed ловит фронт,
            // поэтому начатый прыжок сбрасывается, иначе он слился бы в одно нажатие.
            if (v.netted)
            {
                s.mode = Netted;
                s.why = WhyNetted;
                s.jumpHold = 0;
                s.progAt = 0;
                return Pulse(ref s.jumpTimer, 1, 1) ? BotInput.JUMP : (ushort)0;
            }

            // 1. Чека выдернута — это уже не оружие в руках, а таймер. Бросать немедленно,
            // развернувшись к врагу: GRAB кидает туда, куда смотрит утка.
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

            // 2. Убежать от чужой взрывчатки.
            if (v.danger)
            {
                s.mode = Flee;
                s.why = WhyFlee;
                return MoveTo(v.dx > 0f ? -100f : 100f, 0f, NavMap.Walk, false, 0, ref v, ref s);
            }

            // 3. Граната или мина в руках: взвести можно только вплотную, иначе не долетит.
            if (v.explosiveInHand)
            {
                if (v.hasEnemy && v.canShoot && Math.Abs(v.ex) < ThrowRange)
                {
                    s.mode = Fight;
                    s.why = WhyArm;
                    bool toRight = v.ex > 0f;
                    if (toRight != v.facingRight) return toRight ? BotInput.RIGHT : BotInput.LEFT;
                    // Взвели — на следующем кадре сработает ветка 1 и бросит.
                    return Pulse(ref s.shootTimer, 1, ShootGap) ? BotInput.SHOOT : (ushort)0;
                }
                s.mode = Chase;
                s.why = v.hasEnemy ? WhyCarry : WhyNoEnemy;
                if (!v.hasEnemy) return 0;
                return GoToEnemy(ref v, ref s);
            }

            // 3.5. Несём пойманного врага — к пропасти и бросить туда: упавший за край
            // в сетке гибнет (DTFall), и убийство засчитывается поймавшему. Бросок летит
            // туда, куда смотрит утка, а с зажатым направлением он в 2.5 раза сильнее
            // (Duck.ThrowItem). На краю — развернуться лицом к пропасти и бросить,
            // держа направление ровно в кадр броска: иначе утка шагнёт с края сама.
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

            // 4. Враг прямо над головой. Горизонтально его не достать — угол прицела
            // в игре не выбирается, — но в воздухе с зажатым прыжком утка машет
            // крыльями, и на взмахах ствол поднимается вверх сам. Этим и стреляем.
            // Огнемётом вверх не стреляют: огонь падает обратно на стрелка.
            if (v.holding == 1 && v.canShootUp && !(v.blastInHand && -v.ey < BlastSafe) && !v.holdFire)
            {
                s.mode = Fight;
                s.why = WhyShootUp;
                // Взмахи — это новое нажатие прыжка в воздухе: подпрыгнуть коротко,
                // отпустить и зажать снова. Зажатый с земли прыжок взмахов не даёт —
                // утка просто прыгает, и ствол смотрит вбок; так бот и делал раньше.
                ushort up = 0;
                if (v.grounded) StartJump(ref s, 3, true, WhyShootUp, 0f, 0f);
                else if (s.jumpHold == 0 && s.jumpRest == 0) up |= BotInput.JUMP;
                if (Pulse(ref s.shootTimer, ShootHold, ShootGap)) up |= BotInput.SHOOT;
                return up;
            }

            // 5. Драться: есть чем и есть линия огня.
            if (v.holding == 1 && v.canShoot)
            {
                s.mode = Fight;
                bool targetRight = v.ex > 0f;
                // Ствол смотрит по offDir, поднять его в игре нечем — значит сначала разворот.
                if (targetRight != v.facingRight)
                {
                    s.why = WhyTurn;
                    return targetRight ? BotInput.RIGHT : BotInput.LEFT;
                }

                // Меч, пила, кувалда — технически Gun с патронами, но бьют вплотную.
                // Издалека бот раньше просто махал в воздух: линия огня-то есть.
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

                // Базука, гранатомёт, ракетница рвутся там, куда попали, и ближе BlastSafe
                // взрыв достаёт самого стрелка: вплотную не стрелять, а отходить. STRAFE
                // держит утку лицом к врагу, чтобы выстрелить сразу, как отошла. За спиной
                // пропасть — стоять.
                if (v.blastInHand && Math.Abs(v.ex) < BlastSafe)
                {
                    s.why = WhyBlastBack;
                    if (Abyss(ref v, targetRight ? -1 : 1)) return 0;
                    return (ushort)(BotInput.STRAFE | (targetRight ? BotInput.LEFT : BotInput.RIGHT));
                }

                // Сквозь стекло вплотную не стрелять: ствол упирается в него и задирается
                // вверх (ShootMin, как у удара по стеклу). Отойти лицом к врагу.
                if (v.glassLine && v.hasGlass && (v.glassDx > 0f) == targetRight
                    && Math.Abs(v.glassDx) - GlassReach < ShootMin && !Abyss(ref v, targetRight ? -1 : 1))
                {
                    s.why = WhyGlassBack;
                    return (ushort)(BotInput.STRAFE | (targetRight ? BotInput.LEFT : BotInput.RIGHT));
                }

                ushort m = 0;
                s.why = WhyShoot;
                // Отступать можно, только если за спиной не пропасть. STRAFE держит утку
                // лицом к врагу: без него шаг назад разворачивал её спиной, следующий кадр —
                // обратно, и бот не отходил и не стрелял (в журналах 22.10 так 60–75% кадров
                // отступления).
                if (Math.Abs(v.ex) < KeepDist && !Abyss(ref v, targetRight ? -1 : 1))
                {
                    m |= (ushort)(BotInput.STRAFE | (targetRight ? BotInput.LEFT : BotInput.RIGHT));
                    s.why = WhyBackOff;
                }
                // Гранатомёт стреляет на отпускании SHOOT, а пока его держат, ствол
                // поднимается (GrenadeLauncher.Update): нажать, держать до угла дуги до
                // врага и отпустить. После выстрела ствол остывает и нажатия не слышит, а
                // нажатие ловится по фронту — поэтому до подъёма ствола жать импульсами.
                if (v.lobber)
                {
                    s.why = WhyLob;
                    if (v.lobCooling) return m;
                    if (!v.lobAiming) return Pulse(ref s.shootTimer, 1, 1) ? (ushort)(m | BotInput.SHOOT) : m;
                    return v.lobAim >= v.lobAngle ? m : (ushort)(m | BotInput.SHOOT);
                }

                // Огнемёт выбрасывает огонь, только пока SHOOT зажат, и начинает с 5-го
                // кадра (FlameThrower.Update): импульсы по ShootHold его не зажигали.
                if (v.holdFire || Pulse(ref s.shootTimer, ShootHold, ShootGap)) m |= BotInput.SHOOT;
                return m;
            }

            // 5.5. Враг в сетке лежит — поднять его и нести к пропасти. Руки должны быть
            // свободны: GRAB с чем-то в руках бросает это, а не поднимает.
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

            // 5.6. В руках сеткомёт, а достижим смертельный ствол — сеткомёт бросить,
            // дальше сработает обычный подбор. Другой цели подбора у сеткомёта нет.
            if (v.netGun && v.hasPickup)
            {
                s.mode = Drop;
                s.why = WhyDropNet;
                return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
            }

            // Сторож заметил, что бот топчется на пятачке, идя к цели: маршрут ведёт
            // туда, куда на деле не попасть, и бот повторяет одно и то же движение.
            // Уйти в сторону — разворотом, от того, во что упирался. Цель при этом
            // забыта снаружи, и после выхода маршрут будет другим.
            if (v.breakout)
            {
                s.mode = Unstuck;
                s.why = WhyBreakout;
                if (s.breakDir == 0) s.breakDir = v.facingRight ? -1 : 1;
                // Уходя в сторону, к пропасти не идти — развернуться от края.
                if (Abyss(ref v, s.breakDir)) s.breakDir = -s.breakDir;
                return MoveTo(s.breakDir * 100f, 0f, NavMap.Walk, false, 0, ref v, ref s);
            }
            s.breakDir = 0;

            // 6. Подарок в руках не выбрасывают, а открывают: SHOOT, внутри случайный предмет.
            if (v.presentInHand)
            {
                s.mode = GetGun;
                s.why = WhyPresent;
                return Pulse(ref s.shootTimer, 1, ShootGap) ? BotInput.SHOOT : (ushort)0;
            }

            // 7. Руки заняты не тем: GRAB при занятых руках выбрасывает предмет.
            if (v.holding == 2 || v.holding == 3)
            {
                s.mode = Drop;
                s.why = WhyJunk;
                return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
            }

            // 8. Взять оружие: ствол с земли, а если свободных стволов нет — вскрыть ящик.
            if (v.holding != 1 && v.hasPickup)
            {
                s.mode = GetGun;
                s.why = v.pickupIsBox ? WhyBox : WhyGun;
                ushort m = GoToPickup(ref v, ref s);
                if (v.pickupIsBox)
                {
                    // Ящик вскрывается ударом снизу — ровно как в ванильном AIStateFindGun.
                    // Только если он над головой и прыжок до него достаёт: раньше бот,
                    // стоя на крыше ящика, прыгал на месте до срабатывания сторожа.
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

            // 9. Иначе сближаться.
            s.mode = Chase;
            if (!v.hasEnemy) { s.why = WhyNoEnemy; return 0; }
            // Утки стоят друг на друге: враг почти ровно сверху или снизу, ближе
            // полутора клеток. Выстрела тут нет ни вбок, ни вверх, маршрута тоже нет —
            // это одна клетка. Разойтись в сторону, дальше сработает бой.
            if (Math.Abs(v.ex) < HoverAimX && Math.Abs(v.ey) < DropBelow)
            {
                s.why = WhyStack;
                return Sidestep(ref v, ref s);
            }
            // Со стволом бот идёт не к врагу, а на позицию для выстрела: та же высота,
            // дистанция, чистая линия (NavMap.FiringNode). Дошёл, а линии пока нет —
            // развернуться к врагу и ждать, а не лезть к нему: раньше все шли в точку
            // врага и сходились там, где стрелять уже поздно.
            // В засаде — так же, но лицом к месту, где враг спустится: сам он над головой,
            // и раньше бот без маршрута шёл под него и прыгал в полку. Место спуска под
            // ногами (ближе полуклетки) — поворачиваться не к чему.
            if ((v.holding == 1 || v.ambush) && v.atSpot)
            {
                s.why = v.ambush ? WhyAmbush : WhyAim;
                float side = v.ambush ? v.ambushDx : v.ex;
                bool right = side > 0f;
                if (right != v.facingRight && !(v.ambush && Math.Abs(side) < 8f))
                    return right ? BotInput.RIGHT : BotInput.LEFT;
                s.how = HowHere;
                return 0;
            }
            s.why = WhyChase;
            return GoToEnemy(ref v, ref s);
        }

        // Шаг в сторону пачкой кадров: направление держится, пока не истечёт,
        // иначе бот дрожит на месте и никуда не уходит.
        private static ushort Sidestep(ref BotView v, ref BotState s)
        {
            if (s.wanderFrames <= 0)
            {
                s.wanderFrames = WanderFrames;
                s.wanderDir = s.wanderDir == 0 ? (v.facingRight ? 1 : -1) : -s.wanderDir;
            }
            s.wanderFrames--;
            // Расходясь, в пропасть не шагать: разворот, а если она с обеих сторон — стоять.
            if (Abyss(ref v, s.wanderDir)) s.wanderDir = -s.wanderDir;
            if (Abyss(ref v, s.wanderDir)) { s.how = HowAbyss; return 0; }
            s.how = HowSidestep;
            return s.wanderDir > 0 ? BotInput.RIGHT : BotInput.LEFT;
        }

        // Стекло поперёк маршрута. Шагом его не разбить: пуля бьёт стекло всегда, а
        // телом — только подкат с разгона (человек 25.09 разбил так восемь стёкол на
        // скорости 2.2–3.0). Это программа на несколько кадров, как прыжок: отойти,
        // потом выстрелить или разогнаться и подкатиться. Держится она на целом
        // стекле, а не на шаге «стекло»: маршрут пересчитывается раз в десять кадров
        // от той клетки, куда бот отошёл, и шаг сам меняется на «к стеклу».
        private static bool Smash(float dx, int edge, bool routed, ref BotView v, ref BotState s, out ushort m)
        {
            m = 0;
            int dir = dx > 1f ? 1 : (dx < -1f ? -1 : 0);
            // Начатая программа держит своё направление. На разбеге шаг маршрута ведёт
            // к центру следующей клетки, и бот то встаёт в него (dx = 0), то проскакивает:
            // 22.10 (springs, f28222) программа на этом обрывалась за пару кадров до
            // подката, и за восемь матчей боты разбили одно стекло.
            if (s.smashDir != 0) dir = s.smashDir;
            bool ahead = routed && dir != 0 && v.hasGlass && (v.glassDx > 0f) == (dir > 0);
            if (!ahead)
            {
                // Разбито или маршрут повернул: программа кончилась.
                s.smashDir = 0; s.smashRun = false; s.smashFrames = 0; s.smashTries = 0;
                return false;
            }
            if (s.smashTries >= SmashTries) return false;        // не выходит — дальше сторож
            if (s.smashDir != dir)
            {
                if (edge != NavMap.Smash) return false;          // начинает только шаг «стекло»
                s.smashDir = dir; s.smashRun = false; s.smashFrames = 0;
            }
            s.smashFrames++;
            s.stuckFrames = 0;
            float gap = Math.Abs(v.glassDx) - GlassReach;
            ushort toward = dir > 0 ? BotInput.RIGHT : BotInput.LEFT;
            ushort away = dir > 0 ? BotInput.LEFT : BotInput.RIGHT;
            bool room = dir > 0 ? !v.gapLeft : !v.gapRight;     // сзади есть куда отойти
            bool shoot = v.glassGun && s.smashTries == 0;        // первая попытка — пулей
            if (!s.smashRun)
            {
                if (gap < (shoot ? ShootFrom : RunUp) && room && s.smashFrames < BackFrames)
                {
                    s.how = HowSmashBack;
                    m = away;
                    return true;
                }
                s.smashRun = true;
                s.smashFrames = 0;
            }
            if (shoot)
            {
                // Вплотную ствол задирается вверх, а не вышло за ShootFrames — пуля не
                // берёт или кончилась: дальше подкатом.
                if (gap < ShootMin || s.smashFrames > ShootFrames)
                {
                    s.smashTries++; s.smashRun = false; s.smashFrames = 0;
                    s.how = HowSmashBack;
                    return true;
                }
                s.how = HowSmashShoot;
                if (v.facingRight != (dir > 0)) { m = toward; return true; }
                if (Pulse(ref s.shootTimer, ShootHold, ShootGap)) m = BotInput.SHOOT;
                return true;
            }
            s.how = HowSmashSlide;
            float speed = v.hs * dir;
            if (s.smashFrames > 20 && gap < SlideGap && speed < 0.5f)
            {
                // Подкатился, а стекло цело: ещё раз с разбега.
                s.smashTries++; s.smashRun = false; s.smashFrames = 0;
                return true;
            }
            // Порог 1 — порог подката в самой игре: начавшись на полной скорости, подкат
            // гаснет медленно, и выше порог отпускал бы DOWN посреди него.
            m = toward;
            if (gap <= SlideGap && speed > 1f) m |= BotInput.DOWN;
            return true;
        }

        // Идти к цели. Когда есть маршрут, он прямо говорит, что делать на этом шаге:
        // пройти, прыгнуть или провалиться сквозь платформу. Когда маршрута нет
        // (граф не построен или цель недостижима), работают прежние догадки —
        // прыжок при застревании и блуждание вдоль платформы.
        private static ushort MoveTo(float dx, float dy, int edge, bool routed, int prog,
                                     ref BotView v, ref BotState s)
        {
            ushort smash;
            if (Smash(dx, edge, routed, ref v, ref s, out smash)) return smash;

            bool wantUp = dy < -VertStep;
            bool wantDown = dy > DropBelow;

            // Прыжок по программе: встать в центр клетки, погасить скорость и проиграть
            // программу дословно — так, как её прогнала карта. Раньше бот прыгал с ходу
            // и откуда придётся, а удержание выбирал по высоте: на space12_8 он
            // оторвался, ещё разворачиваясь (hs −1.0), держал 14 кадров вместо 28
            // и не долетел до края блока.
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

            // Провалиться сквозь одностороннюю платформу: игра роняет только если
            // нажать JUMP в воздухе, не отпуская DOWN. Без маршрута — только если
            // под платформой есть на что упасть: иначе это шаг в пропасть.
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
            // Полёт пружиной: дойти до неё ногами и не прыгать — дальше сработает она.
            if (routed && edge == NavMap.Launch) jumpEdge = false;

            // Шаг маршрута ровно под ногами, а мы стоим: под нами то, чего карта не видит, —
            // выступ Nubber или край тайла. Карта считает, что тут пусто и бот вот-вот
            // упадёт к узлу внизу. 20.10 на forest04_8 бот так простоял на выступе 30 с.
            // Сойти с этой опоры туда, где под ногами обрыв; нет такой стороны или там
            // пропасть — пачками кадров в обе стороны, но в пропасть не шагать.
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

            // Маршрута нет, по горизонтали пришли, а по вертикали нет: ходим вдоль
            // платформы пачками кадров и ищем подъём. Стоять столбом нельзя — ровно
            // на этом бот и залипал по двадцать пять секунд.
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

            // Без маршрута к пропасти не ходить: там, где под обрывом до низа карты
            // нет ни пола, ни платформы, шаг — это смерть. Бродя — развернуться,
            // иначе стоять у края. Маршрут знает, куда прыгает, догадка — нет.
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

            // Когда спускаемся, обрыв впереди — это дорога, а не препятствие.
            // Прыгать через обрыв наугад можно только без маршрута: с маршрутом
            // кнопки задаёт ребро, и ребро «шаг» с края значит сойти вниз, а не
            // перелететь цель полным прыжком.
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

        // Высота прыжка — это ровно длина удержания кнопки: Released(Jump) домножает
        // оставшуюся скорость вверх на 0.5. При JumpSpeed = -4.9 и gravity = 0.2
        // вершина на 24.5 кадрах, то есть «держать до упора» — это 28 кадров, а не 14,
        // как было раньше: на четырнадцатом утка набирает 52 пикселя вместо шестидесяти
        // и до третьего этажа не дотягивает.
        private static int JumpHold(float dx, float dy, bool overGap)
        {
            if (overGap) return JumpFull;        // через яму важна дальность, а её даёт время в воздухе
            // Далёкий прыжок — тоже до упора, при любом подъёме: дальность рёбер граф
            // считает по полному прыжку, а средний летит короче. На space12_8 так
            // четыре бота подряд не долетели до края блока и сползли в пропасть.
            if (Math.Abs(dx) > FarJump) return JumpFull;
            float rise = -dy;
            if (rise > 40f) return JumpFull;     // три клетки
            if (rise > 24f) return JumpMid;      // две
            if (rise > 6f) return JumpTap;       // ступенька
            return JumpFull;                     // ровно или вниз: значит летим далеко
        }

        // Маршрут знает дорогу вокруг ям и через этажи, прямое направление — нет.
        // Поэтому идём по маршруту, когда он есть, и только иначе ломимся напрямик.
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

        // Кнопку нельзя держать: Pressed ловит фронт, а после выстрела игра взводит
        // _resetAction, пока SHOOT не отпустят. Поэтому всё нажимается импульсами.
        private static bool Pulse(ref int timer, int hold, int gap)
        {
            bool on = timer < hold;
            timer++;
            if (timer >= hold + gap) timer = 0;
            return on;
        }
    }
}
