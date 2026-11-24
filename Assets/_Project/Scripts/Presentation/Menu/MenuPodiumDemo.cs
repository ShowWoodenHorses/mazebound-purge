using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Services;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Horde;
using Purge.Core.Visual;
using Purge.Presentation.Visual;
using UnityEngine;
using UnityEngine.AddressableAssets;
using P = Purge.Presentation.Visual.PlayerAnimatorParameters;

namespace Purge.Presentation.Menu
{
    /// <summary>Repeating presentation for the menu podium. All actors and effects are prepared before playback.</summary>
    internal sealed class MenuPodiumDemo
    {
        private static readonly string[] WeaponIds =
            { "ak_74", "pomp_shotgun", "lynx", "tesla", "minigun", "grenade_launcher", "flame_gun" };
        private static readonly string[] MonsterAddresses =
        {
            "Assets/_Project/Data/Monsters/Visuals/Skeleton_Castle.asset",
            "Assets/_Project/Data/Monsters/Visuals/Zombie_Forest.asset",
            "Assets/_Project/Data/Monsters/Visuals/Ghoul_Snow.asset"
        };
        private static readonly int HordeFrame = Shader.PropertyToID("_HordeFrame");
        private const int MaxMonsters = 10;
        private const float WavePause = 1.15f;
        private const float SwapAt = 0.65f;
        private const float ApproachRadius = 2.4f;

        private readonly MenuPodiumView _view;
        private readonly Animator _player;
        private readonly CharacterWeaponRig _rig;
        private readonly Func<WeaponDefinition, WeaponModel> _showWeapon;
        private readonly DemoWeapon[] _weapons = new DemoWeapon[WeaponIds.Length];
        private readonly Monster[] _monsters = new Monster[MaxMonsters];
        private readonly MonsterLook[] _looks = new MonsterLook[MonsterAddresses.Length];
        private readonly int[] _waveSizes = { 4, 5, 6, 7, 8, 9, 10 };
        private readonly float[] _entrances = new float[MaxMonsters];
        private readonly DeterministicRandom _random = new DeterministicRandom(173);
        private readonly MeshRenderer[] _tracers = new MeshRenderer[8];
        private readonly Monster[] _chainTargets = new Monster[8];
        private CombatEffect[] _chainImpacts = Array.Empty<CombatEffect>();
        private int _chainCount;
        private Transform _root;
        private CombatEffect _hit;
        private Material _tracerMaterial;
        private Material _lightningMaterial;
        private Mesh _tracerMesh;
        private Mesh _lightningMesh;
        private float _lightningWidth;
        private WeaponModel _model;
        private Monster _target;
        private int _weaponIndex;
        private int _waveSizeIndex;
        private int _waveSize;
        private int _firstLook;
        private float _pauseLeft;
        private float _shotLeft;
        private float _flightLeft;
        private float _flightDuration;
        private Vector3 _from;
        private Vector3 _to;

        public bool IsReady { get; private set; }

        public void Destroy()
        {
            if (_root != null) UnityObjects.Destroy(_root.gameObject);
            UnityObjects.Destroy(_lightningMesh);
        }

        public MenuPodiumDemo(MenuPodiumView view, Animator player, CharacterWeaponRig rig,
            Func<WeaponDefinition, WeaponModel> showWeapon)
        {
            _view = view;
            _player = player;
            _rig = rig;
            _showWeapon = showWeapon;
        }

