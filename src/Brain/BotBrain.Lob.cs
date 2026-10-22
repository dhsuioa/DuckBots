using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Дуга гранатомёта (GrenadeLauncher, патрон ATGrenade). Пока SHOOT зажат, ствол
        // поднимается на 3° за кадр, выстрел — на отпускании. Граната летит со скоростью 9:
        // каждый кадр сдвигается на свою скорость, потом vSpeed += 0.2, не больше 8
        // (Bullet.Update), трения нет. О утку она рвётся, осколки бьют ещё на 25–35 px.
        public const float LobSpeed = 9f, LobGravity = 0.2f, LobMaxFall = 8f;
        public const float LobHitR = 12f;      // дуга проходит так близко от центра врага — попала
        public const float LobBarrel = 12f;    // от центра гранатомёта до дула; ствол вертится вокруг центра
        public const int LobStep = 3;          // столько градусов ствол набирает за кадр
        // Круче не целимся: на ровном полу граната с 75° падает в 200 px от стрелка, с 78°
        // (отпустили на шаг позже) — в 170, а с 87° — в 40, на него самого. Поэтому и
        // выстрел, сорвавшийся на любом угле до этого, стрелка не достанет.
        public const int LobMax = 75;
        public const int LobFrames = 45;       // дольше граната летит — враг уйдёт

        // Стена на отрезке (координаты от центра гранатомёта). Её проверяют глаза: мозг
        // уровня не видит.
        public delegate bool LobWall(float x0, float y0, float x1, float y1);

        // Самый пологий угол ствола, кратный LobStep, при котором граната проходит ближе
        // LobHitR от цели (dx, dy), не задев стену; −1 — такой дуги нет. Сначала без стен:
        // это дёшево, и стены проверяются только у дуги, которая попадает.
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
                // Перелетела врага или уже падает ниже него — дальше не попадёт.
                if (dir * nx > dir * dx + LobHitR || (vy > 0f && ny > dy + LobHitR)) return false;
                x = nx;
                y = ny;
                vy += LobGravity;
                if (vy > LobMaxFall) vy = LobMaxFall;
            }
            return false;
        }

        // Расстояние от точки (px, py) до отрезка (x0, y0)–(x1, y1).
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
