import os, re, shutil, sys, csv

SRC = r"C:\Users\Vladimir\For games\Sounds\Sound Ideas - The SFX KIT Disk\Sound Ideas - The SFX KIT Disk\Sound Ideas - The SFX KIT Disk [Wav]"
DST = r"C:\Users\Vladimir\For games\Sounds\Purge_Shortlist"
ALL = os.path.join(os.path.dirname(__file__), "all.txt")
files = [l.strip() for l in open(ALL, encoding="utf-8") if l.strip()]

# (папка назначения, regex по относительному пути (без учёта регистра), максимум файлов)
R = []
def add(dest, rx, n=3):
    R.append((dest, rx, n))

# ---------- 01 Оружие: выстрелы ----------
G = "01_Оружие_выстрелы/"
add(G+"Пистолет", r"Weapons/Gun_Pistol_Shot", 6)
add(G+"Дробовик", r"Weapons/Shotgun_Shot\d+\.wav", 6)
add(G+"Дробовик_помпа_и_гильза", r"Weapons/Shotgun_Shot\d+_EjectAmmo", 4)
add(G+"Автомат_очередь", r"Weapons/AssaultRifle_ShotLoop\d+\.wav", 7)
add(G+"Автомат_мощный_одиночный", r"Weapons/Gun_AssaultRifle_PowerShot", 5)
add(G+"Пулемёт", r"Weapons/MachineGunBurst", 2)
add(G+"Снайперка", r"Weapons/Gun_SniperRifle_Shot", 5)
add(G+"Револьвер_пистолет-пулемёт_см_Пистолет_Автомат", r"Weapons/Gun_NailRifle_Shot", 3)
add(G+"Гранатомёт", r"Weapons/GrenadeLauncher_Shot", 5)
add(G+"Ракетница", r"Weapons/RocketLauncher_Shot", 6)
add(G+"Ракета_полёт_петля", r"Weapons/Rocket_Missile_Loop|Weapons/Projectile_FlightLoop", 4)
add(G+"Миномёт", r"Weapons/MortarShot|Weapons/Blow_Bottle_MortarShot|Weapons/CannonFire_Whistle", 6)
add(G+"Арбалет", r"Weapons/Crossbow_MagicShot|Foley - Misc/ArrowPass|Foley - Misc/ArrowFlyingLoop", 4)
add(G+"Огнемёт_старт", r"Weapons/FlameThrower_ShotBurst", 3)
add(G+"Огнемёт_петля", r"Weapons/FlameThrower_ShotLoop|Weapons/FlameThrower_IdleLoop", 5)
add(G+"Огнемёт_конец", r"Weapons/FlameThrower_ShotEnd", 3)
add(G+"Криомёт", r"Weapons/FreezeGun_Shot", 2)
add(G+"Кислотная_газовая_слизь", r"Weapons/Spit_BigWetLaunch|Weapons/Web_|Weapons/Fireball_Lava_Launch|Laser_ParasiteGunShot|Splats & Squishes/HugeSpit", 6)

# ---------- 02 Энергооружие ----------
E = "02_Энергооружие_sci-fi/"
add(E+"Лазер_выстрел", r"Sci-Fi Weapons/Laser_Shot\d+\.wav", 4)
add(E+"Лазер_винтовка", r"Sci-Fi Weapons/LaserRifle_Shot\d+\.wav", 3)
add(E+"Плазма_энергвинтовка", r"Sci-Fi Weapons/EnergyRifle_Shot\d+\.wav", 4)
add(E+"Рельсотрон_зарядка_и_выстрел", r"Sci-Fi Weapons/LaserRifle_Charged_Shot|Sci-Fi Weapons/LaserRifle_ChargeUp_Loop|Sci-Fi Weapons/Laser_Cannon_Shot|Sci-Fi Weapons/EnergyTurret_Charge", 6)
add(E+"Энергия_запуск_попадание", r"Sci-Fi Weapons/Energy_Launch|Sci-Fi Weapons/Energy_Impact|Sci-Fi Weapons/EnergyWeapon_Launch|Sci-Fi Weapons/Laser_Impact", 6)
add(E+"Тесла_электрический_разряд", r"Sci-Fi Weapons/ElecEnergy_ImpactSpark|Sci-Fi Weapons/ElecEnergy_ReleaseShot|Sci-Fi Weapons/EnergyRifle_ElecHumLoop|Hit_ElectricPowerBoxSpark|Destruction/Explo_Electric|Magic Weapons/Magic_WandZap|Dragon_AttackElectricity", 9)
add(E+"Шоковая_луч_петля", r"Sci-Fi Weapons/LaserBeam_Loop\d+\.wav|Sci-Fi Weapons/TractorBeamRifle|Sci-Fi Weapons/LaserBeam_Burn", 5)
add(E+"Магическая_энергия_выстрел", r"Magic Weapons/Magic_EnergyBlast|Magic Weapons/Magic_SpiritBlast\d|Magic Weapons/Magic_IceEnergyBlast|Magic Weapons/EnergySword_Shot|Magic Weapons/Fireball_Launch", 8)

