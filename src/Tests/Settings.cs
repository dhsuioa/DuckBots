using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // bots.txt правят руками: кривая строка не роняет мод и не сажает бота.
        private static string SettingsTest()
        {
            int[] l = BotSettings.Parse("2, 0,4,1");
            if (l.Length != BotSettings.Max || l[0] != 2 || l[1] != 0 || l[2] != 4 || l[3] != 1 || l[6] != 0)
                return "настройки: обычная строка прочитана неверно";
            if (BotSettings.Parse(null)[0] != 0 || BotSettings.Parse("")[0] != 0)
                return "настройки: без файла должны быть все OFF";
            l = BotSettings.Parse("abc,9,-1,3");
            if (l[0] != 0 || l[1] != 0 || l[2] != 0 || l[3] != 3)
                return "настройки: мусор и уровни вне списка должны давать OFF";
            return null;
        }
    }
}
