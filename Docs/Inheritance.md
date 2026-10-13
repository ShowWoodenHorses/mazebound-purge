# Наследие Maze — что берём в новый проект

Статус: черновик (2026-10-13), составлен по коду Maze. ТЗ новой игры — [TZ.md](TZ.md).

Пометки:
- ✅ **берём** — как есть или с мелкими правками;
- 🔧 **адаптируем** — основа остаётся, заметные доработки;
- ♻️ **переписываем** — идея и часть кода остаются, реализация новая;
- ❌ **убираем**;
- ❓ **под вопросом** — решится по стресс-тесту или позже.

## Заложить с первого дня (дорого добавлять потом)
1. **Номер этажа** во всех системах, которые работают с клетками (сетка, занятость, поле пути, туман, свет, зоны, сейв).
2. **Стабильные id контента** (не имя файла) — покупки и прокачка в сейве.
3. **Модули** оружия/метательного/способностей/карточек/состояний — общая библиотека, контент только данными.
4. **Свойства клеток флагами**: движение / пули / обзор / замедление / горючесть (+ поверхности). Вместо одного `CellType`.
5. **Сейв забега** — каждое новое состояние забега сразу попадает в сейв.
6. **8 направлений** для движения монстров и атак.
7. **Плоские массивы** для массы монстров (позиции, состояния, HP) — без MonoBehaviour на рядового монстра.

## Правила проекта (из CLAUDE.md Maze)
- ✅ Без физики (Grid + Occupancy + `ISpatialQueryService`), без NavMesh, URP/HDRP, ECS/DOTS (Burst в WebGL не даёт выигрыша), без синглтонов и `Find*`.
- ✅ Слои `Core ← Gameplay ← Application ← Presentation`, Editor отдельно; `Data → Runtime → View`; Animator только отображает.
- ✅ VContainer-скоупы, UniTask с `CancellationToken`, владельцы Addressables-handle, `LevelWarmup` (во время игры ничего не создаётся впервые), `ILevelTickable` / `ILevelLateTickable`.
- ✅ `DeterministicRandom` / `StableHash`, никакого рандома визуала в runtime.
- ✅ Локализация (никакого `ToUpper`, атласы шрифтов из таблицы), ловушки сборки (дубли бандлов, разные объекты из разных Addressable-корней), размер веб-загрузки, тесты PlayMode через итератор-обёртку.
- 🔧 Имя проекта, namespace `Maze.*`, ключ сейва, пакет Android, App ID площадок — новые (см. «Клонирование»).

## Core
| Что | Пометка | Комментарий |
|---|---|---|
| Common: `DeterministicRandom`, `GameLog`, `StableHash` | ✅ | |
| Localization (`LanguageCatalog`, `LocalizationTable`, проверка) | ✅ | |
| Audio: `AudioCatalog`, `SoundCue`, группы звуков оружия | 🔧 | сборка по папкам — расширить на монстров, зоны, метательное |
| Definitions: `PlayerDefinition` | 🔧 | статы + прокачка |
| Definitions: `WeaponDefinition` | ♻️ | модули, редкость, тип патронов, уровни и развилки, флаг «без прокачки» |
| Definitions: `ZombieDefinition` | ♻️ | → `MonsterDefinition`: ранг, восприятие, способности, сопротивления, размер, варианты под биом, чемпионы |
| Generation: `MazeGenerator` | ❌ | → генератор по рецептам биомов (шаги-модули) |
| Grid: `GridPosition`, `GridRect` | ✅ | |
| Grid: `Direction` | 🔧 | 8 направлений |
| Grid: `CellType`, `CellSurface` | ♻️ | флаги свойств + поверхности (лёд, песок, лава, сугроб…) |
| Grid: `LevelGrid`, `LevelGeometry` | 🔧 | этажи, высота рельефа, многоклеточные объекты |
| Level: `LevelData`, `LevelEntityData`, `LevelSettings` | 🔧 | спавны (разрушаемые/нет), арена, сундуки, бочки, колпак, этажи |
| Level: `LevelCatalog` | 🔧 | 5 локаций × сложности, обучающий забег |
| Level: `LevelGenerationSettings` | ♻️ | → рецепт биома |
| Level: `LightSourceData` | ✅ | |
| Level: `PatrolData` | ❌ | патрули не нужны (бродяги идут по маршруту волны) |
| Level: `TutorialScript` | 🔧 | + подсказки при первой встрече |
| Authoring: `LevelEditing`, `LevelAuthoring` | 🔧 | новые слои, этажи, многоклеточные объекты, блокировка областей от перегенерации |
| Lighting: `LightField`, `LightPlacer`, `LightPreset` | ✅🔧 | по этажам; свет от лавы и огня |
| Navigation: `GridBfs` | ✅ | основа flow field и распространения звука |
| Navigation: `GridPathfinder`, `IGridCost`, `IGridPassability` | 🔧 | 8 направлений; A* только для больших боссов/особых случаев |
| Navigation: `KeyDoorSolver` | ✅🔧 | проверка ключей дверей; + ключи сундуков |
| Validation | 🔧 | новые проверки (связность, арена, спавны, этажи, бюджет препятствий) |
| Visibility: `GridLineOfSight`, `GridRaycast` | ✅ | |
| Visibility: `FieldOfView` | 🔧 | 21×21, отдельный флаг «обзор», дым |
| Visual: `VisualTheme`, `VisualSet`, `VisualVariant`, `VisualSelector`, `VisualResolver`, `VisualAssigner`, `VisualData` | ✅🔧 | многоклеточные объекты, сдвиг/поворот внутри клетки, высота |
| Visual: `CellLayer`, `CellVisualContext` | 🔧 | категории стен (Corner/T…) нужны меньше: стены в основном у зданий |
| Visual: `ThemeLighting`, `ThemeWeather`, `LightFixtures`, `VisualColors`, `LevelVisualUsage`, `PlayerVisualDefinition` | ✅ | |
| Visual: `CombatFeedback`, `CombatVisualDefinition`, `WeaponVisualCatalog` | 🔧 | новые эффекты; каталог — по папкам и редкости |
| Visual: `ThemeAwareness` (зоны зрения) | ❓ | на 100+ монстров не годится; может, только для офицеров/элиты |
| Visual: `ThemeFootprints` | ❓ | следы 100+ монстров — нагрузка; может, только игрок |
| Visual: `ThemeRoute` | ❌ | подсказки маршрута нет |

