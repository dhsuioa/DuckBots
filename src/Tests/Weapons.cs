using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Стволы со своей механикой выстрела.
        private static string WeaponsTest()
        {
            // --- огнемёт: SHOOT зажат всё время, пока есть линия ---
            // Огонь идёт только при зажатом SHOOT и с 5-го кадра: импульсы его не зажигали.
            BotView v = new BotView();
            BotState s = new BotState();
            v.hasEnemy = true; v.ex = 40f; v.canShoot = true; v.holding = 1; v.holdFire = true;
            v.facingRight = true; v.grounded = true;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.SHOOT) == 0)
                    return "огнемёт: отпустил SHOOT на кадре " + i + " — огонь не пойдёт";
            if (s.why != WhyShoot) return "причина: огнемёт записан как «" + s.why + "»";

            // --- огнемёт вверх не стреляет: огонь падает на стрелка ---
            v = new BotView(); s = new BotState();
            v.hasEnemy = true; v.ex = 4f; v.ey = -40f; v.canShootUp = true; v.holding = 1; v.holdFire = true;
            v.facingRight = true; v.grounded = true;
            for (int i = 0; i < 20; i++)
                if ((Decide(ref v, ref s) & BotInput.SHOOT) != 0) return "огнемёт: стреляет вверх, на себя";

            return LobTest();
        }

        // Гранатомёт: дуга до врага и цикл «нажать — держать, пока ствол поднимается, —
        // отпустить». Раньше бот жал SHOOT по 3 кадра, и граната уходила почти плоско.
        private static string LobTest()
        {
            // Враг на той же высоте в 120 px: граната за 12 кадров опускается на 13 px,
            // плоский выстрел проходит мимо, нужен чуть задранный ствол.
            int flat = LobAngle(120f, 0f, LobBarrel, null);
            if (flat <= 0 || flat > 12) return "гранатомёт: до врага в 120 px на той же высоте угол " + flat;
            if (LobAngle(-120f, 0f, LobBarrel, null) != flat) return "гранатомёт: влево угол не тот, что вправо";
            int up = LobAngle(120f, -60f, LobBarrel, null);
            if (up <= flat) return "гранатомёт: враг выше на 60, а угол не круче (" + up + " против " + flat + ")";
            // Бортик высотой 6 px посередине: пологие дуги в него упираются, нужна круче.
            LobWall lip = (x0, y0, x1, y1) => (x0 - 60f) * (x1 - 60f) <= 0f && Math.Max(y0, y1) > -6f;
            int over = LobAngle(120f, 0f, LobBarrel, lip);
            if (over <= flat) return "гранатомёт: через бортик угол не круче (" + over + " против " + flat + ")";
            LobWall shut = (x0, y0, x1, y1) => true;
            if (LobAngle(120f, 0f, LobBarrel, shut) != -1) return "гранатомёт: стреляет сквозь стену";
            if (LobAngle(700f, 0f, LobBarrel, null) != -1) return "гранатомёт: дуга на 700 px, а граната столько не летит";

            // Цикл выстрела. Модель ствола: нажатие начинает подъём, через 2 кадра +3° за
            // кадр, пока SHOOT держат; отпускание — выстрел на нынешнем угле.
            BotView v = new BotView();
            BotState s = new BotState();
            v.hasEnemy = true; v.ex = 120f; v.canShoot = true; v.holding = 1; v.blastInHand = true;
            v.lobber = true; v.lobAngle = 30; v.facingRight = true; v.grounded = true;
            ushort prev = 0;
            int wait = 0;
            bool fired = false;
            for (int i = 0; i < 60 && !fired; i++)
            {
                ushort m = Decide(ref v, ref s);
                bool down = (m & BotInput.SHOOT) != 0;
                if (!v.lobAiming && down && (prev & BotInput.SHOOT) == 0) { v.lobAiming = true; wait = 2; }
                else if (v.lobAiming && down) { if (wait > 0) wait--; else v.lobAim += LobStep; }
                else if (v.lobAiming)
                {
                    fired = true;
                    if (v.lobAim < v.lobAngle || v.lobAim > v.lobAngle + LobStep)
                        return "гранатомёт: отпустил на " + v.lobAim + "°, а нужно " + v.lobAngle + "°";
                }
                prev = m;
            }
            if (!fired) return "гранатомёт: так и не выстрелил";
            if (s.why != WhyLob) return "причина: гранатомёт записан как «" + s.why + "»";

            // Ствол остывает — нажатий он не слышит, жать незачем.
            v.lobAiming = false; v.lobAim = 0f; v.lobCooling = true;
            for (int i = 0; i < 5; i++)
                if ((Decide(ref v, ref s) & BotInput.SHOOT) != 0) return "гранатомёт: жмёт, пока ствол остывает";
            return null;
        }
    }
}
