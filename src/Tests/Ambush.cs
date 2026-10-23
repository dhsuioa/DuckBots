using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Враг на острове, куда снизу дороги нет, но откуда он может спрыгнуть: на level_8
        // 23.10 бот без маршрута шёл к нему по прямой, вставал под ним и прыгал в полку.
        // Теперь цель — место, где враг войдёт в нашу часть карты, и ждать там.
        private static string AmbushTest()
        {
            // Полка на пять клеток выше пола (80 пикселей, прыжок даёт 57): снизу на неё
            // не попасть, а с правого края можно сойти вниз.
            string[] island = {
                "............",
                "............",
                "###.........",
                "............",
                "............",
                "............",
                "............",
                "############" };
            NavMap nav = new NavMap();
            nav.Build(Cells(island), 12, 8, 16f, 0f, 0f);
            int bot = nav.NodeNear(8f, 104f), enemy = nav.NodeNear(24f, 24f);
            if (bot < 0 || enemy < 0) return "засада: на карте-острове нет узлов бота или врага";
            nav.SearchAll(bot);
            if (nav.RouteLength(enemy) < float.MaxValue) return "засада: на полку в 80 пикселей граф нашёл дорогу снизу";
            int arrival = nav.Arrival(bot, enemy);
            if (arrival < 0) return "засада: враг спрыгнуть может, а места спуска нет";
            if (nav.CellCol(arrival) != 3 || nav.CellRow(arrival) != 6)
                return "засада: враг спустится в (3,6), сойдя с края, а место — (" + nav.CellCol(arrival) + ","
                    + nav.CellRow(arrival) + ")";
            if (nav.RouteLength(bot) != 0f || nav.RouteLength(arrival) >= float.MaxValue)
                return "засада: после поиска места спуска маршрут считается не от бота";

            // Враг в закрытой комнате: не выйти ни ему, ни к нему — засады нет.
            string[] closed = {
                "#####.......",
                "#...#.......",
                "#...#.......",
                "#####.......",
                "............",
                "############" };
            nav.Build(Cells(closed), 12, 6, 16f, 0f, 0f);
            int outside = nav.NodeNear(152f, 72f), inside = nav.NodeNear(40f, 40f);
            if (outside < 0 || inside < 0) return "засада: на карте с комнатой нет узлов";
            if (nav.Arrival(outside, inside) != -1)
                return "засада: из закрытой комнаты враг не выйдет, а место спуска нашлось";

            // Мозг: без ствола, в засаде, враг над головой. Раньше — к врагу напрямик и
            // прыжок наугад в полку. Теперь повернуться к месту спуска и стоять.
            BotView v = AmbushView(0, -30f, true);
            BotState s = new BotState();
            ushort m = 0;
            for (int f = 0; f < 60; f++)
            {
                m = Decide(ref v, ref s);
                if ((m & BotInput.JUMP) != 0) return "засада: без ствола прыгает под врагом (кадр " + f + ", " + s.how + ")";
                if ((m & BotInput.RIGHT) != 0) return "засада: идёт не к месту спуска, а прочь от него";
                if ((m & BotInput.LEFT) != 0) v.facingRight = false;
            }
            if (m != 0 || v.facingRight) return "засада: не встал лицом к месту спуска (кнопки " + m + ")";
            if (s.why != WhyAmbush) return "причина: засада записана как «" + s.why + "»";

            // Со стволом: смотреть на место спуска, а не на врага над головой.
            v = AmbushView(1, 48f, false);
            s = new BotState();
            if ((Decide(ref v, ref s) & BotInput.RIGHT) == 0) return "засада: со стволом не повернулся к месту спуска";
            v.facingRight = true;
            if (Decide(ref v, ref s) != 0) return "засада: со стволом, лицом к месту спуска, всё равно жмёт кнопки";

            // Место спуска под ногами — не вертеться.
            v = AmbushView(1, 3f, false);
            s = new BotState();
            if (Decide(ref v, ref s) != 0) return "засада: место спуска под ногами, а бот поворачивается";
            return null;
        }

        private static BotView AmbushView(int holding, float ambushDx, bool facingRight)
        {
            BotView v = new BotView();
            v.hasEnemy = true; v.ex = 0f; v.ey = -150f; v.holding = holding;
            v.grounded = true; v.facingRight = facingRight;
            v.ambush = true; v.atSpot = true; v.ambushDx = ambushDx;
            return v;
        }
    }
}