## Gameplay
| Что | Пометка | Комментарий |
|---|---|---|
| Level: `LevelRuntime`, `LevelLoadStage` | ✅ | |
| Level: `LevelProgress` | ♻️ | волны, статистика забега, очки, деньги забега |
| Level: `ExitSystem` | ❌ | |
| Level: `PlayerDeathRule` | 🔧 | воскрешение (навык, реклама) |
| Player: `PlayerSystem`, `PlayerFootsteps` | ✅ | |
| Player: `PlayerMovement` | 🔧 | скольжение по льду, принудительное движение (притягивание/отталкивание), высота |
| Player: `PlayerHealth` | 🔧 | состояния (горение, яд…) |
| Player: `PlayerInventory` | 🔧 | типы патронов, метательное стопкой, ключи |
| Player: `PlayerInteraction` | 🔧 | сундуки, оружие с земли со сравнением |
| Player: `IPlayerInput` | 🔧 | бросок, долгое нажатие на слот |
| Combat: `Aim` | ✅ | + автострельба |
| Combat: `BulletSystem` | 🔧 | снаряды монстров, пробивание, рикошет; пул больше |
| Combat: `PlayerCombat` | ♻️ | на модулях |
| Weapons: `WeaponSystem` | ♻️ | 2 слота + метательное, пистолет-заглушка, патроны по типам |
| Doors: `DoorSystem` | ✅🔧 | + прочность (выбивают), мёрзлые двери уже есть |
| Grid: `LevelPassability` | 🔧 | флаги |
| Grid: `OccupancyMap` | ♻️ | толпа: много монстров в клетке + расталкивание |
| Spatial: `SpatialQueryService` | ♻️ | сетка-хеш под 150 монстров и сотни снарядов |
| Navigation: `NavigationSystem` | ♻️ | flow field от игрока (по этажам), A* для боссов |
| Navigation: `RouteHintSystem` | ❌ | |
| Zombies: `ZombieSystem`, `ZombieController`, `ZombieRuntime` | ♻️ | → монстры на массивах, состояния из Maze (Idle/Alert/Chase/Attack) как основа восприятия |
| Sound: `SoundEventBus` | 🔧 | стены гасят звук (BFS по проходам), ранги радиуса |
| Pickups: `PickupSystem` | 🔧 | дроп по весам, исчезновение с волной (кроме ключей), автоподбор |
| Map: `MapSystem` (фрагменты) | ❌ | → состояние «исследовано» тумана |
| Visibility: `VisibilitySystem` | ✅🔧 | по этажам |
| Tutorial: `TutorialSystem` | ✅🔧 | |
| Новое | — | волны и бюджет, зоны на клетках, состояния, способности, карточки, опыт, сундуки, логова, колпак, очки |

