namespace DuckBots
{
    // Обстановка вокруг бота одним снимком: только числа и флаги, без игровых типов, поэтому
    // Decide проверяется без игры. Координаты — относительно центра утки.
    public struct BotView
    {
        public bool hasEnemy;
        public float ex, ey;
        public bool canShoot;
        public bool glassLine;      // линия огня идёт сквозь целое стекло
        public bool canShootUp;     // враг над головой: достать можно только взмахами
        public int holding;         // 0 пусто, 1 годное оружие, 2 негодное, 3 не оружие
        public bool explosiveInHand;
        public bool armedInHand;    // чека выдернута
        public bool presentInHand;
        public bool meleeInHand;
        public bool blastInHand;    // взрыв достаёт стрелка вблизи
        public bool holdFire;       // огнемёт: огонь идёт, только пока SHOOT зажат
        public bool lobber;         // гранатомёт: выстрел на отпускании, пока держат — ствол поднимается
        public int lobAngle;        // угол ствола для дуги до врага, −1 — дуги нет
        public float lobAim;        // нынешний угол ствола
        public bool lobAiming;
        public bool lobCooling;     // после выстрела ствол нажатий не слышит
        public bool breakout;       // сторож: уйти в сторону
        public bool netted;
        public bool ragdoll;        // координаты утки в рэгдолле — на 5000 выше тела
        public bool atSpot;         // на позиции для выстрела
        public bool netGun;         // сеткомёт, и на карте есть пропасть
        public bool pickupIsTrap;   // цель подбора — враг в сетке
        public bool carrying;       // несём пойманного
        public bool atThrowSpot;
        public int throwDir;
        public bool hasThrowStep;
        public float throwStepX, throwStepY;
        public int throwStepEdge, throwStepProg;
        public bool hasPickup;
        public float px, py;
        public bool pickupIsBox;
        public bool danger;
        public float dx;            // где взведённая взрывчатка
        public bool facingRight;
        public bool grounded;
        public float speedAbs;
        public bool gapLeft;        // под шагом нет опоры
        public bool gapRight;
        public bool abyssLeft;      // и под обрывом до низа карты ничего
        public bool abyssRight;
        public bool platformBelow;  // под ногами односторонняя платформа
        public bool abyssBelow;
        // Шаг маршрута: куда идти и каким ребром (NavMap.Walk/Jump/Drop/Launch/Smash).
        public bool hasEnemyStep, hasPickupStep;
        public float enemyStepX, enemyStepY, pickupStepX, pickupStepY;
        public int enemyStepEdge, pickupStepEdge;
        public int enemyStepProg, pickupStepProg;
        public float hs;
        public bool kicked;         // в этом кадре подбросила пружина
        public float alignDx;       // до центра клетки, откуда прыжок по программе
        public bool hasGlass;       // ближайшее целое вертикальное стекло на высоте утки
        public float glassDx;
        public bool glassGun;       // ствол, которым стекло бьют вблизи
        public bool threatened;     // враг целится в бота
        public float threatDx;
        public bool sliding;
    }

    public struct LobbyView
    {
        public bool humanReady;     // человек в портале
        public bool inBeam;
        public bool centered;       // в центре луча: отсюда CANCEL выводит наружу
        public bool glass;
        public float glassDx;
        public bool gunHeld;
        public bool hasGun;
        public float gunDx, gunDy;
        public int exitDir;
        public bool wallLeft, wallRight;   // ступенька у ног
        public bool grounded;
        public float hs;
        public bool facingRight;
    }

    // Память мозга между кадрами.
    public struct BotState
    {
        public int mode;
        public int shootTimer;
        public int grabTimer;
        public int jumpTimer;
        public int stuckFrames;
        public int jumpHold;      // сколько ещё держать начатый прыжок
        public int jumpRest;      // сколько кнопка должна побыть отпущенной
        public int wanderDir;
        public int wanderFrames;
        public int breakDir;

        public string why, how;   // константы BotBrain: ветка Think и способ хода
        public string jumpBy;
        public float jumpDX, jumpDY;
        public int prog;
        public int progAt;        // идёт кадр progAt − 1; 0 — программы нет

        public int smashDir;      // 0 — удара по стеклу нет
        public bool smashRun;
        public int smashFrames;
        public int smashTries;

        public int threatFrames;
        public int dodgeFrames;   // 0 — уклонения нет
        public int dodgeDir;
        public int dodgeLeft;
        public int dodgeRest;
    }
}