        public async UniTask LoadAsync(SharedDefinitionsService definitions, IAssetOwner owner,
            Func<WeaponDefinition, CancellationToken, UniTask<GameObject>> loadWeapon, CancellationToken cancellation)
        {
            var visual = definitions.CombatVisual;
            if (_player == null || _rig == null || visual == null || definitions.Content == null) return;
            _root = new GameObject("Menu Demo").transform;
            _root.SetParent(_view.Stage, false);
            _root.gameObject.SetActive(false);
            var tracer = await owner.LoadAsync<GameObject>(visual.Bullet, cancellation);
            _lightningMaterial = visual.ChainBolt;
            for (var i = 0; i < _tracers.Length; i++)
            {
                var instance = UnityEngine.Object.Instantiate(tracer, _root, false);
                _tracers[i] = instance.GetComponentInChildren<MeshRenderer>();
                instance.SetActive(false);
            }
            _tracerMaterial = _tracers[0].sharedMaterial;
            _tracerMesh = _tracers[0].GetComponent<MeshFilter>().sharedMesh;
            if (_lightningMaterial != null)
            {
                // The tracer maps its length to U, while the lightning texture runs along V.
                _lightningMesh = UnityEngine.Object.Instantiate(_tracerMesh);
                var uv = _lightningMesh.uv;
                for (var i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].y, uv[i].x);
                _lightningMesh.uv = uv;
                _lightningWidth = visual.ChainBoltWidth / 0.1f;
            }
            _hit = await LoadEffect(owner, visual.TargetHit, cancellation);
            for (var i = 0; i < _weapons.Length; i++)
            {
                var weapon = definitions.Content.Find<WeaponDefinition>(WeaponIds[i]);
                if (weapon == null || await loadWeapon(weapon, cancellation) == null) return;
                var flash = CombatVisualDefinition.IsSet(weapon.Effects?.MuzzleFlash)
                    ? weapon.Effects.MuzzleFlash : visual.MuzzleFlashOf(weapon.Hold) ?? visual.MuzzleFlash;
                var muzzle = weapon.Attack is StreamAttack ? visual.StreamOf(weapon) : flash;
                var impact = weapon.Attack switch
                {
                    ExplosiveAttack explosive => CombatVisualDefinition.IsSet(explosive.Look)
                        ? explosive.Look : visual.ExplosionOf(explosive.Radius),
                    _ => null
                };
                _weapons[i] = new DemoWeapon(weapon, await LoadEffect(owner, muzzle, cancellation),
                    await LoadEffect(owner, impact, cancellation));
                if (weapon.Attack is ChainAttack chain)
                {
                    _chainImpacts = new CombatEffect[Mathf.Clamp(chain.Jumps + 1, 1, _chainTargets.Length)];
                    var chainHit = visual.ChainHitOf(weapon) ?? visual.TargetHit;
                    for (var j = 0; j < _chainImpacts.Length; j++)
                        _chainImpacts[j] = await LoadEffect(owner, chainHit, cancellation);
                }
            }
            for (var i = 0; i < _looks.Length; i++)
            {
                var monsterVisual = await owner.LoadAsync<HordeVisualDefinition>(MonsterAddresses[i], cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (monsterVisual == null || monsterVisual.Mesh == null || monsterVisual.Variants.Length == 0) return;
                var run = monsterVisual.FindClip(HordeClipKind.Run);
                var death = monsterVisual.FindClip(HordeClipKind.Death);
                if (run < 0 || death < 0) return;
                _looks[i] = new MonsterLook(monsterVisual, monsterVisual.Clips[run], monsterVisual.Clips[death]);
            }
            for (var i = 0; i < _monsters.Length; i++)
                _monsters[i] = new Monster(_root, i);
            IsReady = true;
        }

        public void SetActive(bool active)
        {
            if (!IsReady) return;
            if (!active)
            {
                StopShot();
                _root.gameObject.SetActive(false);
                return;
            }

            _root.gameObject.SetActive(true);
            _waveSizeIndex = 0;
            SelectWeapon(0);
            BeginWave();
        }

