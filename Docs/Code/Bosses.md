# Боссы

Справочник по текущему коду (этап 8). Читать, когда задача касается боссов, босс-волн, арены; **обновлять при их изменении.** Общие правила — [CLAUDE.md](../../CLAUDE.md). Смежное: [Monsters.md](Monsters.md), [Abilities.md](Abilities.md), [Horde.md](Horde.md).

## Каркас босса (8а)
- **Босс — ранг `Boss` в массивах орды**, вид — Animator, как у офицеров (`MonsterViewPresenter`, пул `BossPoolPerKind` 2 на вид). `MonsterDefinition.IsBoss`, `HordeKind.IsBoss`.
  - Масса по рангу — неподвижен (∞): ни игрок, ни толпа, ни отбрасывание его не двигают. Двери по рангу — `Break`.
  - **Не останавливается от попаданий** (`MonsterDefinition.Staggers` = false → `HordeSystem.Stagger` игнорирует; решение пользователя 2026-10-26). Клип реакции — не чаще `BossHitRestart` 2,5 с.
  - **Контроль ×0,25** — `MonsterStats.ControlFactor` (0 = по рангу: боссы `BossControlFactor` 0,25, остальные 1); `MonsterDefinition.DurationFactor(status)` умножает только оглушающие состояния (`StatusDefinition.Stuns`), сопротивления — сверху.
- **Тело N×N** — `MonsterStats.Radius` (до 1,5): 1 — 2×2, 1,5 — 3×3. `HordeKind.BodyClearance` = сколько клеток вокруг центра покрывает след (`radius·0,9`); у боссов — 1.
  - `HordeSystem.Bodies.cs` (partial): **своё поле пути** `BodyFlow` — проходимость (с дверями, которые выбивает) сжата на 1 клетку (`Shrink`), строится вместе с остальными полями и только пока живёт широкое тело; `BodyNext` — у стены, на клетке вне поля, шаг к соседу ближе к игроку. Нужен проход ≥ 3 клеток (валидатор уже требует путь тела 3×3 до арены).
  - `FootprintFree` для тел шире клетки проверяет точки по рёбрам и внутри (раз на клетку). Толпа держится от широких тел явно (`WideSeparation`, список живых `_wide` ≤ `MaxWideBodies` 8 — корзины смотрят только на клетку вокруг). `AnyBodyInCell` учитывает широкие тела; `QueryCone` смотрит дальше на `MaxRadius`.
  - Прямая погоня — с `DirectChaseDistance + radius`.
- **`HordeSystem.SpawnBoss(центр, вид)`** — вылезает (`EmergeTime`) на ближайшей клетке, где помещается всё тело (кольца до 8 клеток), сразу охотник. **`SetHeld/IsHeld`** — в погоне стоит на месте лицом к игроку, бьёт подошедшего, кастует.
- **Босс-волна** — `WaveDefinition.Boss` (`WaveBoss`: монстр + `Escort` — группы `WaveGroup`, время от старта волны; пустой монстр = обычная волна; `IsBossWave`). `HordeDefinition.AllMonsters` включает босса и свиту.
  - `WaveSystem`: на старте волны босс вылезает в центре арены этажа игрока (арены нет — далёкая точка); не нашлось места — пробует каждый тик. Свита — `WaveGenerator.Escort` (число × `Difficulty.Count`, не меньше 1), пачка на группу из одной из двух точек, ближайших к арене (`PickNearArena`; заданная точка группы — она). Бюджет и обычные группы ждут: их часы `_budgetTime` идут только после смерти босса (`BossHolds`). Волна кончается, когда всё появилось и живых нет (босс тоже). `PendingCount` считает босса и свиту.
  - Решётки закрыты — точки снаружи недостижимы: свита после закрытия вылезает на случайной достижимой клетке арены (`FallbackCenter`).
- **`BossSystem`** (Gameplay/Horde, шаг `SpawnZombies`, тик после волн): текущий босс (`Begin(slot)` — из волны или песочницы, `Summon(kind, near)`), `HealthShare`, `Thresholds` (пороги `PhaseAbility`/`RageAbility` — засечки полосы), `Phase`, события `Appeared`, `Defeated`, `GatesChanged`, `PhaseChanged`, `Kills`.
  - **Арена** — `LevelData.Arena`, проёмы — `ArenaLayout.FindEntries` при загрузке. **Закрытие** — dev-поле `LevelData.CloseArena` (по умолчанию вкл., до выбора в 11б): пока игрок не на открытых клетках арены — босс ждёт (`SetHeld`), `ShowArenaArrow`; вошёл — босс отпущен, на все клетки проёмов `CellBlockers.Add` (движение стоит, пули и обзор проходят, орда пересобирает поля) до смерти босса. Закрытие выкл. — босс сразу гонится.
  - **Смерть**: `LevelClock.SlowDown(0,3, 0,5 с)`, решётки поднимаются, `Defeated`. Дроп — `Data/Drops/BossDrop` (оружие всегда, редкость на ступень выше элиты: 0/100/45/18/10), деньги/опыт/очки — по рангу.
