using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DuckGame;
using HarmonyLib;

namespace DuckBots
{
    public class DuckBotsMod : Mod
    {
        private static string _dir;

        protected override void OnPreInitialize()
        {
            _dir = configuration.directory;
            // Писать в папку мода нельзя: в Workshop уходит вся она.
            string data = DataDirectory();
            BotManager.OpenLogs(data);
            BotSettings.Load(Path.Combine(data, "bots.txt"));
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLocal;
        }

        private static string DataDirectory()
        {
            try
            {
                string data = Path.Combine(DuckFile.saveDirectory, "DuckBots-data");
                Directory.CreateDirectory(data);
                return data;
            }
            catch (Exception)
            {
                return _dir;
            }
        }

        // Игра грузит мод через Assembly.Load(byte[]) и соседние DLL не ищет: 0Harmony.dll — отсюда.
        private static Assembly ResolveLocal(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string path = Path.Combine(_dir, simpleName + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
            return null;
        }

        protected override void OnPostInitialize()
        {
            string fail = BotBrain.SelfTest();
            BotManager.Log(fail == null
                ? "мод: самопроверка мозга ок"
                : "мод: САМОПРОВЕРКА МОЗГА УПАЛА — " + fail);
            ApplyPatches();
            BotManager.hideFromHash = this;   // подмена отпечатка — в первом кадре
            BotManager.FlushLogs();
        }

        // Отдельный метод: типы Harmony разрешаются при JIT метода, уже после AssemblyResolve.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyPatches()
        {
            Harmony h = new Harmony("duckbots");
            h.PatchAll(Assembly.GetExecutingAssembly());
            BotManager.Log("мод: патчи применены");
        }
    }

    [HarmonyPatch(typeof(Level), "UpdateCurrentLevel")]
    public static class LevelUpdatePatch
    {
        private static void Prefix()
        {
            try
            {
                BotManager.Tick();
            }
            catch (Exception e)
            {
                BotManager.Log("мод: Tick упал: " + e);
            }
            finally
            {
                // Раз в кадр, даже если кадр упал: журнал читают снаружи игры.
                BotManager.FlushLogs();
            }
        }
    }

    // Тип смерти игра передаёт только сюда. Postfix лишь запоминает его для журнала.
    [HarmonyPatch(typeof(Duck), "Kill")]
    public static class DuckKillPatch
    {
        private static void Postfix(Duck __instance, DestroyType type)
        {
            try
            {
                BotManager.OnKilled(__instance, type);
            }
            catch (Exception)
            {
            }
        }
    }

    // Хостя, игра сажает локальными игроками всех из списка, собранного при уходе в сеть.
    // Ботов оттуда вычёркиваем и сажаем сами; в гостях список не трогаем — так боты приходят с нами.
    [HarmonyPatch(typeof(DuckNetwork), "Host")]
    public static class MatchmakingPatch
    {
        private static void Prefix()
        {
            try
            {
                BotManager.DropBotsFromMatchmaking();
            }
            catch (Exception e)
            {
                BotManager.Log("мод: ботов из списка сетевой игры убрать не вышло: " + e);
            }
        }
    }

    // Здесь клиент отправляет хосту список своих игроков.
    [HarmonyPatch(typeof(DuckNetwork), "SendJoinMessage")]
    public static class JoinPatch
    {
        private static void Prefix()
        {
            try
            {
                BotManager.BotsOnlyForFriends();
            }
            catch (Exception e)
            {
                BotManager.Log("мод: проверить приглашение не вышло: " + e);
            }
        }
    }

    // Меню паузы лобби игра пересобирает при каждом открытии.
    [HarmonyPatch(typeof(TeamSelect2), "BuildPauseMenu")]
    public static class PauseMenuPatch
    {
        private static FieldInfo _pauseMenuField;
        private static FieldInfo _pauseGroupField;
        private static FieldInfo _sectionField;

        private static void Postfix(TeamSelect2 __instance)
        {
            try
            {
                Build(__instance);
            }
            catch (Exception e)
            {
                BotManager.Log("меню ботов не собралось: " + e);
            }
        }

        private static FieldInfo Field(Type owner, string name, ref FieldInfo cache)
        {
            if (cache == null)
                cache = owner.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (cache == null) BotManager.Log("не найдено поле " + owner.Name + "." + name);
            return cache;
        }

        private static void Build(TeamSelect2 ts)
        {
            FieldInfo mf = Field(typeof(TeamSelect2), "_pauseMenu", ref _pauseMenuField);
            FieldInfo gf = Field(typeof(TeamSelect2), "_pauseGroup", ref _pauseGroupField);
            if (mf == null || gf == null) return;

            UIMenu pauseMenu = mf.GetValue(ts) as UIMenu;
            UIComponent pauseGroup = gf.GetValue(ts) as UIComponent;
            if (pauseMenu == null || pauseGroup == null) return;

            UIDivider column = FindColumn(pauseMenu);
            if (column == null) { BotManager.Log("не найдена колонка меню паузы"); return; }

            UIMenu botsMenu = BotsMenu(pauseMenu, pauseGroup);
            column.leftSection.Add(new UIMenuItem("BOTS",
                new UIMenuActionOpenMenu(pauseMenu, botsMenu)), true);

            // Игра в конце BuildPauseMenu трижды прогоняет группу, чтобы посчитались размеры.
            pauseGroup.Update();
            pauseGroup.Update();
            pauseGroup.Update();
        }

