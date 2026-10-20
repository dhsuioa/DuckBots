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

        // В сети бот — локальный игрок хоста, как второй геймпад: JoinLocalDuck даёт ему
        // свой сетевой профиль, команду и утку, а клиентам игра сама рассылает его как
        // обычного игрока. Мест нет — повторяем раз в секунду: кто-то может уйти.
        private static int _onlineRetry;
        private static bool _noRoomLogged;   // «мест нет» — одна строка, а не раз в секунду

        private static void SyncSlotsOnline()
        {
            // Выключенные в меню — из лобби долой.
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                Bot b = bots[i];
                if (BotSettings.slots[b.slot].level != 0)
                {
                    b.level = BotSettings.slots[b.slot].level;
                    continue;
                }
                ReleaseOnline(b);
                bots.RemoveAt(i);
                Log("бот " + (b.slot + 1) + " выключен и ушёл из сетевого лобби");
                _onlineRetry = 0;
                _framesSinceChange = 0;
            }

            // Первый локальный профиль игра делает профилем хоста (PrepareProfile:
            // localProfile, hostProfile) — бот не должен оказаться им ни при каком порядке.
            if (DuckNetwork.localProfile == null) return;
            if (_onlineRetry > 0) { _onlineRetry--; return; }
            for (int s = 0; s < BotSettings.Max; s++)
            {
                if (BotSettings.slots[s].level == 0 || HasBot(s)) continue;

                BotInput input = new BotInput("bot" + (s + 1));
                Profile p = DuckNetwork.JoinLocalDuck(input);
                if (p == null)
                {
                    if (!_noRoomLogged) Log("бот " + (s + 1) + ": в сетевом лобби нет свободного места, жду");
                    _noRoomLogged = true;
                    _onlineRetry = 60;
                    return;
                }
                _noRoomLogged = false;
                Bot b = new Bot();
                b.slot = s;
                b.level = BotSettings.slots[s].level;
                b.online = true;
                b.input = input;
                b.profile = p;
                bots.Add(b);
                _framesSinceChange = 0;
                Log("бот " + (s + 1) + " (" + BotSettings.levelNames[b.level] + ") зашёл в сетевое лобби: профиль "
                    + p.name + ", слот " + p.slotType);
            }
        }

        // Kick для слота Local — штатный уход местного игрока: клиентам уходит
        // NMClientDisconnect, профиль сбрасывается. Ввод ResetProfile не чистит, а сеттер
        // inputProfile следующего хозяина профиля дёрнул бы Rumble() у нашего BotInput, —
        // поэтому ссылку обнуляем сами.
        // ponytail: Kick ставит слоту Open, даже если до бота он был Friend или Invite.
        private static void ReleaseOnline(Bot b)
        {
            if (b.profile == null) return;
            if (Network.isActive && b.profile.connection == DuckNetwork.localConnection)
                DuckNetwork.Kick(b.profile);
            b.profile.SetInputProfileLink(null);
        }

        // Уходя в сеть из лобби (своя игра, приглашение, поиск), игра собирает в этот список
        // всех, у кого есть команда, вместе с их вводом, и сажает каждого локальным игроком.
        // Посаженные офлайн боты уехали бы туда со своим BotInput, но уже мимо мода: у хоста
        // он сажает их сам (SyncSlotsOnline), а в чужую игру ботов не везём вовсе.
        public static void DropBotsFromMatchmaking()
        {
            int removed = UIMatchmakingBox.core.matchmakingProfiles.RemoveAll(p => p.inputProfile is BotInput);
            if (removed > 0) Log("мод: уходим в сеть, ботов в список игроков не берём: " + removed);
        }
    }
}
