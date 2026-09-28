using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Подкат от выстрела: разгон, подкат, лежать, пока целятся, и встать. Враг справа,
        // в 100 пикселях; прогонка той же физикой, что у игры, только пригнувшаяся утка
        // не разгоняется, а подкат держится, пока зажат DOWN, — как в Duck.UpdateMove.
        private static string DodgeTest()
        {
            // --- бежим, в нас целятся: через DodgeDelay кадров — DOWN ---
            SimDuck d = new SimDuck();
            d.grounded = true;
            d.hs = 2f;
            bool sliding = false;
            int facing = 1;
            BotState s = new BotState();
            for (int f = 0; f <= DodgeDelay; f++)
            {
                float hs = d.hs;
                bool down = (DodgeFrame(ref d, ref sliding, ref facing, ref s, true, false, false) & BotInput.DOWN) != 0;
                if (f < DodgeDelay && down) return "подкат: ложится раньше, чем заметил прицел (DodgeDelay)";
                if (f < DodgeDelay) continue;
                if (!down) return "подкат: на бегу в него целятся, а DOWN не нажат";
                if (hs <= 1f) return "подкат: DOWN на скорости " + hs + " — это присед, а не подкат";
                if (s.how != HowDodge) return "причина: подкат записан как «" + s.how + "»";
            }

            // --- стоим: сначала разгон, DOWN только после |hs| > 1 ---
            d = new SimDuck();
            d.grounded = true;
            sliding = false;
            facing = 1;
            s = new BotState();
            bool ran = false, slid = false;
            for (int f = 0; f < DodgeDelay + 10 && !slid; f++)
            {
                float hs = d.hs;
                ushort m = DodgeFrame(ref d, ref sliding, ref facing, ref s, true, false, false);
                if ((m & BotInput.DOWN) != 0)
                {
                    if (Math.Abs(hs) <= 1f) return "подкат: DOWN стоя (скорость " + hs + ") — пригнувшись, утка уже не разгонится";
                    slid = true;
                }
                else if (f >= DodgeDelay && (m & BotInput.RIGHT) != 0)
                {
                    ran = true;
                    if (s.how != HowDodgeRun) return "причина: разгон для подката записан как «" + s.how + "»";
                }
            }
            if (!ran || !slid) return "подкат: стоя под прицелом, бот не разогнался и не лёг";

            // --- лежит, пока целятся; перестали — лежит ещё DodgeAfter и встаёт ---
            // Лёжа бот стоит на месте, и MoveTo через дюжину кадров считал его застрявшим
            // и прыгал — JUMP поднимает утку прямо под пулю.
            for (int f = 0; f < 30; f++)
            {
                ushort m = DodgeFrame(ref d, ref sliding, ref facing, ref s, true, false, false);
                if ((m & BotInput.DOWN) == 0) return "подкат: встаёт, хотя в него всё ещё целятся";
                if ((m & BotInput.JUMP) != 0) return "подкат: лёжа под прицелом прыгает — встаёт под пулю";
            }
            int up = -1;
            for (int f = 0; f < DodgeAfter + 3 && up < 0; f++)
                if ((DodgeFrame(ref d, ref sliding, ref facing, ref s, false, false, false) & BotInput.DOWN) == 0) up = f;
            if (up < 0) return "подкат: целиться перестали, а бот так и лежит";
            if (up < DodgeAfter - 1) return "подкат: встаёт через " + up + " кадр. — выстрел мог ещё лететь";

            // --- дольше DodgeMax не лежит, потом DodgeRest не ложится ---
            d = new SimDuck();
            d.grounded = true;
            d.hs = 2f;
            sliding = false;
            facing = 1;
            s = new BotState();
            int held = 0, rest = -1;
            for (int f = 0; f < DodgeDelay + DodgeMax + DodgeRest + 10; f++)
            {
                bool down = (DodgeFrame(ref d, ref sliding, ref facing, ref s, true, false, false) & BotInput.DOWN) != 0;
                if (rest < 0)
                {
                    if (down) held++;
                    else if (held > 0) rest = 0;
                    if (held > DodgeMax + 1) return "подкат: лежит дольше DodgeMax, в лежачего тоже попадают";
                }
                else if (rest < DodgeRest - 1)
                {
                    if (down) return "подкат: после долгого лежания ложится снова через " + rest + " кадр.";
                    rest++;
                }
            }
            if (rest < 0) return "подкат: под прицелом лежит бесконечно";

            // --- разгон туда, куда уже едем: катится влево, маршрут зовёт вправо ---
            // Разворот съедает 5–6 кадров, а подкат можно начать в своей же стороне:
            // 28.09 так гибли в разгоне боты, у которых маршрут только что сменился.
            d = new SimDuck();
            d.grounded = true;
            d.hs = -0.6f;
            sliding = false;
            facing = -1;
            s = new BotState();
            ushort first = DodgeFrame(ref d, ref sliding, ref facing, ref s, true, false, false);
            if ((first & BotInput.RIGHT) != 0) return "подкат: катится влево, а разгоняется вправо — теряет кадры на разворот";
            if ((first & BotInput.LEFT) == 0) return "подкат: катится влево и не разгоняется дальше влево";

            // --- разгон не в пропасть: справа обрыв — разгон влево ---
            // Бот на позиции для выстрела стоит и никуда не идёт; разгон по умолчанию —
            // к стрелку, но там пропасть.
            d = new SimDuck();
            d.grounded = true;
            sliding = false;
            facing = 1;
            s = new BotState();
            for (int f = 0; f <= DodgeDelay; f++)
            {
                ushort m = DodgeFrame(ref d, ref sliding, ref facing, ref s, true, true, true);
                if ((m & BotInput.RIGHT) != 0 && f == DodgeDelay) return "подкат: разгон в пропасть";
                if (f == DodgeDelay && (m & BotInput.LEFT) == 0) return "подкат: справа пропасть, а влево бот не разгоняется";
            }

            return null;
        }

        // Один кадр под прицелом врага справа: обстановка, решение, физика.
        private static ushort DodgeFrame(ref SimDuck d, ref bool sliding, ref int facing, ref BotState s,
                                         bool threat, bool atSpot, bool abyssRight)
        {
            BotView v = new BotView();
            v.hasEnemy = true; v.ex = 100f; v.holding = 1; v.grounded = true;
            v.hs = d.hs; v.speedAbs = Math.Abs(d.hs); v.facingRight = facing > 0;
            v.sliding = sliding; v.threatened = threat; v.threatDx = 100f;
            v.atSpot = atSpot;
            v.gapRight = abyssRight; v.abyssRight = abyssRight;
            if (!atSpot) { v.hasEnemyStep = true; v.enemyStepX = 32f; v.enemyStepEdge = NavMap.Walk; }
            ushort m = Decide(ref v, ref s);
            bool down = (m & BotInput.DOWN) != 0;
            if (down && Math.Abs(d.hs) > 1f) sliding = true;
            if (!down) sliding = false;
            int dir = (m & BotInput.RIGHT) != 0 ? 1 : ((m & BotInput.LEFT) != 0 ? -1 : 0);
            if (dir != 0) facing = dir;
            DuckPhysics.Controls(ref d, down ? 0 : dir, false);   // пригнувшись, не разгоняется
            return m;
        }
    }
}