# ---------- 03 Перезарядка / обращение с оружием ----------
H = "03_Перезарядка_и_обращение/"
add(H+"Перезарядка_огнестрел", r"Weapons Foley/Gun_Reload_Weapon", 12)
add(H+"Перезарядка_огнемёт_ракетница_барабан", r"Weapons Foley/Gun_Reload_(Flamethrower|RocketLauncher|ChamberSpin)", 5)
add(H+"Перезарядка_sci-fi_батареи", r"Weapons Foley/Gun_Reload_Scifi", 6)
add(H+"Достать_оружие_смена", r"Weapons Foley/Gun_DrawSelect_(Weapon|Pistol)", 8)
add(H+"Достать_sci-fi", r"Weapons Foley/Gun_DrawSelect_Scifi", 4)
add(H+"Затвор_возня_с_оружием", r"Weapons Foley/Gun_Handling_Weapon", 10)
add(H+"Убрать_оружие", r"Weapons Foley/Gun_Holster", 3)
add(H+"Пустой_магазин_щелчок", r"Gun_AmmoOut_Scifi|Scifi_Beep_NoAmmo_Error|Sci-Fi Weapons/Laser_Reload", 7)
add(H+"Тиканье_мало_патронов", r"Alarms & Beeps/Beep_Timer|Alarms & Beeps/Beep_TargetTracking", 4)

# ---------- 04 Гильзы ----------
add("04_Гильзы_и_звон/Звон_мелкого_металла", r"Hit_Metal_Small_Rattle|Hit_Glass_Jar_Clink|Hit_SmallMetalCrownSpin|Hit_MetalTargetPing", 8)
add("04_Гильзы_и_звон/Падение_металла_на_пол", r"Hit_LooseMetalDrop|Hit_MetalBarDrop", 6)

# ---------- 05 Ближний бой ----------
M = "05_Ближний_бой/"
add(M+"Взмах_свист", r"Weapons Foley/Swish_WeaponSwing", 6)
add(M+"Взмах_рука_лёгкий", r"Foley - Whooshes/Swish_ArmSwing", 4)
add(M+"Взмах_тяжёлый_кувалда_булава", r"Swing_LargeMace|Whoosh_GiantMaceSwing|Swish_SwingHugeShovel|Swing_LargeAnchor|Whoosh_LargeArmSwings", 6)
add(M+"Взмах_клинок", r"Swish_TwinSwordTwirl|Blade_PikeSlash|EnergySword_Slash", 6)
add(M+"Удар_по_телу_меч_дубина", r"Hit_BeatManWithClub|Punch_WoodClub|Hit_LargeStab_Scythe|Hit_LargeMetalSwordImpact|Hit_ShovelToFace|Hit_MetalPipeToSkull|Hit_PipeImpactHead", 10)
add(M+"Удар_тяжёлый_кувалда", r"Hit_HugeShovelSlam|Hit_HammerSmashRock|Hit_LargeAxeImpactStone|Hit_LargePunch|Punch_Super", 8)
add(M+"Бензопила_и_механика", r"Machinery - Misc/.*(Saw|Chain)|Scrape_MetalOnMetal", 4)

