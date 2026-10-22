using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Стволы со своей механикой выстрела.
        private static string WeaponsTest()
        {
            // --- огнемёт: SHOOT зажат всё время, пока есть линия ---
            // Огонь идёт только при зажатом SHOOT и с 5-го кадра: импульсы его не зажигали.
            BotView v = new BotView();
            BotState s = new BotState();
            v.hasEnemy = true; v.ex = 40f; v.canShoot = true; v.holding = 1; v.holdFire = true;
            v.facingRight = true; v.grounded = true;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.SHOOT) == 0)
                    return "огнемёт: отпустил SHOOT на кадре " + i + " — огонь не пойдёт";
            if (s.why != WhyShoot) return "причина: огнемёт записан как «" + s.why + "»";

            // --- огнемёт вверх не стреляет: огонь падает на стрелка ---
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 4f; v.ey = -40f; v.canShootUp = true; v.holding = 1; v.holdFire = true;
            v.facingRight = true; v.grounded = true;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.SHOOT) != 0) return "огнемёт: стреляет вверх, на себя";

            return null;
        }
    }
}
