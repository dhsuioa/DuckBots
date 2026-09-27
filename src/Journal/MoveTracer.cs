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
    // Кадр самописца: всё, что нужно, чтобы по журналу восстановить движение.
    public struct RecFrame
    {
        public int frame;
        public float x, y, hs, vs;
        public bool grounded, ragdoll, frozen;
        public ushort buttons;
        public int mode;               // -1 у игрока
        public string why, how;
        public bool routed;
        public float stepX, stepY;
        public int edge, jumpHold;
        public int prog, progAt;       // программа прыжка и её кадр (0 — не идёт)
    }

    // Что журнал помнит про одну утку между кадрами: идёт ли сейчас полёт или
    // подкат, откуда начался, что было в руках. Заводится на каждую утку раунда,
    // и на игрока, и на ботов; утки пересоздаются каждый раунд, журнал вместе с ними.
    public class MoveTracer
    {
        public Duck duck;
        public Bot bot;              // null — это игрок
        public string who;
        public bool dead, seen;
        public float lastX, lastY, lastBottom;
        public bool lastJumpDown, lastGrounded;

        // Самописец: последние пять секунд кадр за кадром. Пишется в память,
        // в подробный журнал уходит только по событию — смерть, сторож, промах,
        // падение, бездействие, — поэтому каждое такое событие приходит с записью
        // того, как утка к нему пришла.
        public const int RecLen = 300;
        public readonly RecFrame[] rec = new RecFrame[RecLen];
        public int recHead, recCount;
        public string dumpWhy;       // событие, замеченное вне журнала: сбросить запись в конце кадра
        public DestroyType killType; // чем убита — ловится в Duck.Kill
        public bool ragdoll;
        public string startHow;      // что делал бот, отрываясь от земли

        // полёт
        public bool air;
        public int airFrames, jumpHeld;
        public float fromX, fromY, apexY, startVSpeed;
        public bool jumpPressed, downHeld, throughPlatform;

        // куда вёл маршрут бота, если прыжок начал он сам
        public bool routed;
        public int routeEdge, routeProg;
        public float routeToX, routeToY;

        // руки и подкат — только у игрока
        public Holdable held;
        public bool sliding;
        public int slideFrames;
        public float slideSpeed;
        public Window slideWindow;
    }
}