        public void Tick(float deltaTime)
        {
            var anchor = _view.Anchor;
            Monster nearest = null;
            var closest = float.PositiveInfinity;
            var waveActive = false;
            for (var i = 0; i < _waveSize; i++)
            {
                var monster = _monsters[i];
                if (monster.Pending)
                {
                    waveActive = true;
                    monster.SpawnLeft -= deltaTime;
                    if (monster.SpawnLeft <= 0f) Spawn(monster, i);
                    else continue;
                }

                if (!monster.View.activeSelf) continue;
                if (monster.Dying)
                {
                    waveActive |= monster.PlayDeath(deltaTime);
                    continue;
                }

                waveActive = true;
                var actor = monster.View.transform;
                var distance = MoveMonster(monster, anchor.position, deltaTime);
                if ((distance < closest || monster == _target) && _view.IsInFrame(actor.position + Vector3.up * 0.75f))
                {
                    closest = monster == _target ? 0f : distance;
                    nearest = monster;
                }
            }

            var weapon = _weapons[_weaponIndex];
            _shotLeft -= deltaTime;
            if (_flightLeft > 0f) UpdateShot(weapon, deltaTime);
            if (!waveActive)
            {
                if (_pauseLeft <= 0f) _pauseLeft = WavePause;
                var before = _pauseLeft;
                _pauseLeft -= deltaTime;
                if (before > SwapAt && _pauseLeft <= SwapAt) SelectWeapon((_weaponIndex + 1) % _weapons.Length);
                if (_pauseLeft <= 0f) BeginWave();
                return;
            }
            if (nearest == null || nearest.Dying)
            {
                if (weapon.Definition.Attack is StreamAttack) Stop(weapon.Muzzle);
                return;
            }
            var directionToTarget = nearest.View.transform.position - anchor.position;
            var inRange = directionToTarget.sqrMagnitude <= weapon.Definition.Attack.AimRange * weapon.Definition.Attack.AimRange;
            anchor.rotation = Quaternion.RotateTowards(anchor.rotation, Quaternion.LookRotation(directionToTarget), deltaTime * 520f);
            var stream = weapon.Definition.Attack as StreamAttack;
            var muzzle = _model != null ? _model.MuzzlePosition : anchor.position + Vector3.up;
            if (stream != null && weapon.Muzzle != null && weapon.Muzzle.gameObject.activeSelf)
            {
                if (inRange) weapon.Muzzle.transform.SetPositionAndRotation(muzzle, anchor.rotation);
                else Stop(weapon.Muzzle);
            }
            if (_shotLeft <= 0f && _flightLeft <= 0f && inRange &&
                Vector3.Angle(anchor.forward, directionToTarget) <= 12f &&
                CanFireAt(weapon.Definition.Attack, nearest, directionToTarget.magnitude))
                Fire(weapon, nearest, muzzle);
        }

        private bool CanFireAt(WeaponAttack attack, Monster target, float distance)
        {
            // Give the next runner time to come within jump range before firing at an isolated target.
            return attack is not ChainAttack { Jumps: > 0 } chain || distance <= ApproachRadius + 0.9f ||
                FindChainNeighbor(target, chain.JumpRange, 0) != null;
        }

        private static float MoveMonster(Monster monster, Vector3 podium, float deltaTime)
        {
            var actor = monster.View.transform;
            var offset = actor.position - podium;
            var distance = offset.magnitude;
            var approach = Mathf.Clamp01((distance - ApproachRadius) / 2f);
            var radialStep = Mathf.Min(Mathf.Max(0f, distance - ApproachRadius), 3.4f * approach * deltaTime);
            // Bend the run around the podium so a target waiting its turn keeps moving.
            var turn = (1f - approach) * 1.65f * deltaTime / Mathf.Max(distance, ApproachRadius) * Mathf.Rad2Deg;
            var next = Quaternion.AngleAxis(turn, Vector3.up) * (offset.normalized * (distance - radialStep));
            var movement = next - offset;
            if (movement.sqrMagnitude > 1e-8f) actor.rotation = Quaternion.LookRotation(movement);
            actor.position = podium + next;
            monster.AdvanceRun(movement.magnitude);
            return distance - radialStep;
        }

        private void SelectWeapon(int index)
        {
            StopShot();
            _weaponIndex = index;
            var weapon = _weapons[index].Definition;
            var mesh = weapon.Attack is ChainAttack && _lightningMesh != null ? _lightningMesh : _tracerMesh;
            foreach (var tracer in _tracers) tracer.GetComponent<MeshFilter>().sharedMesh = mesh;
            _model = _showWeapon(weapon);
            _shotLeft = 0f;
        }

