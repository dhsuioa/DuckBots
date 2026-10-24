using System.Collections.Generic;
using System.Reflection;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        // В сетевое лобби игра пускает только при одинаковом отпечатке модов (ModLoader.modHash).
        // Мод для сети невидим, поэтому отпечаток считаем без себя: настоящим GetModHash, вынув
        // себя из списка. Только из первого кадра: OnPostInitialize и OnStart игра зовёт изнутри
        // foreach по _sortedAccessibleMods, и правка списка оттуда роняет запуск.
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

        // Бот в сети — локальный игрок того, у кого мод, как второй геймпад. В гостях профиль даёт
        // игра, а мы находим его по BotInput (AdoptBots). У хоста сажаем сами (JoinLocalDuck), мест
        // нет — повтор раз в секунду. Состав в гостях — тот, с которым пришли.
        private static int _onlineRetry;
        private static bool _noRoomLogged;

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

            // Первый локальный профиль игра делает профилем хоста: бот им быть не должен.
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

        // Бот мог потеряться по дороге (через TitleScreen), а его BotInput остался в списке игроков:
        // заводим заново по слоту из ввода.
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

        // ResetProfile ввод не чистит, а сеттер inputProfile следующего хозяина дёрнул бы Rumble() у BotInput.
        // ponytail: Kick ставит слоту Open, даже если до бота он был Friend или Invite.
        private static void ReleaseOnline(Bot b)
        {
            if (b.profile == null) return;
            if (Network.isActive && b.profile.connection == DuckNetwork.localConnection)
                DuckNetwork.Kick(b.profile);
            b.profile.SetInputProfileLink(null);
        }

        // В чужую игру боты идут только по приглашению или к другу из Steam: оба пути ставят inviteLobbyID.
        public static void BotsOnlyForFriends()
        {
            if (NCSteam.inviteLobbyID != 0UL) { Log("мод: заходим по приглашению, боты идут с нами"); return; }
            int removed = UIMatchmakingBox.core.matchmakingProfiles.RemoveAll(p => p.inputProfile is BotInput);
            if (removed > 0) Log("мод: заходим не по приглашению — к незнакомцам ботов не берём: " + removed);
        }

        public static void DropBotsFromMatchmaking()
        {
            int removed = UIMatchmakingBox.core.matchmakingProfiles.RemoveAll(p => p.inputProfile is BotInput);
            if (removed > 0) Log("мод: хостим, ботов в список игроков не берём, посадим сами: " + removed);
        }
    }
}
