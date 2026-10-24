using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        public const int DodgeDelay = 0;       // ручка силы для этапа 3: через сколько кадров бот замечает прицел
        public const int DodgeAfter = 12;
        public const int DodgeMax = 90;        // в лежачего тоже попадают
        public const int DodgeRest = 30;

        public const string HowDodgeRun = "разгон для подката: в меня целятся";
        public const string HowDodge = "лежу в подкате: в меня целятся";

        // Подкат от выстрела. В подкате утка 11 px высотой против 22 стоя (присед — 16), и пуля
        // стоящего проходит над ней. Подкат начинается только на бегу (|hs| > 1), пригнувшись
        // утка не разгоняется, а лежит, пока зажат DOWN.
        private static ushort Dodge(ushort m, ref BotView v, ref BotState s)
        {
            s.threatFrames = v.threatened ? s.threatFrames + 1 : 0;
            if (s.dodgeRest > 0) s.dodgeRest--;
            bool aimed = s.threatFrames > DodgeDelay;
            if (!v.grounded || s.mode == Flee || v.netted || v.ragdoll)
            {
                EndDodge(ref s);
                return m;
            }
            if (s.dodgeFrames == 0)
            {
                if (!aimed || s.dodgeRest > 0) return m;
                s.dodgeDir = 0;
            }
            s.dodgeFrames++;
            if (aimed) s.dodgeLeft = DodgeAfter;
            else if (--s.dodgeLeft <= 0) { EndDodge(ref s); return m; }
            if (s.dodgeFrames > DodgeMax) { EndDodge(ref s); s.dodgeRest = DodgeRest; return m; }

            // Разгон туда, куда уже катимся (разворот съедает 5–6 кадров), иначе куда шли, иначе к стрелку.
            bool down = v.sliding || Math.Abs(v.hs) > 1f;
            if (!down && s.dodgeDir == 0)
            {
                int dir = v.hs > 0.3f ? 1 : v.hs < -0.3f ? -1
                    : (m & BotInput.RIGHT) != 0 ? 1 : (m & BotInput.LEFT) != 0 ? -1 : (v.threatDx > 0f ? 1 : -1);
                if (Abyss(ref v, dir)) dir = -dir;
                if (Abyss(ref v, dir)) { EndDodge(ref s); s.dodgeRest = DodgeRest; return m; }
                s.dodgeDir = dir;
            }
            // Лёжа не прыгать и не копить застревание: «упёрся: прыжок» поднял бы под пулю.
            s.jumpHold = 0;
            s.progAt = 0;
            s.stuckFrames = 0;
            if (down)
            {
                s.how = HowDodge;
                return (ushort)((m & unchecked((ushort)~BotInput.JUMP)) | BotInput.DOWN);
            }
            s.how = HowDodgeRun;
            return (ushort)((m & unchecked((ushort)~(BotInput.LEFT | BotInput.RIGHT | BotInput.JUMP | BotInput.DOWN)))
                | Toward(s.dodgeDir > 0));
        }

        private static void EndDodge(ref BotState s)
        {
            s.dodgeFrames = 0;
            s.dodgeDir = 0;
            s.dodgeLeft = 0;
        }
    }
}