# ---------- 06 Попадание по врагу ----------
P = "06_Попадание_по_врагу/"
add(P+"Шлёп_слизь_мультяшный", r"Splats & Squishes/Hit_SlimeSplat|Splats & Squishes/Squish_WetSlimeHit|Splats & Squishes/Hit_SlimeBallSplat", 6)
add(P+"Тук_удар_по_телу", r"Punch_Boxing_BodyHit|Hit_Organic_WetImpactSplat|Hit_PistolWhipHit|Hit_Wood_DeepImpact|Hit_DeepHugeChestThump", 8)
add(P+"Кость_хруст", r"Bone FX/Bone_Break|Bone FX/BoneBreak_Small|Bone FX/Hit_TendonPop", 5)
add(P+"Мягкое_плоть", r"Splats & Squishes/Squish_Wet_FleshBlood|Splats & Squishes/Squish_Blood_Flesh", 4)
add(P+"Насекомое_лопнуло", r"Hit_LargeInsectSplat|Insect_MiteImpact", 3)

# ---------- 07 Попадание по броне ----------
add("07_Броня_рикошет/Рикошет_звон", r"Destruction/Bullet_Ricochet|Destruction/Bullet_Hit_Metal|Hit_Metal_HighPitch|Hit_MetalTargetPing", 8)
add("07_Броня_рикошет/Броня_удар", r"Armour_Hit|Armour_Helmet|Hit_Sword_Shield|Hit_MetalClawsSlashArmour|Hit_LightMetal\d|Scifi_ShieldHit", 10)

# ---------- 08 Убийство (хлопок) ----------
add("08_Убийство_хлопок/Поп", r"Bubblegum_Pop|Magic_Poof|Poof_CloudDisappear|Poof_Magic_MortarCannon|Hit_SlimeBallSplat|Splat_PaintballHit", 12)

# ---------- 09 Взрывы ----------
X = "09_Взрывы_метательное/"
add(X+"Взрыв_малый", r"Destruction/Explo_Small_\d+|Explo_Small_Barrel|Explo_Grenade\d|Destruction/Explo_Grenade\.", 6)
add(X+"Взрыв_средний", r"Destruction/Explo_Medium_\d+\.wav|Explo_Medium_PowderKeg|Explo_RocketImpact", 6)
add(X+"Взрыв_большой", r"Destruction/Explo_Large_\d+\.wav|Explo_Large_Bomb", 6)
add(X+"Граната_бросок_и_стук", r"Weapons Foley/Grenade_|Weapons/Grenade_Bounce|Swish_BigThrow|Whoosh_TossObject|Beep_Grenade", 12)
add(X+"Молотов_стекло_и_огонь", r"Smash_GlassShatter|Smash_DistantBottle|Smash_AutoGlassShatter|Fire_Ignite_Large|Fire_FlameBurst|Explo_Grenade_Incendiary|Fire_Furnace_FlameBurst", 9)
add(X+"Дымовая_шашка", r"Explo_Grenade_Smoke|GasBomb_Discharge|Steam/Steam_Burst", 6)
add(X+"Фитиль", r"Fuse_BurnLoop", 2)

# ---------- 10 Зоны и состояния ----------
Z = "10_Зоны_и_состояния/"
add(Z+"Огонь_петля_на_земле", r"Destruction/Fire_Loop|Fire_CampfireLoop|Fire_Barrel_Loop|Fire_FireplaceLoop", 6)
add(Z+"Горение_на_теле", r"Sizzle_FleshBurning_Loop|Fire_DieOut|Flame_SpitFire", 4)
add(Z+"Кислота_шипение", r"Steam_Sizzle_AcidBurn|DarkMagic_AcidBurn|Squish_WetSlime\d|Steam/Steam_Sizzle_Hit", 8)
add(Z+"Заморозка_лёд", r"IceBlock_Freeze|IceCrystal_Form|IceCrystal_Rise|Magic_IceEnergyLoop", 6)
add(Z+"Раскол_льда", r"Shatter_IceBlock|Shatter_ZombieInIce|IceBlock_Shatter|IceBlock_RiseShatter|Smash_IceCrystalShatter|Explo_Large_Ice", 6)
add(Z+"Электричество_треск_петля", r"Scifi_Electricity_Hum_Loop|Magic_ElectricEnergyLoop|Explo_Small_Electricity|Explo_Medium_Electric", 6)
add(Z+"Дым_пар_шипение_петля", r"Steam/Steam_Loop\d+\.wav", 3)

