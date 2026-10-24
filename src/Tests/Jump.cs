using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Прыжки: удержание, кнопка в воздухе, симуляция программы и её исполнение.
        private static string JumpTest()
        {
            BotView v;
            BotState s;
            int known;

            // Удержание по подъёму; через яму и далеко — до упора.
            if (JumpHold(0f, -48f, false) != JumpFull) return "прыжок: на три клетки жмёт не до упора";
            if (JumpHold(0f, -30f, false) != JumpMid) return "прыжок: на две клетки не тот замах";
            if (JumpHold(16f, -16f, false) != JumpTap) return "прыжок: на ступеньку прыгает во всю силу";
            if (JumpHold(0f, 0f, true) != JumpFull) return "прыжок: через яму жмёт вполсилы";
            if (JumpHold(-106f, -25f, false) != JumpFull)
                return "прыжок: далёкий прыжок держит вполсилы — недолёт, как на space12_8";

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

            return null;
        }
    }
}
