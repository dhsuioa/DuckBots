using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        public const string WhyLobbyWait = "лобби: жду, пока человек войдёт в портал";
        public const string WhyLobbyGun = "лобби: иду за дробовиком";
        public const string WhyLobbyShoot = "лобби: бью стекло из дробовика";
        public const string WhyLobbyExit = "лобби: выхожу в портал";
        public const string WhyLobbyBeam = "лобби: в портале";
        public const string WhyLobbyLeave = "лобби: выхожу из портала вслед за человеком";

        // Выход в портал, как у человека: дробовик со ступеньки, стекло проёма, шаг в проём —
        // луч (TeamBeam) забирает утку сам. Человек вышел из портала — CANCEL в центре луча.
        public static ushort Lobby(ref LobbyView v, ref BotState s)
        {
            if (s.jumpRest > 0) s.jumpRest--;
            return HoldJump(LobbyThink(ref v, ref s), ref s);
        }

        private static ushort LobbyThink(ref LobbyView v, ref BotState s)
        {
            if (v.inBeam)
            {
                s.jumpHold = 0;
                if (v.humanReady) { s.why = WhyLobbyBeam; return 0; }
                s.why = WhyLobbyLeave;
                return v.centered && Pulse(ref s.grabTimer, 1, 1) ? BotInput.CANCEL : (ushort)0;
            }
            if (!v.humanReady) { s.why = WhyLobbyWait; return 0; }

            if (v.glass && !v.gunHeld && v.hasGun)
            {
                s.why = WhyLobbyGun;
                if (InGrabReach(v.gunDx, v.gunDy)) return Grab(ref s);
                return LobbyWalk(v.gunDx > 0f ? 1 : -1, ref v, ref s);
            }
            if (v.glass && v.gunHeld)
            {
                s.why = WhyLobbyShoot;
                bool toRight = v.glassDx > 0f;
                if (toRight != v.facingRight) return Toward(toRight);
                if (Math.Abs(v.glassDx) - GlassReach < ShootMin) return Backward(toRight);
                return Pulse(ref s.shootTimer, ShootHold, ShootGap) ? BotInput.SHOOT : (ushort)0;
            }
            // Без дробовика тоже к выходу: упрётся в стекло — заберёт запасной путь (BeamPatience).
            s.why = WhyLobbyExit;
            return LobbyWalk(v.exitDir, ref v, ref s);
        }

        // Стенка у ног — ступенька в 13 px: короткий прыжок.
        private static ushort LobbyWalk(int dir, ref LobbyView v, ref BotState s)
        {
            if (dir > 0 ? v.wallRight : v.wallLeft) StartJump(ref s, JumpTap, v.grounded, s.why, 0f, 0f);
            return Toward(dir > 0);
        }
    }
}
