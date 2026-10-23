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
                hideFromHash = null; // один раз, даже если упадёт
                HideFromModHash(self);
            }

            // Новый уровень: сводка прошлого раунда, журнал уток заново. Здесь, в
            // начале кадра, чтобы все строки нового раунда шли уже с его номером.
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

            // Единственный офлайновый выход из лобби наружу — TitleScreen.
            // Экраны между раундами трогать нельзя: там идёт серия, и снимать
            // ботов с команд посреди неё значит ломать счёт.
            if (Level.current is TitleScreen)
            {
                ReleaseAll(null);
                _lastLobby = null;
                return;
            }

            TeamSelect2 lobby = Level.current as TeamSelect2;
            if (lobby != null)
            {
                // Боты сидят либо по-офлайновому (коробка, вечный профиль слота), либо
                // по-сетевому (свой сетевой профиль) — смешанным список не бывает. Лобби
                // стало сетевым — офлайновую посадку снимаем, а боты ждут сетевых профилей
                // (GoOnline). Стало обратно офлайновым — снимаем целиком. Всё это до Rebind:
                // он искал бы офлайновые профили среди сетевых, не нашёл бы и бросил бы
                // BotInput в профиле, который потом достанется человеку.
                if (bots.Count > 0 && bots[0].online != Network.isActive)
                {
                    if (Network.isActive) GoOnline(GetBoxes(lobby));
                    else ReleaseAll(GetBoxes(lobby));
                }

                if (Network.isActive) SyncSlotsOnline();
                else
                {
                    // Возврат из матча создаёт новый TeamSelect2 со своими коробками,
                    // старые ссылки протухли — переехать на новые.
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

            // Раз в секунду — строка состояния про каждого бота в подробный журнал:
            // что он видит и почему делает то, что делает, даже если режим не менялся.
            if (bots.Count > 0 && Level.current is GameLevel && ++_brainFrames >= 60)
            {
                _brainFrames = 0;
                LogBrain();
            }
        }

        // --- консоль ---

        private static bool _commandsAdded;

        // Команда консоли (клавиша ~) «nextround» — закончить текущую карту и перейти
        // к следующей, тем же путём, что и сама игра (GameMode.Skip). Нужна на случай,
        // когда боты где-то застряли и раунд не кончается: раньше приходилось выходить
        // из матча целиком. Своя, а не встроенная: как та называется в ванили и не
        // заперта ли за читами, по строкам игры не понять, а своя работает точно.
        // Добавляется в первом кадре, когда консоль игры уже поднята.
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
                // «botlog» — полный журнал: самописец всех уток пишется в подробный
                // файл непрерывно, кадр за кадром. Включать на той карте, где бот
                // тупит: без события запись иначе остаётся в памяти.
                // «botdebug» — подписи режимов над ботами и подробный журнал (см. debug).
                DevConsole.AddCommand(new CMD("botdebug", () =>
                {
                    SetDebug(!debug);
                    Log("мод: отладка " + (debug ? "включена: подписи над ботами, botlog-detail.txt" : "выключена"));
                }));
                DevConsole.AddCommand(new CMD("botlog", () =>
                {
                    if (!fullLog && !debug) SetDebug(true);   // без подробного журнала писать некуда
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

        // --- нажатие кнопок ---

        private static void Drive()
        {
            TeamSelect2 lobby = Level.current as TeamSelect2;
            bool humanReady = lobby != null && HumanReady(lobby);

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
                        // Готовность — утка в портале. Туда бот выходит вслед за
                        // человеком и выходит обратно вместе с ним, иначе отсчёт до
                        // матча начался бы без человека (BotManager.Beam).
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
                        // Хайлайты и табло пропускаются голосованием: нужен START от всех
                        // участников, ботов тоже. Бот жмёт его в тот же кадр, что и человек:
                        // табло принимает голоса, только когда в этом кадре START нажал живой
                        // игрок, а Tick идёт до обновления уровня, так что кадр общий.
                        if (Input.Pressed("START", "Any")) m = BotInput.START;
                    }
                }

                b.input.SetMask(m);
            }
        }

        private const int IdleFrames = 180;   // три секунды без единой кнопки — бездействие

        // Журнал решений. Смена режима — строка в основной журнал: из какого в какой,
        // почему и на каких числах. Бездействие — бот жив, не обездвижен, а три
        // секунды не жмёт ни одной кнопки: строка и запись самописца.
        private static void Explain(Bot b, Duck d, ushort m)
        {
            // Начало уклонения — строка: как часто боты ложатся под прицелом и от кого.
            bool dodging = b.state.how == BotBrain.HowDodge || b.state.how == BotBrain.HowDodgeRun;
            if (dodging && !b.dodging)
                Log(BotName(b) + ": ухожу подкатом, стрелок " + (int)b.view.threatDx + " — " + Context(b, d));
            b.dodging = dodging;
            // Выстрел из гранатомёта (SHOOT отпущен, пока ствол поднимался) — строка: на
            // каком угле отпустил, какой был нужен (−1 — дуги уже нет) и где враг.
            if (b.view.lobber && b.view.lobAiming && (m & BotInput.SHOOT) == 0)
                Log(BotName(b) + ": граната навесом угол=" + (int)b.view.lobAim + " нужен=" + b.view.lobAngle
                    + " " + Context(b, d));
            if (b.state.mode != b.lastMode)
            {
                Log(BotName(b) + ": режим " + (b.lastMode < 0 ? "старт" : BotBrain.modeNames[b.lastMode])
                    + "→" + BotBrain.modeNames[b.state.mode] + " " + Context(b, d) + " — " + Reason(b.state));
                b.lastMode = b.state.mode;
            }
            // Без врагов и целей стоять не зазорно — это конец раунда. В засаде — тоже.
            if (m != 0 || d.immobilized || !(b.view.hasEnemy || b.view.hasPickup)
                || b.state.why == BotBrain.WhyAmbush) { b.idleFrames = 0; return; }
            if (++b.idleFrames != IdleFrames) return;
            Log(BotName(b) + ": бездействие 3с " + Context(b, d) + " — " + Reason(b.state) + " запись=f" + frame);
            RequestDump(d, "бездействие");
        }
    }
}
