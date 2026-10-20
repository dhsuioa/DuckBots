using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DuckGame;
using HarmonyLib;

namespace DuckBots
{
    public static partial class BotManager
    {
        // --- доступ к приватным полям игры ---

        private static List<ProfileBox2> GetBoxes(TeamSelect2 ts)
        {
            if (_boxesField == null)
                _boxesField = typeof(TeamSelect2).GetField("_profiles",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_boxesField == null) { Log("не найдено поле TeamSelect2._profiles"); return null; }
            return _boxesField.GetValue(ts) as List<ProfileBox2>;
        }

        private static FieldInfo BoxInputField()
        {
            if (_boxInputField == null)
                _boxInputField = typeof(ProfileBox2).GetField("_inputProfile",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_boxInputField == null) Log("не найдено поле ProfileBox2._inputProfile");
            return _boxInputField;
        }

        private static InputProfile GetBoxInput(ProfileBox2 box)
        {
            FieldInfo f = BoxInputField();
            if (f == null) return null;
            return f.GetValue(box) as InputProfile;
        }

        private static void SetBoxInput(ProfileBox2 box, InputProfile input)
        {
            FieldInfo f = BoxInputField();
            if (f != null) f.SetValue(box, input);
        }

        // --- занять и освободить слот ---

        private static bool BoxTaken(ProfileBox2 box)
        {
            for (int i = 0; i < bots.Count; i++)
                if (bots[i].box == box) return true;
            return false;
        }

        private static bool HasBot(int slot)
        {
            for (int i = 0; i < bots.Count; i++)
                if (bots[i].slot == slot) return true;
            return false;
        }

        // Бот не подсовывает состояние, а занимает свободную коробку и жмёт в ней START.
        // Дальше игра сама открывает дверь, выбирает команду и спавнит утку.
        private static void Attach(Bot b, ProfileBox2 box)
        {
            b.box = box;
            b.profile = box.profile;
            b.savedBoxInput = GetBoxInput(box);
            SetBoxInput(box, b.input);
            // Именно SetInputProfileLink, а не свойство inputProfile: его сеттер,
            // подменяя непустой профиль, дёргает Rumble() и ApplyDefaultMapping()
            // на прежнем — то есть на живом контроллере человека, сбрасывая ему
            // раскладку. Нам нужна голая ссылка, без побочек.
            box.profile.SetInputProfileLink(b.input);
        }

        private static void SyncSlots(TeamSelect2 ts)
        {
            List<ProfileBox2> boxes = GetBoxes(ts);
            if (boxes == null) return;

            // Выключенные в меню — со слота долой.
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                Bot b = bots[i];
                if (BotSettings.slots[b.slot].level != 0)
                {
                    b.level = BotSettings.slots[b.slot].level; // уровень могли покрутить
                    continue;
                }
                Release(b, boxes.Contains(b.box));
                bots.RemoveAt(i);
                Log("бот " + (b.slot + 1) + " выключен и снят со слота");
                _framesSinceChange = 0;
            }

            // Включённые, которых ещё нет — на свободные коробки с конца.
            for (int s = 0; s < BotSettings.Max; s++)
            {
                if (BotSettings.slots[s].level == 0) continue;
                if (HasBot(s)) continue;

                ProfileBox2 box = FindFreeBox(boxes);
                if (box == null) break; // мест нет, дальше искать бессмысленно

                Bot b = new Bot();
                b.slot = s;
                b.level = BotSettings.slots[s].level;
                b.input = new BotInput("bot" + (s + 1));
                b.pressStartFrames = 2;
                Attach(b, box);
                bots.Add(b);
                _framesSinceChange = 0;
                Log("бот " + (s + 1) + " (" + BotSettings.levelNames[b.level] + ") занял коробку "
                    + boxes.IndexOf(box) + " (" + box.profile.name + "), жмёт START");
            }
        }

