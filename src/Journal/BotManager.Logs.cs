using System;
using System.Globalization;
using System.IO;
using System.Text;
using DuckGame;

namespace DuckBots
{
    // Два журнала: основной (botlog.txt) — события и решения, подробный (botlog-detail.txt) —
    // самописец, маршруты, карты. По f= строки обоих сводятся. Потоки открыты, сброс раз в кадр.
    public static partial class BotManager
    {
        public static int frame, round;
        public static bool fullLog;                   // команда botlog
        private const long DetailCap = 64L << 20;
        private static StreamWriter _mainLog, _detailLog;
        private static long _detailBytes;
        private static string _detailPath;

        // Отладка: подробный журнал, подписи режимов над утками и строки журнала в консоли. У игроков
        // её нет. Включается файлом debug.txt в папке данных или командой botdebug.
        public static bool debug;

        public static void SetDebug(bool on)
        {
            debug = on;
            if (on && _detailLog == null && _detailPath != null)
            {
                _detailLog = OpenLog(_detailPath);
                if (_detailLog != null) _detailLog.WriteLine("--- отладка включена " + DateTime.Now + " ---");
            }
        }

        public static void OpenLogs(string dir)
        {
            string main = Path.Combine(dir, "botlog.txt"), detail = Path.Combine(dir, "botlog-detail.txt");
            _detailPath = detail;
            debug = File.Exists(Path.Combine(dir, "debug.txt"));
            // Игру перезапускают после каждой сборки: прошлый матч — в *.prev.txt. Пустой запуск
            // (меньше 16 КБ) старую копию не трогает.
            try
            {
                if (File.Exists(main) && new FileInfo(main).Length > 16384)
                {
                    File.Copy(main, Path.Combine(dir, "botlog.prev.txt"), true);
                    if (File.Exists(detail)) File.Copy(detail, Path.Combine(dir, "botlog-detail.prev.txt"), true);
                }
            }
            catch (Exception) { }
            _mainLog = OpenLog(main);
            if (debug) _detailLog = OpenLog(detail);
            string head = "--- запуск " + DateTime.Now + " ---";
            if (_mainLog != null) _mainLog.WriteLine(head);
            if (_detailLog != null) _detailLog.WriteLine(head);
            FlushLogs();
        }

        private static StreamWriter OpenLog(string path)
        {
            try
            {
                // ReadWrite — журнал можно читать, пока игра пишет.
                return new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write,
                    FileShare.ReadWrite), new UTF8Encoding(false));
            }
            catch (Exception) { return null; }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + " f=" + frame + " р=" + round + " ";
        }

        public static void Log(string msg)
        {
            if (debug) DevConsole.Log(DCSection.Mod, "DuckBots: " + msg);
            if (_mainLog == null) return;
            try { _mainLog.WriteLine(Stamp() + msg); }
            catch (Exception) { }
        }

        // Подробный журнал пишется: выключен — строки для него не собирать.
        private static bool DetailOn { get { return _detailLog != null && _detailBytes <= DetailCap; } }

        public static void Detail(string msg)
        {
            if (_detailLog == null || _detailBytes > DetailCap) return;
            try
            {
                string line = Stamp() + msg;
                _detailBytes += line.Length * 2;   // кириллица в UTF-8 — два байта
                _detailLog.WriteLine(line);
                if (_detailBytes > DetailCap)
                    _detailLog.WriteLine(Stamp() + "мод: подробный журнал дорос до 64 МБ, дальше не пишется");
            }
            catch (Exception) { }
        }

        public static void FlushLogs()
        {
            try
            {
                if (_mainLog != null) _mainLog.Flush();
                if (_detailLog != null) _detailLog.Flush();
            }
            catch (Exception) { }
        }
    }
}
