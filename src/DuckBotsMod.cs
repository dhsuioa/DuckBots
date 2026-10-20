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
    public class DuckBotsMod : Mod
    {
        private static string _dir;

        protected override void OnPreInitialize()
        {
            // Игра грузит мод через Assembly.Load(byte[]) и не ищет соседние DLL сама,
            // поэтому 0Harmony.dll рядом с модом надо разрешать вручную.
            _dir = configuration.directory;
            BotManager.OpenLogs(_dir);
            BotSettings.Load(Path.Combine(_dir, "bots.txt"));
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLocal;
        }

        private static Assembly ResolveLocal(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string path = Path.Combine(_dir, simpleName + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
            return null;
        }

        protected override void OnPostInitialize()
        {
            // Мозг — чистая функция, и проверить его можно до всякой игры: дешевле,
            // чем ловить поломку глазами посреди матча.
            string fail = BotBrain.SelfTest();
            BotManager.Log(fail == null
                ? "мод: самопроверка мозга ок"
                : "мод: САМОПРОВЕРКА МОЗГА УПАЛА — " + fail);
            ApplyPatches();
            BotManager.hideFromHash = this; // подмена отпечатка — в первом кадре, см. там
            BotManager.FlushLogs();
        }

        // Отдельный метод: типы Harmony разрешаются при JIT-компиляции метода,
        // а он вызывается уже после того, как обработчик AssemblyResolve повешен.
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

    // Тип смерти игра передаёт только сюда: DTFall — падение, DTShot — пуля или
    // осколки, DTImpale — шипы и пилы, DTIncinerate — огонь. Postfix ничего не
    // меняет в игре, только запоминает причину для журнала.
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
                // Журнал не должен ронять смерть утки.
            }
        }
    }

    // Уходя в сеть из лобби, игра собирает здесь список локальных игроков. Ботов из него
    // вычёркиваем, иначе они уедут в сетевую игру без мозга (BotManager.DropBotsFromMatchmaking).
    [HarmonyPatch(typeof(TeamSelect2), "FillMatchmakingProfiles")]
    public static class MatchmakingPatch
    {
        private static void Postfix()
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

    // Меню паузы лобби игра пересобирает целиком при каждом открытии,
    // поэтому пункт добавляем здесь, а не один раз при старте.
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
                // Падение здесь уронило бы всю паузу, а не только наш пункт.
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

            // Игра в конце BuildPauseMenu трижды прогоняет группу, чтобы посчитались
            // размеры. Мы добавились после этих прогонов — повторяем за ней.
            pauseGroup.Update();
            pauseGroup.Update();
            pauseGroup.Update();
        }

        // Подменю BOTS: семь строк OFF/DUCKLING/…, BACK возвращает в меню back.
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

        // В меню паузы лобби единственный ребёнок секции — UIDivider:
        // слева колонка пунктов, справа картинка.
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

    // В сети пауза — другое меню: «MULTIPLAYER» собирает DuckNetwork.OpenMenu в свою
    // группу при каждом открытии, BuildPauseMenu лобби тут ни при чём. Пункт BOTS — только
    // хосту и только в лобби: ботов в сети сажает хост (BotManager.SyncSlotsOnline), а в
    // матче состав не меняют. Меню к этому моменту уже открыто; Insert помечает его
    // грязным, и раскладка пересчитается сама.
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
            if (!Network.isServer || !(Level.current is TeamSelect2)) return;
            if (_groupProp == null)
                _groupProp = typeof(DuckNetwork).GetProperty("_ducknetUIGroup",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            UIComponent group = _groupProp == null ? null : _groupProp.GetValue(null, null) as UIComponent;
            UIMenu menu = DuckNetwork.core._ducknetMenu;
            if (group == null || menu == null) { BotManager.Log("не найдено сетевое меню паузы"); return; }

            UIMenu botsMenu = PauseMenuPatch.BotsMenu(menu, group);
            UIMenuItem item = new UIMenuItem("|DGBLUE|BOTS", new UIMenuActionOpenMenu(menu, botsMenu), UIAlign.Left);
            // Рядом со своими: после EDIT SLOTS. Не нашли — в конец.
            IList<UIComponent> parts = PauseMenuPatch.Section(menu);
            int at = -1;
            for (int i = 0; parts != null && i < parts.Count; i++)
            {
                UIMenuItem mi = parts[i] as UIMenuItem;
                if (mi != null && mi.text != null && mi.text.Contains("EDIT SLOTS")) at = i + 1;
            }
            if (at < 0) menu.Add(item, true);
            else menu.Insert(item, at, true);
        }
    }

    // Подпись над ботом: в бою сразу видно, почему он ведёт себя именно так.
    // Лог отвечает на вопрос «что было», подпись — на вопрос «что он думает прямо сейчас».
    [HarmonyPatch(typeof(Duck), "Draw")]
    public static class DuckDrawPatch
    {
        private static void Postfix(Duck __instance)
        {
            try
            {
                if (__instance.dead || !(__instance.inputProfile is BotInput)) return;
                Bot b = BotManager.Find(__instance);
                if (b == null) return;
                Graphics.DrawString(BotBrain.modeNames[b.state.mode],
                    new Vec2(__instance.x - 12f, __instance.y - 26f), Color.White, 0.9f);
            }
            catch (Exception)
            {
                // Падение в отрисовке уронило бы кадр целиком, а это всего лишь подпись.
            }
        }
    }
}
