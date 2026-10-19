# Runtime-каркас, загрузка, Addressables, тесты

Справочник по текущему коду (перенесён из CLAUDE.md 2026-10-17). Читать, когда задача касается этих систем; **обновлять при их изменении.** Общие правила — [CLAUDE.md](../../CLAUDE.md).

## Runtime-каркас
- Сцены: **Bootstrap** (всегда загружена; `ProjectLifetimeScope` + UI-экраны) и **Game** (грузится аддитивно на каждый уровень; `LevelLifetimeScope` с `autoRun = false`, камера, свет). Пересоздаются меню **Purge → Dev → Build Runtime Scenes** (`RuntimeScenesBuilder`) — правки UI вносить в билдер, а не только в сцену.
- Точка входа — `ApplicationEntryPoint` (VContainer `IAsyncStartable`). Сервисы приложения реализуют `IApplicationService`, инициализируются `GameFlow.InitializeApplication` в порядке регистрации.
- `GameFlow` (Application) не знает VContainer: уровень создаёт `ILevelSessionFactory` → `LevelSessionFactory` (Composition) грузит Game, строит `LevelLifetimeScope` дочерним к проектному и регистрирует в нём `LevelData` и `IAssetOwner` уровня. Переходы GameFlow идут по одному, новый отменяет текущий
  - любое исключение → полная выгрузка уровня → `GameFlowState.Error`.
- Шаги загрузки уровня (§9) — `ILevelLoadStep` со `Stage` (`LevelLoadStage`), регистрируются в `LevelLifetimeScope` из любого слоя; `LevelRuntime` выполняет их по порядку.
  - Последний — `LevelWarmup` (стадия `Warmup`): все вьюхи уровня (и скрытые, с неактивными детьми) и объекты от `IViewWarmup` один раз рисуются служебной камерой на весь уровень, затем `GC.Collect`.
  - **Во время игры ничего не создавать впервые:** новые виды объектов (эффекты, свет, декор) создавать/пулить при загрузке и отдавать в прогрев через `IViewWarmup`. Per-frame логика уровня — `ILevelTickable`: тикается только в `Running` (пауза = нет тиков, `Time.timeScale` не трогаем).
- Addressables: только через `IAddressablesService.CreateOwner(name)` → `IAssetOwner.LoadAsync`; handle регистрируется в owner сразу. Owner уровня освобождается после Dispose LevelScope и выгрузки Game. `AddressablesService.ActiveHandleCount` — для проверки утечек в тестах.
- Логи — `GameLog` (`[Purge][Channel]`, каналы из §108), не голый `Debug.Log`. `Info` в release-сборке пишется без стектрейса.
- Каталог уровней — `LevelCatalog` (Addressable `"LevelCatalog"`, `Data/Levels/LevelCatalog.asset`); Build/Sync добавляет уровень. `LevelId` = имя ассета уровня. Уровень открыт, если пройден предыдущий в каталоге (`ProgressService.IsUnlocked`; новые уровни из обновления открываются сами).
- **Уровни сейчас:** игровых уровней Maze нет (Level_01–30 удалены 2026-10-14); в основном каталоге — dev-уровень забега `Level_Run` (с 6а, PLAY; **Build Run Level**) и обучение `Level_Tutorial`; `Level_Dev` — в dev-каталоге.
  - **Dev-уровни** (Dev, Items, weapon, light, стресс, песочница) — второй каталог `Data/Levels/DevLevelCatalog.asset` (Addressable `"LevelCatalog/Dev"`) и группа **Purge Dev Levels**; переключатель **Dev** в тулбаре Level Designer (`LevelSync.IsDevLevel/SetDevLevel`), Build/Sync сохраняет принадлежность.
  - `PurgeBuilder` в release-сборке выключает группу (`IncludeInBuild`), `LevelCatalogService` грузит dev-каталог только при `Debug.isDebugBuild` и если адрес есть (`IAddressablesService.ExistsAsync`).
  - `ILevelCatalog.Levels` — только игровые (прогресс, «следующий», номера), `DevLevels` — dev, `Find` — оба. В выборе уровней dev — с новой страницы «DEV», подпись — id без `Level_`, вне прогрессии. Визуальные префабы, нужные только dev-уровням, остаются в Purge Visuals (попадают в релиз).
- **Ловушка:** пространство имён `Purge.Application` перекрывает `UnityEngine.Application` — в коде `Purge.*` писать `UnityEngine.Application.xxx` полностью.
- **Ловушка:** VContainer (`ScriptTemplateProcessor`) перезаписывает шаблоном любой **новый** `*LifetimeScope.cs` при создании его `.meta`. Создав такой файл снаружи Unity: refresh → записать содержимое ещё раз.
- UniTask в Unity 6: `AsyncOperation` ожидать через `.ToUniTask()` (прямой `await` не компилируется).

## Тесты
- **Ловушка:** `[UnitySetUp]`/`[UnityTest]` в PlayMode не должны возвращать `UniTask.ToCoroutine(...)` напрямую — только через итератор-обёртку (`yield return UniTask.ToCoroutine(...)`, см. `BootstrapFlowTests.Async`).
  - MCP гоняет PlayMode без перезагрузки домена, и после EditMode-прогона Test Framework читает поле состояния итератора → NRE во всех SetUp/TearDown.
