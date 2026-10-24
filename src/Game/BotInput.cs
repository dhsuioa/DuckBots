using System.Collections.Generic;
using DuckGame;

namespace DuckBots
{
    // Утка читает ввод через виртуальные Down/Pressed/Released: бот отвечает из своей маски,
    // а физику, оружие и смерть игра обрабатывает как для живого игрока.
    public class BotInput : InputProfile
    {
        public const ushort LEFT = 1;
        public const ushort RIGHT = 2;
        public const ushort UP = 4;
        public const ushort DOWN = 8;
        public const ushort SHOOT = 16;
        public const ushort JUMP = 32;
        public const ushort GRAB = 64;
        public const ushort QUACK = 128;
        public const ushort START = 256;
        public const ushort RAGDOLL = 512;
        public const ushort STRAFE = 1024;
        public const ushort CANCEL = 2048;   // в лобби: выйти из портала

        private static readonly Dictionary<string, ushort> _bits = new Dictionary<string, ushort>
        {
            { "LEFT", LEFT }, { "RIGHT", RIGHT }, { "UP", UP }, { "DOWN", DOWN },
            { "SHOOT", SHOOT }, { "JUMP", JUMP }, { "GRAB", GRAB }, { "QUACK", QUACK },
            { "START", START }, { "RAGDOLL", RAGDOLL }, { "STRAFE", STRAFE }, { "CANCEL", CANCEL },
            { "MENULEFT", LEFT }, { "MENURIGHT", RIGHT }, { "MENUUP", UP }, { "MENUDOWN", DOWN },
            { "SELECT", START }
        };

        private ushort _mask;
        private ushort _prevMask;

        // Строка меню BOTS: по ней бот находится снова, когда игра сама привела его в сеть.
        public readonly int slot;

        public BotInput(int slot) : base("bot" + (slot + 1)) { this.slot = slot; }

        public ushort mask { get { return _mask; } }

        // Раз за кадр, до того как игра читает ввод.
        public void SetMask(ushort value)
        {
            _prevMask = _mask;
            _mask = value;
        }

        private static ushort BitFor(string trigger)
        {
            ushort bit;
            if (trigger != null && _bits.TryGetValue(trigger, out bit)) return bit;
            return 0;
        }

        public override bool Down(string trigger)
        {
            if (trigger == "ANY") return _mask != 0;
            return (_mask & BitFor(trigger)) != 0;
        }

        public override bool Pressed(string trigger, bool any = false)
        {
            if (any || trigger == "ANY") return _mask != 0 && _prevMask == 0;
            ushort b = BitFor(trigger);
            return (_mask & b) != 0 && (_prevMask & b) == 0;
        }

        public override bool Released(string trigger)
        {
            ushort b = BitFor(trigger);
            return (_mask & b) == 0 && (_prevMask & b) != 0;
        }
    }
}
