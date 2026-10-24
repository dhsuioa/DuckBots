using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Взрывчатка в руках: вплотную не стрелять — взрыв достаёт стрелка.
        private static string BlastTest()
        {
            // Базука вплотную: отходить лицом к врагу.
            BotView v = new BotView();
            BotState s = new BotState();
            v.hasEnemy = true; v.ex = 30f; v.canShoot = true; v.holding = 1; v.blastInHand = true;
            v.facingRight = true; v.grounded = true;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.SHOOT) != 0) return "взрывчатка: стреляет из базуки в упор";
                if ((m & BotInput.LEFT) == 0 || (m & BotInput.STRAFE) == 0)
                    return "взрывчатка: вплотную не отходит лицом к врагу";
            }
            if (s.why != WhyBlastBack) return "причина: отход со взрывчаткой записан как «" + s.why + "»";

            // Сзади пропасть: стоять и не стрелять.
            v.gapLeft = true; v.abyssLeft = true; s = new BotState();
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.SHOOT) != 0) return "взрывчатка: у пропасти стреляет в упор";
                if ((m & BotInput.LEFT) != 0) return "взрывчатка: отходит в пропасть";
            }

            // На безопасной дистанции стреляет.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = BlastSafe + 20f; v.canShoot = true; v.holding = 1; v.blastInHand = true;
            v.facingRight = true; v.grounded = true;
            bool shot = false;
            for (int i = 0; i < 20; i++) if ((Decide(ref v, ref s) & BotInput.SHOOT) != 0) shot = true;
            if (!shot) return "взрывчатка: издалека не стреляет";

            // Враг над головой близко: вверх тоже не стрелять.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 4f; v.ey = -40f; v.canShootUp = true; v.holding = 1; v.blastInHand = true;
            v.facingRight = true; v.grounded = true;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.SHOOT) != 0) return "взрывчатка: стреляет вверх в упор";

            // Обычный ствол вплотную стреляет.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 30f; v.canShoot = true; v.holding = 1;
            v.facingRight = true; v.grounded = true;
            shot = false;
            for (int i = 0; i < 20; i++) if ((Decide(ref v, ref s) & BotInput.SHOOT) != 0) shot = true;
            if (!shot) return "взрывчатка: обычный ствол вплотную перестал стрелять";

            // Обычный ствол ближе KeepDist: отходит лицом к врагу и стреляет.
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 10f; v.canShoot = true; v.holding = 1;
            v.facingRight = true; v.grounded = true;
            shot = false;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Decide(ref v, ref s);
                if ((m & BotInput.LEFT) == 0 || (m & BotInput.STRAFE) == 0)
                    return "отход: ствол вплотную, а бот не отходит лицом к врагу (кнопки " + m + ")";
                if ((m & BotInput.SHOOT) != 0) shot = true;
            }
            if (!shot) return "отход: отходя, не стреляет";
            if (s.why != WhyBackOff) return "причина: отход со стволом записан как «" + s.why + "»";

            return null;
        }
    }
}
