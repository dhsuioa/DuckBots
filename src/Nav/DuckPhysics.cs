using System;

namespace DuckBots
{
    public struct SimDuck
    {
        public float x, bottom, hs, vs;
        public bool grounded, jumping, hovering, jumpWas;
        public int groundValid, jumpValid;
    }

    // Физика утки по кадрам, как Duck.UpdateMove и PhysicsObject.UpdatePhysics, сверена с
    // самописцем. Трение 0.25 действует и в воздухе. Отпустил прыжок в подъёме — половина
    // скорости. Взмах — новое нажатие в воздухе после окна койота, с ним падаешь не быстрее
    // ~1 px за кадр. Нажатие прыжка игра помнит 4 кадра, койот — 7.
    public static class DuckPhysics
    {
        public const float Accel = 0.55f, RunMax = 3.1f, Friction = 0.25f, Gravity = 0.2f;
        public const float JumpSpeed = -4.9f, FallMax = 8.2f, HoverFall = 1f, HoverLift = 0.15f;
        public const float HalfWidth = 4f, Height = 22f;   // бокс 8×22, низ на 15 ниже центра

        // Кнопки и скорости одного кадра, без столкновений — их считает карта.
        public static void Controls(ref SimDuck d, int dir, bool jumpDown)
        {
            bool pressed = jumpDown && !d.jumpWas, released = !jumpDown && d.jumpWas;
            d.jumpWas = jumpDown;
            d.jumpValid = pressed ? 4 : Math.Max(0, d.jumpValid - 1);
            if (d.grounded) { d.groundValid = 7; d.hovering = false; }
            else d.groundValid = Math.Max(0, d.groundValid - 1);

            if (dir < 0 && d.hs > -RunMax) { d.hs -= Accel; if (d.hs < -RunMax) d.hs = -RunMax; }
            if (dir > 0 && d.hs < RunMax) { d.hs += Accel; if (d.hs > RunMax) d.hs = RunMax; }

            bool canJump = d.jumpValid > 0 && d.groundValid > 0;
            if (canJump)
            {
                d.vs = JumpSpeed;
                d.jumping = true;
                d.groundValid = 0;
                d.jumpValid = 0;
                d.hovering = false;
                d.grounded = false;
            }
            if (released)
            {
                if (d.jumping)
                {
                    d.jumping = false;
                    if (d.vs < 0f) d.vs *= 0.5f;
                }
                d.hovering = false;
            }
            if (!canJump && d.groundValid <= 0)
            {
                if (!d.hovering && pressed) d.hovering = true;
                if (d.hovering && d.vs >= 0f)
                {
                    if (d.vs > HoverFall) d.vs = HoverFall;
                    d.vs -= HoverLift;
                }
            }

            // Трение, потом гравитация — до перемещения.
            if (d.hs > -Friction && d.hs < Friction) d.hs = 0f;
            else d.hs -= d.hs > 0f ? Friction : -Friction;
            d.vs += Gravity;
            if (d.vs > FallMax) d.vs = FallMax;
        }
    }
}
