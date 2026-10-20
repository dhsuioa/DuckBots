using System;

namespace DuckBots
{
    public static partial class BotBrain
    {
        // Опора, которой не видит карта: выступ Nubber торчит из тайла на несколько пикселей,
        // а карта размечает клетку по центру и считает её пустой. Бот стоит на выступе,
        // шаг маршрута — ровно под ним, «на месте». 20.10 на forest04_8 бот так простоял
        // 30 секунд, и сторож сработал семь раз подряд. Надо сойти туда, где опоры нет.
        private static string StepOffTest()
        {
            ushort m;
            // Слева обрыв, справа тайл: сойти влево.
            BotView v = StepOffView(true, false, false, false);
            BotState s = new BotState();
            m = Decide(ref v, ref s);
            if ((m & BotInput.LEFT) == 0 || (m & BotInput.RIGHT) != 0)
                return "сход с опоры: шаг под ногами, слева обрыв, а бот не идёт влево (кнопки " + m + ", " + s.how + ")";
            if (s.how != HowStepOff) return "причина: сход с опоры записан как «" + s.how + "»";

            // Справа обрыв: вправо.
            v = StepOffView(false, true, false, false);
            s = new BotState();
            m = Decide(ref v, ref s);
            if ((m & BotInput.RIGHT) == 0) return "сход с опоры: справа обрыв, а бот не идёт вправо";

            // Обрыв с обеих сторон, слева пропасть: только вправо — и так кадр за кадром.
            v = StepOffView(true, true, true, false);
            s = new BotState();
            for (int f = 0; f < WanderFrames * 3; f++)
            {
                m = Decide(ref v, ref s);
                if ((m & BotInput.LEFT) != 0) return "сход с опоры: шагает в пропасть слева на кадре " + f;
                if ((m & BotInput.RIGHT) == 0) return "сход с опоры: стоит, хотя справа можно сойти (кадр " + f + ")";
            }
            return null;
        }

        private static BotView StepOffView(bool gapLeft, bool gapRight, bool abyssLeft, bool abyssRight)
        {
            BotView v = new BotView();
            v.hasEnemy = true; v.ex = 0f; v.ey = 80f; v.holding = 1;
            v.grounded = true; v.facingRight = true;
            v.hasEnemyStep = true; v.enemyStepX = 0f; v.enemyStepY = 55f; v.enemyStepEdge = NavMap.Walk;
            v.gapLeft = gapLeft; v.gapRight = gapRight;
            v.abyssLeft = abyssLeft; v.abyssRight = abyssRight;
            return v;
        }
    }
}