- **`LevelClock`** (Gameplay/Level): темп симуляции — `LevelRuntime.Tick` получает `dt × Scale`; замедление тает в реальных секундах. `Time.timeScale` не трогается; вьюхи Animator монстров получают `Animator.speed = Scale`, рядовые — по времени орды. Карточки повышения уровня ждут конца замедления (`CardsPresenter`).
- **Модуль «Фаза по HP»** — [Abilities.md](Abilities.md), «Фазы (8а)».

## Вид, HUD, звук (8а)
- **Полоса HP** — `BossBarView` (HUD, место отсчёта паузы, `RuntimeScenesBuilder.BuildBossBar`): имя `monster.<id>` (`TextKeys.MonsterPrefix`; нет строки — id), заливка со следом, засечки фаз. `BossHudPresenter` (шаг `InitializeUI`): показ на `Appeared`, HP каждый кадр, на `Defeated` — белая вспышка экрана `HudScreen.Flash` и баннер `hud.boss.defeated`.
- **Стрелки** — `MonsterArrowsPresenter` + `MonsterArrowsView` (`_boss` — крупная красная, `_arena` — акцентная, под ней): стрелка босса за экраном **всегда**, выключатель «Стрелки на монстров» на неё не действует (решение пользователя 2026-10-26); стрелка к арене — пока босс ждёт в ней. Босс в секторах обычных стрелок не участвует.
- **Карта и мини-карта** — точка босса всегда (вне обзора и при выключенном слое «Монстры»), ×1,8 крупной точки (`MapPresenter.AddMonsterIcons`).
- **Решётки** — `ArenaGatesView` (шаг `SpawnZombies`): секции `VisualTheme.ArenaGate` (у Леса — `Art/Items/Purge/Gate/Gate_02`, длина `ArenaGateLength` 2,5 м, растягиваются по проёму) посреди глубины каждого проёма; поднимаются из-под земли за 0,35 с при `GatesClosed`, опускаются и прячутся.
- **Звук** — `HordeAudioPresenter`: `Monster/boss_stinger` + `boss_roar` при появлении (без затухания — босс слышен везде), `boss_hurt` от попадания не чаще 3 с, `boss_death`, `arena_gate` на закрытие и открытие; рык фазы — `CastSound` модуля. Музыки босса нет (не найдена) — играет `game`.
- **Аналитика** — `boss_start` (level, boss, wave), `boss_kill` (level, boss, wave, time — секунды боя) в `LevelAnalytics`.
- **Сейв забега** — нового поля нет: забег сохраняется на паузах между волнами, босс-волна при продолжении начинается заново.

## Контент
- **Build Monsters** собирает боссов из `Art/Monster/<Биом>/Boss` (`OfficersBuilder.Boss.cs`; Горгулья и Элементаль — этапы 13, 14): контроллер `Art/Monster/Boss.controller` на клипах UAC Creature (ожидание/ходьба/бег/реакции — `Brute_*`, атаки `Boss_Attack_1–3`, выход — `Boss_Entrance` (Roar4) через состояние Rise за `EmergeTime`, рык фазы — `Brute_Rage`, смерть `Boss_Death` 4,5 с, оглушение — офицерское), рост головы 2,2 м (2×2) / 2,7 м (3×3).
- Черновые данные (новый ассет; дальше правятся в нём): HP — Мутант `mutant_forest` 6000 (3×3, 1,6 кл/с, фаза-ярость с 50 % ×1,5), Мясник `zombie_boss_common` 3000 (2×2, 1,8, ярость с 30 % ×1,5), Колосс `golem_common` 4000 (3×3, 1,4), Горгона `medusa_common` 3000, Демон `demon_common` 3500 (2×2, 2,0); `EmergeTime` 2,5 с, труп 8 с, удар — досягаемость радиус + 1. Способности — 8б–8г.
- **Стенды**: песочница — строка **Boss** оверлея (`StressController.SpawnOfficer` → `BossSystem.Summon`, 9 клеток от игрока); `Level_Forest_01` — сценарий `Forest_01`: W3 — Мясник + свита (10 лесных зомби сразу, 10 Ходоков на 8 с), W5 — Мутант + свита (14 лесных, 2 плевуна на 10 с, Громила на 20 с).
- Тесты — `Tests/EditMode/Monsters/BossTests.cs`.
