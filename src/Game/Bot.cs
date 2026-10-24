using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DuckGame;

namespace DuckBots
{
    // Короткая память о целях, к которым нет дороги: забытая цель через несколько секунд
    // возвращается, геометрия могла измениться.
    public class SkipList
    {
        private readonly object[] _items = new object[4];
        private readonly int[] _left = new int[4];

        public void Add(object item, int frames)
        {
            int slot = 0;
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i] == item) { slot = i; break; }
                if (_left[i] < _left[slot]) slot = i;   // вытесняем ту, что забудется раньше всех
            }
            _items[slot] = item;
            _left[slot] = frames;
        }

        public bool Contains(object item)
        {
            if (item == null) return false;
            for (int i = 0; i < _items.Length; i++)
                if (_left[i] > 0 && _items[i] == item) return true;
            return false;
        }

        public void Tick()
        {
            for (int i = 0; i < _left.Length; i++)
                if (_left[i] > 0 && --_left[i] == 0) _items[i] = null;
        }
    }

    // Объект, а не элемент int[]: FieldBinding привязывается только к полю объекта.
    public class BotSlot
    {
        public int level;
    }

    public static class BotSettings
    {
        public const int Max = 7;   // восемь игроков минус человек

        // level — индекс сюда. Шрифты игры — ASCII, кириллицы в них нет.
        public static readonly List<string> levelNames =
            new List<string> { "OFF", "DUCKLING", "AVERAGE", "GOOD", "BEAST" };

        public static readonly BotSlot[] slots = CreateSlots();

        private static string _file;
        private static readonly int[] _saved = new int[Max];

        private static BotSlot[] CreateSlots()
        {
            BotSlot[] s = new BotSlot[Max];
            for (int i = 0; i < Max; i++) s[i] = new BotSlot();
            return s;
        }

        // «2,0,4» → уровни слотов. Нет файла, мусор, число вне списка — OFF: игрок из Workshop
        // не должен найти в лобби чужую утку.
        public static int[] Parse(string text)
        {
            int[] levels = new int[Max];
            if (text == null) return levels;
            string[] parts = text.Split(',');
            for (int i = 0; i < Max && i < parts.Length; i++)
            {
                int v;
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                    && v >= 0 && v < levelNames.Count)
                    levels[i] = v;
            }
            return levels;
        }

        public static void Load(string file)
        {
            _file = file;
            string text = null;
            try
            {
                if (File.Exists(file)) text = File.ReadAllText(file);
            }
            catch (Exception e)
            {
                BotManager.Log("мод: настройки ботов не прочитались, все OFF: " + e.Message);
            }
            int[] levels = Parse(text);
            for (int i = 0; i < Max; i++) _saved[i] = slots[i].level = levels[i];
        }

        public static void SaveIfChanged()
        {
            if (_file == null) return;
            bool changed = false;
            for (int i = 0; i < Max; i++)
                if (slots[i].level != _saved[i]) { changed = true; break; }
            if (!changed) return;
            for (int i = 0; i < Max; i++) _saved[i] = slots[i].level;
            try
            {
                File.WriteAllText(_file, string.Join(",", _saved));
            }
            catch (Exception e)
            {
                BotManager.Log("мод: настройки ботов не сохранились: " + e.Message);
            }
        }
    }

    public class Bot
    {
        public int slot;
        public int level;
        public bool online;                 // сетевой локальный игрок, коробки у него нет
        public Profile profile;
        public BotInput input;
        public ProfileBox2 box;
        public InputProfile savedBoxInput;  // что лежало в коробке до подмены
        public int pressStartFrames;
        public int routeWait;               // до пересчёта маршрута
        public bool hasEnemyStep, hasPickupStep;
        public float enemyStepX, enemyStepY, pickupStepX, pickupStepY;
        public int enemyStepEdge, pickupStepEdge;
        public int enemyStepProg, pickupStepProg;
        public BotState state;
        public BotView view;
        public readonly SkipList skip = new SkipList();
        public Window glass;                // по которому бил в прошлом кадре
        public bool dodging;
        public Thing pickup;
        public int pickupNode = -1;
        public Thing danger;
        public bool atSpot;
        public int spotWait, spotOff;
        public float prevVs;                // для толчка пружины
        public int flightStart = -1;        // узел над пружиной, с которой подбросило
        public bool hasThrowStep, atThrowSpot;
        public float throwStepX, throwStepY;
        public int throwStepEdge, throwStepProg, throwDir;
        public float routeStartX = float.NaN;   // центр узла, откуда считан маршрут

        public float anchorX, anchorY;
        public int anchorFrames, breakoutFrames;

        public BotState lobbyState;         // свои счётчики мозга в лобби
        public int lobbyFrames;
        public bool lobbyGlass, lobbyBeam;
        public ProfileBox2 lobbyBox;

        public int lastMode = -1;
        public int idleFrames;
        public readonly int[] path = new int[512];
        public int pathLen;
    }
}
