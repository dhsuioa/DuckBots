using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Лобби: выйти в портал вслед за человеком — взять дробовик на ступеньке, разбить
        // стекло, шагнуть в проём; человек вышел из портала — выйти CANCEL в его центре.
        private static string LobbyTest()
        {
            // --- человек не в портале: стоять ---
            LobbyView v = new LobbyView();
            BotState s = new BotState();
            v.glass = true; v.hasGun = true; v.gunDx = 60f; v.gunDy = -13f; v.exitDir = 1; v.grounded = true;
            ushort all = 0;
            for (int i = 0; i < 10; i++) all |= Lobby(ref v, ref s);
            if (all != 0) return "лобби: человек не в портале, а бот жмёт кнопки " + all;

            // --- человек в портале, дробовик справа за ступенькой: идти и запрыгнуть ---
            v.humanReady = true; v.wallRight = true;
            s = new BotState();
            all = 0;
            for (int i = 0; i < 10; i++) all |= Lobby(ref v, ref s);
            if ((all & BotInput.RIGHT) == 0) return "лобби: к дробовику не идёт";
            if ((all & BotInput.JUMP) == 0) return "лобби: у ступеньки не запрыгивает";

            // --- дробовик в зоне подбора: взять ---
            v.wallRight = false; v.gunDx = 4f; v.gunDy = 8f;
            s = new BotState();
            all = 0;
            for (int i = 0; i < GrabGap + 2; i++) all |= Lobby(ref v, ref s);
            if ((all & BotInput.GRAB) == 0) return "лобби: дробовик рядом, а бот его не берёт";

            // --- дробовик в руках, до стекла 40 px: стрелять, не сходя с места ---
            v = new LobbyView();
            s = new BotState();
            v.humanReady = true; v.glass = true; v.gunHeld = true; v.hasGun = true; v.glassDx = 40f;
            v.facingRight = true; v.exitDir = 1; v.grounded = true;
            bool shot = false;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Lobby(ref v, ref s);
                if ((m & (BotInput.LEFT | BotInput.RIGHT)) != 0) return "лобби: с дробовиком на позиции бот ходит (кнопки " + m + ")";
                if ((m & BotInput.SHOOT) != 0) shot = true;
            }
            if (!shot) return "лобби: дробовик в руках, а стекло не бьёт";

            // --- до стекла 20 px: ствол упрётся — отойти лицом к стеклу ---
            v.glassDx = 20f;
            s = new BotState();
            for (int i = 0; i < 10; i++)
            {
                ushort m = Lobby(ref v, ref s);
                if ((m & BotInput.SHOOT) != 0) return "лобби: стреляет в стекло вплотную";
                if ((m & BotInput.LEFT) == 0 || (m & BotInput.STRAFE) == 0)
                    return "лобби: вплотную к стеклу не отходит лицом к нему";
            }

            // --- стекло разбито: к выходу ---
            v.glass = false;
            s = new BotState();
            ushort exit = Lobby(ref v, ref s);
            if ((exit & BotInput.RIGHT) == 0 || (exit & BotInput.SHOOT) != 0)
                return "лобби: стекло разбито, а бот не идёт в проём (кнопки " + exit + ")";
            if (s.why != WhyLobbyExit) return "причина: выход в портал записан как «" + s.why + "»";

            // --- в портале: пока человек там — ничего; вышел — CANCEL в центре луча ---
            v = new LobbyView();
            s = new BotState();
            v.inBeam = true; v.centered = true; v.humanReady = true;
            for (int i = 0; i < 10; i++)
                if (Lobby(ref v, ref s) != 0) return "лобби: в портале рядом с человеком жмёт кнопки";
            v.humanReady = false;
            int presses = 0;
            ushort was = 0;
            for (int i = 0; i < 20; i++)
            {
                ushort m = Lobby(ref v, ref s);
                if ((m & BotInput.CANCEL) != 0 && (was & BotInput.CANCEL) == 0) presses++;
                was = m;
            }
            if (presses < 5) return "лобби: человек вышел из портала, а бот не жмёт CANCEL (нажатий " + presses + ")";
            v.centered = false;
            s = new BotState();
            for (int i = 0; i < 10; i++)
                if (Lobby(ref v, ref s) != 0) return "лобби: жмёт не в центре луча — луч не услышит";
            return null;
        }
    }
}
