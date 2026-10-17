# Mazebound: Purge — 3D Top-Down Horde Shooter

ТЗ (черновик, итог обсуждения 2026-10-13): [Docs/TZ.md](Docs/TZ.md). Что из Maze берём, адаптируем, убираем: [Docs/Inheritance.md](Docs/Inheritance.md). Опыт Maze — правила и ловушки: [Docs/Lessons.md](Docs/Lessons.md). **Целиком не читать** — только раздел по задаче: работа с Unity/MCP — раздел 2; ловушки кода и Unity — 3; сборка, Addressables, веб, площадки — 4 (раздел 1 уже перенесён в этот файл).

Что ещё найти из ассетов (модели, анимации, звуки, по этапам): [Docs/AssetsToFind.md](Docs/AssetsToFind.md) — отмечать найденное.

**Прогресс, план этапов, принятые решения — [Docs/Progress.md](Docs/Progress.md). Перед началом работы читать только разделы 1, 2, 4, 5 (коротко, план, закреплённое, открытые пункты): найти номера строк заголовков `## ` через Grep и читать эти диапазоны.** Раздел 3 (решения) — искать по теме задачи (Grep), не целиком; раздел 6 (история) — только по необходимости. **Не нашлось решения по теме задачи (в Progress.md, ТЗ, справочниках) — спросить пользователя, как делать, прежде чем приступать к реализации; не придумывать решение самому.** После завершения этапа обновить статус и добавить запись в начало истории (для формата достаточно прочитать последнюю запись).

**Происхождение:** 2026-10-13 проект создан копией Mazebound (Maze, `../Maze`) и переименован `Maze` → `Purge`. Код пока — код Maze без систем лабиринта (убраны 2026-10-14, этап 1), ядро боя переписывается. Устройство текущего кода — справочники **[Docs/Code/](Docs/Code/)** (карта ниже) — править их по мере переделки систем. Архив Maze (ТЗ, прогресс, CLAUDE.md) — `Docs/Maze/`, только для справки.

**Название — Mazebound: Purge** (`productName`; одно на всех языках, не переводится). **Кодовое имя — `Purge`**: namespace `Purge.*`, сборки, шейдеры `Purge/*`, меню **Purge**, ключ сейва `Purge.SaveData`, пакет `ru.bgames.mazebound.purge`. Кодовое имя от названия не зависит — при смене названия его не трогать.
**Суть:** top-down шутер на истребление — волны монстров (100+ одновременно), большой арсенал (огнестрел, ближнее, массовое, метательное), патроны по типам, дроп по весам и редкости, карточки улучшений в бою, магазин и прокачка оружия/персонажа за деньги между забегами. Режимы: 5 уровней-локаций (4 сложности, финальный босс) и бесконечное выживание с рекордами.
**Платформы:** WebGL 2 (desktop и мобильные браузеры), Android. 30–60 FPS на слабом телефоне в браузере. Первые площадки — **CrazyGames и RuStore**, затем Яндекс Игры, ВКонтакте, GameDistribution. Одна ветка `main`, площадка выбирается при сборке (define + WebGL-шаблон + адаптеры в `ProjectLifetimeScope`).

## Стек (установлено)
Unity 6000.3.8f1 (6.3 LTS) · Built-in RP · VContainer 1.19 · UniTask 2.5 · Addressables 4.1 · Input System 1.18 (только новый) · uGUI · Animator · Test Framework (EditMode/PlayMode) · MCP for Unity.
Встроенное: `Mesh.CombineMeshes`, Static Batching, `UnityEngine.Pool`, `JsonUtility` (SaveData).

## ЗАПРЕЩЕНО
- URP/HDRP, NavMesh, ECS/DOTS, LOD.
- **Любая физика:** Rigidbody, Collider, `Physics.Raycast/Overlap*`, `OnCollision*`, `OnTrigger*`. Вместо неё Grid + Occupancy + `ISpatialQueryService`.
- Singleton с mutable state, `*.Instance`, `FindObjectOfType`/`FindAnyObjectByType`, `GameObject.Find`, `Resources.FindObjectsOfTypeAll`.
- Запуск gameplay из `Awake/Start/OnEnable`. Lifecycle ведут LifetimeScope и `GameFlow`.
- Per-frame A*, per-frame Visibility, тяжёлый AI в каждом кадре, лишние аллокации, `Debug.Log` в горячих циклах.
- **Random в runtime для выбора visual prefab.** Runtime берёт только сохранённый `VariantId`.
- God Object. Prefab, определяющий gameplay-семантику (проходимость, LOS и т.п.).

