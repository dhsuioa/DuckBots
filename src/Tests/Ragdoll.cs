using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Рэгдолл: встать новым нажатием JUMP. Обзор врёт (утка на 5000 выше тела), а подкат прыжок не глушит.
        private static string RagdollTest()
        {
            BotView v = new BotView();
            BotState s = new BotState();
            v.ragdoll = true; v.grounded = true; v.facingRight = true;
            v.hasEnemy = true; v.ex = 2f; v.ey = 4956f; v.holding = 1; v.canShoot = true;
            v.hasEnemyStep = true; v.enemyStepX = 36f; v.enemyStepY = 4987f; v.enemyStepEdge = NavMap.Jump;
            v.threatened = true; v.threatDx = 40f; v.hs = 2f;
            int presses = 0;
            ushort was = 0, all = 0;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.JUMP) != 0 && (was & BotInput.JUMP) == 0) presses++;
                was = m;
                all |= m;
            }
            if (presses < 5) return "рэгдолл: бот не жмёт прыжок, чтобы встать, нажатий " + presses;
            if ((all & ~BotInput.JUMP) != 0) return "рэгдолл: бот жмёт не только прыжок (кнопки " + all + ")";
            if (s.mode != Ragdoll) return "рэгдолл: режим " + modeNames[s.mode] + " — сторож счёл бы лежание застреванием";
            if (s.why != WhyRagdoll) return "причина: рэгдолл записан как «" + s.why + "»";
            return null;
        }
    }
}