# ---------- 11 Подбор ----------
K = "11_Подбор_предметов/"
add(K+"Патроны", r"Weapons Foley/Gun_Ammo_Pickup|Pickups/Pickup_Ammo", 10)
add(K+"Оружие_подобрать", r"Pickups/Gun_Weapon_Pickup", 6)
add(K+"Деньги_монеты_звяк", r"Pickups/Pickup_Coins|Pickups/Pickup_GemBells", 10)
add(K+"Метательное_подбор_лёгкий", r"Pickups/Pickup_Misc\d|Pickups/Pickup_MiscSwish", 6)
add(K+"Редкий_дроп_блеск", r"Pickups/Pickup_Special\d|Pickups/Pickup_SpecialStar|Pickups/Pickup_Secret|Pickups/Pickup_MagicBells", 8)
add(K+"Лечение_аптечка", r"Pickups/Health_Powerup|Pickups/Pickup_Heart|Pickups/Pickup_Scifi_Health|Pickups/Pickup_HolyCross", 6)
add(K+"Ключ", r"Pickups/Pickup_Key|Foley - Misc/Pickup_Keys|Foley - Misc/Key_InsertMetalGate", 6)
add(K+"Выброс_оружия_на_землю", r"Hit_LooseMetalDrop|Hit_MetalDrop_Rattle|Hit_MetalBarDrop", 4)
add(K+"Бустеры_магия_ускорение", r"Pickups/Pickup_Magic\d|Pickups/Pickup_Speed|Pickups/Pickup_Magic_Speed|Pickups/Pickup_Armour\d|Pickups/Pickup_ArmourShield", 8)

# ---------- 12 Прогресс и волны ----------
W = "12_Прогресс_и_волны/"
add(W+"Повышение_уровня_карточки", r"Magic_Spell_Appearance|Magic Hits/Magic_AiryHit|Magic Spells/Magic_Spell_Heal|Magic_Spell_Wish|Pickups/Pickup_SwordPowerup|Misc Magic/Magic_Glow", 10)
add(W+"Выбор_карточки_переброс", r"Menu FX/Menu_Magic|Menu FX/Menu_Scifi_Select|Menu FX/Menu_Accept|Menu FX/Menu_Select", 8)
add(W+"Воскрешение", r"Magic_Spell_Resurrect|DarkMagic_SpiritResurrect|Misc Magic/Magic_Respawn", 6)
add(W+"Начало_волны_рог_сирена", r"Alarm_Klaxon_Loop|Alarm_Loop|Alarm_FactoryBuzzer|Eerie_MusicSting|Music_Sting|Music_LogoSting|Bell_FactoryRing", 10)
add(W+"Конец_волны_победа", r"Menu FX/Music_|Pickups/Pickup_GuitarRiff|Pickups/Pickup_GuitarLeadRiff|Pickups/Pickup_SpecialFilm", 8)
add(W+"Тики_отсчёта_пропустить", r"Alarms & Beeps/Beep\d|Beep_TextScroll|Menu FX/Menu_Click", 8)
add(W+"Сердцебиение_малый_HP_петля", r"Heartbeat_Loop|Amb_Eerie_HeartbeatLoop", 5)
add(W+"Сундук_открыть", r"TreasureChestOpen|Pickup_TreasureChest|Cartoon_OpenMagicBag|Hatch_LargeMetal_Open|Door_LargeWood_Unlock", 6)
add(W+"Сундук_заперт_ключом", r"Door_Locked_HandleRattle|Gate_MetalRattle_Locked|Gate_Metal_Unlock|Hit_LockedMetalDoor_Rattle|Hatch_LargeMetal_Lock", 6)
add(W+"Колпак_безопасной_зоны", r"Scifi_DomeShield_Activate|Scifi_DomeShield_Deactivate|Scifi_DomeShield_Loop|Energy_DomeDisperse|Magic_AirDomeOn|Magic_AirDomeOff|HealthStation_AmbLoop|HealthStation_Activate", 12)