## Application
| Что | Пометка | Комментарий |
|---|---|---|
| Assets: `AddressablesService` | ✅ | |
| Flow: `GameFlow` | 🔧 | забег, волны, карточки, «Продолжить», воскрешение |
| Flow: `ILevelSession`, `PauseController` | ✅ | |
| Levels: `LevelCatalogService` | 🔧 | |
| Platform: Yandex, CrazyGames, VK, GameDistribution, RuStore, Ads, Analytics, AppFocus, Account, `SlottedCloudSave`, `WebCalls` | ✅ | + покупки (Яндекс, RuStore, ВК через сервер), таблицы рекордов |
| Platform: `GameAnalytics` | 🔧 | новые события |
| Save: `SaveService` | ✅ | облако, склейка, слияние при входе |
| Save: `SaveData`, `ProgressService` | ♻️ | покупки, прокачка, дерево, сложности, деньги, сейв забега |
| Save: `TouchLayout` | 🔧 | новые элементы кластера |
| Services: Audio, Input, Localization, Settings, `SharedDefinitionsService`, `UiPointer` | ✅🔧 | новые действия ввода и настройки |

## Composition
| Что | Пометка | Комментарий |
|---|---|---|
| `ProjectLifetimeScope`, `LevelLifetimeScope`, `LevelSessionFactory`, `ApplicationEntryPoint` | ✅🔧 | регистрация новых систем |

## Presentation
| Что | Пометка | Комментарий |
|---|---|---|
| Геометрия: `GeometryBuilder`, `LevelGeometryView`, `VisibilityChunk`, `CellVisibilityMask`, `PrefabMeshParts`, шейдер `Maze/Geometry` | ✅🔧 | высота, многоклеточные объекты, этажи |
| `SnowdriftMask` | 🔧 | → общая маска поверхностей и зон (огонь, кислота, лава, лёд) |
| Туман: `FogMask`, `FogOfWarView` | 🔧 | 3 состояния, по этажам |
| Свет: `LevelLighting`, `LevelLightMap`, `TorchFlame`, `LightFixturesView`, `BlobShadows` | ✅ | |
| `HitFlash`, `CombatEffect`, `SwingArc`, `NoiseWavesView`, `CombatFeedbackView` | ✅ | |
| `CombatViewPresenter` | 🔧 | новые эффекты, огонь, молнии, взрывы |
| `TopDownCamera` | 🔧 | дальше (±7), переход между этажами |
| `EntityView`, `EntityViewRegistry`, `VisibilityController`, `LevelWarmup`, `VisualPrefabLibrary`, `LevelVisualSystem`, `UnityObjects` | ✅ | |
| Игрок: `PlayerViewPresenter`, `PlayerWeaponPresenter`, `CharacterWeaponRig`, `TwoBoneIK`, `WeaponModel`, `CharacterHead`, `PlayerAnimatorParameters`, `LocomotionAnimation` | ✅ | новые анимации: бросок |
| `ZombieViewPresenter`, `ZombieAnimatorParameters` | ♻️ | рядовые — запечённая анимация + GPU instancing; офицеры/элита/боссы — Animator |
| Двери: `DoorViewPresenter`, `DoorVisual`, `FrozenDoorsView`, `FrozenDoorIce` | ✅ | |
| `PickupViewPresenter` | 🔧 | много дропа, пул |
| Погода: `SnowfallView`, `BreathView` | ✅❓ | пар изо рта на 100 монстров — ограничить |
| `RouteLineView`, `RouteLineMesh` | ❌ | |
| `VisionZonesView`, `VisionZoneShape` | ❓ | см. `ThemeAwareness` |
| `FootprintsView`, `FootprintField` | ❓ | см. `ThemeFootprints` |
| UI: `UiShape`, `UiStyle`, `UiButton`, `UiSwitch`, `UiAppear`, `UiIconSet`, `UiSound`, шейдер `Maze/UIShape` | ✅ | + палитра редкостей |
| UI: тач (`TouchControls`, `TouchStick`, `TouchLayoutEditor`, `SafeAreaFitter`…) | ✅ | |
| UI: `TouchAttackCluster` | 🔧 | сектор метательного, долгое нажатие, запас патронов |
| UI: `UIRoot`, `UIScreen`, `ScreenRouter`, `LoadingScreen`, `ErrorScreen`, `RotateDeviceScreen`, `PauseScreen`, `SettingsScreen`/`SettingsPresenter`, `LanguagePicker`, `FpsCounter`, `TutorialScreen`/`TutorialPresenter` | ✅🔧 | |
| UI: `HudScreen`, `HudPresenter`, `HudWeaponSlot` | ♻️ | новый HUD |
| UI: `MainMenuScreen`, `LevelSelectScreen`, `LevelTile`, `LevelPages`, `ResultScreen` | ♻️ | нижняя панель вкладок, выбор режима, итог забега |
| UI: `ConfirmExitScreen` | ❌ | (диалог как шаблон — пригодится) |
| Карта: `MapRenderer`, `MapPresenter`, `MapIconSet`, `MapIconLayout` | 🔧 | мини-карта, масштаб, этажи, 3 состояния |
| Карта: `MapLayer` (слои за рекламу), `MapRouteGraphic` | ❌ | |
| Audio: `AppAudioPresenter`, `LevelAudioPresenter` | ✅🔧 | ограничение голосов для толпы |
| Localization: `LocalizationPresenter`, `LocalizedText`, `TextKeys` | ✅ | |
| Новое | — | арсенал и 3D-превью, дерево, магазин, подготовка, карточки, задания, ежедневные награды, рекорды, мини-карта |

