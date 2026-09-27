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

            UIMenu botsMenu = new UIMenu("BOTS", Layer.HUD.camera.width / 2f,
                                         Layer.HUD.camera.height / 2f, 180f);
            for (int i = 0; i < BotSettings.Max; i++)
                botsMenu.Add(new UIMenuItemNumber("BOT " + (i + 1), null,
                    new FieldBinding(BotSettings.slots[i], "level",
                                     0f, BotSettings.levelNames.Count - 1, 1f),
                    valStrings: BotSettings.levelNames), true);
            botsMenu.Add(new UIMenuItem("BACK", new UIMenuActionOpenMenu(botsMenu, pauseMenu),
                                        backButton: true), true);
            botsMenu.Close();
            pauseGroup.Add(botsMenu, false);
            botsMenu.DoUpdate();

            column.leftSection.Add(new UIMenuItem("BOTS",
                new UIMenuActionOpenMenu(pauseMenu, botsMenu)), true);

            // Игра в конце BuildPauseMenu трижды прогоняет группу, чтобы посчитались
            // размеры. Мы добавились после этих прогонов — повторяем за ней.
            pauseGroup.Update();
            pauseGroup.Update();
            pauseGroup.Update();
        }

        // UIMenu.Add кладёт не в components, а в приватный _section. Внутри него
        // единственный ребёнок — UIDivider: слева колонка пунктов, справа картинка.
        private static UIDivider FindColumn(UIMenu menu)
        {
            FieldInfo sf = Field(typeof(UIMenu), "_section", ref _sectionField);
            if (sf == null) return null;
            UIComponent section = sf.GetValue(menu) as UIComponent;
            if (section == null) return null;

            IList<UIComponent> parts = section.components;
            for (int i = 0; i < parts.Count; i++)
                // Тип именно точный: UIMenuItem — тоже наследник UIDivider.
                if (parts[i].GetType() == typeof(UIDivider)) return (UIDivider)parts[i];
            return null;
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
