using DuckGame;

namespace DuckBots
{
    public struct RecFrame
    {
        public int frame;
        public float x, y, hs, vs;
        public bool grounded, ragdoll, frozen;
        public ushort buttons;
        public int mode;               // −1 у человека
        public string why, how;
        public bool routed;
        public float stepX, stepY;
        public int edge, jumpHold;
        public int prog, progAt;       // программа прыжка и её кадр, 0 — не идёт
    }

    // Что журнал помнит про одну утку раунда между кадрами.
    public class MoveTracer
    {
        public Duck duck;
        public Bot bot;                // null — человек
        public string who;
        public bool dead, seen;
        public float lastX, lastY, lastBottom;
        public bool lastJumpDown, lastGrounded;

        // Самописец: последние 5 с в памяти, в подробный журнал — только по событию.
        public const int RecLen = 300;
        public readonly RecFrame[] rec = new RecFrame[RecLen];
        public int recHead, recCount;
        public string dumpWhy;
        public DestroyType killType;
        public bool ragdoll;
        public string startHow;

        public bool air;
        public int airFrames, jumpHeld;
        public float fromX, fromY, apexY, startVSpeed;
        public bool jumpPressed, downHeld, throughPlatform;

        public bool routed;
        public int routeEdge, routeProg;
        public float routeToX, routeToY;

        public Holdable held;
        public bool sliding;
        public int slideFrames;
        public float slideSpeed;
        public Window slideWindow;
    }
}