        // Кладётся закрытым в ту же группу, что и back, — иначе переход не откроется.
        public static UIMenu BotsMenu(UIMenu back, UIComponent group)
        {
            UIMenu botsMenu = new UIMenu("BOTS", Layer.HUD.camera.width / 2f,
                                         Layer.HUD.camera.height / 2f, 180f);
            for (int i = 0; i < BotSettings.Max; i++)
                botsMenu.Add(new UIMenuItemNumber("BOT " + (i + 1), null,
                    new FieldBinding(BotSettings.slots[i], "level",
                                     0f, BotSettings.levelNames.Count - 1, 1f),
                    valStrings: BotSettings.levelNames), true);
            botsMenu.Add(new UIMenuItem("BACK", new UIMenuActionOpenMenu(botsMenu, back),
                                        backButton: true), true);
            botsMenu.Close();
            group.Add(botsMenu, false);
            botsMenu.DoUpdate();
            return botsMenu;
        }

        // UIMenu.Add кладёт не в components, а в приватный _section.
        public static IList<UIComponent> Section(UIMenu menu)
        {
            FieldInfo sf = Field(typeof(UIMenu), "_section", ref _sectionField);
            UIComponent section = sf == null ? null : sf.GetValue(menu) as UIComponent;
            return section == null ? null : section.components;
        }

        private static UIDivider FindColumn(UIMenu menu)
        {
            IList<UIComponent> parts = Section(menu);
            if (parts == null) return null;
            for (int i = 0; i < parts.Count; i++)
                // Тип именно точный: UIMenuItem — тоже наследник UIDivider.
                if (parts[i].GetType() == typeof(UIDivider)) return (UIDivider)parts[i];
            return null;
        }
    }

    // В сети пауза — другое меню, его собирает DuckNetwork.OpenMenu. Только хосту: в лобби — BOTS,
    // в матче — SKIP, которого у хоста в ванили нет. Insert помечает меню грязным, раскладка пересчитается.
    [HarmonyPatch(typeof(DuckNetwork), "OpenMenu")]
    public static class NetPauseMenuPatch
    {
        private static PropertyInfo _groupProp;

        private static void Postfix()
        {
            try
            {
                Build();
            }
            catch (Exception e)
            {
                BotManager.Log("сетевое меню ботов не собралось: " + e);
            }
        }

        private static void Build()
        {
            if (!Network.isServer) return;
            GameLevel match = Level.current as GameLevel;
            bool lobby = Level.current is TeamSelect2;
            if (!lobby && (match == null || match.matchOver)) return;
            if (_groupProp == null)
                _groupProp = typeof(DuckNetwork).GetProperty("_ducknetUIGroup",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            UIComponent group = _groupProp == null ? null : _groupProp.GetValue(null, null) as UIComponent;
            UIMenu menu = DuckNetwork.core._ducknetMenu;
            if (group == null || menu == null) { BotManager.Log("не найдено сетевое меню паузы"); return; }

            if (lobby)
            {
                UIMenu botsMenu = PauseMenuPatch.BotsMenu(menu, group);
                Place(menu, new UIMenuItem("|DGBLUE|BOTS", new UIMenuActionOpenMenu(menu, botsMenu), UIAlign.Left),
                      ItemIndex(menu, "EDIT SLOTS") + 1);
            }
            else
            {
                Place(menu, new UIMenuItem("|DGRED|SKIP", new UIMenuActionCloseMenuCallFunction(group,
                    new UIMenuActionCloseMenuCallFunction.Function(Skip)), UIAlign.Left),
                      ItemIndex(menu, "DISCONNECT"));
            }
        }

        private static void Skip()
        {
            BotManager.Log("мод: раунд пропущен хостом из меню");
            GameMode.Skip();
        }

        private static int ItemIndex(UIMenu menu, string label)
        {
            IList<UIComponent> parts = PauseMenuPatch.Section(menu);
            for (int i = 0; parts != null && i < parts.Count; i++)
            {
                UIMenuItem mi = parts[i] as UIMenuItem;
                if (mi != null && mi.text != null && mi.text.Contains(label)) return i;
            }
            return -1;
        }

        // Соседа не нашли (at ≤ 0) — в конец.
        private static void Place(UIMenu menu, UIMenuItem item, int at)
        {
            if (at <= 0) menu.Add(item, true);
            else menu.Insert(item, at, true);
        }
    }

    [HarmonyPatch(typeof(Duck), "Draw")]
    public static class DuckDrawPatch
    {
        private static void Postfix(Duck __instance)
        {
            try
            {
                if (!BotManager.debug || __instance.dead || !(__instance.inputProfile is BotInput)) return;
                Bot b = BotManager.Find(__instance);
                if (b == null) return;
                Graphics.DrawString(BotBrain.modeNames[b.state.mode],
                    new Vec2(__instance.x - 12f, __instance.y - 26f), Color.White, 0.9f);
            }
            catch (Exception)
            {
            }
        }
    }
}