## Архитектурные правила
- Слои: `Core ← Gameplay ← Application ← Presentation`. Editor зависит от Runtime, Runtime никогда не зависит от Editor.
- Gameplay не знает о Presentation. View (MonoBehaviour) — только Transform/Animator/Renderer/UI/VFX. Схема: `Data → Runtime → View`.
- Animator только отображает состояние. Состоянием управляет gameplay, не наоборот.
- `LevelData` (ScriptableObject, Addressable) — source of truth. Runtime state хранится отдельно.
- Дорогие операции запускаются событиями: Visibility — по `PlayerCellChanged`; A* — при смене target, двери, AI task или после invalidation.
- Все async-операции — UniTask с `CancellationToken`. Уровневые операции привязаны к lifetime `LevelLifetimeScope`.
- У каждого Addressables handle есть owner. При Dispose LevelScope level-specific ассеты освобождаются. Потерянных handle быть не должно.
- Navigation, Visibility и Detection — три независимые системы.
- Невидимые объекты только скрываются (View = Hidden), AI и симуляция продолжают работать.
- При критической ошибке: отменить async, освободить Addressables, уничтожить LevelScope, показать error state.
- **Монстр-рядовой — не MonoBehaviour.** Масса монстров живёт в плоских массивах (позиции, состояния, HP); вьюхи рядовых — запечённая анимация + GPU instancing.

## Заложить с первого дня (дорого добавлять потом)
1. **Номер этажа** во всех системах, работающих с клетками (сетка, занятость, поле пути, туман, свет, зоны, сейв).
2. **Стабильные id контента** (генерируются один раз, не имя файла) — по ним в сейве покупки и прокачка.
3. **Контент — это данные.** Оружие, метательное, способности монстров, карточки, состояния, зоны собираются из **модулей** в инспекторе; код пишется один раз на модуль (новую механику). Новый предмет = ассет + статы + внесение в дроп/магазин, **без правки кода и без ручного ввода id** (билдеры — по папкам, ссылки на контент — выпадающие списки).
4. **Свойства клеток флагами**: движение / пули / обзор / замедление / горючесть + поверхности (вместо одного `CellType`).
5. **Сейв забега**: каждое новое состояние забега сразу попадает в сейв.
6. **8 направлений** для движения монстров и атак.
7. **Плоские массивы** для массы монстров, снарядов и зон; всё «по клеткам» — маски-текстуры W×H, а не объект на клетку.
8. **Размер веб-загрузки**: до первого `gameplayStart` ≤ 20 МБ (CrazyGames) — локации, монстры и эффекты грузятся по мере надобности.

## Ключевые игровые правила (подробно — Docs/TZ.md)
- Сетка 1×1 м, клетка `(x, y)` ↔ мир `(x, 0, y)`, North = +Z. Свободные локации по биомам (Лес, Замок, Пустыня, Снег, Вулкан), рельеф (высота для вида), этажи (не взаимодействуют между собой).
- Слоты: 2 оружия (огнестрел/ближнее в любом сочетании; пустой слот — базовый пистолет) + метательное стопкой. Патроны по типам (9 мм, 5,45, 7,62, дробь, баллоны, батареи, заряды), запас не ограничен.
- Монстры: ранги рядовые / офицеры / элита / боссы, восприятие бродяга / зовущий / охотник, шум гасится стенами, способности с предупреждением. Зоны на клетках и состояния (горение, кислота, заморозка…), комбинации стихий.
- Обзор ~21×21, туман: не исследовано / исследовано / в обзоре.
- Одна валюта; донат только ускоряет; реклама — никогда внутри забега (кроме rewarded по кнопке).


## Карта кода — справочники Docs/Code
**Читать только справочник систем, которых касается задача** (не все подряд). Изменил систему — обнови её справочник в той же задаче. Новая крупная система — новый файл здесь и строка в этой таблице.