        // Слоты берём с конца, нулевой не трогаем никогда: это место человека.
        private static ProfileBox2 FindFreeBox(List<ProfileBox2> boxes)
        {
            for (int i = boxes.Count - 1; i >= 1; i--)
            {
                ProfileBox2 box = boxes[i];
                if (box == null || box.profile == null) continue;
                if (box.playerActive) continue;
                if (box.profile.team != null) continue;
                if (box.profile.connection != null) continue;
                if (BoxTaken(box)) continue;
                return box;
            }
            return null;
        }

        // Новое лобби — те же профили, другие коробки. Игра сама усаживает профиль
        // обратно, если у него осталась команда (ProfileBox2.Update), поэтому не
        // снимаем ботов, а переезжаем на новые коробки.
        private static void Rebind(TeamSelect2 ts)
        {
            List<ProfileBox2> boxes = GetBoxes(ts);
            if (boxes == null) return;

            for (int i = bots.Count - 1; i >= 0; i--)
            {
                Bot b = bots[i];
                ProfileBox2 box = null;
                for (int j = 0; j < boxes.Count; j++)
                    if (boxes[j] != null && boxes[j].profile == b.profile) { box = boxes[j]; break; }

                if (box == null)
                {
                    // Профиль потерялся — пусть бот зайдёт заново на общих основаниях.
                    bots.RemoveAt(i);
                    continue;
                }

                Attach(b, box);
                // Если игра уже усадила профиль сама, START не нужен.
                b.pressStartFrames = box.playerActive ? 0 : 2;
            }
            _framesSinceChange = 0;
            Log("лобби пересоздано, ботов переподключено: " + bots.Count);
        }

        // Порядок важен: SelectTeam() внутри CloseDoor() перетирает
        // profile.inputProfile только если оно null. Профиль здесь —
        // вечный Profiles.defaultProfiles[i], он переживает и лобби, и матч,
        // так что оставить в нём мёртвый BotInput значит отнять слот у человека.
        private static void Release(Bot b, bool boxIsLive)
        {
            if (b.online)
            {
                ReleaseOnline(b);
                return;
            }
            if (b.profile != null)
            {
                // Геттер inputProfile сам подставит обратно настоящий контроллер
                // этого слота, как только в поле окажется null — чинить руками нечего.
                b.profile.SetInputProfileLink(null);
                if (b.profile.team != null) b.profile.team.Leave(b.profile);
            }
            if (b.box != null)
            {
                SetBoxInput(b.box, b.savedBoxInput);
                // CloseDoor у мёртвой коробки полез бы в текущий уровень
                // за турелями и шляпами по старым координатам.
                if (boxIsLive && b.box.playerActive) b.box.CloseDoor();
            }
            // Самопроверка ради той самой болезни: профиль вечный, и если в нём
            // остался наш мёртвый BotInput, человек на этом слоте получит утку,
            // которая не шевелится. Сверяемся именно с BotInput, а не с null:
            // геттер никогда не отдаёт null, он сам подставляет контроллер слота.
            if (b.profile != null && (b.profile.inputProfile is BotInput || b.profile.team != null))
                Log("ВНИМАНИЕ: профиль " + b.profile.name + " отпущен не до конца: ввод="
                    + b.profile.inputProfile.GetType().Name
                    + ", команда=" + (b.profile.team == null ? "нет" : b.profile.team.name));

            // ponytail: луч TeamSelect2._beam приватный, RemoveDuck не зовём —
            // после снятия бота посреди лобби луч может повисеть до конца лобби.
        }

        private static void ReleaseAll(List<ProfileBox2> liveBoxes)
        {
            if (bots.Count == 0) return;
            for (int i = 0; i < bots.Count; i++)
                Release(bots[i], liveBoxes != null && liveBoxes.Contains(bots[i].box));
            Log("боты сняты со слотов: " + bots.Count);
            bots.Clear();
            _framesSinceChange = -1;
        }
    }
}
