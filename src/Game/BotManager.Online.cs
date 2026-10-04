using System;
using System.Collections.Generic;
using System.Reflection;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        // --- сеть ---

        // Входя в сетевое лобби, игра сверяет отпечаток модов (ModLoader.modHash: хеш имён
        // и версий всех включённых модов) и рвёт соединение, если он разный. Мод не добавляет
        // ни своих Thing, ни NetMessage, так что для сети его будто нет, — и отпечаток мы
        // считаем без себя: друг без мода заходит к нам, мы — в ванильные лобби. Формулу не
        // повторяем: на время вызова убираем себя из списка модов и зовём настоящий GetModHash.
        // Зовётся в первом кадре (Tick), а не из OnPostInitialize: тот игра вызывает изнутри
        // foreach по этому же _sortedAccessibleMods, и правка списка роняла загрузку игры
        // («Коллекция была изменена»). OnStart зовётся так же.
        public static Mod hideFromHash;

        public static void HideFromModHash(Mod self)
        {
            BindingFlags st = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            FieldInfo listField = typeof(ModLoader).GetField("_sortedAccessibleMods", st);
            MethodInfo hashMethod = typeof(ModLoader).GetMethod("GetModHash", st);
            PropertyInfo hashProp = typeof(ModLoader).GetProperty("modHash", st);
            IList<Mod> mods = listField == null ? null : listField.GetValue(null) as IList<Mod>;
            int at = mods == null ? -1 : mods.IndexOf(self);
            if (hashMethod == null || hashProp == null || at < 0)
            {
                Log("мод: ВНИМАНИЕ отпечаток модов не подменён, в сети мод виден — без мода к нам не войти");
                return;
            }
            string before = hashProp.GetValue(null, null) as string;
            mods.RemoveAt(at);
            try
            {
                hashProp.SetValue(null, hashMethod.Invoke(null, null), null);
            }
            finally
            {
                mods.Insert(at, self);
            }
            Log("мод: отпечаток модов для сети " + before + " → " + hashProp.GetValue(null, null));
        }

        // Уходя в сеть из лобби (своя игра, приглашение, поиск), игра собирает в этот список
        // всех, у кого есть команда, вместе с их вводом, и сажает каждого локальным игроком.
        // Посаженные офлайн боты уехали бы туда со своим BotInput, а мод в сети ими не правит:
        // стояли бы столбом в чужой игре. Поэтому ботов из списка вычёркиваем.
        public static void DropBotsFromMatchmaking()
        {
            int removed = UIMatchmakingBox.core.matchmakingProfiles.RemoveAll(p => p.inputProfile is BotInput);
            if (removed > 0) Log("мод: уходим в сеть, ботов в список игроков не берём: " + removed);
        }
    }
}
