using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        public const float GlassReach = DuckPhysics.HalfWidth + 3f;   // зазор меряется от бока утки до бока стекла
        public const float RunUp = 32f;
        public const float SlideGap = 16f;     // отсюда подкат долетает до стекла
        public const float ShootMin = 16f;     // ближе ствол упирается в стекло и задирается вверх
        public const float ShootFrom = 24f;
        public const int BackFrames = 24;
        public const int ShootFrames = 40;
        public const int SmashTries = 3;

        public const string HowSmashBack = "отхожу от стекла";
        public const string HowSmashShoot = "стреляю в стекло";
        public const string HowSmashSlide = "бью стекло подкатом";

        // Стекло бьёт пуля или подкат с разгона, шагом его не разбить. Программа держится на целом
        // стекле, а не на шаге «стекло»: маршрут пересчитывается от клетки, куда бот отошёл.
        private static bool Smash(float dx, int edge, bool routed, ref BotView v, ref BotState s, out ushort m)
        {
            m = 0;
            int dir = dx > 1f ? 1 : (dx < -1f ? -1 : 0);
            // На разбеге шаг ведёт к центру следующей клетки, и dx бывает нулём: держать своё направление.
            if (s.smashDir != 0) dir = s.smashDir;
            bool ahead = routed && dir != 0 && v.hasGlass && (v.glassDx > 0f) == (dir > 0);
            if (!ahead)
            {
                s.smashDir = 0; s.smashRun = false; s.smashFrames = 0; s.smashTries = 0;
                return false;
            }
            if (s.smashTries >= SmashTries) return false;
            if (s.smashDir != dir)
            {
                if (edge != NavMap.Smash) return false;
                s.smashDir = dir; s.smashRun = false; s.smashFrames = 0;
            }
            s.smashFrames++;
            s.stuckFrames = 0;
            float gap = Math.Abs(v.glassDx) - GlassReach;
            ushort toward = dir > 0 ? BotInput.RIGHT : BotInput.LEFT;
            ushort away = dir > 0 ? BotInput.LEFT : BotInput.RIGHT;
            bool room = dir > 0 ? !v.gapLeft : !v.gapRight;
            bool shoot = v.glassGun && s.smashTries == 0;
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
                s.smashTries++; s.smashRun = false; s.smashFrames = 0;
                return true;
            }
            // Порог 1 — порог подката в самой игре: выше он отпускал бы DOWN посреди подката.
            m = toward;
            if (gap <= SlideGap && speed > 1f) m |= BotInput.DOWN;
            return true;
        }
    }
}