        private void BeginWave()
        {
            _pauseLeft = 0f;
            _target = null;
            if (_waveSizeIndex == _waveSizes.Length) _waveSizeIndex = 0;
            if (_waveSizeIndex == 0) Shuffle(_waveSizes, _waveSizes.Length);
            _waveSize = _waveSizes[_waveSizeIndex++];
            var weapon = _weapons[_weaponIndex].Definition;
            var spacing = weapon.Attack switch
            {
                StreamAttack => 1.25f,
                _ => Mathf.Clamp(weapon.Cooldown, 0.85f, 1.15f)
            };
            _firstLook = (_firstLook + 1) % _looks.Length;
            var groupSize = weapon.Attack is ChainAttack { Jumps: > 0 } ? 2 : 1;
            var groups = (_waveSize + groupSize - 1) / groupSize;
            for (var i = 0; i < groups; i++)
                _entrances[i] = 0.035f + 0.93f * (i + 0.15f + 0.7f * (float)_random.NextDouble()) / groups;
            Shuffle(_entrances, groups);
            if (groupSize == 2)
            {
                // Bring two runners from adjacent points, so the lightning has a nearby monster to jump to.
                for (var i = _waveSize - 1; i >= 0; i--)
                    _entrances[i] = _entrances[i / 2] + (i % 2 == 0 ? -0.018f : 0.018f);
            }
            for (var i = 0; i < _monsters.Length; i++)
            {
                var monster = _monsters[i];
                monster.View.SetActive(false);
                monster.Pending = i < _waveSize;
                monster.SpawnLeft = i / groupSize * spacing + i % groupSize * 0.10f + (float)_random.NextDouble() * 0.12f;
            }
        }

        private void Shuffle<T>(T[] values, int count)
        {
            for (var i = count - 1; i > 0; i--)
            {
                var other = _random.NextInt(i + 1);
                (values[i], values[other]) = (values[other], values[i]);
            }
        }

        private void Spawn(Monster monster, int index)
        {
            var look = (_firstLook + index) % _looks.Length;
            var weapon = _weapons[_weaponIndex].Definition;
            var hits = weapon.Id == "minigun" ? 7 + look : weapon.Attack switch
            {
                StreamAttack => 8 + look,
                ExplosiveAttack => 1,
                BulletAttack { Pellets: > 1 } => 1,
                _ => 1 + look
            };
            monster.Spawn(_looks[look], _view.FloorAtViewport(_entrances[index], 1.10f), hits);
        }

        private void Fire(DemoWeapon weapon, Monster target, Vector3 muzzle)
        {
            _target = target;
            _from = muzzle;
            if (weapon.Definition.Attack is ChainAttack chain) PickChainTargets(target, chain);
            _shotLeft = Mathf.Clamp(weapon.Definition.Cooldown, 0.11f, 1.1f);
            _player.SetFloat(P.ShootSpeedHash, 1f / Mathf.Clamp(weapon.Definition.Cooldown, 0.08f, 0.6f));
            _player.SetTrigger(P.ShootHash);
            _rig.Kick(0.04f, 2f, 0.025f);
            var stream = weapon.Definition.Attack as StreamAttack;
            if (weapon.Muzzle != null)
            {
                if (stream == null || !weapon.Muzzle.gameObject.activeSelf)
                    Play(weapon.Muzzle, _from, _view.Anchor.rotation);
                if (stream != null)
                {
                    var targetPoint = target.View.transform.position + Vector3.up * 0.9f;
                    var reach = Vector3.Distance(muzzle, targetPoint);
                    weapon.Muzzle.SetLifetimeFactor(Mathf.Clamp01(reach / Mathf.Max(weapon.Muzzle.Radius, 0.01f)));
                }
            }
            _flightLeft = _flightDuration = weapon.Definition.Attack switch
            {
                ExplosiveAttack => 0.34f,
                StreamAttack => 0.13f,
                ChainAttack => 0.18f,
                _ => 0.10f
            };
        }

        private void PickChainTargets(Monster first, ChainAttack attack)
        {
            _chainCount = 1;
            _chainTargets[0] = first;
            var maxTargets = Mathf.Min(attack.Jumps + 1, _chainImpacts.Length);
            while (_chainCount < maxTargets)
            {
                var next = FindChainNeighbor(_chainTargets[_chainCount - 1], attack.JumpRange, _chainCount);
                if (next == null) break;
                _chainTargets[_chainCount++] = next;
            }
        }