| Справочник | Что внутри |
|---|---|
| [Data.md](Docs/Code/Data.md) | Этажи в `GridPosition`, `CellFlags` и препятствия, стабильные id и `ContentCatalog`, модули контента и инспектор, сейв v1 |
| [Runtime.md](Docs/Code/Runtime.md) | Сцены Bootstrap/Game, `ApplicationEntryPoint`, `GameFlow`, `LevelLifetimeScope`, шаги загрузки и `LevelWarmup`, `ILevelTickable`, Addressables-owner'ы, `GameLog`, каталог уровней и dev-уровни, ловушки тестов |
| [Level.md](Docs/Code/Level.md) | `LevelData`, визуал клеток и выбор вариантов, декор и placement'ы, ключи/двери в данных, Test/Play, сборки и размер загрузки, сторонние паки, Level Designer, валидатор; геометрия и шейдер `Purge/Geometry`, камера, свет и светильники, биомы, зима (сугробы, мёрзлые двери), видимость и туман |
| [Player.md](Docs/Code/Player.md) | Тики, ввод и тач-контролы, настройки управления, прицел, движение игрока; двери, `PlayerInteraction`, `PickupSystem`, `WeaponSystem` (слоты, базовый пистолет), `WeaponDefinition` и модули, патроны, `WeaponsBuilder`, оружие в руках (`CharacterWeaponRig`) |
| [Combat.md](Docs/Code/Combat.md) | `PlayerCombat`, ощущение стрельбы (Feel), `BulletSystem`, `CombatTargets`, звуки для слуха, эффекты боя, полоски HP и цифры, зоны зрения и шум (вид), следы, анимации игрока; метательное, эффекты, зоны, состояния, дроп и его вид |
| [Monsters.md](Docs/Code/Monsters.md) | `ZombieSystem` Maze (восприятие, A*, Alert, вид, победа/смерть), орда `HordeSystem` и её рендер, боевая песочница, стресс-тест |
| [MetaUI.md](Docs/Code/MetaUI.md) | Карта, таймер, завершение уровня, сейв-сервисы; UI: `UiShape`, `UiStyle`, иконки, кнопки, экраны, HUD; локализация; обучение |
| [Audio.md](Docs/Code/Audio.md) | `AudioBuilder` и `AudioCatalog`, `AudioService`, музыка, `LevelAudioPresenter`, `WeaponAudioPresenter`, звуковые группы оружия |
| [Platforms.md](Docs/Code/Platforms.md) | Веб (фокус, шаблон, release-настройки), аналитика, реклама, `IGamePlatform`, облачный сейв, сборка под площадку, Яндекс, CrazyGames, ВК, GameDistribution, RuStore (Android) |

Ещё: префабы — [Docs/Prefabs.md](Docs/Prefabs.md), меню **Purge** — [Docs/PurgeMenu.md](Docs/PurgeMenu.md), Level Designer — [Docs/LevelDesigner.md](Docs/LevelDesigner.md), стресс-тест — [Docs/StressTest.md](Docs/StressTest.md).

## Сквозные соглашения и ловушки (подробно — в справочниках)
- Клетка `(x, y)` ↔ мир `(x, 0, y)`; North = +Y сетки = +Z мира; вне сетки = стена (`Solid`). Камера не вращается по Y.
- Случайность только `DeterministicRandom` / `StableHash` (не `System.Random`, не `string.GetHashCode`). Логи — `GameLog`, не `Debug.Log`.
- Addressables только через `IAddressablesService.CreateOwner` → `IAssetOwner.LoadAsync`. Во время игры ничего не создавать впервые — пулить при загрузке и прогревать (`IViewWarmup`).
- Порядок шагов загрузки и тиков = порядок регистрации в `LevelLifetimeScope` (геймплей раньше вьюх). Тики только в `Running`, `Time.timeScale` не трогаем.
- Мутация `LevelData` — только `internal` через `LevelEditing`/`LevelAuthoring`. Ссылки между объектами — по id, на Definitions — прямые SO-ссылки; строковые ссылки на контент — `[ContentRef]` (выпадающий список).
- Вёрстка UI — только в `RuntimeScenesBuilder` (сцена пересобирается). Никакого `ToUpper` (турецкое i); новый символ в шрифте — только через `Strings.tsv` + Build Localization.
- Ловушка: `Purge.Application` перекрывает `UnityEngine.Application` — писать `UnityEngine.Application.xxx`.
- Ловушка: VContainer перезаписывает шаблоном новый `*LifetimeScope.cs` при создании `.meta` — после refresh записать содержимое ещё раз.
- Ловушка: UniTask в Unity 6 — `AsyncOperation` ждать через `.ToUniTask()`. В PlayMode `[UnitySetUp]/[UnityTest]` — только `yield return UniTask.ToCoroutine(...)` в итераторе.
- Ловушка сборки: не-Addressable ассет из нескольких групп в сборке — разные объекты; не сравнивать определения из разных Addressable-корней только по ссылке.
- Ловушка MCP: не делать `refresh_unity` во время Play после правки скриптов — сначала Stop.
- Сторонние паки — в `Import` (вне git); используемое переносит **Collect Used Third-Party Assets** до коммита.

