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
    // Короткая память о недостижимых целях. Бот выбирает ближайший ствол по прямой,
    // а дороги к нему может не быть вовсе — ствол в закрытой комнате этажом ниже.
    // Без этой памяти он держался за такой ствол бесконечно, а в логе три бота
    // разом стояли с «маршрут=нет» над одним и тем же стволом. Забытая цель
    // возвращается через несколько секунд: геометрия меняется, дорога может открыться.
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

    // Настройка одного слота. Отдельный объект, а не элемент int[], потому что
    // FieldBinding умеет привязываться только к полю объекта по имени.
    public class BotSlot
    {
        public int level;
    }

    public static class BotSettings
    {
        public const int Max = 7; // восемь игроков минус место человеку

        // Значение level — индекс в этом списке, так UIMenuItemNumber показывает
        // слова вместо цифр. Шрифты игры — ASCII-спрайтшиты, кириллицы в них нет.
        public static readonly List<string> levelNames =
            new List<string> { "OFF", "DUCKLING", "AVERAGE", "GOOD", "BEAST" };

        public static readonly BotSlot[] slots = CreateSlots();

        // Выбор в меню переживает перезапуск игры: строка уровней в файле в папке мода.
        // Без файла все слоты OFF — игрок из Workshop не должен найти в лобби чужую утку.
        private static string _file;
        private static readonly int[] _saved = new int[Max];

        private static BotSlot[] CreateSlots()
        {
            BotSlot[] s = new BotSlot[Max];
            for (int i = 0; i < Max; i++) s[i] = new BotSlot();
            return s;
        }

        // «2,0,4» → уровни слотов. Нехватка, мусор и числа вне списка — OFF:
        // файл правят руками, и кривая строка не должна посадить бота в слот.
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

        // Каждый кадр лобби: семь сравнений, файл пишется только когда выбор изменился.
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
        public int slot;                   // индекс в BotSettings.slots
        public int level;
        public bool online;                // сетевой локальный игрок хоста, коробки у него нет
        public Profile profile;
        public BotInput input;
        public ProfileBox2 box;
        public InputProfile savedBoxInput; // что лежало в коробке до подмены
        public int pressStartFrames;
        public int routeWait;                                        // до следующего пересчёта маршрута
        public bool hasEnemyStep, hasPickupStep;
        public float enemyStepX, enemyStepY, pickupStepX, pickupStepY;
        public int enemyStepEdge, pickupStepEdge;
        public int enemyStepProg, pickupStepProg;
        public BotState state;   // счётчики мозга, живут между кадрами
        public BotView view;     // последняя обстановка, её же пишем в лог и рисуем над уткой
        public readonly SkipList skip = new SkipList();   // цели, забытые сторожем
        public Window glass;                               // стекло, по которому бот бил в прошлом кадре
        public bool dodging;                               // в прошлом кадре уклонялся от прицела
        public Thing pickup;                                // за чем бот сейчас идёт
        public int pickupNode = -1;                         // узел, куда за ней идти
        public Thing danger;                                // от чего бежит: в журнал
        public bool atSpot;                                 // стоит на позиции для выстрела
        public int spotWait, spotOff;                       // ждёт на ней без линии; позиция забыта
        public bool hasThrowStep, atThrowSpot;              // несёт пойманного: путь к краю
        public float throwStepX, throwStepY;
        public int throwStepEdge, throwStepProg, throwDir;
        public float routeStartX = float.NaN;               // центр узла, откуда считан маршрут

        // Сторож залипания: где бот стоял, сколько кадров не сдвинулся оттуда,
        // и сколько ещё уходить в сторону, если сторож уже сработал.
        public float anchorX, anchorY;
        public int anchorFrames, breakoutFrames;

        // Журнал: режим прошлого кадра (смена пишется с причиной), сколько кадров
        // подряд ни одной кнопки, и маршрут, по которому бот шёл в прошлый раз.
        public int lastMode = -1;
        public int idleFrames;
        public readonly int[] path = new int[512];
        public int pathLen;
    }
}