# ---------- 13 Монстры ----------
Mo = "TSK-04/Monsters & Creatures/"
Q = "13_Монстры/"
add(Q+"Зомби_стон_покой", r"Zombies/Zombie(01|04|05|08|11)_Groan", 6)
add(Q+"Зомби_рык_погоня", r"Zombies/Zombie(02|06|07|10)_Growl\d", 6)
add(Q+"Зомби_атака", r"Zombies/Zombie(06|12)_(GrowlAttack|SwipeAttack)|Zombies/Zombie04_Claw_Swipe", 6)
add(Q+"Зомби_урон", r"Zombies/Zombie\d+_Hurt_Grunt|Zombies/ZombieFemale_Hurt", 6)
add(Q+"Зомби_смерть", r"Zombies/Zombie\d+_Death|Zombies/ZombieFemale_Death", 6)
add(Q+"Бегун_визг_хрип", r"Zombies/Zombie06_Growl_Roar|ZombieDog_(Attack|Growl)|Zombies/Zombie09_CrawlGrunt|Zombies/Zombie04_CrawlMoan|Werewolf_Grunt_Breath", 8)
add(Q+"Крикун_призыв_крик", r"Ghost02_Scare_Scream|Ghost04_Death_Scream|AlienHumanoid_Scream|Male_RageScream|Troll01_Scream|Werewolf_Howl|Rhinosaur_Howl|Male_DeathScream", 10)
add(Q+"Плевун_плевок", r"Zombie06_SpitAttack|GiantBeetle_Spit|MudMonster_AttackSplat|Weapons/Spit_BigWetLaunch|Splats & Squishes/HugeSpit|Splat_GoopDrop", 8)
add(Q+"Громила_рёв_удар", r"RockMonster_Roar|RockMonster_Growl|LargeDemon_GrowlRoar|ApeMonster_Roar|Rhinosaur_GrowlSnort", 8)
add(Q+"Громила_шаги_тяжёлые", r"Footstep_Giant\d|Footstep_GiantBeast|Footstep_HugeCreature|Footstep_LargeBeast|Smash_DeepFootStomp", 8)
add(Q+"Громила_дверь_выбита", r"Hit_KickMetalDoor|Hit_RamMetalDoor|Smash_WoodDoor|Hit_KickWoodDoor|Smash_MetalGrateBreak|Crash_MetalCageBreak", 8)
add(Q+"Крюк_цепь", r"Chain_Break|Fence_Chain_Rattle|Chain_Fall_Drop|Hit_ChainFence|Scrape_ClawsDrag|Armour_Rattle", 8)
add(Q+"Скелет_кости", r"Skeletons/Skeleton_(Bones_Rattle|Attack_BoneRattle|Death_BoneShatter|Hit_Shatter|EmergeFromGround|Footstep|Death_BoneHit)", 10)
add(Q+"Появление_из_земли", r"Squish_ZombieEmerge|Coffin_EmergeFromGround|GravestoneRise|Skeleton_EmergeFromGround|Crack_WoodSpikesEmerge|Lava_Emerge", 8)
add(Q+"Шаман_заклинание_щит", r"Magic_Spell_Summon|DarkMagic_Summon|DarkMagic_CastSpell\d|Magic_Spell_Shield|Magic_Spell_GhostShield|Witch_Laugh|Misc Magic/MagicSpell_Cast\d|Magic_Spell_Reflect|Magic_ShieldCatch", 12)
add(Q+"Шаман_подъём_трупа", r"DarkMagic_SpiritResurrect|Magic_Spell_Resurrect|Haunted_CursedEarthLoop|CursedEarthHandGrab", 5)
add(Q+"Предупреждение_способности_нарастающий", r"Beep_TargetLock|Superheating_PreExplosion|Scifi_PowerUp\d|Energy & Forcefields/Amb_Scifi_EnergyRise|DarkMagic_EvilEnergyRise|ElecEnergy_ChargeUp|Fireball_Form", 10)
add(Q+"Чемпион_появление", r"DarkMagic_DemonAppear|Dragon_Roar|EvilGhost_Appear|Ghost05_Appear|Magic_Appear\d", 8)
add(Q+"Гуль_снежный_и_зверь", r"SnowMonster_|FatMonster_Growl|HogMonster_Growl|HyenaMonster_AttackGrowl|LizardMan_AttackHiss|TentacleMonster_Growl", 10)
add(Q+"Шаги_монстров_зомби", r"Footstep_Zombie_|Footstep_Beast|Footstep_LizardMan|Footstep_SlimeCreature", 8)
add(Q+"Урон_смерть_зверей_для_разных_монстров", r"HogMonster_DeathRoar|ScalyCreature_DeathRoar|LizardMan_DeathRoar|FatMonster_DeathRoar|HogMonster_HurtGrowl|ScalyCreature_HurtGrowl|Troll0[1-5]_Hurt|Troll0[1-5]_Death", 12)

