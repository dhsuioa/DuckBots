using System.Collections.Generic;
using System.Reflection;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
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

        // Бот занимает свободную коробку и жмёт в ней START, остальное игра делает сама.
        private static void Attach(Bot b, ProfileBox2 box)
        {
            b.box = box;
            b.profile = box.profile;
            b.savedBoxInput = GetBoxInput(box);
            SetBoxInput(box, b.input);
            // Не сеттер inputProfile: подменяя непустой профиль, он дёргает Rumble() и
            // ApplyDefaultMapping() у прежнего — у живого контроллера человека.
            box.profile.SetInputProfileLink(b.input);
        }

        private static void SyncSlots(TeamSelect2 ts)
        {
            List<ProfileBox2> boxes = GetBoxes(ts);
            if (boxes == null) return;

            for (int i = bots.Count - 1; i >= 0; i--)
            {
                Bot b = bots[i];
                if (BotSettings.slots[b.slot].level != 0)
                {
                    b.level = BotSettings.slots[b.slot].level;
                    continue;
                }
                Release(b, boxes.Contains(b.box));
                bots.RemoveAt(i);
                Log("бот " + (b.slot + 1) + " выключен и снят со слота");
                _framesSinceChange = 0;
            }

            // Живой игрок жмёт START в коробке с ботом: её ввод подменён, сама она нажатия не видит.
            // Бот уступает её, игрок входит вторым нажатием. START человека в своей коробке — не вход.
            if (_heldFrames > 0) _heldFrames--;
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                Bot b = bots[i];
                if (b.box == null || b.savedBoxInput == null || !b.savedBoxInput.Pressed("START")) continue;
                if (HumanPressedStart(boxes)) continue;
                Log("лобби: живой игрок жмёт START в коробке " + boxes.IndexOf(b.box)
                    + " — бот " + (b.slot + 1) + " уступает её");
                _heldBox = b.box;
                _heldFrames = HeldFrames;
                Release(b, boxes.Contains(b.box));
                bots.RemoveAt(i);
                _framesSinceChange = 0;
            }

            for (int s = 0; s < BotSettings.Max; s++)
            {
                if (BotSettings.slots[s].level == 0) continue;
                if (HasBot(s)) continue;

                ProfileBox2 box = FindFreeBox(boxes);
                if (box == null) break;

                Bot b = new Bot();
                b.slot = s;
                b.level = BotSettings.slots[s].level;
                b.input = new BotInput(s);
                b.pressStartFrames = 2;
                Attach(b, box);
                bots.Add(b);
                _framesSinceChange = 0;
                Log("бот " + (s + 1) + " (" + BotSettings.levelNames[b.level] + ") занял коробку "
                    + boxes.IndexOf(box) + " (" + box.profile.name + "), жмёт START");
            }
        }

        private static ProfileBox2 _heldBox;
        private static int _heldFrames;
        private const int HeldFrames = 600;   // уступленную коробку столько не занимать

        // Со второй комнаты: нулевая — человека, комната N привязана к контроллеру N.
        private static ProfileBox2 FindFreeBox(List<ProfileBox2> boxes)
        {
            for (int i = 1; i < boxes.Count; i++)
            {
                ProfileBox2 box = boxes[i];
                if (box == null || box.profile == null) continue;
                if (box.playerActive) continue;
                if (box == _heldBox && _heldFrames > 0) continue;
                if (box.profile.team != null) continue;
                if (box.profile.connection != null) continue;
                if (BoxTaken(box)) continue;
                return box;
            }
            return null;
        }

        private static bool HumanPressedStart(List<ProfileBox2> boxes)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                ProfileBox2 box = boxes[i];
                if (box == null || !box.playerActive || BoxTaken(box)) continue;
                InputProfile input = GetBoxInput(box);
                if (input != null && input.Pressed("START")) return true;
            }
            return false;
        }

        // Профиль с командой игра усаживает обратно сама (ProfileBox2.Update): ботов не снимаем,
        // а переезжаем на новые коробки.
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
                    bots.RemoveAt(i);
                    continue;
                }

                Attach(b, box);
                b.pressStartFrames = box.playerActive ? 0 : 2;
            }
            _framesSinceChange = 0;
            Log("лобби пересоздано, ботов переподключено: " + bots.Count);
        }

        // Профиль вечный (Profiles.defaultProfiles): BotInput, оставленный в нём, отнял бы слот у
        // человека. SelectTeam() в CloseDoor() перетирает inputProfile, только если тот null.
        private static void Release(Bot b, bool boxIsLive)
        {
            if (b.online)
            {
                ReleaseOnline(b);
                return;
            }
            if (b.profile != null)
            {
                // Вместо null геттер inputProfile сам подставит настоящий контроллер слота.
                b.profile.SetInputProfileLink(null);
                if (b.profile.team != null) b.profile.team.Leave(b.profile);
            }
            if (b.box != null)
            {
                SetBoxInput(b.box, b.savedBoxInput);
                // CloseDoor у мёртвой коробки полез бы в текущий уровень по старым координатам.
                if (boxIsLive && b.box.playerActive) b.box.CloseDoor();
            }
            // null геттер не отдаёт никогда — сверяемся с BotInput.
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
