using System;
using System.Collections.Generic;
using System.Linq;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Presentation.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Weapons: the player's weapons from the models in Art/Weapons/Purge (pivot at the grip). The
    /// folder under it is the weapon's class (<see cref="Classes"/>: draft modules and how it is held), a deeper folder is
    /// its caliber: the ammo type is the <see cref="AmmoDefinition"/> that lists the deepest folder of the model's path in
    /// its <see cref="AmmoDefinition.ModelFolders"/>. Not weapons: <c>For_monsters</c> (monster equipment), <c>Throw</c>
    /// (throwables — stage 4б) and <c>*_Ammo</c> models (projectiles).
    /// <list type="number">
    /// <item><see cref="WeaponDefinition"/> per model in Data/Weapons. A model already in <see cref="WeaponVisualCatalog"/>
    /// keeps its definition; a new one gets <c>Data/Weapons/&lt;name&gt;.asset</c> with the draft modules of its class
    /// (<see cref="DraftFor"/>). Values are tuned in the asset and never overwritten.</item>
    /// <item>Held prefab = the model + <see cref="WeaponModel"/> with a Muzzle (guns) or Tip (melee) mark at the far
    /// end of the mesh; a melee model that is not long along +Z gets a grip rotation that turns it there.
    /// <see cref="WeaponVisualCatalog"/> (Data/Weapons/WeaponVisuals, "Weapons/Visual") maps definitions to them.</item>
    /// <item>Pickup prefab (Art/Weapons/Pickups) = the model lying on its side, so its silhouette is seen from above,
    /// diagonal in the cell, on the floor; the placeholder theme's Weapon set gets one variant per definition, and
    /// saved levels whose weapon visuals no longer fit are reassigned.</item>
    /// <item><see cref="CharacterWeaponRig"/> on the player prefab: palm centres and the melee socket, from the hand
    /// and finger bones (independent of any pose).</item>
    /// <item>Gun's <see cref="WeaponModel.GripLeft"/> (Grip_L): where the left palm is on the gun in the shooting pose
    /// of its <see cref="WeaponHold"/> (<see cref="PlayerAnimationsBuilder.GripPoseClip"/>), posed as in game — the left hand IK pulls the hand there in every other pose.</item>
    /// </list>
    /// Run Convert Object Materials to Purge Lit after new models.
    /// </summary>
    internal static class WeaponsBuilder
    {
        private const string ArtPath = "Assets/_Project/Art/Weapons";
        private const string ModelsPath = ArtPath + "/Purge";
        private const string PickupsPath = ArtPath + "/Pickups";
        private const string ProjectileSuffix = "_Ammo";
        private static readonly string[] NotWeapons = { "For_monsters", "Throw" };
        private const string DataPath = "Assets/_Project/Data/Weapons";
        private const string AmmoPath = "Assets/_Project/Data/Ammo";
        private const string CatalogPath = DataPath + "/WeaponVisuals.asset";
        private const string WeaponSetPath = "Assets/_Project/Data/Themes/PlaceholderWeaponSet.asset";

        /// <summary>Same as the characters, so a weapon on the floor is as big as in the hands.</summary>
        private const float PickupScale = 0.75f;

        private const float PickupYaw = 45f; // diagonal: the longest guns fit the cell

        /// <summary>Moment of the hold's aiming idle the left grip mark is taken from (normalized).</summary>
        private const float GripPoseTime = 0.5f;

        private sealed class Weapon
        {
            public string ModelPath;
            public WeaponDefinition Definition;
        }

        /// <summary>A class folder: how its weapons are held and their draft attack.</summary>
        private sealed class WeaponClass
        {
            public WeaponHold Hold;
            public Func<WeaponAttack> Attack;
            public int Magazine;
            public float ReloadTime;
            public float Speed = 1f;
        }

        /// <summary>
        /// Class folders of Art/Weapons/Purge and the draft of a new weapon of the class (tuned in the asset afterwards).
        /// A new class folder needs a line here (a new kind of weapon is code: how it is held, its attack module).
        /// </summary>
        private static readonly Dictionary<string, WeaponClass> Classes = new Dictionary<string, WeaponClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pistol"] = new WeaponClass
            {
                Hold = WeaponHold.Pistol, Magazine = 12, ReloadTime = 1.2f,
                Attack = () => new BulletAttack { Damage = 12f, Interval = 0.28f, Spread = 3f, Range = 12f },
            },
            ["Auto"] = new WeaponClass
            {
                Hold = WeaponHold.Rifle, Magazine = 30, ReloadTime = 2f, Speed = 0.9f,
                Attack = () => new BulletAttack { Damage = 9f, Interval = 0.1f, Automatic = true, Spread = 4f, Range = 14f },
            },
            ["Shotgun"] = new WeaponClass
            {
                Hold = WeaponHold.Shotgun, Magazine = 6, ReloadTime = 2.5f, Speed = 0.9f,
                Attack = () => new BulletAttack { Damage = 8f, Interval = 0.8f, Pellets = 8, Spread = 26f, Range = 7f, Speed = 24f },
            },
            ["Sniper_rifle"] = new WeaponClass
            {
                Hold = WeaponHold.Rifle, Magazine = 5, ReloadTime = 2.5f, Speed = 0.85f,
                Attack = () => new BulletAttack { Damage = 45f, Interval = 1f, Spread = 0f, Range = 22f, Speed = 40f, Pierce = 2 },
            },
            ["Ballons"] = new WeaponClass
            {
                Hold = WeaponHold.Heavy, Magazine = 100, ReloadTime = 2.5f, Speed = 0.75f,
                Attack = () => new StreamAttack { DamagePerSecond = 45f, Range = 3.5f, Angle = 30f },
            },
            ["Energy"] = new WeaponClass
            {
                Hold = WeaponHold.Heavy, Magazine = 20, ReloadTime = 2f, Speed = 0.85f,
                Attack = () => new ChainAttack { Damage = 25f, Interval = 0.45f, Automatic = true },
            },
            ["Another"] = new WeaponClass
            {
                Hold = WeaponHold.Shoulder, Magazine = 4, ReloadTime = 2.5f, Speed = 0.8f,
                Attack = () => new ExplosiveAttack(),
            },
            ["Melee"] = new WeaponClass
            {
                Hold = WeaponHold.Melee,
                Attack = () => new MeleeAttack(),
            },
        };

        [MenuItem("Purge/Dev/Build Weapons")]
        public static void Build()
        {
            try
            {
                var catalog = LoadOrCreate<WeaponVisualCatalog>(CatalogPath);
                var weapons = CollectWeapons(catalog, out var created);
                var icons = new Dictionary<WeaponDefinition, Sprite>();
                foreach (var old in catalog.MutableWeapons)
                    if (old?.Definition != null && old.Icon != null) icons[old.Definition] = old.Icon;
                catalog.MutableWeapons.Clear();
                var set = AssetDatabase.LoadAssetAtPath<VisualSet>(WeaponSetPath);
                if (set == null)
                    throw new InvalidOperationException($"'{WeaponSetPath}' not found: run Purge → Dev → Create Placeholder Theme first.");
                set.MutableVariants.Clear();
                set.DefaultVariantId = null;

                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                var visualsGroup = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup) ?? settings.DefaultGroup;
                EnsureFolder(PickupsPath);

                var pickups = new HashSet<string>();
                foreach (var weapon in weapons)
                {
                    var definition = weapon.Definition;
                    MarkModel(weapon.ModelPath, definition.IsMelee);
                    icons.TryGetValue(definition, out var icon);
                    catalog.MutableWeapons.Add(new WeaponVisualDefinition(definition, Reference(weapon.ModelPath), icon));

                    var pickupPath = BuildPickup(weapon.ModelPath);
                    pickups.Add(pickupPath);
                    var pickupGuid = AssetDatabase.AssetPathToGUID(pickupPath);
                    if (settings.FindAssetEntry(pickupGuid) == null)
                        settings.CreateOrMoveEntry(pickupGuid, visualsGroup);
                    set.MutableVariants.Add(new VisualVariant("weapon_" + definition.Id, definition: definition,
                        prefab: new AssetReferenceGameObject(pickupGuid)));
                }

                var removedPickups = DeleteUnusedPickups(pickups, settings);
                EditorUtility.SetDirty(catalog);
                EditorUtility.SetDirty(set);
                EditorUtility.SetDirty(settings);
                var playerPrefab = PlayerAnimationsBuilder.PlayerPrefabPath();
                RigPlayer(playerPrefab);
                MarkGrips(playerPrefab, weapons);
                var reassigned = ReassignLevels(set);
                var problem = LevelDesigner.LevelSync.SyncShared(settings);
                if (problem != null) Debug.LogWarning("[Purge] " + problem);
                AssetDatabase.SaveAssets();
                UiIconsBuilder.BuildWeaponIcons(catalog); // Models may have changed.
                WarnUnusedDefinitions(weapons);
                ReportProblems(weapons);
                var news = created.Count > 0 ? $" ({string.Join(", ", created)}: draft values, tune them in {DataPath})" : "";
                var removed = removedPickups > 0 ? $", {removedPickups} unused removed" : "";
                Debug.Log($"[Purge] Weapons built: {weapons.Count} weapon(s), {created.Count} new{news}; held and pickup " +
                          $"prefabs{removed}, player rig; {reassigned} weapon visual(s) in levels reassigned.");
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building weapons failed: " + e.Message);
                throw;
            }
        }

        /// <summary>
        /// Every weapon model under <see cref="ModelsPath"/>: the catalog's ones first in its order, new ones after them
        /// by path. A model in the catalog keeps its definition; a new one gets one by its name.
        /// </summary>
        private static List<Weapon> CollectWeapons(WeaponVisualCatalog catalog, out List<string> created)
        {
            var order = new List<string>();
            var known = new Dictionary<string, WeaponDefinition>();
            foreach (var entry in catalog.MutableWeapons)
            {
                var guid = entry?.HeldPrefab?.AssetGUID;
                if (entry?.Definition == null || string.IsNullOrEmpty(guid) || known.ContainsKey(guid)) continue;
                known[guid] = entry.Definition;
                order.Add(guid);
            }

            int Rank(string path)
            {
                var index = order.IndexOf(AssetDatabase.AssetPathToGUID(path));
                return index < 0 ? int.MaxValue : index;
            }

            var models = AssetDatabase.FindAssets("t:Prefab", new[] { ModelsPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsWeaponModel)
                .OrderBy(Rank)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (models.Count == 0)
                throw new InvalidOperationException($"No weapon models in {ModelsPath}.");

            var ammo = AssetDatabase.FindAssets("t:" + nameof(AmmoDefinition), new[] { AmmoPath })
                .Select(guid => AssetDatabase.LoadAssetAtPath<AmmoDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .ToList();

            created = new List<string>();
            var weapons = new List<Weapon>();
            var ids = new Dictionary<string, string>();
            foreach (var path in models)
            {
                if (!known.TryGetValue(AssetDatabase.AssetPathToGUID(path), out var definition))
                {
                    definition = DefinitionFor(path, ammo, out var isNew);
                    if (definition == null) continue;
                    if (isNew) created.Add(definition.name);
                }

                if (ids.TryGetValue(definition.Id, out var other))
                    throw new InvalidOperationException($"Weapons '{other}' and '{path}' have the same id '{definition.Id}': " +
                                                        "change the id in one of the definitions.");
                ids[definition.Id] = path;
                weapons.Add(new Weapon { ModelPath = path, Definition = definition });
            }

            return weapons;
        }

        private static bool IsWeaponModel(string path)
        {
            if (System.IO.Path.GetFileNameWithoutExtension(path).EndsWith(ProjectileSuffix, StringComparison.OrdinalIgnoreCase))
                return false;
            var folders = FoldersOf(path);
            return folders.Length > 0 && !NotWeapons.Any(folder => string.Equals(folders[0], folder, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Folders between <see cref="ModelsPath"/> and the file, outermost first.</summary>
        private static string[] FoldersOf(string modelPath)
        {
            var parts = modelPath.Substring(ModelsPath.Length + 1).Split('/');
            return parts.Take(parts.Length - 1).ToArray();
        }

        /// <summary>
        /// Data/Weapons/&lt;name&gt;.asset for a model not in the catalog. An existing asset of that name is used as it
        /// is; a new one gets the draft of the model's class. Null (with a warning) for a model outside any class folder.
        /// </summary>
        private static WeaponDefinition DefinitionFor(string modelPath, List<AmmoDefinition> ammo, out bool isNew)
        {
            isNew = false;
            var name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            var path = $"{DataPath}/{name}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (definition != null)
                return definition;

            var folders = FoldersOf(modelPath);
            if (folders.Length == 0 || !Classes.TryGetValue(folders[0], out var weaponClass))
            {
                Debug.LogWarning($"[Purge] Weapon model '{modelPath}' is not in a known class folder ({string.Join(", ", Classes.Keys)}): skipped.");
                return null;
            }

            isNew = true;
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            AssetDatabase.CreateAsset(definition, path); // ContentIdAssigner gives it its id
            DraftFor(definition, weaponClass, AmmoFor(folders, ammo));
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>The ammo type listing the deepest folder of the path; null when none does.</summary>
        private static AmmoDefinition AmmoFor(string[] folders, List<AmmoDefinition> ammo)
        {
            for (var i = folders.Length - 1; i >= 0; i--)
                foreach (var type in ammo)
                    if (type.ModelFolders != null && type.ModelFolders.Any(folder => string.Equals(folder, folders[i], StringComparison.OrdinalIgnoreCase)))
                        return type;
            return null;
        }

        /// <summary>Draft modules of a new weapon of the class.</summary>
        private static void DraftFor(WeaponDefinition definition, WeaponClass weaponClass, AmmoDefinition ammo)
        {
            var modules = new List<WeaponModule> { weaponClass.Attack() };
            if (weaponClass.Hold != WeaponHold.Melee && weaponClass.Hold != WeaponHold.MeleeHeavy)
            {
                modules.Add(new AmmoModule { Ammo = ammo, Magazine = weaponClass.Magazine, ReloadTime = weaponClass.ReloadTime });
                if (ammo == null)
                    Debug.LogWarning($"[Purge] No ammo type lists a folder of weapon '{definition.name}': set its ammo by hand.");
            }

            if (!Mathf.Approximately(weaponClass.Speed, 1f))
                modules.Add(new MobilityModule { InHands = weaponClass.Speed });
            var id = definition.Id;
            definition.Configure(string.IsNullOrEmpty(id) ? ContentIds.FromName(definition.name) : id, modules.ToArray());
            definition.ConfigureLook(Rarity.Common, weaponClass.Hold);
        }

        /// <summary>Setup problems of every weapon (modules), as warnings.</summary>
        private static void ReportProblems(List<Weapon> weapons)
        {
            var problems = new List<string>();
            foreach (var weapon in weapons)
                weapon.Definition.Validate(problems);
            foreach (var problem in problems)
                Debug.LogWarning("[Purge] " + problem);
        }

        /// <summary>
        /// Pickup prefabs no weapon uses any more (a renamed or removed model) leave Pickups and Addressables, so they do
        /// not ship: the folder is this builder's own.
        /// </summary>
        private static int DeleteUnusedPickups(HashSet<string> used, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings)
        {
            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PickupsPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (used.Contains(path)) continue;
                settings.RemoveAssetEntry(guid);
                if (AssetDatabase.DeleteAsset(path)) removed++;
            }

            return removed;
        }

        /// <summary>Definitions in Data/Weapons without a model have no look in game: reported.</summary>
        private static void WarnUnusedDefinitions(List<Weapon> weapons)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponDefinition", new[] { DataPath }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && weapons.All(weapon => weapon.Definition != definition))
                    Debug.LogWarning($"[Purge] Weapon definition '{definition.name}' has no model in {ModelsPath}, so it has " +
                                     "no look in game: delete it or put its model back.");
            }
        }

        /// <summary>
        /// Adds <see cref="WeaponModel"/> with a mark at the far end of the mesh: Muzzle for guns, Tip for melee. A melee
        /// model longer along another axis than along Z (packs differ) gets the grip rotation that turns that axis to +Z.
        /// </summary>
        private static void MarkModel(string path, bool melee)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var markName = melee ? "Tip" : "Muzzle";
                var obsoleteName = melee ? "Muzzle" : "Tip";
                var obsolete = root.transform.Find(obsoleteName);
                if (obsolete != null) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);

                var mark = root.transform.Find(markName);
                if (mark == null)
                {
                    mark = new GameObject(markName).transform;
                    mark.SetParent(root.transform, false);
                }

                var axis = melee ? LongAxis(root) : Vector3.forward;
                mark.localPosition = FarEnd(root, axis);
                mark.localRotation = Quaternion.FromToRotation(Vector3.forward, axis);

                var model = root.GetComponent<WeaponModel>() ?? root.AddComponent<WeaponModel>();
                var serialized = new SerializedObject(model);
                serialized.FindProperty("_muzzle").objectReferenceValue = melee ? null : mark;
                serialized.FindProperty("_tip").objectReferenceValue = melee ? mark : null;
                serialized.FindProperty("_gripRotation").quaternionValue = Quaternion.FromToRotation(axis, Vector3.forward);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>The axis (±X, ±Y, ±Z of the root) the mesh is longest along, towards the side farther from the pivot.</summary>
        private static Vector3 LongAxis(GameObject root)
        {
            var bounds = RootBounds(root);
            var size = bounds.size;
            var axis = size.z >= size.x && size.z >= size.y ? Vector3.forward : size.y >= size.x ? Vector3.up : Vector3.right;
            return Vector3.Dot(bounds.center, axis) < 0f ? -axis : axis;
        }

        /// <summary>Bounds of every mesh in the root's space.</summary>
        private static Bounds RootBounds(GameObject root)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var local = mesh.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (first) bounds = new Bounds(point, Vector3.zero);
                    else bounds.Encapsulate(point);
                    first = false;
                }
            }

            return bounds;
        }

        /// <summary>
        /// Centre of the mesh vertices within 3 cm of the farthest point along <paramref name="axis"/>, in the root's space
        /// (not readable meshes: the middle of the bounds' far face).
        /// </summary>
        private static Vector3 FarEnd(GameObject root, Vector3 axis)
        {
            var points = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                foreach (var vertex in mesh.vertices)
                    points.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
            }

            if (points.Count == 0)
            {
                var bounds = RootBounds(root);
                return bounds.center + Vector3.Scale(bounds.extents, axis);
            }

            var max = float.MinValue;
            foreach (var point in points) max = Mathf.Max(max, Vector3.Dot(point, axis));
            var sum = Vector3.zero;
            var count = 0;
            foreach (var point in points)
                if (Vector3.Dot(point, axis) >= max - 0.03f)
                {
                    sum += point;
                    count++;
                }

            var end = sum / count;
            return end + axis * (max - Vector3.Dot(end, axis));
        }

        /// <summary>
        /// Art/Weapons/Pickups/&lt;model&gt;_Pickup: the held model lying on its side (seen in profile from above),
        /// diagonal, centred in the cell and resting on the floor. Rebuilt in place (GUID kept).
        /// </summary>
        private static string BuildPickup(string heldPath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(heldPath);
            var path = $"{PickupsPath}/{name}_Pickup.prefab";
            var root = new GameObject(name + "_Pickup");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(heldPath), root.transform);
                model.transform.localScale = Vector3.one * PickupScale;
                model.transform.localRotation = Quaternion.Euler(0f, PickupYaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
                model.transform.localPosition = Vector3.zero;

                var bounds = new Bounds();
                var first = true;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if (first) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    first = false;
                }

                model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y + 0.005f, -bounds.center.z);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            return path;
        }

        /// <summary>
        /// Palm centres under both hands and the melee socket under the right one, from finger bone positions in hand
        /// space: the grip runs along the knuckles toward the index finger (blade above the thumb), edge toward the
        /// fingers. The Synty hand has a thumb, an index and one merged finger.
        /// </summary>
        private static void RigPlayer(string playerPrefab)
        {
            var root = PrefabUtility.LoadPrefabContents(playerPrefab);
            try
            {
                var animator = root.GetComponent<Animator>();
                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (right == null || left == null)
                    throw new InvalidOperationException("The player avatar has no hand bones.");

                var palmRight = Child(right, "Palm_R");
                palmRight.localPosition = PalmCentre(animator, right, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal);
                palmRight.localRotation = Quaternion.identity;
                var palmLeft = Child(left, "Palm_L");
                palmLeft.localPosition = PalmCentre(animator, left, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal);
                palmLeft.localRotation = Quaternion.identity;

                var finger = right.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal).position);
                var index = right.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightIndexProximal).position);
                var forward = (index - finger).normalized;
                var up = Vector3.ProjectOnPlane(Vector3.Lerp(finger, index, 0.5f), forward).normalized;
                var socket = Child(right, "WeaponSocket_Melee");
                socket.localPosition = palmRight.localPosition;
                socket.localRotation = Quaternion.LookRotation(forward, up);

                var rig = root.GetComponent<CharacterWeaponRig>() ?? root.AddComponent<CharacterWeaponRig>();
                var serialized = new SerializedObject(rig);
                serialized.FindProperty("_meleeSocket").objectReferenceValue = socket;
                serialized.FindProperty("_palmRight").objectReferenceValue = palmRight;
                serialized.FindProperty("_palmLeft").objectReferenceValue = palmLeft;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, playerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Grip_L of every gun: the left palm relative to the gun, in the shooting pose, posed as in game.</summary>
        private static void MarkGrips(string playerPrefab, List<Weapon> weapons)
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefab));
            try
            {
                var rig = player.GetComponent<CharacterWeaponRig>();
                foreach (var weapon in weapons)
                {
                    if (weapon.Definition.IsMelee) continue;
                    var path = weapon.ModelPath;
                    // The pose of the weapon's own hold: a pistol is held otherwise than a minigun.
                    var clip = CharacterAnimationUtility.LoadClip(PlayerAnimationsBuilder.GripPoseClip(weapon.Definition.Hold));
                    clip.SampleAnimation(player, clip.length * GripPoseTime);

                    var gun = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), rig.PalmRight, false);
                    gun.transform.localPosition = Vector3.zero;
                    gun.transform.localRotation = Quaternion.identity;
                    gun.transform.rotation = rig.TwoHandedRotation();
                    var position = gun.transform.InverseTransformPoint(rig.PalmLeft.position);
                    var rotation = Quaternion.Inverse(gun.transform.rotation) * rig.PalmLeft.rotation;
                    UnityEngine.Object.DestroyImmediate(gun);

                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var mark = Child(root.transform, "Grip_L");
                        mark.localPosition = position;
                        mark.localRotation = rotation;
                        var serialized = new SerializedObject(root.GetComponent<WeaponModel>());
                        serialized.FindProperty("_gripLeft").objectReferenceValue = mark;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        private static Vector3 PalmCentre(Animator animator, Transform hand, HumanBodyBones finger, HumanBodyBones index)
        {
            var knuckles = Vector3.Lerp(animator.GetBoneTransform(finger).position, animator.GetBoneTransform(index).position, 0.5f);
            return Vector3.Lerp(Vector3.zero, hand.InverseTransformPoint(knuckles), 0.6f);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        /// <summary>Weapons of saved levels whose visual no longer fits their definition get a new one.</summary>
        private static int ReassignLevels(VisualSet set)
        {
            var reassigned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:LevelData"))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || level.VisualTheme == null || level.VisualTheme.GetSet(VisualKind.Weapon) != set)
                    continue;

                var changed = false;
                foreach (var weapon in level.Weapons)
                {
                    var variant = set.FindVariant(level.VisualData.GetObjectAssignment(weapon.Id).VariantId);
                    if (variant != null && variant.Matches(VisualCategory.General, weapon.Definition))
                        continue;
                    VisualAssigner.AssignObject(level, weapon);
                    changed = true;
                    reassigned++;
                }

                if (changed) EditorUtility.SetDirty(level);
            }

            return reassigned;
        }

        private static AssetReferenceGameObject Reference(string path) => new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(path));

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
