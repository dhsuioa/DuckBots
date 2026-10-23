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
        // Карта картинкой: так её видно глазами прямо в самопроверке.
        private static byte[] Cells(string[] rows)
        {
            int w = rows[0].Length;
            byte[] c = new byte[w * rows.Length];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = rows[y][x];
                    byte cell = NavMap.Empty;
                    if (ch == '#' || ch == '|') cell = NavMap.Solid;   // стекло для физики — стена
                    else if (ch == 'S') cell = NavMap.SpringUp;
                    else if (ch == '/') cell = NavMap.SpringDiagRight;
                    else if (ch == '\\') cell = NavMap.SpringDiagLeft;
                    else if (ch == '=') cell = NavMap.Platform;
                    else if (ch == '^') cell = NavMap.Deadly;
                    c[y * w + x] = cell;
                }
            return c;
        }

        // Мозг — чистая функция, поэтому проверяется без игры. Гоняется при загрузке мода,
        // но её же можно позвать снаружи: csc собирает консольную обвязку из двух строк
        // со ссылкой на DuckBots.dll. Возвращает null, если всё хорошо, иначе — что упало.
        public static string SelfTest()
        {
            BotView v;
            BotState s;

            // Враг справа, заряженный ствол, линия огня: стреляем импульсами, а не держим.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 60f; v.canShoot = true; v.holding = 1;
            v.facingRight = true; v.grounded = true;
            bool shot = false, released = false;
            for (int i = 0; i < 40; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.SHOOT) != 0) shot = true; else released = true;
            }
            if (!shot) return "бой: SHOOT не нажимается";
            if (!released) return "бой: SHOOT висит зажатым, полуавтомат выстрелит один раз";
            // Причина решения уходит в журнал, поэтому она обязана совпадать с веткой.
            if (s.why != WhyShoot) return "причина: бой записан как «" + s.why + "»";

            // Тот же враг, но смотрим не туда: сначала развернуться, стрелять потом.
            v.facingRight = false; s = new BotState();
            ushort turn = Decide(ref v, ref s);
            if ((turn & BotInput.RIGHT) == 0) return "разворот: не поворачивается к врагу";
            if ((turn & BotInput.SHOOT) != 0) return "разворот: стреляет, не развернувшись";
            if (s.why != WhyTurn) return "причина: разворот записан как «" + s.why + "»";

            // Чужая взрывчатка справа: уходить влево и не стрелять.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 60f; v.canShoot = true; v.holding = 1; v.facingRight = true;
            v.grounded = true; v.danger = true; v.dx = 20f;
            ushort flee = Decide(ref v, ref s);
            if ((flee & BotInput.LEFT) == 0) return "опасность: не убегает от взрывчатки";
            if ((flee & BotInput.SHOOT) != 0) return "опасность: стреляет вместо того, чтобы убегать";
            if (s.why != WhyFlee) return "причина: бегство записано как «" + s.why + "»";

            // Граната в руках, враг близко: сначала чека, потом бросок.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 40f; v.canShoot = true; v.holding = 1; v.facingRight = true;
            v.grounded = true; v.explosiveInHand = true;
            if ((Decide(ref v, ref s) & BotInput.SHOOT) == 0) return "граната: не выдёргивает чеку";
            if (s.why != WhyArm) return "причина: чека записана как «" + s.why + "»";
            v.armedInHand = true; s = new BotState();
            if ((Decide(ref v, ref s) & BotInput.GRAB) == 0) return "граната: взведённую не бросает";
            if (s.why != WhyThrow) return "причина: бросок записан как «" + s.why + "»";

            // Взведённая граната в руках важнее всего: бросать даже когда рядом рвануло.
            v.danger = true; v.dx = 20f; s = new BotState();
            if ((Decide(ref v, ref s) & BotInput.GRAB) == 0) return "граната: держит взведённую, убегая";

            // Граната есть, но враг далеко: чеку не трогать, идти к нему.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            v.explosiveInHand = true; v.facingRight = true;
            ushort far = Decide(ref v, ref s);
            if ((far & BotInput.SHOOT) != 0) return "граната: взводит её на другом конце арены";
            if ((far & BotInput.RIGHT) == 0) return "граната: не идёт к врагу";

            // Пустые руки, ствол слева: идти к нему, вплотную — подобрать.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 80f; v.grounded = true; v.speedAbs = 1f;
            v.hasPickup = true; v.px = -40f;
            if ((Decide(ref v, ref s) & BotInput.LEFT) == 0) return "оружие: не идёт к стволу";
            if (s.why != WhyGun || s.how != HowDirect)
                return "причина: путь к стволу записан как «" + s.why + "; " + s.how + "»";
            v.px = -4f; s = new BotState();
            bool grabbed = false;
            for (int i = 0; i < GrabGap + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) grabbed = true;
            if (!grabbed) return "оружие: не подбирает ствол под ногами";

            // Негодное в руках (пустой ствол, саксофон, ящик) — выбросить.
            v = new BotView(); s = new BotState();
            v.holding = 2; v.grounded = true;
            bool dropped = false;
            for (int i = 0; i < GrabGap + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) dropped = true;
            if (!dropped) return "негодное оружие: не выбрасывается";
            if (s.why != WhyJunk) return "причина: выброс записан как «" + s.why + "»";

            // Ствол есть, линии огня нет, подбирать нечего — сближаться.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 80f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            if ((Decide(ref v, ref s) & BotInput.RIGHT) == 0) return "сближение: стоит на месте";

            // Упёрлись в стену: должен прыгнуть, а не тереться о неё бесконечно.
            v.speedAbs = 0f; s = new BotState();
            bool jumped = false;
            for (int i = 0; i < StuckFrames + JumpFull + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) jumped = true;
            if (!jumped) return "застревание: не прыгает, упёршись в стену";
            // Журнал промахов считает только прыжки маршрута: этот — от застревания.
            if (s.jumpBy != HowStuckJump) return "причина: прыжок от застревания записан как «" + s.jumpBy + "»";

            // Впереди яма: прыгать сразу, не дожидаясь, пока упрёмся, — упираться не во что.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 80f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            v.gapRight = true;
            ushort over = Decide(ref v, ref s);
            if ((over & BotInput.JUMP) == 0) return "яма: идёт в обрыв, не прыгая";
            if ((over & BotInput.RIGHT) == 0) return "яма: прыгает, но не вперёд";
            if (s.jumpBy != HowGapJump) return "причина: прыжок через яму записан как «" + s.jumpBy + "»";

            // Цель ниже, под ногами платформа: провалиться сквозь неё, а не искать обход.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 10f; v.ey = 60f; v.holding = 1; v.grounded = true;
            v.platformBelow = true;
            bool down = false, downJump = false;
            for (int i = 0; i < 10; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.DOWN) != 0) down = true;
                if ((m & BotInput.DOWN) != 0 && (m & BotInput.JUMP) != 0) downJump = true;
            }
            if (!down) return "спуск: не нажимает вниз на платформе";
            if (!downJump) return "спуск: не добавляет прыжок, а без него игра не роняет";
            // А если под платформой пропасть — без маршрута не проваливаться:
            // это тот же шаг с обрыва, только вниз.
            v.abyssBelow = true; s = new BotState();
            for (int i = 0; i < 10; i++)
                if ((Decide(ref v, ref s) & (BotInput.DOWN | BotInput.JUMP)) == (BotInput.DOWN | BotInput.JUMP))
                    return "спуск: проваливается сквозь платформу в пропасть";

            // Враг над головой: стрелять вверх можно только на взмахах, а взмахи в игре —
            // это новое нажатие прыжка в воздухе (Duck.UpdateMove: _hovering ставит только
            // Pressed после отрыва). Прежняя проверка требовала держать прыжок с земли —
            // она была подогнана под неверное понимание: так утка просто прыгает.
            // Теперь: подпрыгнуть, отпустить, зажать снова и держать, стреляя.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 4f; v.ey = -40f; v.holding = 1; v.canShootUp = true;
            v.facingRight = true; v.grounded = true;
            bool jumpedOff = false, releasedUp = false, shotUp = false;
            int hoverHeld = 0;
            for (int i = 0; i < 30; i++)
            {
                ushort m = Decide(ref v, ref s);
                bool j = (m & BotInput.JUMP) != 0;
                if (j && !jumpedOff) jumpedOff = true;
                else if (jumpedOff && !j) releasedUp = true;
                else if (releasedUp && j) hoverHeld++;
                if ((m & BotInput.SHOOT) != 0) shotUp = true;
                if (jumpedOff) v.grounded = false;   // оторвалась
            }
            if (!jumpedOff || !releasedUp) return "вверх: не подпрыгивает и не отпускает — взмахов не будет";
            if (hoverHeld < 10) return "вверх: взмахи не держит, кадров " + hoverHeld;
            if (!shotUp) return "вверх: машет крыльями, но не стреляет";

            // Цель строго над головой. Раньше тут был return 0, и бот стоял столбом
            // по двадцать пять секунд — это видно в логе матча. Теперь должен и идти,
            // и прыгать, разыскивая подъём.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 0f; v.ey = -60f; v.holding = 1; v.grounded = true;
            bool moved = false, hopped = false;
            for (int i = 0; i < 30; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & (BotInput.LEFT | BotInput.RIGHT)) != 0) moved = true;
                if ((m & BotInput.JUMP) != 0) hopped = true;
            }
            if (!moved) return "вертикаль: цель над головой, а бот стоит столбом";
            if (!hopped) return "вертикаль: не прыгает к цели наверху";

            // За полторы секунды направление должно смениться: иначе бот уходит в стену
            // навсегда, вместо того чтобы поискать подъём с другой стороны.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 0f; v.ey = -60f; v.holding = 1; v.grounded = true;
            bool sawLeft = false, sawRight = false;
            for (int i = 0; i < WanderFrames * 3; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.LEFT) != 0) sawLeft = true;
                if ((m & BotInput.RIGHT) != 0) sawRight = true;
            }
            if (!sawLeft || !sawRight) return "вертикаль: ходит в одну сторону и не разворачивается";

            // Цель ниже, платформы под ногами нет: идти к краю, а обрыв по дороге —
            // это дорога вниз, а не препятствие, прыгать через него не надо.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 0f; v.ey = 70f; v.holding = 1; v.grounded = true;
            v.gapLeft = true; v.gapRight = true;
            bool steppedOff = false, jumpedOver = false;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & (BotInput.LEFT | BotInput.RIGHT)) != 0) steppedOff = true;
                if ((m & BotInput.JUMP) != 0) jumpedOver = true;
            }
            if (!steppedOff) return "вниз: не идёт искать край";
            if (jumpedOver) return "вниз: перепрыгивает обрыв, хотя спускаться надо именно там";

            // Подарок в руках: открыть, а не выбросить.
            v = new BotView(); s = new BotState();
            v.holding = 3; v.presentInHand = true; v.grounded = true;
            bool opened = false, thrown = false;
            for (int i = 0; i < ShootGap + 2; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.SHOOT) != 0) opened = true;
                if ((m & BotInput.GRAB) != 0) thrown = true;
            }
            if (!opened) return "подарок: не открывает";
            if (thrown) return "подарок: выбрасывает вместо того, чтобы открыть";
            if (s.why != WhyPresent) return "причина: подарок записан как «" + s.why + "»";

            // --- карта проходимости ---
            // Выдуманная арена: пол с ямой в две клетки, полка на три клетки выше
            // и недосягаемая полка под потолком. Проверка дважды оказывалась подогнанной
            // под прежнюю модель: сначала полка стояла в пяти клетках от средней, а по
            // физике туда долетают; потом — в девяти клетках и на две выше, а туда
            // долетают полным прыжком со взмахами после вершины (они дают дальность,
            // но не высоту). Теперь полка на три клетки выше средней, в девяти вбок:
            // подъём 48 из 60 возможных, и на спуске до неё остаётся 12 пикселей —
            // пролететь 136 пикселей не успеть даже на взмахах.
            string[] arena = {
                "....................",
                ".................==.",
                "....................",
                "....................",
                "......===...........",
                "....................",
                "....................",
                "####..##############" };
            NavMap nav = new NavMap();
            nav.Build(Cells(arena), 20, 8, 16f, 0f, 0f);
            if (nav.nodeCount == 0) return "карта: ни одного узла на арене с полом";

            float sx, sy;
            int edge;
            // Через яму: идти вправо и перепрыгнуть.
            if (!nav.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "карта: не нашла дорогу через яму";
            if (sx <= 24f) return "карта: первый шаг в другую сторону от цели";

            // Наверх, на полку: подъём в три клетки утке по силам (потолок прыжка 60).
            if (!nav.NextStep(24f, 104f, 120f, 56f, out sx, out sy, out edge))
                return "карта: не нашла дорогу на полку";

            // А на полку под потолком дороги нет, и выдумывать её нельзя.
            if (nav.NextStep(24f, 104f, 280f, 24f, out sx, out sy, out edge))
                return "карта: выдумала маршрут туда, куда не запрыгнуть";

            // Сверка чужого движения с графом — основа журнала игрока.
            int known;
            if (nav.MoveKnown(56f, 104f, 104f, 104f, out known) != NavMap.KnownEdge || known != NavMap.Jump)
                return "сверка: прыжок через яму граф должен знать прямым ребром";
            // Девять клеток по ровному утка перелетает одним прыжком (137 пикселей
            // полёта), а четырнадцать — уже нет: туда только несколькими движениями.
            if (nav.MoveKnown(24f, 104f, 248f, 104f, out known) != NavMap.Detour)
                return "сверка: дальняя точка того же пола достижима, но не одним движением";
            if (nav.MoveKnown(24f, 104f, 280f, 24f, out known) != NavMap.NoPath)
                return "сверка: полка под потолком должна быть «пути нет»";

            // --- дальность прыжка по движениям игрока ---
            // Журнал игрока поймал движения, которые граф считал невозможными.
            // Подъём на три клетки со сдвигом на три: у игрока «вверх 51, итог +48,
            // вбок -48 — в графе НЕТ». Прежняя таблица на такой подъём разрешала
            // сдвиг только на две клетки.
            string[] ledge = {
                "............",
                "............",
                "............",
                "............",
                ".......#####",
                ".......#####",
                ".......#####",
                "############" };
            NavMap onLedge = new NavMap();
            onLedge.Build(Cells(ledge), 12, 8, 16f, 0f, 0f);
            if (onLedge.MoveKnown(72f, 104f, 120f, 56f, out known) != NavMap.KnownEdge)
                return "дальность: подъём на 3 клетки со сдвигом на 3 игрок делает, а граф не знает";

            // Прыжок вниз на семь клеток и вбок на одиннадцать над пропастью: у игрока
            // «итог -112, вбок 180 — в графе НЕТ». Падение добавляет время в воздухе,
            // а таблица это не учитывала вовсе.
            string[] chasm = {
                "........................",
                "........................",
                "####....................",
                "........................",
                "........................",
                "........................",
                "........................",
                "........................",
                "........................",
                "..............##########",
                "........................" };
            NavMap overChasm = new NavMap();
            overChasm.Build(Cells(chasm), 24, 11, 16f, 0f, 0f);
            if (overChasm.MoveKnown(56f, 24f, 232f, 136f, out known) != NavMap.KnownEdge)
                return "дальность: прыжок вниз на 7 и вбок на 11 игрок делает, а граф не знает";

            // Под картой пропасть, а не пол: в Duck Game падение вниз смертельно.
            // Раньше за нижним краем карты считалась стена, и нижний ряд становился
            // невидимым полом, на который бот мог «приземлиться».
            string[] abyss = {
                "........",
                "........",
                "###.....",
                "........",
                "........" };
            NavMap overAbyss = new NavMap();
            overAbyss.Build(Cells(abyss), 8, 5, 16f, 0f, 0f);
            if (overAbyss.nodeCount != 3)
                return "пропасть: под картой невидимый пол, узлов " + overAbyss.nodeCount + " вместо 3";

            // --- платформа ловит падающую утку ---
            // Вверх сквозь одностороннюю платформу пролетают, а сверху она ловит.
            // Граф же считал её пустотой в обе стороны и предлагал «прыжок вниз»
            // сквозь ту платформу, на которой бот стоит: в игре утка подпрыгивает
            // и садится обратно — бот прыгал на месте бесконечно. Вниз сквозь
            // платформу — только приёмом DOWN+JUMP, ребром «вниз».
            string[] shelf = {
                "........",
                "........",
                "..===...",
                "........",
                "........",
                "########" };
            NavMap onShelf = new NavMap();
            onShelf.Build(Cells(shelf), 8, 6, 16f, 0f, 0f);
            if (onShelf.MoveKnown(56f, 24f, 72f, 72f, out known) == NavMap.KnownEdge && known == NavMap.Jump)
                return "платформа: граф прыгает вниз сквозь платформу, на которой стоит";
            // А спрыгнуть с края платформы вбок, мимо неё, можно.
            if (onShelf.MoveKnown(72f, 24f, 120f, 72f, out known) != NavMap.KnownEdge)
                return "платформа: спрыгнуть с края мимо платформы граф должен уметь";

            // Шипы — не пол: через них перепрыгивают, а не проходят.
            string[] spiky = {
                "............",
                "............",
                "............",
                "............",
                "............",
                "............",
                ".....^......",
                "############" };
            NavMap nav2 = new NavMap();
            nav2.Build(Cells(spiky), 12, 8, 16f, 0f, 0f);
            if (!nav2.NextStep(72f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "шипы: не нашла дорогу мимо шипов";
            if (edge != NavMap.Jump) return "шипы: собирается пройти по ним ногами";

            // --- прыжок: длительность удержания по требуемому подъёму ---
            if (JumpHold(0f, -48f, false) != JumpFull) return "прыжок: на три клетки жмёт не до упора";
            if (JumpHold(0f, -30f, false) != JumpMid) return "прыжок: на две клетки не тот замах";
            if (JumpHold(16f, -16f, false) != JumpTap) return "прыжок: на ступеньку прыгает во всю силу";
            if (JumpHold(0f, 0f, true) != JumpFull) return "прыжок: через яму жмёт вполсилы";
            // Далёкий прыжок держать до упора при любом подъёме: граф считает дальность
            // по полному прыжку. На space12_8 бот прыгал на 106 влево и 25 вверх средним
            // прыжком, дуга пришла ниже края блока, и четыре бота сползли в пропасть.
            if (JumpHold(-106f, -25f, false) != JumpFull)
                return "прыжок: далёкий прыжок держит вполсилы — недолёт, как на space12_8";

            // --- пружина ---
            // Полка в семи клетках над полом: прыжком туда не попасть никак (потолок
            // прыжка три клетки), а пружиной — запросто. Это ровно случай со скриншота.
            string[] withSpring = {
                "............",
                "............",
                "......===...",
                "............",
                "............",
                "............",
                "............",
                "............",
                ".....S......",
                "############" };
            string[] noSpring = {
                "............",
                "............",
                "......===...",
                "............",
                "............",
                "............",
                "............",
                "............",
                "............",
                "############" };

            NavMap sprung = new NavMap();
            sprung.Build(Cells(withSpring), 12, 10, 16f, 0f, 0f);
            NavMap plain = new NavMap();
            plain.Build(Cells(noSpring), 12, 10, 16f, 0f, 0f);

            if (plain.NextStep(24f, 136f, 104f, 24f, out sx, out sy, out edge))
                return "пружина: без неё маршрут на полку не должен находиться вовсе";
            if (!sprung.NextStep(24f, 136f, 104f, 24f, out sx, out sy, out edge))
                return "пружина: с ней маршрут на полку не нашёлся";

            // --- телепорт ---
            // Стена в пять клеток делит арену пополам: перепрыгнуть нельзя.
            string[] split = {
                "............",
                "............",
                ".....##.....",
                ".....##.....",
                ".....##.....",
                ".....##.....",
                ".....##.....",
                "############" };
            NavMap walled = new NavMap();
            walled.Build(Cells(split), 12, 8, 16f, 0f, 0f);
            if (walled.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "телепорт: через стену в пять клеток маршрута быть не должно";

            NavMap linked = new NavMap();
            linked.Build(Cells(split), 12, 8, 16f, 0f, 0f);
            linked.AddLink(40f, 104f, 152f, 104f);
            if (!linked.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "телепорт: пара связана, а маршрут всё равно не нашёлся";

            // --- меч ---
            // Два бота с мечами на одной высоте в 120 пикселях: линия «огня» есть,
            // и раньше оба стояли и махали в воздух по двадцать секунд. С мечом надо
            // идти вплотную, а бить только оттуда.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 120f; v.canShoot = true; v.holding = 1;
            v.meleeInHand = true; v.facingRight = true; v.grounded = true; v.speedAbs = 1f;
            bool closing = false, swungAir = false;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.RIGHT) != 0) closing = true;
                if ((m & BotInput.SHOOT) != 0) swungAir = true;
            }
            if (!closing) return "меч: стоит на месте вместо того, чтобы подойти";
            if (swungAir) return "меч: машет в воздух за 120 пикселей";
            if (s.why != WhyMeleeGo) return "причина: подход с мечом записан как «" + s.why + "»";

            // Вплотную — бить и не отступать: KeepDist для ствола, а не для меча.
            v.ex = 12f; s = new BotState();
            bool swung = false, backed = false;
            for (int i = 0; i < ShootHold + ShootGap + 2; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.SHOOT) != 0) swung = true;
                if ((m & BotInput.LEFT) != 0) backed = true;
            }
            if (!swung) return "меч: вплотную не бьёт";
            if (backed) return "меч: вплотную отступает, как со стволом";
            if (s.why != WhyMeleeHit) return "причина: удар мечом записан как «" + s.why + "»";

            // --- память о недостижимых целях ---
            SkipList skip = new SkipList();
            object gunA = new object(), gunB = new object();
            skip.Add(gunA, 3);
            if (!skip.Contains(gunA)) return "память: только что забытая цель не помнится";
            if (skip.Contains(gunB)) return "память: помнит то, чего не добавляли";
            skip.Tick(); skip.Tick(); skip.Tick();
            if (skip.Contains(gunA)) return "память: не отпускает цель, хотя срок вышел";

            // --- сторож залипания ---
            // Маршрут велит прыгать ровно вверх-вниз, а прыжок ничего не меняет: бот
            // прыгает на месте, пока раунд не закончат руками. Сторож замечает это
            // снаружи и включает breakout — тогда надо уйти вбок, а не прыгать дальше.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 0f; v.ey = 90f; v.holding = 1;
            v.grounded = true; v.facingRight = true; v.speedAbs = 1f;
            v.hasEnemyStep = true; v.enemyStepX = 0f; v.enemyStepY = 40f;
            v.enemyStepEdge = NavMap.Jump;
            v.breakout = true;
            bool brokeOut = false;
            for (int i = 0; i < 10; i++)
                if ((Decide(ref v, ref s) & (BotInput.LEFT | BotInput.RIGHT)) != 0) brokeOut = true;
            if (!brokeOut) return "сторож: бот залип и продолжает прыгать на месте";
            if (s.why != WhyBreakout) return "причина: уход от залипания записан как «" + s.why + "»";

            // --- утки друг на друге ---
            // Враг ровно на 16 пикселей выше или ниже: это щель между всеми порогами.
            // Вбок стрелять можно при |dy| < 16, вверх — при dy < -16, «надо наверх» —
            // тоже при dy < -16. Ни одна ветка не срабатывала, маршрута нет (одна
            // клетка), и оба бота стояли по девять секунд. Надо разойтись.
            for (int side = -1; side <= 1; side += 2)
            {
                v = new BotView(); s = new BotState();
                v.hasEnemy = true; v.ex = 0f; v.ey = 16f * side; v.holding = 1;
                v.grounded = true; v.facingRight = true;
                bool apart = false;
                for (int i = 0; i < 10; i++)
                    if ((Decide(ref v, ref s) & (BotInput.LEFT | BotInput.RIGHT)) != 0) apart = true;
                if (!apart)
                    return side < 0 ? "стопка: враг на голове, а бот стоит"
                                    : "стопка: бот стоит на враге и не слезает";
                if (s.why != WhyStack) return "причина: расхождение записано как «" + s.why + "»";
            }

            // --- прыжок в воздухе ---
            // Утка отрывается от земли на следующем кадре после нажатия, и если в
            // воздухе кнопку отпустить, игра режет скорость вдвое. Значит держать
            // надо и в полёте. Прежние проверки держали grounded = true на каждом
            // кадре и поэтому не видели, что бот отпускает кнопку сразу после отрыва.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 10f; v.ey = -45f; v.holding = 1; v.grounded = true;
            v.hasEnemyStep = true; v.enemyStepX = 10f; v.enemyStepY = -45f;
            v.enemyStepEdge = NavMap.Jump;
            int airHeld = 0;
            bool took = false;
            for (int i = 0; i < 60; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.JUMP) != 0) { took = true; airHeld++; }
                else if (took) break;
                if (took) v.grounded = false;   // нажал — дальше только воздух
            }
            if (!took) return "прыжок: даже не начинается";
            if (s.jumpBy != HowRouteJump) return "причина: прыжок по маршруту записан как «" + s.jumpBy + "»";
            if (airHeld < JumpFull)
                return "прыжок: отпускает кнопку в воздухе через " + airHeld
                    + " кадр., полного прыжка не выйдет";

            // После приземления новый прыжок возможен только через отпускание:
            // Pressed ловит фронт, зажатая кнопка второй раз не сработает.
            v.grounded = true;
            bool letGo = false, again = false;
            for (int i = 0; i < 60 && !again; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.JUMP) == 0) letGo = true;
                else if (letGo) again = true;
            }
            if (!again) return "прыжок: после приземления не прыгает снова";

            // --- длина маршрута до всех узлов сразу ---
            // Цель выбирается по длине маршрута, а не по прямой: за стеной в пять
            // клеток соседний по прямой ствол недостижим, через телепорт — рядом.
            walled.SearchAll(walled.NodeNear(24f, 104f));
            if (walled.RouteLength(walled.NodeNear(168f, 104f)) < float.MaxValue)
                return "длина маршрута: за стеной в пять клеток дорога нашлась";
            linked.SearchAll(linked.NodeNear(24f, 104f));
            if (linked.RouteLength(linked.NodeNear(168f, 104f)) >= float.MaxValue)
                return "длина маршрута: через телепорт дорога не нашлась";
            // Путь целиком: от старта к цели, и первый шаг совпадает с NextStep.
            int from = nav.NodeNear(24f, 104f), to = nav.NodeNear(120f, 56f);
            nav.SearchAll(from);
            int[] path = new int[64];
            int pathLen = nav.PathTo(to, path);
            if (pathLen < 2 || path[0] != from || path[pathLen - 1] != to)
                return "путь: не ведёт от старта к цели";
            float fx, fy;
            int fe;
            int fprog;
            if (!nav.FirstStep(from, to, out fx, out fy, out fe, out fprog)
                || fx != nav.CellX(path[1]) || fy != nav.CellY(path[1]))
                return "путь: первый шаг не совпадает с путём";

            // Под обрывом пропасти пол не найден, под полкой — найден.
            if (overAbyss.SafeBelow(88f, 40f)) return "пропасть: под обрывом нашёлся пол, которого нет";
            if (!overAbyss.SafeBelow(24f, 8f)) return "пропасть: над полом пол не найден";

            // --- сторож и заставка раунда ---
            // Обездвиженная утка не застряла, как бы долго ни стояла: в логе 24.09
            // 145 срабатываний из 170 пришлись на заставку, все боты разом.
            int watch = 0;
            for (int i = 0; i < StuckLimit + 60; i++)
                if (Watchdog(ref watch, true, false, true))
                    return "сторож: срабатывает на заставке раунда, пока утки обездвижены";
            bool fired = false;
            for (int i = 0; i < StuckLimit && !fired; i++)
                if (Watchdog(ref watch, true, false, false)) fired = true;
            if (!fired) return "сторож: не замечает бота, четыре секунды стоящего на месте";

            // --- пропасть ---
            // Без маршрута к краю пропасти не идти и не прыгать через неё: так
            // погибли 78 ботов из 80 павших «неясно».
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            v.gapRight = true; v.abyssRight = true;
            for (int i = 0; i < 30; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.RIGHT) != 0) return "пропасть: без маршрута шагает к краю";
                if ((m & BotInput.JUMP) != 0) return "пропасть: без маршрута прыгает в пропасть";
            }
            if (s.how != HowAbyss) return "пропасть: причина записана как «" + s.how + "»";

            // Сторож уводит в сторону, а там пропасть: развернуться от края.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ey = 90f; v.grounded = true; v.speedAbs = 1f;
            v.facingRight = true; v.breakout = true; v.gapLeft = true; v.abyssLeft = true;
            ushort away = Decide(ref v, ref s);
            if ((away & BotInput.LEFT) != 0) return "пропасть: уходя от залипания, шагает в пропасть";
            if ((away & BotInput.RIGHT) == 0) return "пропасть: уходя от залипания, не разворачивается от края";

            // Расходясь с уткой над головой — тоже не в пропасть.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ey = -16f; v.holding = 1; v.grounded = true;
            v.facingRight = true; v.gapRight = true; v.abyssRight = true;
            if ((Decide(ref v, ref s) & BotInput.RIGHT) != 0) return "пропасть: расходясь, шагает в пропасть";

            // Бегство от гранаты к пропасти: лучше стоять у края, чем упасть.
            v = new BotView(); s = new BotState();
            v.grounded = true; v.speedAbs = 1f; v.danger = true; v.dx = 20f;
            v.gapLeft = true; v.abyssLeft = true;
            if ((Decide(ref v, ref s) & (BotInput.LEFT | BotInput.JUMP)) != 0)
                return "пропасть: убегая от гранаты, прыгает в пропасть";

            // Со стволом вплотную бот отступает — но не спиной в пропасть.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 10f; v.canShoot = true; v.holding = 1; v.facingRight = true;
            v.grounded = true; v.gapLeft = true; v.abyssLeft = true;
            if ((Decide(ref v, ref s) & BotInput.LEFT) != 0) return "пропасть: отступая от врага, шагает в пропасть";

            // С маршрутом обрыв — не повод прыгать: ребро «шаг» с края значит сойти
            // вниз, а раньше бот брал такой спуск полным прыжком и перелетал цель.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 100f; v.ey = 40f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            v.gapRight = true;
            v.hasEnemyStep = true; v.enemyStepX = 16f; v.enemyStepY = 23f; v.enemyStepEdge = NavMap.Walk;
            for (int i = 0; i < 5; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) return "маршрут: ребро «шаг» с края берёт прыжком";

            // --- ящик ---
            // Стоя на крыше ящика, бить его снизу нельзя — прыгать на месте незачем.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.grounded = true;
            v.hasPickup = true; v.pickupIsBox = true; v.px = 0f; v.py = 16f;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) return "ящик: стоя на нём, прыгает на месте";
            // А стоя под ящиком — бить.
            v.py = -40f; s = new BotState();
            bool hit = false;
            for (int i = 0; i < 5; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) hit = true;
            if (!hit) return "ящик: под ящиком не прыгает";

            // Маршрут к ящику ведёт под него, а не на крышу: ящик — сплошной блок,
            // и ближайшей к нему клеткой NodeNear считал ту, что сверху.
            string[] boxRoom = {
                "............",
                "............",
                ".....#......",
                "............",
                "............",
                "............",
                "############" };
            NavMap boxMap = new NavMap();
            boxMap.Build(Cells(boxRoom), 12, 7, 16f, 0f, 0f);
            int under = boxMap.NodeBelow(88f, 40f, 4);
            if (under < 0) return "ящик: места под ящиком не нашлось";
            if (boxMap.CellCol(under) != 5 || boxMap.CellRow(under) != 5)
                return "ящик: цель не под ящиком, а в клетке " + boxMap.CellCol(under) + "," + boxMap.CellRow(under);

            // --- подбор ---
            // Ствол на полу под ногами — на 12 пикселей ниже центра утки. Игра такой
            // берёт (круг 18 вокруг точки на 4 ниже центра), а бот не жал GRAB: в матче
            // 24.09 двое стояли на одном PelletGun по четыре секунды.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.grounded = true; v.hasPickup = true; v.px = 0f; v.py = 12f;
            bool picked = false;
            for (int i = 0; i < GrabGap + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) picked = true;
            if (!picked) return "подбор: ствол под ногами не поднимает";

            // --- сетка ---
            // Пойманная утка вырывается прыжками: каждое нажатие срезает время сетки.
            // Жать импульсами — Pressed ловит фронт. Раньше боты сидели в сетке все
            // шесть секунд, и игрок успевал сбросить их в пропасть.
            v = new BotView(); s = new BotState();
            v.netted = true; v.hasEnemy = true; v.ex = 30f; v.holding = 1; v.canShoot = true; v.facingRight = true;
            int presses = 0;
            ushort was = 0;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.JUMP) != 0 && (was & BotInput.JUMP) == 0) presses++;
                was = m;
            }
            if (presses < 5) return "сетка: бот в сетке не вырывается, нажатий прыжка " + presses;
            if (s.why != WhyNetted) return "причина: сетка записана как «" + s.why + "»";

            // --- позиция для выстрела ---
            // Со стволом бот идёт не к врагу, а на позицию: та же высота, дистанция,
            // чистая линия. Дошёл, а линии нет — стоять лицом к врагу, а не лезть в упор:
            // иначе все сходятся в одну точку.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = -90f; v.ey = -40f; v.holding = 1; v.grounded = true;
            v.facingRight = true; v.atSpot = true;
            ushort aim = Decide(ref v, ref s);
            if ((aim & BotInput.LEFT) == 0) return "позиция: не разворачивается к врагу";
            aim = 0;
            v.facingRight = false;
            for (int i = 0; i < 10; i++) aim |= Decide(ref v, ref s);
            if ((aim & (BotInput.LEFT | BotInput.RIGHT | BotInput.JUMP)) != 0)
                return "позиция: дошёл до позиции и всё равно лезет к врагу";
            if (s.why != WhyAim) return "причина: ожидание на позиции записано как «" + s.why + "»";

            // На карте: враг стоит на платформе, бот внизу. Позиция — на той же платформе,
            // на дистанции, а не клетка самого врага.
            string[] duel = {
                "................",
                "................",
                "................",
                "..=========.....",
                "................",
                "................",
                "################" };
            NavMap duelMap = new NavMap();
            duelMap.Build(Cells(duel), 16, 7, 16f, 0f, 0f);
            duelMap.SearchAll(duelMap.NodeNear(24f, 88f));
            int spot = duelMap.FiringNode(152f, 48f, 32f, 128f);
            if (spot < 0) return "позиция: на платформе врага места для выстрела не нашлось";
            if (duelMap.CellRow(spot) != 2) return "позиция: не на высоте врага, а в строке " + duelMap.CellRow(spot);
            if (Math.Abs(duelMap.CellCol(spot) - 9) < 2) return "позиция: вплотную к врагу, колонка " + duelMap.CellCol(spot);

            // --- прыжок по программе ---
            // Геометрия space12_8: с пола прыжок на висящий блок в семи клетках влево
            // и двух вверх. Средний прыжок (14 кадров) туда не долетает — бот ударится
            // в бок блока, как четыре бота в матче 24.09; а ребро в графе есть — с той
            // программой, которая долетает.
            string[] blockJump = {
                "......................",
                "......................",
                "......................",
                "......................",
                "......................",
                "....###...............",
                "......................",
                "........##############",
                "......................" };
            NavMap bj = new NavMap();
            bj.Build(Cells(blockJump), 22, 9, 16f, 0f, 0f);
            int bjFrom = bj.NodeNear(216f, 104f), bjEdge = bj.NodeNear(104f, 72f);
            int bjFrames;
            if (bj.Simulate(bjFrom, NavMap.Prog(-1, 14, 0, NavMap.Forever, 0), out bjFrames) == bjEdge)
                return "симуляция: средний прыжок долетел до блока, а в игре бот бился в его бок";
            // Полный прыжок отсюда садится центром в 0.7 пикселя от кромки блока, а сетка
            // расходится с игрой на несколько пикселей: такую посадку граф не принимает.
            // А на блок дорога есть — с запасом, с соседней клетки или со взмахами.
            if (bj.Simulate(bjFrom, NavMap.Prog(-1, 28, 0, NavMap.Forever, 0), out bjFrames) == bjEdge)
                return "симуляция: граф принял посадку на кромку блока впритык";
            if (bj.MoveKnown(216f, 104f, 104f, 72f, out known) == NavMap.NoPath)
                return "симуляция: на висящий блок дороги нет";

            // --- сеткомёт ---
            // Несём пойманного и стоим на краю лицом к пропасти: бросить, держа
            // направление — так бросок в 2.5 раза сильнее (Duck.ThrowItem).
            v = new BotView(); s = new BotState();
            v.carrying = true; v.holding = 3; v.grounded = true;
            v.atThrowSpot = true; v.throwDir = 1; v.facingRight = true;
            ushort thr = Decide(ref v, ref s);
            if ((thr & BotInput.GRAB) == 0 || (thr & BotInput.RIGHT) == 0)
                return "сеткомёт: на краю не бросает пойманного с силой";
            if (s.why != WhyThrowTrap) return "причина: бросок пойманного записан как «" + s.why + "»";
            // Спиной к пропасти — сначала развернуться: бросок летит туда, куда смотрит утка.
            v.facingRight = false; s = new BotState();
            thr = Decide(ref v, ref s);
            if ((thr & BotInput.GRAB) != 0) return "сеткомёт: бросает пойманного спиной к пропасти";
            if ((thr & BotInput.RIGHT) == 0) return "сеткомёт: не разворачивается к пропасти";
            // Стоя на краю, между бросками не шагать к пропасти: сам упадёт.
            v.facingRight = true;
            for (int i = 0; i < GrabGap; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.RIGHT) != 0 && (m & BotInput.GRAB) == 0)
                    return "сеткомёт: на краю шагает к пропасти без броска";
            }
            // До края далеко — нести по маршруту, не бросая на полпути.
            v = new BotView(); s = new BotState();
            v.carrying = true; v.holding = 3; v.grounded = true; v.speedAbs = 1f;
            v.hasThrowStep = true; v.throwStepX = -40f; v.throwStepY = 7f; v.throwStepEdge = NavMap.Walk;
            bool carried = false;
            for (int i = 0; i < 10; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.GRAB) != 0) return "сеткомёт: бросает пойманного на полпути";
                if ((m & BotInput.LEFT) != 0) carried = true;
            }
            if (!carried) return "сеткомёт: не несёт пойманного к краю";
            // Пойманный враг лежит, а в руках сеткомёт: сначала освободить руки.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 100f; v.holding = 1; v.netGun = true; v.grounded = true;
            v.hasPickup = true; v.pickupIsTrap = true; v.px = 40f;
            bool freed = false;
            for (int i = 0; i < 3; i++) if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) freed = true;
            if (!freed) return "сеткомёт: не освобождает руки, чтобы поднять пойманного";
            // С пустыми руками — подойти и поднять.
            v.holding = 0; v.netGun = false; v.px = 4f; v.py = 8f; s = new BotState();
            bool lifted = false;
            for (int i = 0; i < GrabGap + 2; i++) if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) lifted = true;
            if (!lifted) return "сеткомёт: не поднимает пойманного под ногами";
            // В руках сеткомёт, а достижим смертельный ствол — сеткомёт бросить.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.holding = 1; v.netGun = true; v.grounded = true;
            v.hasPickup = true; v.px = 100f;
            bool swapped = false;
            for (int i = 0; i < 3; i++) if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) swapped = true;
            if (!swapped) return "сеткомёт: держит сеткомёт, хотя достижим смертельный ствол";
            // На карте: край полки над пропастью знает, в какую сторону бросать. Стена
            // слева — туда не бросишь; справа пропасть шире броска.
            string[] throwRoom = {
                "#.......",
                "#.......",
                "####....",
                "........",
                "........" };
            NavMap tm = new NavMap();
            tm.Build(Cells(throwRoom), 8, 5, 16f, 0f, 0f);
            int edgeNode = tm.NodeNear(56f, 24f), innerNode = tm.NodeNear(24f, 24f);
            if (tm.AbyssDir(edgeNode) != 1) return "сеткомёт: край над пропастью не найден";
            if (tm.AbyssDir(innerNode) != 0) return "сеткомёт: пропасть найдена у стены";
            tm.SearchAll(innerNode);
            int abyssSide;
            if (tm.NearestAbyssEdge(out abyssSide) != edgeNode || abyssSide != 1)
                return "сеткомёт: ближайший край для броска не найден";

            // Исполнитель: сначала встать в центр клетки и погасить скорость, потом —
            // кнопки ровно по программе, кадр в кадр, как её прогнала карта.
            int prog = NavMap.Prog(-1, 20, 0, 16, 0);
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = -150f; v.holding = 1; v.grounded = true; v.alignDx = 6f;
            v.hasEnemyStep = true; v.enemyStepX = -64f; v.enemyStepY = -32f;
            v.enemyStepEdge = NavMap.Jump; v.enemyStepProg = prog;
            ushort toCenter = Decide(ref v, ref s);
            if ((toCenter & BotInput.JUMP) != 0) return "программа: прыгает, не встав в центр клетки";
            if ((toCenter & BotInput.RIGHT) == 0) return "программа: не идёт к центру клетки";
            v.alignDx = 0f; v.hs = 0f;
            for (int f = 0; f < 40; f++)
            {
                ushort m = Decide(ref v, ref s);
                ushort want = NavMap.ProgJumpAt(prog, f) ? BotInput.JUMP : (ushort)0;
                int pd = NavMap.ProgDirAt(prog, f);
                if (pd < 0) want |= BotInput.LEFT;
                if (pd > 0) want |= BotInput.RIGHT;
                if ((m & (BotInput.LEFT | BotInput.RIGHT | BotInput.JUMP)) != want)
                    return "программа: на кадре " + f + " кнопки " + m + " вместо " + want;
                v.grounded = false;   // оторвалась
            }

            return GlassTest() ?? DodgeTest() ?? BlastTest() ?? SpringTest() ?? SettingsTest() ?? StepOffTest() ?? PickupTest() ?? ReachTest() ?? RagdollTest() ?? WeaponsTest() ?? LobbyTest();
        }
    }
}