# ---------- 14 Боссы ----------
B = "14_Боссы/"
add(B+"Рёв_выход_босса", r"Dragon_Roar|Dragon_CryRoar|LargeDemon_GrowlRoar|ApeMonster_Roar|HeatCreature_Roar|PlantMonster_Roar|RockMonster_Roar|LizardGiant_Roar\d|PlanetCreature_Roar", 10)
add(B+"Стингер_удар", r"Music_Sting|Eerie_MusicSting|Music_Tocatta_Horror|Music_HorrorStrings|Music_LogoSting|Magic_Spell_Appearance", 8)
add(B+"Закрытие_арены_лязг", r"Gate_MetalFenceClose|Door_JailClose|Door_HeavyMetalClose|Door_LargeMetalClose|Door_MetalBunkerClose|Drawbridge_Lower|Door_LargeStoneOpen|Door_LargeWoodCastleClose", 8)
add(B+"Удар_по_земле_рывок", r"Smash_DeepRumbleStomp|Quake_EarthCrack|Rock_Crack_Deep|Smash_HugeRockImpact|Whoosh_BigJump|Whoosh_Deep|Smash_BoulderImpact|Dragon_JumpRoar", 10)
add(B+"Шаги_босса", r"Footstep_GiantRockman|Footstep_GiantRobot|Footstep_Dragon_Land|Footstep_LavaMonster|Footstep_HugeMetalRobot|Footstep_GiantMetal", 8)
add(B+"Урон_босса", r"Dragon_HurtRoar|RockMonster_HurtGrowl|LargeDemon_HurtRoar|ApeMonster_HurtRoar|HeatCreature_HurtRoar|TreeMonster_HurtRoar", 8)
add(B+"Смерть_босса_длинная", r"Dragon_DeathRoar|RockMonster_DeathRoar|HeatCreature_DeathRoar|PlantMonster_DeathRoar|TreeMonster_DeathRoar|Beast_Large_Death|Robot_HugeDeathFall|Explo_Large_Debris|Smash_LargeMetalStatueFall", 10)
add(B+"Дыхание_огонь_лёд_газ", r"Dragon_BreathFire\d|Dragon_BreathIce|Dragon_BreathGas|Dragon_FlameAttack|Dragon_BreathFireLoop", 8)

# ---------- 15 Игрок ----------
Pl = "15_Игрок/"
add(Pl+"Урон_голос", r"Human Misc Male Vocal/Male07_Hurt|Human Misc Male Vocal/Male10_Hurt|Human Misc Male Vocal/Male_Pain_Grunt|Human Misc Male Vocal/Male_Young_Hurt|Human Misc Male Vocal/Male14_Hurt", 8)
add(Pl+"Смерть_голос", r"Human Misc Male Vocal/Male_Death\d|Human Misc Male Vocal/Male07_Death|Human Misc Male Vocal/Male09_Death|Male_DeathScream|Male_Death_Grunt", 6)
add(Pl+"Рывок_прыжок_усилие", r"Male_Jump_Grunt|Male_Effort_Grunt|Male_Land_Grunt|Male_Attack_Grunt|Swish_CartoonJump|Whoosh_BigJump", 8)
add(Pl+"Падение_тела", r"Bodyfalls/Bodyfall_(Dirt|Grass|Gravel|Snow|Hay|Concrete|Weeds)", 8)
add(Pl+"Шаги_трава", r"Footsteps/Grass/Foot_Grass_(Walk|Run)\d", 4)
add(Pl+"Шаги_листья_лес", r"Footsteps/Leaves/Foot_Leaves_BareRun", 4)
add(Pl+"Шаги_земля_пустыня", r"Footsteps/Dirt3/Foot_Dirt3_SneakerRun|Footsteps/Dirt1/", 4)
add(Pl+"Шаги_гравий_пустыня", r"Footsteps/Gravel/Foot_Gravel_BootRun", 4)
add(Pl+"Шаги_камень_замок", r"Footsteps/Concrete/Foot_Conc_BootRun|Footsteps/Concrete/Foot_Conc_BootWalk", 4)
add(Pl+"Шаги_снег", r"Footsteps/Snow - Crunchy/Foot_Snow_Crunchy_(Run|Walk)", 4)
add(Pl+"Шаги_снег_глубокий", r"Footsteps/Snow - Deep/", 3)
add(Pl+"Шаги_лёд", r"Footsteps/Ice/Foot_Ice_(Walk|Run)", 4)
add(Pl+"Шаги_грязь_лава_вулкан", r"Footsteps/Mud/Foot_Mud_Run|Footsteps/Gravel/Foot_Gravel_BareRun", 4)

