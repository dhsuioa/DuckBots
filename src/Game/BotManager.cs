using System;
using System.Collections.Generic;
using System.Reflection;
using DuckGame;

namespace DuckBots
{
    public static partial class BotManager
    {
        public static readonly List<Bot> bots = new List<Bot>();

        private static TeamSelect2 _lastLobby;
        private static int _framesSinceChange = -1;
        private static int _brainFrames;

        private static FieldInfo _boxesField;
        private static FieldInfo _boxInputField;

        public static void Tick()
        {
            if (Level.current == null) return;
            frame++;
            if (!_commandsAdded) AddCommands();
            if (hideFromHash != null)
            {
                Mod self = hideFromHash;
                hideFromHash = null;
                HideFromModHash(self);
            }

            // Сводка прошлого раунда — в начале кадра, до строк нового.
            if (Level.current != _traceLevel)
            {
                RoundSummary();
                _tracers.Clear();
                _traceLevel = Level.current;
                if (Level.current is GameLevel)
                {
                    round++;
                    _roundStart = frame;
                }
            }

            // Единственный офлайновый выход из лобби — TitleScreen. Между раундами ботов не снимать:
            // идёт серия, и счёт сломался бы.
            if (Level.current is TitleScreen)
            {
                ReleaseAll(null);
                _lastLobby = null;
                return;
            }

            TeamSelect2 lobby = Level.current as TeamSelect2;
            if (lobby != null)
            {
                // Посадка офлайн и в сети не смешивается. Смена снимается до Rebind: тот искал бы
                // офлайновые профили среди сетевых и бросил бы BotInput в профиле человека.
                if (bots.Count > 0 && bots[0].online != Network.isActive)
                {
                    if (Network.isActive) GoOnline(GetBoxes(lobby));
                    else ReleaseAll(GetBoxes(lobby));
                }

                if (Network.isActive) SyncSlotsOnline();
                else
                {
                    // Возврат из матча создаёт новый TeamSelect2 со своими коробками.
                    if (lobby != _lastLobby) Rebind(lobby);
                    SyncSlots(lobby);
                }
                _lastLobby = lobby;
                BotSettings.SaveIfChanged();
            }

            if (_framesSinceChange >= 0)
            {
                _framesSinceChange++;
                if (_framesSinceChange % 60 == 0 && _framesSinceChange <= 600) DumpBots();
            }

            Drive();
            TraceAll();

            if (bots.Count > 0 && Level.current is GameLevel && ++_brainFrames >= 60)
            {
                _brainFrames = 0;
                LogBrain();
            }
        }

        private static bool _commandsAdded;

        // В первом кадре, когда консоль игры уже поднята. nextround идёт тем же путём, что игра (GameMode.Skip).
        private static void AddCommands()
        {
            _commandsAdded = true;
            try
            {
                DevConsole.AddCommand(new CMD("nextround", () =>
                {
                    if (!(Level.current is GameLevel))
                    {
                        DevConsole.Log(DCSection.Mod, "nextround: сейчас не матч");
                        return;
                    }
                    Log("мод: раунд пропущен командой nextround");
                    GameMode.Skip();
                }));
                DevConsole.AddCommand(new CMD("botdebug", () =>
                {
                    SetDebug(!debug);
                    Log("мод: отладка " + (debug ? "включена: подписи над ботами, botlog-detail.txt" : "выключена"));
                }));
                DevConsole.AddCommand(new CMD("botlog", () =>
                {
                    if (!fullLog && !debug) SetDebug(true);
                    fullLog = !fullLog;
                    Log("мод: полный журнал " + (fullLog
                        ? "включён — каждый кадр каждой утки идёт в botlog-detail.txt"
                        : "выключен"));
                }));
                Log("мод: консольные команды nextround, botdebug и botlog добавлены");
            }
            catch (Exception e)
            {
                Log("мод: консольные команды добавить не вышло: " + e.Message);
            }
        }

        private static void Drive()
        {
            TeamSelect2 lobby = Level.current as TeamSelect2;
            bool humanReady = lobby != null && HumanReady(lobby);
            // Карта — одна на всех ботов, перебор клеток раз в NavRebuild кадров.
            if (bots.Count > 0 && Level.current is GameLevel) EnsureNav();

            for (int i = 0; i < bots.Count; i++)
            {
                Bot b = bots[i];
                ushort m = 0;

                if (b.pressStartFrames > 0)
                {
                    m = BotInput.START;
                    b.pressStartFrames--;
                }
                else
                {
                    Duck d = b.profile == null ? null : b.profile.duck;

                    if (d != null && lobby != null)
                    {
                        m = LobbyStep(b, d, lobby, humanReady);
                    }
                    else if (d != null && !d.dead && Level.current is GameLevel)
                    {
                        b.view = Look(b, d);
                        m = BotBrain.Decide(ref b.view, ref b.state);
                        Watch(b, d);
                        Explain(b, d, m);
                    }
                    else if (Level.current is HighlightLevel || Level.current is HighlightPlayback
                        || Level.current is RockScoreboard)
                    {
                        // Пропуск хайлайтов и табло — голосованием START, и голоса табло считает только
                        // в кадр, когда START нажал живой игрок. Tick идёт до обновления уровня: кадр общий.
                        if (Input.Pressed("START", "Any")) m = BotInput.START;
                    }
                }

                b.input.SetMask(m);
            }
        }

        private const int IdleFrames = 180;

        private static void Explain(Bot b, Duck d, ushort m)
        {
            bool dodging = b.state.how == BotBrain.HowDodge || b.state.how == BotBrain.HowDodgeRun;
            if (dodging && !b.dodging)
                Log(BotName(b) + ": ухожу подкатом, стрелок " + (int)b.view.threatDx + " — " + Context(b, d));
            b.dodging = dodging;
            if (b.view.lobber && b.view.lobAiming && (m & BotInput.SHOOT) == 0)
                Log(BotName(b) + ": граната навесом угол=" + (int)b.view.lobAim + " нужен=" + b.view.lobAngle
                    + " " + Context(b, d));
            if (b.state.mode != b.lastMode)
            {
                Log(BotName(b) + ": режим " + (b.lastMode < 0 ? "старт" : BotBrain.modeNames[b.lastMode])
                    + "→" + BotBrain.modeNames[b.state.mode] + " " + Context(b, d) + " — " + Reason(b.state));
                b.lastMode = b.state.mode;
            }
            // Без врагов и целей стоять можно: это конец раунда.
            if (m != 0 || d.immobilized || !(b.view.hasEnemy || b.view.hasPickup)) { b.idleFrames = 0; return; }
            if (++b.idleFrames != IdleFrames) return;
            Log(BotName(b) + ": бездействие 3с " + Context(b, d) + " — " + Reason(b.state) + " запись=f" + frame);
            RequestDump(d, "бездействие");
        }
    }
}