## Editor
| Что | Пометка | Комментарий |
|---|---|---|
| Build: `MazeBuilder`, `WebPlatform`, `AndroidStore`, `AndroidSdkAssemblyFilter` | ✅ | переименование, новые App ID |
| Level Designer: окно, `GridCanvas`, панели, `LevelPlayLauncher`, `LevelSync`, превью, хэндлы | 🔧 | этажи, новые кисти, многоклеточные объекты |
| Level Designer: `GenerationPanel` | ♻️ | рецепты биомов |
| Dev: `AudioBuilder`, `LocalizationBuilder`, `UiStyleBuilder`, `UiIconsBuilder`, `DamageNumbersBuilder`, `ThirdPartyAssetCollector`, `CharacterMeshExtractor`, `PlayerAnimationsBuilder`, `CharacterAnimationUtility`, `LitMaterialsConverter`, `GeometryShaderEditorGuard`, `BiomeThemeBuilder`, `WeatherBuilder`, `FogAssetsBuilder`, `LightingAssetsBuilder`, `PlaceholderThemeBuilder`, `WeaponSoundGroupDrawer` | ✅ | |
| Dev: `WeaponsBuilder` | 🔧 | редкость, классы, шаблоны развилок |
| Dev: `ZombieAnimationsBuilder` | 🔧 | + запекание анимации в текстуру |
| Dev: `CombatEffectsBuilder`, `DecorBuilder`, `MapIconsBuilder`, `LevelPropsBuilder` (светильники ✅, выходы ❌) | 🔧 | |
| Dev: `RuntimeScenesBuilder` | ♻️ | новые экраны |
| Dev: `RouteLineBuilder` | ❌ | |
| Dev: `AwarenessVisualsBuilder`, `FootprintsBuilder` | ❓ | |
| Dev: `TempLevelBuilder` | ❓ | проверить, нужен ли |

## Ассеты и данные
- ✅ Шейдеры: `Maze/Geometry`, `Maze/Lit` (+ вариант с запечённой анимацией), `Maze/Fog`, `Maze/UIShape`, `Maze/Particle`, `Maze/SwingArc`, `Maze/Ring`, `Maze/Overhead`, `Maze/BlobShadow`, `MazeLighting.cginc`. Имена `Maze/*` → новый префикс.
- ✅ Арт: персонаж игрока, модели оружия (10), зомби, темы биомов (замок, лес, снег), эффекты, иконки UI, шрифты, звуки и музыка, WebGL-шаблон, jslib-мосты.
- ✅ Строки локализации — общие (меню, настройки, площадки); игровые — новые.
- ❌ Уровни Maze, каталоги уровней, сценарий обучения Maze.

## Документы
- ♻️ `CLAUDE.md` и `Docs/Progress.md` — пишутся заново по ТЗ (правила проекта переносятся).
- 🔧 `Docs/MazeMenu.md`, `Docs/LevelDesigner.md`, `Docs/Prefabs.md` — переделываются по ходу этапов.
- ✅ `Docs/PrivacyPolicy` — шаблон (новое название и контакты).

## Тесты
- ✅ Тесты оставшихся систем (Core, сейв, локализация, flow, площадки).
- ❌ Тесты лабиринта, выхода, фрагментов карты, подсказки маршрута.

## Клонирование (порядок)
- **Новый репозиторий** с одним начальным коммитом (история Maze остаётся в Maze).
- **Переименование** сразу при клонировании: namespace `Maze.*`, сборки, шейдеры `Maze/*`, меню **Maze**, ключ сейва, пакет Android, WebGL-шаблон.
- Общий код двух игр (площадки, сейв, UI) не выносится в пакет — исправления переносятся руками.
- Название — **Mazebound: Purge**, кодовое имя — **`Purge`**: `Maze.*` → `Purge.*`, `Maze/*` → `Purge/*`, меню **Purge**, ключ сейва `Purge.SaveData`, пакет `ru.bgames.mazebound.purge`.
