# Машины-тараны (этап 9)

Справочник по текущему коду. Читать, когда задача касается машин; **обновлять при их изменении.** Решения — карточка этапа 9 в [TZ.md](../TZ.md). Сделано: 9а (езда); таран и поломка — 9б, машины на уровне (генератор, кисть, сейв, аналитика) — 9в.

## Данные (Core/Vehicles, `Data/Vehicles`, Build Vehicles)
- `VehicleDefinition` (`ContentDefinition`) — список модулей `VehicleModule`: **Engine** (`MaxSpeed`, `Acceleration`, `Braking`, `Rolling` — накат без педалей, `ReverseSpeed`, `Power` — для 9б), **Steering** (`TurnRadius`), **Hull** (`Health`, `Length`/`Width` — прямоугольник корпуса, `WallSafeSpeed`, `WallDamage`), **Ram** (`Mass`, 9б), **Look** (`Prefab`, `CameraZoom`, звуки двигателя `Idle/Drive/Start/Rev` — `[VehicleSound]`, выпадающий список папок `Sounds/Vehicle`).
  - Engine/Steering/Hull обязательны (`Validate`); без модуля — значения по умолчанию.
- На уровне — `VehicleData` (`LevelData.Vehicles`, id `vehicle_N`): клетка центра корпуса, `Definition` (прямая ссылка), `Heading` — градусы по часовой от севера. `LevelEditing.AddVehicle`. В `AllEntities` есть; общая фабрика видов (`LevelVisualSystem`) их пропускает.
- **Build Vehicles** (`VehiclesBuilder`): префаб `Art/Vehicles/<имя>` → `Data/Vehicles/<имя>.asset`; новый — черновики по размеру модели (длиннее 5,5 м — тяжёлая), звуки по последнему слову имени. Модели: `Vehicle_Light` (`SM_Veh_Car_Medium_01`), `Vehicle_Heavy` (`SM_Veh_Truck_01` + `_Tanker`) из PolygonMilitary, без коллайдеров, центр корпуса в начале координат.

## Геймплей (Gameplay/Vehicles)
- `VehicleBody` — геометрия без физики: курс в радианах по часовой от севера, перед `(sin h, cos h)`; пересечение прямоугольника с клеткой (SAT), клетки под корпусом (`Cells`, с отступом), расстояние до точки, отрезок (`Segment`, для пуль).
- `VehicleSystem` (шаг `InitializeSystems`, тик **сразу после `PlayerSystem`**): `Vehicle` — `Position`, `Heading`, `Speed` (со знаком), `Steer`, `Throttle`, `Health`, `State` (Parked/Driven), `InContact`, `IsScraping`.
  - **Стоящая** — `CellBlockers` на клетки корпуса (с отступом 0,2: угол, задевший клетку, её не закрывает), `IShotBlocker` в `SpatialQueryService` (пули, снаряды, `IsClear` по `BlocksBullets`; обзор проходит), монстров из закрытых клеток выталкивает `HordeSystem.PushOutOfBlocked`.
  - `FindEnterable` — стоящая в пределах `EnterReach` (1 клетка) от игрока; `Enter` — снимает блоки, `PlayerSystem.EnterVehicle`, `WeaponSystem.InputLocked`; `TryExit` — паркует, ищет место игроку: левый бок (3 точки), правый, зад, перед, с шагом отступа, пока ноги не выйдут из закрытых клеток; нет места — остаётся внутри.
  - **Езда**: газ `PlayerAction.Gas` (W, ↑, RT, кнопка Газ), тормоз/назад `Brake` (S, ↓, LT, кнопка Тормоз), руль — `Move.x` (мёртвая зона 0,15). Поворот только в движении: радиус `TurnRadius` + 0,25 × |скорость|.
  - Движение подшагами ≤ 0,2: стена при угле курса к ней < 45° — скольжение вдоль (`IsScraping`), иначе остановка вплотную (деление шага пополам); урон корпусу при новом касании = (нормальная скорость − `WallSafeSpeed`) × `WallDamage`, событие `WallHit`. Поворот, упёршийся в стену, не делается.
  - HP 0 пока ничего не делает (поломка — 9б); монстры проходят сквозь едущую машину (таран — 9б).
- `PlayerSystem` в машине: `InVehicle`, `Ride` (позиция и взгляд машины; **клетка игрока обновляется не чаще `RideCellInterval` 0,25 с** — поле пути орды, видимость, подбор), `ExitVehicle`; толчки/`Knock`/`Displace` не действуют. `PlayerCombat`, `PlayerThrowing` в машине не атакуют и не бросают.
- `PlayerInteraction`: в машине Use — только выйти (`VehicleExited` / `VehicleExitBlocked`); пешком порядок: оружие → метательное → сундук → **машина** → дверь (`InteractionTarget.Vehicle`).

## Вид, звук, HUD
- `VehicleViewPresenter` (`BuildVisuals`, `IViewWarmup`): модель Look на машину, `EntityView` (`VisualKind.Vehicle`, видимость по клетке центра), каждый late tick — позиция, курс, колёса (дети `*Wheel*`: катятся по пройденному, `*Wheel_f*` поворачиваются до 30°, вращение в системе машины). Камера: `TopDownCamera.ExtraHalfExtent` = `CameraZoom` × доля скорости, сглажено 0,6 с.
- `PlayerViewPresenter`: в машине модель игрока выключена, камера следует.
- `VehicleAudioPresenter`: дверь и запуск при посадке, дверь при выходе; холостой ↔ ход (петли, перекрёстно до 25 % скорости, тон хода 0,75–1,35 — `AudioService.SetVoice(…, pitch)`), рёв при газе с места, `wall_hit` по силе удара, `scrape_loop` при скольжении. Общие звуки — папки `Sounds/Vehicle/door|wall_hit|scrape_loop`; для 9б лежат `monster_hit`, `bounce`, `stall`, `smoke_loop`.
- Тач: в машине `TouchAttackCluster.SetDriving` прячет контейнер `Combat` (атака, стик прицела, слоты, бросок) и показывает `Drive` — Газ (вся четверть, `<Gamepad>/rightTrigger`) и Тормоз (сектор на дуге, `leftTrigger`); Use со значком машины (`UseTarget.Vehicle`). Нет места выйти — `hud.vehicle.blocked`.
- Стенд: `Level_Sandbox` — все машины `Data/Vehicles` к юго-востоку от старта (Build Stress Test Levels). Тесты — `VehicleTests`.
