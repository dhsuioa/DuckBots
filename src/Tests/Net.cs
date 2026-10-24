using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Сетка и сеткомёт: вырваться, поймать, донести до пропасти и бросить.
        private static string NetTest()
        {
            BotView v;
            BotState s;

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

            return null;
        }
    }
}
