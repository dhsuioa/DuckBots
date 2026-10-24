using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Карта из строк: так её видно прямо в проверке.
        private static byte[] Cells(string[] rows)
        {
            int w = rows[0].Length;
            byte[] c = new byte[w * rows.Length];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = rows[y][x];
                    byte cell = NavMap.Empty;
                    if (ch == '#' || ch == '|') cell = NavMap.Solid;
                    else if (ch == 'S') cell = NavMap.SpringUp;
                    else if (ch == '/') cell = NavMap.SpringDiagRight;
                    else if (ch == '\\') cell = NavMap.SpringDiagLeft;
                    else if (ch == '=') cell = NavMap.Platform;
                    else if (ch == '^') cell = NavMap.Deadly;
                    c[y * w + x] = cell;
                }
            return c;
        }

        // Самопроверка без игры: мод зовёт её при загрузке, обвязка — снаружи по DuckBots.dll. null — всё хорошо, иначе — что упало.
        public static string SelfTest()
        {
            return BrainTest() ?? MapTest() ?? JumpTest() ?? NetTest() ?? GlassTest() ?? DodgeTest() ?? BlastTest() ?? SpringTest() ?? SettingsTest() ?? StepOffTest() ?? PickupTest() ?? ReachTest() ?? RagdollTest() ?? WeaponsTest() ?? LobbyTest();
        }
    }
}
