using System;
using System.Collections.Generic;
using System.Reflection;
using DuckGame;

namespace DuckBots
{
    // Лобби: выход в портал вслед за человеком. Портал — луч TeamBeam в щели между
    // рядами комнат: утку в своём прямоугольнике он забирает сам (TakeDuck, beammode),
    // и её коробка готова. Бот выходит туда так же, как человек: дробовик со ступеньки,
    // стекло в проёме, шаг наружу (BotBrain.Lobby).
    public static partial class BotManager
    {
        private static FieldInfo _windowField;
        private const int BeamPatience = 600;   // 10 с: дольше не выбрался — луч забирает сам

        // Готов ли живой человек: активная коробка не наша, и её утка в портале.
        private static bool HumanReady(TeamSelect2 ts)
        {
            List<ProfileBox2> boxes = GetBoxes(ts);
            if (boxes == null) return false;
            for (int i = 0; i < boxes.Count; i++)
            {
                ProfileBox2 box = boxes[i];
                if (box == null || !box.playerActive || box.duck == null || IsBotBox(box)) continue;
                if (box.ready) return true;
            }
            return false;
        }

        // Своя коробка — по коробке или по профилю: у сетевого бота коробки нет.
        private static bool IsBotBox(ProfileBox2 box)
        {
            for (int i = 0; i < bots.Count; i++)
                if (bots[i].box == box || (bots[i].profile != null && bots[i].profile == box.profile)) return true;
            return false;
        }

        private static ProfileBox2 BoxOf(TeamSelect2 ts, Duck d)
        {
            List<ProfileBox2> boxes = GetBoxes(ts);
            if (boxes == null) return null;
            for (int i = 0; i < boxes.Count; i++)
                if (boxes[i] != null && boxes[i].duck == d) return boxes[i];
            return null;
        }

        // Лучей в ванили два: _beam на x=160 у первых двух колонок, _beam2 у третьей.
        private static TeamBeam BeamNear(TeamSelect2 ts, float x)
        {
            TeamBeam a = ts._beam, c = ts._beam2;
            if (a == null) return c;
            if (c == null) return a;
            return Math.Abs(a.x - x) <= Math.Abs(c.x - x) ? a : c;
        }

        private static Window RoomWindow(ProfileBox2 box)
        {
            if (_windowField == null)
                _windowField = typeof(ProfileBox2).GetField("_window",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return _windowField == null ? null : _windowField.GetValue(box) as Window;
        }

        // Стенка у ног в шаге от утки — ступенька, на которую надо запрыгнуть. Стекло не
        // в счёт: его бьют.
        private static bool StepWall(Duck d, int dir)
        {
            Block k = Level.CheckPoint<Block>(new Vec2(d.x + dir * 12f, d.bottom - 4f));
            return k != null && !(k is Window);
        }

        // Один кадр бота в лобби: обстановка, журнал, запасной путь и решение.
        private static ushort LobbyStep(Bot b, Duck d, TeamSelect2 ts, bool humanReady)
        {
            ProfileBox2 box = BoxOf(ts, d);
            TeamBeam beam = BeamNear(ts, d.x);
            if (box == null || beam == null) return 0;

            LobbyView v = new LobbyView();
            v.humanReady = humanReady;
            v.inBeam = d.beammode;
            v.centered = Math.Abs(d.x - beam.x) < 2f;
            Window w = RoomWindow(box);
            v.glass = w != null && !w.destroyed && !w.removeFromLevel;
            if (v.glass) v.glassDx = w.x - d.x;
            VirtualShotgun gun = box.gun;
            v.hasGun = gun != null && !gun.removeFromLevel && (gun.owner == null || gun.owner == d);
            v.gunHeld = gun != null && d.holdObject == gun;
            if (gun != null)
            {
                v.gunDx = gun.x - d.x;
                v.gunDy = gun.y - d.y;
            }
            v.exitDir = box.rightRoom ? -1 : 1;
            v.wallLeft = StepWall(d, -1);
            v.wallRight = StepWall(d, 1);
            v.grounded = d.grounded;
            v.hs = d.hSpeed;
            v.facingRight = d.offDir > 0;

            // Журнал: где что в этой комнате (раз на лобби) и что бот сделал.
            string who = BotName(b);
            if (b.lobbyBox != box)
            {
                b.lobbyBox = box;
                b.lobbyGlass = v.glass;
                b.lobbyBeam = v.inBeam;
                b.lobbyFrames = 0;
                Log("лобби: " + who + " комната x=" + (int)box.x + " y=" + (int)box.y
                    + " выход=" + (v.exitDir > 0 ? "вправо" : "влево")
                    + " стекло=" + (w == null ? "нет" : (int)w.x + "," + (int)w.y)
                    + " дробовик=" + (gun == null ? "нет" : (int)gun.x + "," + (int)gun.y)
                    + " луч=" + (int)beam.x + " утка=" + (int)d.x + "," + (int)d.y);
            }
            string t = " за " + (b.lobbyFrames / 60f).ToString("0.0") + " с";
            if (b.lobbyGlass && !v.glass) Log("лобби: " + who + " разбил стекло" + (humanReady ? t : ""));
            if (!b.lobbyBeam && v.inBeam) Log("лобби: " + who + " в портале" + (humanReady ? t : ""));
            if (b.lobbyBeam && !v.inBeam) Log("лобби: " + who + " вышел из портала");
            b.lobbyGlass = v.glass;
            b.lobbyBeam = v.inBeam;

            // ponytail: запасной путь нечестный — луч забирает утку прямо из комнаты. Он
            // нужен, чтобы застрявший бот не держал матч у чужого игрока; честно — понять
            // по журналу, где бот застрял, и научить его выбираться оттуда.
            if (humanReady && !v.inBeam)
            {
                if (++b.lobbyFrames == BeamPatience)
                {
                    Log("лобби: " + who + " за " + BeamPatience / 60 + " с не выбрался из комнаты — луч забрал сам; "
                        + (b.lobbyState.why ?? "-") + " стекло=" + (v.glass ? "цело" : "разбито")
                        + " дробовик=" + (v.gunHeld ? "в руках" : v.hasGun ? "на месте" : "нет")
                        + " утка=" + (int)d.x + "," + (int)d.y + " запись=f" + frame);
                    RequestDump(d, "лобби");
                    beam.TakeDuck(d);
                }
            }
            else b.lobbyFrames = 0;

            return BotBrain.Lobby(ref v, ref b.lobbyState);
        }
    }
}
