using System;
using System.Globalization;
using System.Text;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        // Порядок совпадает с битами BotInput: LEFT = 1, RIGHT = 2…
        private static readonly string[] _buttonNames =
            { "LEFT", "RIGHT", "UP", "DOWN", "SHOOT", "JUMP", "GRAB", "QUACK", "START", "RAGDOLL", "STRAFE" };
        private static readonly string[] _buttonLetters =
            { "L", "R", "U", "D", "S", "J", "G", "Q", "St", "Rg", "Sf" };

        private static void Record(MoveTracer tr, Duck d)
        {
            RecFrame f = new RecFrame();
            f.frame = frame;
            Ragdoll doll = d.ragdoll;
            f.ragdoll = doll != null;
            f.x = doll != null ? doll.x : d.x;
            f.y = doll != null ? doll.y : d.y;
            f.hs = d.hSpeed;
            f.vs = d.vSpeed;
            f.grounded = d.grounded;
            f.frozen = d.immobilized;
            f.mode = -1;
            Bot b = tr.bot;
            if (b != null)
            {
                f.buttons = b.input.mask;
                f.mode = b.state.mode;
                f.why = b.state.why;
                f.how = b.state.how;
                f.jumpHold = b.state.jumpHold;
                f.prog = b.state.prog;
                f.progAt = b.state.progAt;
                BotView v = b.view;
                bool pick = b.state.mode == BotBrain.GetGun && v.hasPickupStep;
                f.routed = pick || v.hasEnemyStep;
                f.stepX = pick ? v.pickupStepX : v.enemyStepX;
                f.stepY = pick ? v.pickupStepY : v.enemyStepY;
                f.edge = pick ? v.pickupStepEdge : v.enemyStepEdge;
            }
            else
            {
                InputProfile ip = d.inputProfile;
                if (ip != null)
                    for (int i = 0; i < _buttonNames.Length; i++)
                        if (ip.Down(_buttonNames[i])) f.buttons |= (ushort)(1 << i);
            }
            tr.rec[tr.recHead] = f;
            tr.recHead = (tr.recHead + 1) % MoveTracer.RecLen;
            if (tr.recCount < MoveTracer.RecLen) tr.recCount++;
            if (fullLog) Detail(tr.who + ": " + FrameLine(f));
        }

        private static string FrameLine(RecFrame f)
        {
            string s = "кадр=" + f.frame + " x=" + F1(f.x) + " y=" + F1(f.y)
                + " hs=" + F2(f.hs) + " vs=" + F2(f.vs) + " земля=" + (f.grounded ? 1 : 0)
                + (f.ragdoll ? " рэгдолл" : "") + (f.frozen ? " обездвижен" : "")
                + " кн=" + Buttons(f.buttons);
            if (f.mode < 0) return s;
            return s + " режим=" + BotBrain.modeNames[f.mode] + " прыжок=" + f.jumpHold
                + (f.progAt > 0 ? " прог=" + (f.progAt - 1) + "{" + NavMap.ProgWord(f.prog) + "}" : "")
                + " шаг=" + StepWord(f) + " — " + (f.why ?? "-") + (f.how != null ? "; " + f.how : "");
        }

        private static string StepWord(RecFrame f)
        {
            return f.routed ? EdgeName(f.edge) + "(" + (int)f.stepX + "," + (int)f.stepY + ")" : "нет";
        }

        private static string Buttons(ushort m)
        {
            if (m == 0) return "-";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _buttonLetters.Length; i++)
                if ((m & (1 << i)) != 0)
                {
                    if (sb.Length > 0) sb.Append('+');
                    sb.Append(_buttonLetters[i]);
                }
            return sb.ToString();
        }

        private static string F1(float x) { return x.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string F2(float x) { return x.ToString("0.00", CultureInfo.InvariantCulture); }

        private static void Dump(MoveTracer tr, string why, int frames)
        {
            if (!DetailOn) return;
            int n = Math.Min(frames, tr.recCount);
            Detail(tr.who + ": запись событие=" + why + " кадров=" + n);
            for (int i = n; i >= 1; i--)
                Detail(tr.who + ": " + FrameLine(tr.rec[(tr.recHead - i + MoveTracer.RecLen) % MoveTracer.RecLen]));
        }

        // Кадр этого тика в самописец ещё не лёг: запись сбросит журнал в конце кадра.
        private static void RequestDump(Duck d, string why)
        {
            MoveTracer tr;
            if (_tracers.TryGetValue(d, out tr)) tr.dumpWhy = why;
        }
    }
}
