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

        // В сети бот — локальный игрок того, у кого стоит мод, как второй геймпад: свой
        // сетевой профиль, команда и утка, а остальным игра сама рассылает его как обычного
        // игрока. Своих локальных уток ведёт сама их машина, поэтому боты работают и у хоста,
        // и в гостях. Уходя в сеть, боты не снимаются, а ждут профиля (profile == null):
        // - в гостях профиль даёт игра — при входе в чужое лобби она приводит всех из списка
        //   игроков (FillMatchmakingProfiles) и раздаёт их новым профилям ввод из этого
        //   списка; наш BotInput — метка, по ней профиль и находим (Adopt);
        // - у хоста из списка ботов вычёркивает DropBotsFromMatchmaking, и сажает их мод:
        //   JoinLocalDuck. Мест нет — повтор раз в секунду: кто-то может уйти.
        // Состав в гостях — тот, с которым пришли: JoinLocalDuck у клиента не работает.
        private static int _onlineRetry;
        private static bool _noRoomLogged;   // «мест нет» — одна строка, а не раз в секунду

        // Офлайновая посадка снимается (вечный профиль слота, коробка), бот остаётся
        // ждать сетевого профиля с тем же BotInput.
        private static void GoOnline(List<ProfileBox2> liveBoxes)
        {
            for (int i = 0; i < bots.Count; i++)
            {
                Bot b = bots[i];
                Release(b, liveBoxes != null && liveBoxes.Contains(b.box));
                b.online = true;
                b.profile = null;
                b.box = null;
                b.savedBoxInput = null;
            }
            Log("мод: лобби стало сетевым, " + (Network.isServer ? "мы хост" : "мы в гостях")
                + ", ботов ждут сетевых профилей: " + bots.Count);
            _framesSinceChange = 0;
        }

        private static void SyncSlotsOnline()
        {
            AdoptBots();
            if (!Network.isServer) return;

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
                if (BotSettings.slots[s].level == 0) continue;
                Bot b = BotOf(s);
                if (b != null && b.profile != null) continue;

                BotInput input = b != null ? b.input : new BotInput(s);
                Profile p = DuckNetwork.JoinLocalDuck(input);
                if (p == null)
                {
                    if (!_noRoomLogged) Log("бот " + (s + 1) + ": в сетевом лобби нет свободного места, жду");
                    _noRoomLogged = true;
                    _onlineRetry = 60;
                    return;
                }
                _noRoomLogged = false;
                if (b == null)
                {
                    b = new Bot();
                    b.slot = s;
                    b.online = true;
                    b.input = input;
                    bots.Add(b);
                }
                b.level = BotSettings.slots[s].level;
                b.profile = p;
                _framesSinceChange = 0;
                Log("бот " + (s + 1) + " (" + BotSettings.levelNames[b.level] + ") зашёл в сетевое лобби: профиль "
                    + p.name + ", слот " + p.slotType);
            }
        }

        private static Bot BotOf(int slot)
        {
            for (int i = 0; i < bots.Count; i++)
                if (bots[i].slot == slot) return bots[i];
            return null;
        }

        // Свои сетевые профили с нашим BotInput — боты, которых игра привела с нами.
        // Бот мог и потеряться по дороге: если вход в чужое лобби идёт через TitleScreen,
        // там ботов снимают, а BotInput в списке игроков остаётся. Тогда заводим его
        // заново по номеру слота из ввода — иначе утка стояла бы у друга без мозга.
        private static void AdoptBots()
        {
            List<Profile> ps = DuckNetwork.profiles;
            for (int i = 0; i < ps.Count; i++)
            {
                Profile p = ps[i];
                BotInput input = p.inputProfile as BotInput;
                if (input == null || p.connection != DuckNetwork.localConnection) continue;
                Bot b = null;
                for (int j = 0; j < bots.Count; j++)
                    if (bots[j].input == input) { b = bots[j]; break; }
                if (b != null && b.profile == p) continue;
                if (b == null)
                {
                    b = new Bot();
                    b.slot = input.slot;
                    b.online = true;
                    b.input = input;
                    bots.Add(b);
                }
                b.level = BotSettings.slots[b.slot].level;
                b.profile = p;
                _framesSinceChange = 0;
                Log("бот " + (b.slot + 1) + " (" + BotSettings.levelNames[b.level] + ") пришёл с нами в сеть: профиль "
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

        // Уходя в сеть из лобби, игра собирает в этот список всех, у кого есть команда,
        // вместе с их вводом. Хостя, DuckNetwork.Host сажает каждого из списка локальным
        // игроком. Этот путь для ботов не проверен, и есть повод его опасаться:
        // PrepareForOnline сразу после Host переносит команды по номерам коробок, а боты
        // сидят в коробках с конца. Проверена 20.10 посадка через JoinLocalDuck — поэтому
        // хостя ботов из списка вычёркиваем и сажаем сами (SyncSlotsOnline). В гостях
        // список оставляем: так боты и приходят с нами.
        public static void DropBotsFromMatchmaking()
        {
            int removed = UIMatchmakingBox.core.matchmakingProfiles.RemoveAll(p => p.inputProfile is BotInput);
            if (removed > 0) Log("мод: хостим, ботов в список игроков не берём, посадим сами: " + removed);
        }
    }
}
