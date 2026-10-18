# Зомби Maze (`ZombieSystem`)

Справочник по текущему коду (перенесён из CLAUDE.md 2026-10-17). Читать, когда задача касается этих систем; **обновлять при их изменении.** Общие правила — [CLAUDE.md](../../CLAUDE.md).

Монстры разбиты на справочники: [Zombies.md](Zombies.md) — зомби Maze · [Monsters.md](Monsters.md) — данные, вид рядовых, восприятие, сложность · [Abilities.md](Abilities.md) — способности, офицеры, Громила, двери, масса · [Elite.md](Elite.md) — элита, чемпионы, эффекты монстров · [Horde.md](Horde.md) — орда, песочница, стресс-тест.

## Зомби
- `ZombieSystem` (шаг `SpawnZombies`, тик после пуль): `ZombieRuntime` (`IDamageable`, в `SpatialQueryService` и `OccupancyMap`) + `ZombieController` на каждого. Убитый: убирается из occupancy и spatial, `KilledCount++`, событие `Died` (вьюха удаляется).
- `ZombieController`: обнаружение раз в 0.1 с (`ZombieDetection`: VisionOnly — `VisionRange`/`VisionAngle` от `Facing` + `IsClear`; HearingOnly — `Hear(SoundEvent)`, дистанция ≤ min(радиус звука, `HearingRadius`); VisionAndHearing — видит во все стороны в `DetectionRadius`, стены и закрытые двери загораживают (`IsClear`), и слышит звуки по `HearingRadius`, как HearingOnly).
  - Цель — последняя замеченная клетка; Chase к ней, дошёл без нового обнаружения → Return (на спавн → Idle). Патрулей нет (убраны 2026-10-14). Игрок в соседней по стороне клетке → Attack (первый удар через пол-интервала). Урон не вызывает реакции (§76).
- Движение по `NavigationSystem.TryFindPath` (A* только при смене цели или `Version` дверей); в клетку игрока не входят (`CanZombieEnter`), друг через друга — да; закрытая перед носом дверь → новый поиск.
- Скорость: Return — `MoveSpeed` (walk), Chase — `ChaseSpeed` (run).
- **Alert** (сверх §74): заметив игрока из Idle/Return — стоит и рычит `ZombieController.AlertDuration` (0.5 с), потом Chase; игрок рядом — сразу Attack; после атаки погоня возобновляется без рыка.
- Вид: `ZombieViewPresenter` (шаг `SpawnZombies` после системы) — `EntityViewRegistry.Add/Move/Remove`, параметры `ZombieAnimatorParameters` (наличие кэшируется — `animator.parameters` аллоцирует); убитый остаётся `CorpseTime` (анимация смерти), потом удаляется.
  - Префабы `Art/Zombie/*` + общий контроллер собирает **Purge → Dev → Build Zombie Animations** (он же заполняет Zombie Set темы — вариант на префаб, тип в `Definition` варианта сохраняется, новому ставится автоматически — и переназначает визуал зомби в уровнях; модель игрока для билдеров — единственный префаб в `Art/Player`).
  - Темп шага клипов — `LocomotionAnimation.Playback` (≤1.4×). Билдеры персонажей пересобирают контроллер на месте — не удалять ассет (ломает ссылки префабов).
- Смерть игрока: `PlayerDeathRule` (резолвится в `LevelSessionFactory`) → `LevelRuntime.Finish(Failed)`; `LevelRuntime.Tick` прерывает кадр, когда уровень завершён.
- **Победа (временно, до волн этапа 6):** `AllKilledRule` (резолвится там же) — последний убитый зомби → `LevelRuntime.Finish(Completed)`; уровень без зомби так не завершается (решение пользователя 2026-10-14).
