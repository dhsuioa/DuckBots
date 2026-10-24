using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Мозг: решения Decide на выдуманных обстановках.
        private static string BrainTest()
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

            // Ствол под ногами, на 12 px ниже центра, — в зоне подбора.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 200f; v.grounded = true; v.hasPickup = true; v.px = 0f; v.py = 12f;
            bool picked = false;
            for (int i = 0; i < GrabGap + 2; i++)
                if ((Decide(ref v, ref s) & BotInput.GRAB) != 0) picked = true;
            if (!picked) return "подбор: ствол под ногами не поднимает";

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

            return null;
        }
    }
}