## Структура папок (целевая)
```
Assets/_Project/
  Scripts/
    Core/          Purge.Core          — Grid, LevelData, A*, генератор, валидатор, SpatialQuery (чистая логика)
    Gameplay/      Purge.Gameplay      — системы уровня, Player/Zombie/Combat/Weapon/Door runtime
    Application/   Purge.Application   — GameFlow, Save/Audio/Input/Addressables/Settings сервисы
    Presentation/  Purge.Presentation  — Views, UI, LevelVisualSystem, VisibilityController
    Composition/   Purge.Composition   — ProjectLifetimeScope, LevelLifetimeScope (composition root)
    Editor/        Purge.Editor        — Level Designer Tool (Editor-only)
  Tests/EditMode/  Purge.Tests.EditMode
  Tests/PlayMode/  Purge.Tests.PlayMode
  Data/            Levels/, Themes/, VisualSets/, Weapons/, Zombies/
  Prefabs/  Art/  Scenes/ (Bootstrap — первая в Build)  Settings/
```

## Рабочие договорённости
- Общение на русском.
- **Git-коммиты не делать.** Их всегда делает пользователь. **После завершения этапа из плана** в конце ответа предложить название коммита.
- **Сборки (APK, WebGL и т.п.) запускать только после подтверждения пользователя**, кроме случаев, когда он сам попросил сборку.
- **Этапы — по команде пользователя.** Если пользователь согласовал несколько этапов подряд — выполнять их подряд с коротким отчётом после каждого (каждый заканчивается в рабочем состоянии).
- Генерацию уровней (контент) в историю Progress.md не писать — только код и тулзы.
- Справочники обновлять вместе с изменениями: [Docs/Code/](Docs/Code/) (устройство систем — тот файл, чью систему меняли), [Docs/PurgeMenu.md](Docs/PurgeMenu.md) (команды меню **Purge**), [Docs/LevelDesigner.md](Docs/LevelDesigner.md) (тулза), [Docs/Prefabs.md](Docs/Prefabs.md) (префабы). Пока они описывают унаследованный код Maze.
- Закреплённые правила (Progress.md, «Закреплённое») не игнорировать: нарушено — исправить, а не просто сообщить.
- С Unity работаем через MCP for Unity. После правок скриптов проверять консоль (`read_console`). Ловушки MCP — Docs/Lessons.md, раздел 2.
- **Исправления общего с Maze кода** (площадки, сейв, UI) переносятся между проектами вручную — при правке такого кода упомянуть, нужно ли то же в Maze.

## Unity MCP — экономия контекста
- Сначала определить минимум данных для задачи; брать только конкретные GameObject/Component/Asset/Script. Не запрашивать то, что есть в файлах проекта (читать файлы), и то, что уже получено в этой сессии.
- Не запрашивать всю иерархию сцены (только если пользователь явно попросил): найти объект по имени/тегу/признаку → получить только нужные компоненты и свойства.
- Не выгружать целиком большие списки объектов, компонентов, ассетов, логов. Консоль — только ошибки/предупреждения по текущей проблеме (фильтр по типу и тексту, небольшой `count`).
- Большой результат MCP сначала фильтровать, в анализ брать только нужное. Широкое исследование проекта — чтением конкретных файлов, не через MCP.
- Контекст разросся — предложить `/compact`; перед полностью новой задачей — предложить `/clear`.
