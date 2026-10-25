# Объекты уровня: бочки, сундуки, колпак (этап 7в)

Справочник по текущему коду. Читать, когда задача касается бочек, сундуков, ключа сундука, колпака; **обновлять при их изменении.** Решения — карточка 7в в [TZ.md](../TZ.md). Данные на уровне (`BarrelData`, `ChestData`, кисти) — [Level.md](Level.md).

## Данные (Core/Objects, `Data/Objects`, Build Level Objects)
- `BarrelDefinition` (на `BarrelKind`): `Health`, `Effects` (модули `EffectModule` — взрыв + зона), `Prefab`. `ChestDefinition` (на `ChestKind`): `OpenTime`, `Reward` (`DropTable`), `Champions`/`ChampionRadius`, `Prefab` (ребёнок `*Lid*` открывается), `GivesWeapon`. `RefugeDefinition` (один): `WaveTime`, `HealPerSecond`, `PushTime`, `Look`. `ChestKeyDefinition` — предмет (вид — `ItemVisualCatalog`, Build Items).
- Все — `ContentDefinition`, находятся через `ContentCatalog.GetAll<T>` по виду (нет ассета вида — предупреждение, объект без эффекта/награды).

## Геймплей (Gameplay/Objects, шаг `InitializeSystems`, колпак — `SpawnZombies`)
- **`CellBlockers`** (Gameplay/Grid): счётчик на клетку; `LevelPassability.IsWalkable` его учитывает (игрок и орда); орда на `Changed` пересобирает проходимость и поле пути.
- **`BarrelSystem`**: `Barrel : IDamageable` в `SpatialQueryService` (пули, ближний, струя, цепь, взрывы игрока); `IsAimTarget = false` (автонаведение мимо), `HurtByMonsters = false` (`CombatTargets.Blast(..., hostile: true)` их пропускает). Зона игрока в клетке бочки — урон раз в 0,25 с.
  - 0 HP → через `ChainDelay` 0,12 с `EffectSystem.Apply(эффекты, hostile: false)` (игрока не ранит), бочка снята, клетка свободна, `Exploded`, `BrokenIds`. Цепь — взрыв ломает соседние, у каждой своя задержка (без рекурсии в `Blast`).
- **`ChestSystem`**: `Chest` (`State` Closed/Opening/Challenge/Open, `Progress`), `FindUsable` (Чебышёв ≤ 1, по `Facing`), `TryUse` → `Opening` / `NeedsKey`; тик — отошёл дальше 1 клетки → `OpeningStopped`.
  - Награда — `DropRoller.Roll(..., never: true)` (обычный) или `DropRoller.Weapon` (запертый, испытание; все оружия пула в слотах — любое) с `DropSystem.Context()` (× сложность); кладётся в свободную соседнюю клетку ближе к игроку, но не в клетку игрока (там подобралась бы сразу и незаметно — 7г; только если других нет), `PickupSystem.Drop*(keep: true)` — не истекает и не чистится волной. Медкиты таблиц сундуков — `DropPool.Include(table)`, ключ — `DropPool.Include(item)` (виды грузятся).
  - Ключ: `HordeSystem.Killed` первой элиты (на уровне с запертым) → `PickupSystem.DropChestKey` (`PickupKind.ChestKey`, подбор при входе → `PlayerInventory.ChestKeys`, золотой значок в HUD); `KeyDropped`.
  - Испытание: `HordeSystem.SpawnChampion` (охотник, аффикс случайный, вне счёта волны: `OutsideWaveAlive`, `WaveAliveCount` — по нему `WaveSystem` чистит волну и ограничивает появление); виды — рядовые/офицеры текущей волны (пауза — следующей), иначе орды; все мертвы → оружие, `ChallengeDone`.
  - События: `OpeningStarted/Stopped`, `Opened` (и старт испытания), `ChallengeStarted`, `ChallengeDone`, `KeyDroppedAt`. `PlayerInteraction`: оружие → метательное → **сундук** → дверь (`InteractionTarget.Chest`, `InteractionResult.ChestOpening/ChestLocked`).
- **`RefugeSystem`** (`LevelSettings.Refuge`): центр — клетка игрока при появлении, `Edge` = радиус + 0,5; `IShotBarrier` для `SpatialQueryService.Barrier` — `QuerySegment` (пули, снаряды монстров) и `IsClear` с `BlocksBullets` (взрывы, ближний, зоны `PlaceArea`, удары монстров) не пересекают край в обе стороны, обзор — пересекает.
  - `HordeSystem.SetRefuge/ClearRefuge`: клетки, задетые кругом, закрыты для стояния и появления (`CanStand`); поле пути их не исключает — толпа у края; монстры внутри при включении — наружу; обычный удар по игроку под колпаком не проходит. `CombatTargets.IsShielded` — удар по площади и навесные снаряды под колпаком ничего не делают. `ZoneSystem.ClearInside` при включении, огонь не перетекает через край.
  - Лечение `HealPerSecond` × Max HP. В `Running` время идёт только пока игрок внутри; 0 → `PlayerSystem.Knock` наружу, через `PushTime` гаснет (`Lowered`); вне `Running` горит, время полное.
- Сейв забега: `RunSnapshot.BrokenBarrels/OpenedChests/ChestKeyDropped` ↔ `RunSave` (`RunCheckpoints.Take`); продолжение — системы читают `LevelLaunchOptions.Resume` (разбитых нет, открытые открыты, ключ — игроку, если запертый не открыт). Незаконченное испытание не сохраняется.
- Аналитика (`LevelAnalytics`): `chest_open` (kind, wave), `chest_key` (wave), `challenge_done` (wave).

## Вид и звук
- `LevelObjectsPresenter` (`BuildVisuals`): модели видов → `EntityView` (`VisualKind.Barrel/Chest`, видимость как у объектов), подгон по ширине (бочка 0,6, сундук 0,85 клетки), поворот по `StableHash(id)`; взорванная — убрана, крышка — `LidAngle` за 0,35 с (при открытии чуть приоткрывается).
- `RefugeView`: `FX_Refuge_Dome` (Polygon Arsenal `ShieldBlue`, сфера радиуса 1 при масштабе 1, `scalingMode = Hierarchy`), масштаб = `Edge`, мигает последние 3 с в волне.
  - С 7г горит ровно: главная сфера — одна частица с вечной жизнью, без изменения альфы по времени (было 4 вспышки/с); материал `PolyShieldBlue` — альфа-смешивание, голубая заливка (альфа 0,22) + яркая кромка.
- `LevelObjectsHudPresenter`: строка `hud.refuge` под временем (`HudScreen.SetRefuge`, красная < 3 с), полоска `HudWorldBar` над открываемым сундуком, сообщения `hud.chest.*`, `hud.refuge.over`.
- `LevelObjectsAudioPresenter`: `ChestOpen` (`Other/chest_open*`), `ChestLocked` (`Other/chest_locked*`), петля `DomeLoop` (`Other/dome_loop`) у колпака; взрыв бочки — звук взрыва эффектов, ключ — `PickupKey`.
- Тесты — `LevelObjectsTests`, `BiomeGeneratorTests.Objects_*`.
