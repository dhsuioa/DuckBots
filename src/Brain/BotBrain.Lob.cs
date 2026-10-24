using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Гранатомёт: пока SHOOT зажат, ствол поднимается на LobStep° за кадр, выстрел — на
        // отпускании. Граната летит со скоростью 9, потом vSpeed += 0.2 до 8 (Bullet.Update).
        public const float LobSpeed = 9f, LobGravity = 0.2f, LobMaxFall = 8f;
        public const float LobHitR = 12f;
        public const float LobBarrel = 12f;    // от центра гранатомёта до дула
        public const int LobStep = 3;
        public const int LobMax = 75;          // круче граната падает на стрелка
        public const int LobFrames = 45;

        // Стена на отрезке, координаты от центра гранатомёта.
        public delegate bool LobWall(float x0, float y0, float x1, float y1);

        // Самый пологий угол, кратный LobStep, при котором граната проходит ближе LobHitR
        // от цели мимо стен; −1 — дуги нет.
        public static int LobAngle(float dx, float dy, float barrel, LobWall wall)
        {
            for (int a = 0; a <= LobMax; a += LobStep)
                if (LobFly(a, dx, dy, barrel, null) && (wall == null || LobFly(a, dx, dy, barrel, wall)))
                    return a;
            return -1;
        }

        private static bool LobFly(int a, float dx, float dy, float barrel, LobWall wall)
        {
            float dir = dx < 0f ? -1f : 1f;
            double r = a * Math.PI / 180.0;
            float c = (float)Math.Cos(r), sn = (float)Math.Sin(r);
            float x = dir * barrel * c, y = -barrel * sn;
            float vx = dir * LobSpeed * c, vy = -LobSpeed * sn;
            for (int t = 0; t < LobFrames; t++)
            {
                float nx = x + vx, ny = y + vy;
                if (wall != null && wall(x, y, nx, ny)) return false;
                if (SegDist(x, y, nx, ny, dx, dy) < LobHitR) return true;
                if (dir * nx > dir * dx + LobHitR || (vy > 0f && ny > dy + LobHitR)) return false;
                x = nx;
                y = ny;
                vy += LobGravity;
                if (vy > LobMaxFall) vy = LobMaxFall;
            }
            return false;
        }

        private static float SegDist(float x0, float y0, float x1, float y1, float px, float py)
        {
            float ex = x1 - x0, ey = y1 - y0;
            float len = ex * ex + ey * ey;
            float k = len > 0f ? ((px - x0) * ex + (py - y0) * ey) / len : 0f;
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;
            float qx = x0 + k * ex - px, qy = y0 + k * ey - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }
    }
}