# ---------- 16 Интерфейс и мета ----------
U = "16_Интерфейс_и_мета/"
add(U+"Покупка_прокачка", r"Pickup_Coins|Menu FX/Menu_Mechanical|Menu_Powerdown|Pickups/Pickup_Special\d|Pickup_GemBells", 8)
add(U+"Не_хватает_денег_ошибка", r"Menu_FailError|Beep_Error|Menu_Scifi_Error|Scifi_Beep_NoAmmo_Error|Error_SynthTone", 8)
add(U+"Смена_вкладки", r"Menu_Backup|Menu_Enter|Menu_Zoom\d|Menu_Scifi_Interface", 6)
add(U+"Новая_редкость_красная_точка", r"Menu_Scifi_Appear|Magic_Spell_Appearance|Beep_Scifi_Menu|Menu_LowTone|Pickup_Bounce", 6)

# ---------- 17 Эмбиент биомов ----------
A = "17_Эмбиент_биомов/"
add(A+"Лес", r"Amb_Birds_ForestPark|Amb_Cricket_Loop|Amb_Eerie_NightLoop|Wind_LeavesBlowLoop|Amb_TropicalForestLoop|Wind_Loop\d|Amb_Eerie_WindToneLoop", 8)
add(A+"Замок", r"Amb_Eerie_CastleLoop|Amb_Eerie_CellarLoop|Amb_Eerie_CaveLoop|Amb_TempleWindLoop|Amb_Eerie_GraveyardLoop|Wind_EerieLoop|Amb_HauntingWindLoop|Amb_Eerie_DroneLoop", 9)
add(A+"Снег_вьюга", r"Wind_ColdHowlingLoop|Wind_ColdLoop|Wind_ColdWhistlingLoop|Wind_ColdGust|Wind_RushingStormLoop", 7)
add(A+"Пустыня", r"Wind_DesertLoop|Wind_SandstormLoop|Wind_CanyonLoop|Wind_DistantHowlingLoop", 6)
add(A+"Вулкан", r"Amb_LowRumbleLoop|Lava_Bubble_Loop|Lava_Rumble_Loop|Lava_RumbleFlow_Loop|Quake_RumbleLoop|Volcano_Lava_Erupt_Loop|Fire_FurnaceBlaze_Loop|Lava_EruptRumble_Loop", 9)

# ---------- выполнение ----------
def sample(items, n):
    if len(items) <= n:
        return items
    step = (len(items) - 1) / (n - 1) if n > 1 else 0
    return [items[round(i * step)] for i in range(n)]

SKIP = re.compile(r"TSK-06/|TSK-01/(Automobiles|Airplanes|Trucks|Misc Transportation)/")
rows = []
report = []
used = {}
for dest, rx, n in R:
    pat = re.compile(rx, re.I)
    m = [f for f in files if pat.search(f) and not SKIP.search(f)]
    # равномерная выборка, но не более n; сохраняем порядок
    picked = sample(m, n)
    report.append((dest, len(m), len(picked)))
    for f in picked:
        rows.append((dest, f))

if "--dry" in sys.argv:
    for d, total, p in report:
        print(f"{p:3d}/{total:4d}  {d}" + ("   <-- ПУСТО" if total == 0 else ""))
    print("TOTAL", len(rows), "unique", len({f for _, f in rows}))
    sys.exit()

if os.path.exists(DST):
    shutil.rmtree(DST)
os.makedirs(DST)
idx = []
for dest, f in rows:
    d = os.path.join(DST, *dest.split("/"))
    os.makedirs(d, exist_ok=True)
    name = os.path.basename(f)
    tgt = os.path.join(d, name)
    if os.path.exists(tgt):
        name = f.split("/")[0] + "_" + name
        tgt = os.path.join(d, name)
    shutil.copy2(os.path.join(SRC, *f.split("/")), tgt)
    idx.append((dest, name, f))

with open(os.path.join(DST, "index.csv"), "w", encoding="utf-8-sig", newline="") as fh:
    w = csv.writer(fh, delimiter=";")
    w.writerow(["Категория", "Файл", "Исходный путь в паке", "Берём? (заполнить)"])
    for dest, name, f in idx:
        w.writerow([dest, name, f, ""])
print("copied", len(idx))
