using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Карта из строк: так её видно прямо в проверке.
        private static byte[] Cells(string[] rows)
        {
            int w = rows[0].Length;
            byte[] c = new byte[w * rows.Length];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = rows[y][x];
                    byte cell = NavMap.Empty;
                    if (ch == '#' || ch == '|') cell = NavMap.Solid;
                    else if (ch == 'S') cell = NavMap.SpringUp;
                    else if (ch == '/') cell = NavMap.SpringDiagRight;
                    else if (ch == '\\') cell = NavMap.SpringDiagLeft;
                    else if (ch == '=') cell = NavMap.Platform;
                    else if (ch == '^') cell = NavMap.Deadly;
                    c[y * w + x] = cell;
                }
            return c;
        }

        // Самопроверка без игры: мод зовёт её при загрузке, обвязка — снаружи по DuckBots.dll. null — всё хорошо, иначе — что упало.
        public static string SelfTest()
        {
            BotView v;
            BotState s;

            // Враг справа, линия огня: стрелять импульсами, а не держать.
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
            if (s.why != WhyShoot) return "причина: бой записан как «" + s.why + "»";

            // Враг за спиной: сначала разворот.
            v.facingRight = false; s = new BotState();
            ushort turn = Decide(ref v, ref s);
            if ((turn & BotInput.RIGHT) == 0) return "разворот: не поворачивается к врагу";
            if ((turn & BotInput.SHOOT) != 0) return "разворот: стреляет, не развернувшись";
            if (s.why != WhyTurn) return "причина: разворот записан как «" + s.why + "»";

            // Чужая взрывчатка справа: уходить влево, не стреляя.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 60f; v.canShoot = true; v.holding = 1; v.facingRight = true;
            v.grounded = true; v.danger = true; v.dx = 20f;
            ushort flee = Decide(ref v, ref s);
            if ((flee & BotInput.LEFT) == 0) return "опасность: не убегает от взрывчатки";
            if ((flee & BotInput.SHOOT) != 0) return "опасность: стреляет вместо того, чтобы убегать";
            if (s.why != WhyFlee) return "причина: бегство записано как «" + s.why + "»";

            // Граната, враг близко: чека, потом бросок.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 40f; v.canShoot = true; v.holding = 1; v.facingRight = true;
            v.grounded = true; v.explosiveInHand = true;
            if ((Decide(ref v, ref s) & BotInput.SHOOT) == 0) return "граната: не выдёргивает чеку";
            if (s.why != WhyArm) return "причина: чека записана как «" + s.why + "»";
            v.armedInHand = true; s = new BotState();
            if ((Decide(ref v, ref s) & BotInput.GRAB) == 0) return "граната: взведённую не бросает";
            if (s.why != WhyThrow) return "причина: бросок записан как «" + s.why + "»";

            // Взведённую в руках бросать, даже когда рядом рвануло.
            v.danger = true; v.dx = 20f; s = new BotState();
            if ((Decide(ref v, ref s) & BotInput.GRAB) == 0) return "граната: держит взведённую, убегая";

            // Граната, враг далеко: идти к нему, чеку не трогать.
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

            // Негодное в руках — выбросить.
            v = new BotView(); s = new BotState();
            v.holding = 2; v.grounded = true;
            bool dropped = false;
            for (int i = 0; i < GrabGap + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) dropped = true;
            if (!dropped) return "негодное оружие: не выбрасывается";
            if (s.why != WhyJunk) return "причина: выброс записан как «" + s.why + "»";

            // Ствол есть, линии нет: сближаться.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 80f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            if ((Decide(ref v, ref s) & BotInput.RIGHT) == 0) return "сближение: стоит на месте";

            // Упёрся в стену: прыгнуть.
            v.speedAbs = 0f; s = new BotState();
            bool jumped = false;
            for (int i = 0; i < StuckFrames + JumpFull + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) jumped = true;
            if (!jumped) return "застревание: не прыгает, упёршись в стену";
            if (s.jumpBy != HowStuckJump) return "причина: прыжок от застревания записан как «" + s.jumpBy + "»";

            // Впереди яма: прыгать сразу.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 80f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            v.gapRight = true;
            ushort over = Decide(ref v, ref s);
            if ((over & BotInput.JUMP) == 0) return "яма: идёт в обрыв, не прыгая";
            if ((over & BotInput.RIGHT) == 0) return "яма: прыгает, но не вперёд";
            if (s.jumpBy != HowGapJump) return "причина: прыжок через яму записан как «" + s.jumpBy + "»";

            // Цель ниже, под ногами платформа: DOWN+JUMP; над пропастью — нет.
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
            v.abyssBelow = true; s = new BotState();
            for (int i = 0; i < 10; i++)
                if ((Decide(ref v, ref s) & (BotInput.DOWN | BotInput.JUMP)) == (BotInput.DOWN | BotInput.JUMP))
                    return "спуск: проваливается сквозь платформу в пропасть";

            // Враг над головой: подпрыгнуть, отпустить и снова держать прыжок — взмахи, — стреляя.
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
                if (jumpedOff) v.grounded = false;
            }
            if (!jumpedOff || !releasedUp) return "вверх: не подпрыгивает и не отпускает — взмахов не будет";
            if (hoverHeld < 10) return "вверх: взмахи не держит, кадров " + hoverHeld;
            if (!shotUp) return "вверх: машет крыльями, но не стреляет";

            // Цель строго над головой: идти и прыгать, искать подъём.
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

            // За полторы секунды направление блуждания должно смениться.
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

            // Цель ниже, платформы нет: идти к краю, обрыв не перепрыгивать.
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

            // Подарок: открыть, а не выбросить.
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

            // Пол с ямой, полка на три клетки выше и недосягаемая полка под потолком.
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
            if (!nav.NextStep(24f, 104f, 168f, 104f, out sx, out sy, out edge))
                return "карта: не нашла дорогу через яму";
            if (sx <= 24f) return "карта: первый шаг в другую сторону от цели";

            if (!nav.NextStep(24f, 104f, 120f, 56f, out sx, out sy, out edge))
                return "карта: не нашла дорогу на полку";

            if (nav.NextStep(24f, 104f, 280f, 24f, out sx, out sy, out edge))
                return "карта: выдумала маршрут туда, куда не запрыгнуть";

            int known;
            if (nav.MoveKnown(56f, 104f, 104f, 104f, out known) != NavMap.KnownEdge || known != NavMap.Jump)
                return "сверка: прыжок через яму граф должен знать прямым ребром";
            if (nav.MoveKnown(24f, 104f, 248f, 104f, out known) != NavMap.Detour)
                return "сверка: дальняя точка того же пола достижима, но не одним движением";
            if (nav.MoveKnown(24f, 104f, 280f, 24f, out known) != NavMap.NoPath)
                return "сверка: полка под потолком должна быть «пути нет»";

            // Подъём на 3 клетки со сдвигом на 3: человек так прыгает.
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

            // Прыжок вниз на 7 и вбок на 11 над пропастью.
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

            // Под картой пропасть, а не пол.
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

            // Платформа ловит падающую утку: вниз сквозь неё — только ребром «вниз», с края мимо — можно.
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
            if (onShelf.MoveKnown(72f, 24f, 120f, 72f, out known) != NavMap.KnownEdge)
                return "платформа: спрыгнуть с края мимо платформы граф должен уметь";

            // Шипы перепрыгивают, а не проходят.
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

            // Удержание по подъёму; через яму и далеко — до упора.
            if (JumpHold(0f, -48f, false) != JumpFull) return "прыжок: на три клетки жмёт не до упора";
            if (JumpHold(0f, -30f, false) != JumpMid) return "прыжок: на две клетки не тот замах";
            if (JumpHold(16f, -16f, false) != JumpTap) return "прыжок: на ступеньку прыгает во всю силу";
            if (JumpHold(0f, 0f, true) != JumpFull) return "прыжок: через яму жмёт вполсилы";
            if (JumpHold(-106f, -25f, false) != JumpFull)
                return "прыжок: далёкий прыжок держит вполсилы — недолёт, как на space12_8";

            // Полка в семи клетках: прыжком не попасть, пружиной — да.
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

            // Стена в пять клеток: маршрута нет, с телепортом — есть.
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

            // Меч в 120 px: подойти, а не махать в воздух.
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

            // Меч вплотную: бить и не отступать.
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

            // Память о недостижимых целях забывает по сроку.
            SkipList skip = new SkipList();
            object gunA = new object(), gunB = new object();
            skip.Add(gunA, 3);
            if (!skip.Contains(gunA)) return "память: только что забытая цель не помнится";
            if (skip.Contains(gunB)) return "память: помнит то, чего не добавляли";
            skip.Tick(); skip.Tick(); skip.Tick();
            if (skip.Contains(gunA)) return "память: не отпускает цель, хотя срок вышел";

            // Сторож сказал «застрял»: уйти вбок, а не прыгать дальше.
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

            // Враг ровно на 16 px выше или ниже — щель между порогами: разойтись.
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

            // Прыжок держится и в воздухе; новый — только через отпускание.
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
                if (took) v.grounded = false;
            }
            if (!took) return "прыжок: даже не начинается";
            if (s.jumpBy != HowRouteJump) return "причина: прыжок по маршруту записан как «" + s.jumpBy + "»";
            if (airHeld < JumpFull)
                return "прыжок: отпускает кнопку в воздухе через " + airHeld
                    + " кадр., полного прыжка не выйдет";

            v.grounded = true;
            bool letGo = false, again = false;
            for (int i = 0; i < 60 && !again; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.JUMP) == 0) letGo = true;
                else if (letGo) again = true;
            }
            if (!again) return "прыжок: после приземления не прыгает снова";

            // Длина маршрута: за стеной дороги нет, через телепорт — есть; путь и первый шаг совпадают.
            walled.SearchAll(walled.NodeNear(24f, 104f));
            if (walled.RouteLength(walled.NodeNear(168f, 104f)) < float.MaxValue)
                return "длина маршрута: за стеной в пять клеток дорога нашлась";
            linked.SearchAll(linked.NodeNear(24f, 104f));
            if (linked.RouteLength(linked.NodeNear(168f, 104f)) >= float.MaxValue)
                return "длина маршрута: через телепорт дорога не нашлась";
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

            // Под обрывом пропасти пола нет, над полом — есть.
            if (overAbyss.SafeBelow(88f, 40f)) return "пропасть: под обрывом нашёлся пол, которого нет";
            if (!overAbyss.SafeBelow(24f, 8f)) return "пропасть: над полом пол не найден";

            // Сторож молчит, пока утка обездвижена (заставка раунда).
            int watch = 0;
            for (int i = 0; i < StuckLimit + 60; i++)
                if (Watchdog(ref watch, true, false, true))
                    return "сторож: срабатывает на заставке раунда, пока утки обездвижены";
            bool fired = false;
            for (int i = 0; i < StuckLimit && !fired; i++)
                if (Watchdog(ref watch, true, false, false)) fired = true;
            if (!fired) return "сторож: не замечает бота, четыре секунды стоящего на месте";

            // Без маршрута к краю пропасти не идти и не прыгать.
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

            // Сторож уводит к пропасти — развернуться.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ey = 90f; v.grounded = true; v.speedAbs = 1f;
            v.facingRight = true; v.breakout = true; v.gapLeft = true; v.abyssLeft = true;
            ushort away = Decide(ref v, ref s);
            if ((away & BotInput.LEFT) != 0) return "пропасть: уходя от залипания, шагает в пропасть";
            if ((away & BotInput.RIGHT) == 0) return "пропасть: уходя от залипания, не разворачивается от края";

            // Расходясь — не в пропасть.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ey = -16f; v.holding = 1; v.grounded = true;
            v.facingRight = true; v.gapRight = true; v.abyssRight = true;
            if ((Decide(ref v, ref s) & BotInput.RIGHT) != 0) return "пропасть: расходясь, шагает в пропасть";

            // Убегая от гранаты — не в пропасть.
            v = new BotView(); s = new BotState();
            v.grounded = true; v.speedAbs = 1f; v.danger = true; v.dx = 20f;
            v.gapLeft = true; v.abyssLeft = true;
            if ((Decide(ref v, ref s) & (BotInput.LEFT | BotInput.JUMP)) != 0)
                return "пропасть: убегая от гранаты, прыгает в пропасть";

            // Отступая от врага — не в пропасть.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 10f; v.canShoot = true; v.holding = 1; v.facingRight = true;
            v.grounded = true; v.gapLeft = true; v.abyssLeft = true;
            if ((Decide(ref v, ref s) & BotInput.LEFT) != 0) return "пропасть: отступая от врага, шагает в пропасть";

            // С маршрутом ребро «шаг» с края — сойти, а не прыгать.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 100f; v.ey = 40f; v.holding = 1; v.grounded = true; v.speedAbs = 1f;
            v.gapRight = true;
            v.hasEnemyStep = true; v.enemyStepX = 16f; v.enemyStepY = 23f; v.enemyStepEdge = NavMap.Walk;
            for (int i = 0; i < 5; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) return "маршрут: ребро «шаг» с края берёт прыжком";

            // Стоя на ящике, не прыгать; под ящиком — бить.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.grounded = true;
            v.hasPickup = true; v.pickupIsBox = true; v.px = 0f; v.py = 16f;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) return "ящик: стоя на нём, прыгает на месте";
            v.py = -40f; s = new BotState();
            bool hit = false;
            for (int i = 0; i < 5; i++)
                if ((Decide(ref v, ref s) & BotInput.JUMP) != 0) hit = true;
            if (!hit) return "ящик: под ящиком не прыгает";

            // Маршрут к ящику ведёт под него, а не на крышу.
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

            // Ствол под ногами, на 12 px ниже центра, — в зоне подбора.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.grounded = true; v.hasPickup = true; v.px = 0f; v.py = 12f;
            bool picked = false;
            for (int i = 0; i < GrabGap + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) picked = true;
            if (!picked) return "подбор: ствол под ногами не поднимает";

            // В сетке: жать прыжок импульсами.
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

            // На позиции без линии: развернуться к врагу и ждать.
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

            // Позиция для выстрела — на высоте врага и на дистанции.
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

            // Висящий блок: средний прыжок не долетает, посадку на кромку граф не берёт, но дорога есть.
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
            if (bj.Simulate(bjFrom, NavMap.Prog(-1, 28, 0, NavMap.Forever, 0), out bjFrames) == bjEdge)
                return "симуляция: граф принял посадку на кромку блока впритык";
            if (bj.MoveKnown(216f, 104f, 104f, 72f, out known) == NavMap.NoPath)
                return "симуляция: на висящий блок дороги нет";

            // Несём пойманного на краю: бросить с зажатым направлением; спиной к пропасти — развернуться.
            v = new BotView(); s = new BotState();
            v.carrying = true; v.holding = 3; v.grounded = true;
            v.atThrowSpot = true; v.throwDir = 1; v.facingRight = true;
            ushort thr = Decide(ref v, ref s);
            if ((thr & BotInput.GRAB) == 0 || (thr & BotInput.RIGHT) == 0)
                return "сеткомёт: на краю не бросает пойманного с силой";
            if (s.why != WhyThrowTrap) return "причина: бросок пойманного записан как «" + s.why + "»";
            v.facingRight = false; s = new BotState();
            thr = Decide(ref v, ref s);
            if ((thr & BotInput.GRAB) != 0) return "сеткомёт: бросает пойманного спиной к пропасти";
            if ((thr & BotInput.RIGHT) == 0) return "сеткомёт: не разворачивается к пропасти";
            v.facingRight = true;
            for (int i = 0; i < GrabGap; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.RIGHT) != 0 && (m & BotInput.GRAB) == 0)
                    return "сеткомёт: на краю шагает к пропасти без броска";
            }
            // До края далеко: нести по маршруту, не бросая.
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
            // Пойманный лежит, в руках сеткомёт: освободить руки, потом поднять.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 100f; v.holding = 1; v.netGun = true; v.grounded = true;
            v.hasPickup = true; v.pickupIsTrap = true; v.px = 40f;
            bool freed = false;
            for (int i = 0; i < 3; i++) if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) freed = true;
            if (!freed) return "сеткомёт: не освобождает руки, чтобы поднять пойманного";
            v.holding = 0; v.netGun = false; v.px = 4f; v.py = 8f; s = new BotState();
            bool lifted = false;
            for (int i = 0; i < GrabGap + 2; i++) if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) lifted = true;
            if (!lifted) return "сеткомёт: не поднимает пойманного под ногами";
            // С сеткомётом, а достижим смертельный ствол — сеткомёт бросить.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.holding = 1; v.netGun = true; v.grounded = true;
            v.hasPickup = true; v.px = 100f;
            bool swapped = false;
            for (int i = 0; i < 3; i++) if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) swapped = true;
            if (!swapped) return "сеткомёт: держит сеткомёт, хотя достижим смертельный ствол";
            // Край над пропастью знает сторону броска.
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

            // Исполнитель: встать в центр клетки, потом кнопки ровно по программе.
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
                v.grounded = false;
            }

            return GlassTest() ?? DodgeTest() ?? BlastTest() ?? SpringTest() ?? SettingsTest() ?? StepOffTest() ?? PickupTest() ?? ReachTest() ?? RagdollTest() ?? WeaponsTest() ?? LobbyTest();
        }
    }
}