        private Monster FindChainNeighbor(Monster from, float range, int used)
        {
            Monster nearest = null;
            var point = from.View.transform.position;
            var closest = range * range;
            for (var i = 0; i < _waveSize; i++)
            {
                var candidate = _monsters[i];
                if (candidate == from || !candidate.View.activeSelf || candidate.Dying) continue;
                var position = candidate.View.transform.position;
                var distance = (position - point).sqrMagnitude;
                if (distance >= closest || Array.IndexOf(_chainTargets, candidate, 0, used) >= 0 ||
                    !_view.IsInFrame(position + Vector3.up * 0.9f)) continue;
                nearest = candidate;
                closest = distance;
            }
            return nearest;
        }

        private void UpdateShot(DemoWeapon weapon, float deltaTime)
        {
            _flightLeft -= deltaTime;
            var progress = 1f - Mathf.Max(0f, _flightLeft) / _flightDuration;
            _to = _target.View.transform.position + Vector3.up * 0.9f;
            var direction = _to - _from;
            var attack = weapon.Definition.Attack;
            if (attack is BulletAttack bullets)
            {
                var count = Mathf.Min(bullets.Pellets, _tracers.Length);
                for (var i = 0; i < count; i++)
                {
                    var spread = count > 1 ? (i / (count - 1f) - 0.5f) * bullets.Spread : 0f;
                    var flight = Quaternion.AngleAxis(spread, Vector3.up) * direction;
                    DrawTracer(i, _from + flight * progress, flight, Mathf.Min(flight.magnitude * progress, 0.85f), _tracerMaterial);
                }
            }
            else if (attack is ChainAttack)
            {
                UpdateChainShot();
                return;
            }
            else if (attack is ExplosiveAttack)
            {
                var position = Vector3.Lerp(_from, _to, progress) + Vector3.up * (2f * progress * (1f - progress));
                DrawTracer(0, position, direction, 0.26f, _tracerMaterial);
            }

            if (_flightLeft > 0f) return;
            HideTracers();
            var hitPosition = _target.View.transform.position;
            Play(weapon.Impact ?? _hit, hitPosition + Vector3.up * (attack is ExplosiveAttack ? 0.1f : 0.85f),
                Quaternion.LookRotation(-direction));
            _target.TakeHit();
        }

        private void UpdateChainShot()
        {
            if (_flightLeft <= 0f) HideTracers();
            var from = _from;
            for (var i = 0; i < _chainCount; i++)
            {
                var target = _chainTargets[i];
                var point = target.View.transform.position + Vector3.up * 0.9f;
                var direction = point - from;
                if (_flightLeft <= 0f)
                {
                    var rotation = direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(-direction) : Quaternion.identity;
                    Play(_chainImpacts[i] ?? _hit, point, rotation);
                    target.TakeHit();
                }
                else if (direction.sqrMagnitude > 1e-8f)
                    DrawTracer(i, point, direction, direction.magnitude, _lightningMaterial ?? _tracerMaterial,
                        _lightningMesh != null ? _lightningWidth : 1.5f);
                from = point;
            }
        }

        private void DrawTracer(int index, Vector3 head, Vector3 direction, float length, Material material, float width = 1.5f)
        {
            var renderer = _tracers[index];
            renderer.sharedMaterial = material;
            renderer.transform.SetPositionAndRotation(head, Quaternion.LookRotation(direction));
            renderer.transform.localScale = new Vector3(width, width, length);
            renderer.gameObject.SetActive(true);
        }

        private void StopShot()
        {
            HideTracers();
            _flightLeft = 0f;
            Stop(_hit);
            Stop(_weapons[_weaponIndex].Muzzle);
            Stop(_weapons[_weaponIndex].Impact);
            foreach (var impact in _chainImpacts) Stop(impact);
        }

        private void HideTracers()
        {
            foreach (var tracer in _tracers) tracer.gameObject.SetActive(false);
        }

