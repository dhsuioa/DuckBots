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

        // Лобби: выйти в портал вслед за человеком так же, как выходит он сам. Из комнаты
        // ведёт проём над ступенькой, закрытый стеклом, а на ступеньке лежит дробовик
        // комнаты. Взять его, разбить стекло и шагнуть в проём: луч (TeamBeam) стоит
        // вплотную к нему и забирает утку сам. Человек вышел из портала — нажать CANCEL
        // в центре луча, и он вернёт утку в комнату.
        public static ushort Lobby(ref LobbyView v, ref BotState s)
        {
            if (s.jumpRest > 0) s.jumpRest--;
            ushort m = LobbyThink(ref v, ref s);
            if (s.jumpHold > 0)
            {
                m |= BotInput.JUMP;
                if (--s.jumpHold == 0) s.jumpRest = JumpRest;
            }
            return m;
        }

        private static ushort LobbyThink(ref LobbyView v, ref BotState s)
        {
            if (v.inBeam)
            {
                s.jumpHold = 0;
                if (v.humanReady) { s.why = WhyLobbyBeam; return 0; }
                // Луч слышит CANCEL только в своём центре (|x − луч| < 2), а туда несёт сам.
                s.why = WhyLobbyLeave;
                return v.centered && Pulse(ref s.grabTimer, 1, 1) ? BotInput.CANCEL : (ushort)0;
            }
            if (!v.humanReady) { s.why = WhyLobbyWait; return 0; }

            if (v.glass && !v.gunHeld && v.hasGun)
            {
                s.why = WhyLobbyGun;
                if (InGrabReach(v.gunDx, v.gunDy))
                    return Pulse(ref s.grabTimer, 1, GrabGap) ? BotInput.GRAB : (ushort)0;
                return LobbyWalk(v.gunDx > 0f ? 1 : -1, ref v, ref s);
            }
            if (v.glass && v.gunHeld)
            {
                // Как у удара по стеклу (Smash): вплотную ствол упирается в стекло и
                // задирается вверх — отойти лицом к нему.
                s.why = WhyLobbyShoot;
                bool toRight = v.glassDx > 0f;
                if (toRight != v.facingRight) return toRight ? BotInput.RIGHT : BotInput.LEFT;
                if (Math.Abs(v.glassDx) - GlassReach < ShootMin)
                    return (ushort)(BotInput.STRAFE | (toRight ? BotInput.LEFT : BotInput.RIGHT));
                return Pulse(ref s.shootTimer, ShootHold, ShootGap) ? BotInput.SHOOT : (ushort)0;
            }
            // Стекло разбито — в проём. Дробовика нет, а стекло цело — тоже к выходу:
            // упрётся, и через BeamPatience его заберёт запасной путь.
            s.why = WhyLobbyExit;
            return LobbyWalk(v.exitDir, ref v, ref s);
        }

        // Шаг в сторону dir. Стенка у ног — ступенька в 13 пикселей: короткий прыжок.
        private static ushort LobbyWalk(int dir, ref LobbyView v, ref BotState s)
        {
            if (dir > 0 ? v.wallRight : v.wallLeft) StartJump(ref s, JumpTap, v.grounded, s.why, 0f, 0f);
            return dir > 0 ? BotInput.RIGHT : BotInput.LEFT;
        }
    }
}
