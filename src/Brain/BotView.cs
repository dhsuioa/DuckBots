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
    // Обстановка вокруг бота одним снимком. Только числа и флаги, никаких игровых
    // типов: поэтому Decide можно прогнать на выдуманных ситуациях, не запуская игру.
    public struct BotView
    {
        public bool hasEnemy;
        public float ex, ey;        // враг относительно бота
        public bool canShoot;       // линия огня чиста и по высоте совпали
        public bool canShootUp;     // враг над головой: достать можно только взмахами
        public int holding;         // 0 пусто, 1 годное оружие, 2 негодное, 3 не оружие
        public bool explosiveInHand;// граната или мина: их не держат, их бросают
        public bool armedInHand;    // чека уже выдернута — бросать немедленно
        public bool presentInHand;  // подарок: его открывают, а не выбрасывают
        public bool meleeInHand;    // меч, пила, кувалда: бьют вплотную, а не издалека
        public bool blastInHand;    // базука, гранатомёт, ракетница: взрыв достаёт стрелка вблизи
        public bool breakout;       // сторож: бот топчется на месте — уйти в сторону
        public bool netted;         // бот сам в сетке: вырываться прыжками
        public bool ragdoll;        // бот в рэгдолле: тело в Ragdoll, координаты утки — на 5000 выше
        public bool atSpot;         // стоит на позиции для выстрела: ждать линию, а не лезть к врагу
        // Сеткомёт: поймать врага сеткой, поднять и бросить в пропасть.
        public bool netGun;         // в руках сеткомёт, и на карте есть куда бросать
        public bool pickupIsTrap;   // цель подбора — враг в сетке
        public bool carrying;       // несём пойманного врага
        public bool atThrowSpot;    // стоим на краю пропасти, откуда бросать
        public int throwDir;        // с какой стороны пропасть
        public bool hasThrowStep;
        public float throwStepX, throwStepY;
        public int throwStepEdge, throwStepProg;
        public bool hasPickup;
        public float px, py;        // ближайший свободный ствол или ящик
        public bool pickupIsBox;
        public bool danger;
        public float dx;            // взведённая взрывчатка рядом, знак = с какой стороны
        public bool facingRight;
        public bool grounded;
        public float speedAbs;      // |hSpeed|, по нему видно, что упёрлись
        public bool gapLeft;        // слева под ногами нет опоры на расстоянии шага
        public bool gapRight;
        public bool abyssLeft;      // а под обрывом до низа карты ни пола, ни платформы
        public bool abyssRight;
        public bool platformBelow;  // стоим на односторонней платформе, сквозь неё можно вниз
        public bool abyssBelow;     // а под ней пропасть: проваливаться некуда
        // Следующий шаг маршрута: куда идти и что там делать (NavMap.Walk/Jump/Drop).
        public bool hasEnemyStep, hasPickupStep;
        public float enemyStepX, enemyStepY, pickupStepX, pickupStepY;
        public int enemyStepEdge, pickupStepEdge;
        public int enemyStepProg, pickupStepProg;   // программа прыжка, если шаг — прыжок
        public float hs;            // скорость по x со знаком
        public bool kicked;         // в этом кадре бота подбросило (пружина): шаг маршрута — уже из полёта
        public float alignDx;       // сколько до центра своей клетки: прыжок по программе — оттуда
        // Ближайшее целое вертикальное стекло на высоте утки, от центра до центра:
        // у зазора ноль знак стороны теряется. Маршрут ведёт сквозь стекло ребром Smash.
        public bool hasGlass;
        public float glassDx;
        public bool glassGun;       // в руках ствол, которым стекло можно разбить вблизи
        // Враг целится в бота: ствол, смотрит на него, та же высота, чистая линия.
        public bool threatened;
        public float threatDx;      // где стрелок, относительно бота
        public bool sliding;        // утка в подкате: лежит, пока зажат DOWN
    }

    // Что бот помнит между кадрами. Только счётчики: решение принимается заново каждый кадр.
    public struct BotState
    {
        public int mode;
        public int shootTimer;
        public int grabTimer;
        public int jumpTimer;
        public int stuckFrames;
        public int jumpHold;      // сколько ещё кадров держать начатый прыжок
        public int jumpRest;      // сколько кадров кнопка должна побыть отпущенной
        public int wanderDir;     // куда идём вдоль платформы, когда цель строго сверху или снизу
        public int wanderFrames;
        public int breakDir;      // куда уходим, когда сторож сказал «застрял»

        // Почему решено именно так: ветка мозга (why) и способ хода (how). Это
        // строки-константы BotBrain: журнал их печатает, самопроверка сверяет,
        // а кадр не рождает мусора.
        public string why, how;
        public string jumpBy;          // кто начал текущий прыжок: ребро маршрута или догадка
        public float jumpDX, jumpDY;   // куда он целился, относительно точки отрыва
        public int prog;               // программа прыжка по ребру маршрута
        public int progAt;             // идёт её кадр progAt - 1; 0 — программы нет

        // Удар по стеклу — тоже программа на несколько кадров: отойти, потом
        // выстрелить или разогнаться и подкатиться.
        public int smashDir;           // в какую сторону стекло; 0 — программы нет
        public bool smashRun;          // отход кончился, бьём
        public int smashFrames;        // кадров в нынешней фазе
        public int smashTries;         // неудачных попыток подряд

        // Подкат от выстрела: разогнаться, лечь и лежать, пока в бота целятся.
        public int threatFrames;       // сколько кадров подряд в бота целятся
        public int dodgeFrames;        // сколько кадров уклонение уже идёт; 0 — не идёт
        public int dodgeDir;           // куда разгоняемся перед подкатом
        public int dodgeLeft;          // сколько ещё лежать, если целиться перестали
        public int dodgeRest;          // сколько ещё кадров не ложиться после долгого лежания
    }
}
