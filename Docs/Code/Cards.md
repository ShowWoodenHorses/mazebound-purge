# Опыт и карточки забега (этап 6в)

Справочник по текущему коду. Читать, когда задача касается опыта, карточек, статов забега; **обновлять при их изменении.** Решения — [Progress.md](../Progress.md), раздел 3, «Забег (6)», пункт «6в».

## Данные
- **`CardDefinition`** (Core/Cards, `ContentDefinition`, меню Purge/Definitions/Card): `Rarity` (фиксирована), `MaxLevel`, `Icon` (Sprite), модули `CardModule` (`[SerializeReference, ModuleList]`); `ShownValue` модуля — число для текста (доли — в процентах).
  - `StatCard` (`RunStat` + `PerLevel`, можно несколько): WeaponDamage, FireRate, ReloadTime, Magazine, MoveSpeed, MaxHealth, PickupRadius, HealOnKill, ExplosionDamage, ExplosionRadius, CritChance. Доли — MaxHealth/PickupRadius/HealOnKill абсолютные (`StatCard.IsPercent`).
  - `StatusDamageCard` (состояние + доля урона по монстрам с ним; «Урон по горящим» → `Status_Burning`), `ThrowablesCard` (`Count` в слот сразу; пустой слот — `Fallback`, граната).
- **`CardDeck`** (`Data/Cards/CardDeck.asset`, адрес `Cards/Deck`, Purge Shared, необязательна): список карточек, кривая опыта `ExperienceFor(level)` = `FirstLevel` (10) × `Growth` (1,25)^(level−1), `Choices` (3), веса по редкости 10/6/3/1,5/0,5. Грузит `SharedDefinitionsService.Cards`.
- **Опыт за убийство** — `MonsterDefinition.Experience` (0 = по рангу 1/4/15/50, `ExperienceOf`).
- **Purge → Dev → Build Cards** (`CardsBuilder`): создаёт недостающие 12 карточек первого набора в `Data/Cards` (существующие не трогает), даёт иконку тем, у кого её нет (`UiIconsBuilder.BuildCardIcons` → `Art/UI/Icons/Cards/card_*.png`), собирает колоду из папки. Новая карточка = ассет в папке + `card.<id>` / `card.<id>.text` в `Strings.tsv` (`{0}`, `{1}` — `ShownValue` модулей по порядку); Build Localization предупреждает о карточке без текстов.

## Геймплей (Gameplay/Run)
- **`RunExperience`** (шаг `SpawnZombies`): `HordeSystem.Killed` → опыт, `Level` (с 1), `Experience`/`Next`/`Progress`, `Pending` — повышения без выбранной карточки; `Changed`, `LeveledUp`. Активен только при колоде и **уровне с волнами** (`HordeSystem.UsesWaves`) — в песочнице и стресс-тесте карточек нет.
- **`CardSystem`**: `OpenNext()` — предложение для следующего `Pending` (по весам, без повторов, карточки на максимуме не предлагаются; нечего предложить — повышение списывается), `Choose(i)`, `Reroll()` (раз на предложение; сначала — другие карточки), `Apply(card)` (уровень + модули), `Levels`. Детерминирована по seed забега. Лечение за убийство — тоже здесь (`Killed`).
  - Применение: статы → `RunStats`; MaxHealth → `PlayerHealth.SetMax` + лечение на столько же; скорость → `PlayerSystem.BonusSpeed`; магнит → `PickupSystem.MagnetRadius`.
- **`RunStats`**: суммы по `RunStat`, бонусы по состояниям; `CooldownFactor`, `ReloadFactor` (не меньше 0,25), `MagazineOf(weapon)`, `CritChance`, `CriticalFactor` (×2).
- **`RunDamage`** (`IDamageModifier` → `CombatTargets.Modifier`): `PlayerDamage` — `Weapon` (струи, цепи: урон оружия), `Precise` (пули, ближний: урон оружия + крит), `Explosion` (взрывы игрока: урон и радиус взрывов), `None` (зоны, состояния, взрывы монстров — без изменений). Бонус по состоянию — к Weapon/Precise/Explosion.
  - `CombatTargets.Damage(..., weapon, kind)`, `Blast(..., kind)` возвращает итоговый радиус (его же получают `Exploded` у `BulletSystem`/`EffectSystem`), событие `CriticalHit`.
- **`PlayerCombat`**: `CooldownOf`, `MagazineOf`, перезарядка × `ReloadFactor` → `WeaponRuntime.ReloadDuration` (по нему `ReloadProgress` и скорость анимации).
- **Магнит** (`PickupSystem`): раз в 0,1 с дроп монстров (патроны, метательное при месте в слоте, аптечки при неполном HP) в радиусе → `Pickup.Attracted`, летит к игроку 12 кл/с (`Position`), подбирается на месте; не поместилось — обратно (`Home`). Оружие и ключи — только в своей клетке.

## Поток и вид
- **`GameFlowState.Cards`** — пауза (`OpenCards`/`CloseCards`; закрыть нельзя, пока идёт реклама); Esc не закрывает; HUD виден; звуки паузы; повышение — звук `AudioCatalog.LevelUp` и вспышка (`RunFeedbackPresenter`, 6д). Продолжение забега — `CardSystem.Restore`, `RunExperience.Restore` ([MetaUI.md](MetaUI.md), «Сейв забега»).
- Бустеры (11б): `RunExperience.AddPending(n)` — карточки без повышения уровня (стартовая карточка); `CardSystem.FreeRerolls` / `SetFreeRerolls` / `RerollFree()` — бесплатный переброс (раз на предложение, как обычный), в сейве забега `FreeRerolls`.
- **`CardsPresenter`** (Presentation, шаг `InitializeUI`, `LateTick`): полоса опыта в HUD (`HudScreen.SetExperience`, «УР. N» под HP); в `Playing` при `Pending` — `OpenNext` → `CardsScreen` + `OpenCards`; выбор → следующее предложение или `CloseCards`; переброс — сначала бесплатный (`RerollFree`, кнопка без значка рекламы), иначе `AdsService.ShowRewardedAsync("cards_reroll")`.
- **`CardsScreen`** (вёрстка — `RuntimeScenesBuilder.BuildCards`): заголовок, «УРОВЕНЬ N · ВЫБЕРИТЕ КАРТОЧКУ», 3 карточки (рамка и подпись цветом редкости `UiStyle.Rarities`, иконка в кольце, имя, текст, «НОВАЯ» / «УРОВЕНЬ n/max»), «ПЕРЕБРОСИТЬ» с значком рекламы. Клики игнорируются 0,4 с после показа; первая карточка выбрана для геймпада.
- **Крит** — `CombatFeedbackView` (по `CriticalHit`): число крупнее (`CombatFeedback.CriticalScale`) цветом `CriticalColor`; обычный урон по орде цифр не показывает. Мелкие лечения подряд у игрока складываются в одно число.
- Тесты — `CardTests` (EditMode).