        private async UniTask<CombatEffect> LoadEffect(IAssetOwner owner, AssetReferenceGameObject reference,
            CancellationToken cancellation)
        {
            if (!CombatVisualDefinition.IsSet(reference)) return null;
            var prefab = await owner.LoadAsync<GameObject>(reference, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var instance = UnityEngine.Object.Instantiate(prefab, _root, false);
            instance.SetActive(false);
            var effect = instance.GetComponent<CombatEffect>();
            return effect != null ? effect : instance.AddComponent<CombatEffect>();
        }

        private static void Play(CombatEffect effect, Vector3 position, Quaternion rotation)
        {
            if (effect == null) return;
            effect.transform.SetPositionAndRotation(position, rotation);
            effect.gameObject.SetActive(true);
            effect.Play();
        }

        private static void Stop(CombatEffect effect)
        {
            if (effect == null) return;
            effect.Stop();
            effect.gameObject.SetActive(false);
        }

        private readonly struct DemoWeapon
        {
            public readonly WeaponDefinition Definition;
            public readonly CombatEffect Muzzle;
            public readonly CombatEffect Impact;

            public DemoWeapon(WeaponDefinition definition, CombatEffect muzzle, CombatEffect impact)
            {
                Definition = definition;
                Muzzle = muzzle;
                Impact = impact;
            }
        }

        private readonly struct MonsterLook
        {
            public readonly HordeVisualDefinition Visual;
            public readonly HordeClip Run;
            public readonly HordeClip Death;
            public readonly Material[] Materials;

            public MonsterLook(HordeVisualDefinition visual, HordeClip run, HordeClip death)
            {
                Visual = visual;
                Run = run;
                Death = death;
                Materials = new Material[visual.Mesh.subMeshCount];
                Materials[0] = visual.Variants[0];
                Array.Copy(visual.ExtraMaterials, 0, Materials, 1, Math.Min(visual.ExtraMaterials.Length, Materials.Length - 1));
            }
        }

        private sealed class Monster
        {
            public readonly GameObject View;
            public bool Pending;
            public float SpawnLeft;
            public bool Dying => _hitsLeft == 0;

            private MonsterLook _look;
            private int _hitsLeft;
            private float _animationTime;
            private readonly MeshFilter _mesh;
            private readonly MeshRenderer _renderer;
            private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

            public Monster(Transform parent, int index)
            {
                View = new GameObject("Demo Monster " + index);
                View.transform.SetParent(parent, false);
                _mesh = View.AddComponent<MeshFilter>();
                _renderer = View.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                View.SetActive(false);
            }

            public void Spawn(MonsterLook look, Vector3 position, int hits)
            {
                _look = look;
                _hitsLeft = hits;
                Pending = false;
                _animationTime = 0f;
                View.transform.position = position;
                View.transform.localScale = Vector3.one * look.Visual.Scale;
                _mesh.sharedMesh = look.Visual.Mesh;
                _renderer.sharedMaterials = look.Materials;
                View.SetActive(true);
            }

            public void TakeHit()
            {
                if (--_hitsLeft == 0) _animationTime = 0f;
            }

            public void AdvanceRun(float distance)
            {
                _animationTime += distance / Mathf.Max(0.01f, _look.Visual.LocomotionSpeed);
                ShowFrame(_look.Run);
            }

            public bool PlayDeath(float deltaTime)
            {
                _animationTime += deltaTime * 1.7f;
                if (_animationTime >= _look.Death.Length)
                {
                    View.SetActive(false);
                    return false;
                }
                ShowFrame(_look.Death);
                return true;
            }

            private void ShowFrame(HordeClip clip)
            {
                var rate = clip.FrameRate > 0f ? clip.FrameRate : _look.Visual.FrameRate;
                var frame = Mathf.FloorToInt(_animationTime * rate);
                frame = clip.FirstFrame + (clip.Loop ? frame % clip.FrameCount : Mathf.Min(frame, clip.FrameCount - 1));
                _properties.SetVector(HordeFrame, new Vector4(frame, frame, 0f, 0f));
                _renderer.SetPropertyBlock(_properties);
            }
        }
    }
}
