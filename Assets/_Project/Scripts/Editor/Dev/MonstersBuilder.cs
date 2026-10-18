using System;
using System.Collections.Generic;
using System.IO;
using Purge.Core.Drops;
using Purge.Core.Horde;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Editor.Content;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Monsters: monsters from the folders <c>Art/Monster/&lt;Biome&gt;/&lt;Rank&gt;/*.prefab</c>
    /// (Common, Castle, Forest, Snow… × Mass, Officer, Special = elite, Boss). Stage 5a builds the rank-and-file (Mass),
    /// stage 5c — the officers, 5e — the elite (Animator: <see cref="OfficersBuilder"/>):
    /// <list type="number">
    /// <item>Clips: the "Monster_*" clips in Art/Animations are copied once from the Zombie_Animations and Ultimate
    /// Animation Collection packs in Import (<see cref="ClipTable"/>) and imported as Humanoid, root motion baked into
    /// the pose.</item>
    /// <item>Definition <c>Data/Monsters/&lt;Biome&gt;/&lt;prefab&gt;.asset</c> (<see cref="MonsterDefinition"/>): made once
    /// with the rank's draft numbers and drop table; rank and biome follow the folder, everything else is kept.</item>
    /// <item>Look <c>Data/Monsters/Visuals/&lt;prefab&gt;.asset</c> (<see cref="HordeVisualDefinition"/> with the monster
    /// id): the skinned mesh welded by position + UV, the equipment of its list glued in (weight 1 on its bone, at the
    /// model's <see cref="EquipmentModel"/> offset; a submesh per material), bone matrices of the clips in a texture
    /// (no VAT), materials Purge/Horde. The chase follows the definition's speed: ≥ <see cref="SprintSpeed"/> cells/s
    /// sprints, slower runs (the walks are baked too, for slow wandering); the run's ground speed is measured so the
    /// renderer plays it at the monster's speed and the feet do not slide. Assets are updated in place (same GUIDs) in
    /// <c>Art/Horde/&lt;prefab&gt;</c>; the look's equipment list and splash colour are kept.</item>
    /// <item>The stress test and the sandbox get the looks of the monsters in their lists
    /// (<see cref="StressTestDefinition.MonsterVisuals"/>); the content catalog is rebuilt.</item>
    /// </list>
    /// </summary>
    internal static class MonstersBuilder
    {
        private const string MonsterArtPath = "Assets/_Project/Art/Monster";
        private const string BakedArtPath = "Assets/_Project/Art/Horde";
        private const string DataPath = "Assets/_Project/Data/Monsters";
        private const string VisualsPath = DataPath + "/Visuals";
        private const string MassDropPath = "Assets/_Project/Data/Drops/HordeDrop.asset";
        private const string ZombiePackPath = "Assets/_Project/Import/Animations/Zombie_Animations/Animations";
        private const string UacPath = "Assets/_Project/Import/Animations/Ultimate Animation Collection";
        private const string ClipPrefix = "Monster_";
        private const float FrameRate = 30f;

        private static readonly string[] StressAssets =
        {
            "Assets/_Project/Data/Horde/StressTest.asset", "Assets/_Project/Data/Horde/Sandbox.asset",
        };

        /// <summary>A monster this fast (cells/s) chases with the sprint clips, slower ones — with the run.</summary>
        private const float SprintSpeed = 2.8f;

        private enum Gait
        {
            Any,
            Run,
            Sprint,
        }

        /// <summary>A baked clip: one or more source clips played one after another (the parts of climbing out).</summary>
        private sealed class ClipEntry
        {
            public string Name;
            public HordeClipKind Kind;
            public HordeClipSide Side;
            public Gait Gait;
            public bool Loop;
            public string[] Parts; // "Monster_*" clips in Art/Animations
            /// <summary>Frames per second of clip time; 0 = <see cref="FrameRate"/>.</summary>
            public float Fps;
            /// <summary>Keeps only the first seconds (0 = all).</summary>
            public float MaxLength;
            /// <summary>Fixed frame count over the whole clip (0 = by the rate): for clips always played faster.</summary>
            public int Frames;
            /// <summary>Keeps the body's ground drift (deaths: the body falls away); others are baked in place.</summary>
            public bool KeepDrift;
            /// <summary>Window (start, end seconds) of each part; null = the whole parts. End 0 = to the part's end.</summary>
            public Vector2[] Windows;
            /// <summary>Seconds each part blends into the next (smooth joint between poses that differ).</summary>
            public float Blend;
        }

        private static float PartStart(ClipEntry entry, int part) =>
            entry.Windows != null && part < entry.Windows.Length ? entry.Windows[part].x : 0f;

        private static float PartEnd(ClipEntry entry, AnimationClip[] sources, int part)
        {
            var length = sources[part].length;
            if (entry.Windows == null || part >= entry.Windows.Length || entry.Windows[part].y <= 0f) return length;
            return Mathf.Min(length, entry.Windows[part].y);
        }

        /// <summary>Element-wise blend of two bone matrices (poses close enough for a short joint).</summary>
        private static Matrix4x4 Lerp(Matrix4x4 a, Matrix4x4 b, float t)
        {
            var result = new Matrix4x4();
            for (var i = 0; i < 16; i++) result[i] = Mathf.Lerp(a[i], b[i], t);
            return result;
        }

        /// <summary>"Monster_*" clip → its source in Import: (pack folder, file name without extension).</summary>
        private static readonly (string Name, bool Uac, string Source)[] ClipTable =
        {
            ("Monster_Idle", false, "Zombie_Idle_01"),
            ("Monster_Walk_1", false, "Zombie_Walk_02_Forward_InPlace"),
            ("Monster_Walk_Fast", false, "Zombie_Walk_Fast01_Forward_InPlace"),
            ("Monster_Run_1", false, "Zombie_Run_02_Forward_InPlace"),
            ("Monster_Sprint_1", false, "Zombie_Sprint_01_Forward_InPlace"),
            ("Monster_Sprint_2", false, "Zombie_Sprint_02_Forward_InPlace"),
            ("Monster_Attack_1", false, "Zombie_Attack01"),
            ("Monster_Attack_2", false, "Zombie_Attack02"),
            ("Monster_Attack_3", false, "Zombie_Attack03"),
            ("Monster_Hit_Head", false, "Zombie_HitReact_Head"),
            ("Monster_Hit_LeftArm", false, "Zombie_HitReact_LeftArm"),
            ("Monster_Hit_RightArm", false, "Zombie_HitReact_RightArm"),
            ("Monster_Emerge_Crawl", false, "Zombie_Crawl_OutOfGround_To_CrawlIdle"),
            ("Monster_Emerge_Stand", false, "Zombie_Crawl_CrawlIdle_To_Stand"),
            ("Monster_Stunned", true, "Humanoid@Stunned"),
            ("Monster_Death_Front", true, "Humanoid@DeathFrontCombat"),
            ("Monster_Death_Back", true, "Humanoid@DeathBackCombat"),
            ("Monster_Death_Left", true, "Humanoid@DeathLeftCombat"),
            ("Monster_Death_Right", true, "Humanoid@DeathRightCombat"),
        };

        /// <summary>
        /// What a rank-and-file look bakes (the gait's locomotion only). The texture is the look's whole download, so
        /// what is seen briefly or sped up is baked sparser: attacks play squeezed into the attack interval, a hit
        /// reaction only while the monster is staggered, climbing out — squeezed into the emerge time.
        /// </summary>
        private static readonly ClipEntry[] MassClips =
        {
            Entry("Idle", HordeClipKind.Idle, loop: true, fps: 15f),
            // Walks (~0.2 and ~0.6 cells/s): slow wandering of stage 5b; the chase is the gait's run.
            Entry("Walk_1", HordeClipKind.Walk, loop: true, fps: 15f),
            Entry("Walk_Fast", HordeClipKind.Walk, loop: true),
            Entry("Run_1", HordeClipKind.Run, loop: true, gait: Gait.Run),
            Entry("Sprint_1", HordeClipKind.Run, loop: true, gait: Gait.Sprint),
            Entry("Sprint_2", HordeClipKind.Run, loop: true, gait: Gait.Sprint),
            Entry("Attack_1", HordeClipKind.Attack, fps: 15f),
            Entry("Attack_2", HordeClipKind.Attack, fps: 15f),
            Entry("Attack_3", HordeClipKind.Attack, fps: 15f),
            Entry("Hit_Head", HordeClipKind.HitReact, maxLength: 0.8f),
            Entry("Hit_LeftArm", HordeClipKind.HitReact, maxLength: 0.8f),
            Entry("Hit_RightArm", HordeClipKind.HitReact, maxLength: 0.8f),
            Entry("Stunned", HordeClipKind.Stun, loop: true, fps: 15f),
            Entry("Death_Front", HordeClipKind.Death, side: HordeClipSide.Front, fps: 15f),
            Entry("Death_Back", HordeClipKind.Death, side: HordeClipSide.Back, fps: 15f),
            Entry("Death_Left", HordeClipKind.Death, side: HordeClipSide.Left, fps: 15f),
            Entry("Death_Right", HordeClipKind.Death, side: HordeClipSide.Right, fps: 15f),
            // Climbing out: 13.3 s of the pack's clips squeezed into ~2 s jerked (×7). Only the moments that read: the
            // torso coming out of the ground (crawl 2.4–5.0 s; underground before, crawling forward after) and getting up
            // (stand 0.6–3.2 s), blended at the joint — ~5 s, played at ~2.5×.
            new ClipEntry
            {
                Name = "Emerge", Kind = HordeClipKind.Emerge, Frames = 72,
                Parts = new[] { ClipPrefix + "Emerge_Crawl", ClipPrefix + "Emerge_Stand" },
                Windows = new[] { new Vector2(2.4f, 5.0f), new Vector2(0.6f, 3.2f) },
                Blend = 0.4f,
            },
        };

        private static ClipEntry Entry(string name, HordeClipKind kind, bool loop = false, Gait gait = Gait.Any,
            HordeClipSide side = HordeClipSide.Any, float fps = 0f, float maxLength = 0f) =>
            new ClipEntry
            {
                Name = name, Kind = kind, Loop = loop, Gait = gait, Side = side, Fps = fps, MaxLength = maxLength,
                Parts = new[] { ClipPrefix + name }, KeepDrift = kind == HordeClipKind.Death,
            };

        [MenuItem("Purge/Dev/Build Monsters")]
        public static void Build()
        {
            try
            {
                var copied = CopyClips();
                CharacterAnimationUtility.ImportClips(ClipPrefix, IsLooping);

                copied += OfficersBuilder.PrepareClips(UacClips());
                OfficersBuilder.PrepareAssets();

                var visuals = new List<HordeVisualDefinition>();
                var officers = new List<(GameObject, MonsterDefinition)>();
                var problems = new List<string>();
                var report = new List<string>();
                foreach (var (prefab, biome, rank) in FindPrefabs())
                {
                    // Officers and elite — Animator (stages 5c, 5e: the Armadillo and the Shamans; the Hook later); bosses — stage 7.
                    if (rank == MonsterRank.Officer || rank == MonsterRank.Elite && OfficersBuilder.IsBuiltElite(prefab.name))
                    {
                        var officer = EnsureDefinition(prefab, biome, rank, out var created);
                        if (created) OfficersBuilder.Seed(officer, prefab.name);
                        officer.Validate(problems);
                        officers.Add((prefab, officer));
                        continue;
                    }

                    if (rank != MonsterRank.Mass) continue;
                    var definition = EnsureDefinition(prefab, biome, rank, out _);
                    definition.Validate(problems);
                    visuals.Add(BakeMass(prefab, definition, report));
                }

                var views = OfficersBuilder.Build(officers, report);

                foreach (var path in StressAssets)
                {
                    var stress = AssetDatabase.LoadAssetAtPath<StressTestDefinition>(path);
                    if (stress == null) continue;
                    // Only the looks of its own monsters: the level's download carries them.
                    var used = visuals.FindAll(visual => Array.Exists(stress.Monsters,
                        spawn => spawn?.Monster != null && spawn.Monster.Id == visual.MonsterId));
                    stress.SetMonsterVisuals(used.ToArray());
                    // The sandbox calls the officers up with its buttons (stage 5c).
                    if (stress.IsSandbox) stress.SetOnDemandMonsters(officers.ConvertAll(officer => officer.Item2).ToArray());
                    var shown = views.FindAll(view => Array.Exists(stress.Monsters,
                                                          spawn => spawn?.Monster != null && spawn.Monster.Id == view.MonsterId) ||
                                                      Array.Exists(stress.OnDemandMonsters,
                                                          monster => monster != null && monster.Id == view.MonsterId));
                    stress.SetMonsterViews(shown.ToArray());
                    EditorUtility.SetDirty(stress);
                }

                AssetDatabase.SaveAssets();
                ContentCatalogBuilder.Build(log: false);
                foreach (var problem in problems)
                    Debug.LogWarning("[Purge] " + problem);
                Debug.Log($"[Purge] Monsters built: {visuals.Count} rank-and-file look(s), {views.Count} officer(s), {copied} clip(s) copied.\n" +
                          string.Join("\n", report));
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building monsters failed: " + e.Message);
                throw;
            }
        }

        private static bool IsLooping(string name)
        {
            foreach (var entry in MassClips)
                if (entry.Loop && Array.IndexOf(entry.Parts, name) >= 0)
                    return true;
            return false;
        }

        /// <summary>Copies the missing "Monster_*" clips from the packs in Import (once; the copies stay in the project).</summary>
        private static int CopyClips()
        {
            var copied = 0;
            Dictionary<string, string> uac = null;
            foreach (var (name, fromUac, source) in ClipTable)
            {
                var target = $"{CharacterAnimationUtility.AnimationsPath}/{name}.fbx";
                if (AssetDatabase.LoadMainAssetAtPath(target) != null) continue;
                string path;
                if (fromUac)
                {
                    uac ??= UacClips();
                    uac.TryGetValue(source, out path);
                }
                else
                {
                    path = FindZombieClip(source);
                }

                if (path == null)
                    throw new InvalidOperationException($"Clip '{name}' is missing and '{source}' is not in Import: put the " +
                                                        (fromUac ? "Ultimate Animation Collection" : "Zombie_Animations") +
                                                        " pack into Import/Animations.");
                if (!AssetDatabase.CopyAsset(path, target))
                    throw new InvalidOperationException($"Could not copy '{path}' to '{target}'.");
                copied++;
            }

            return copied;
        }

        private static string FindZombieClip(string name)
        {
            foreach (var extension in new[] { ".fbx", ".FBX" })
            {
                var path = $"{ZombiePackPath}/{name}{extension}";
                if (File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>The UAC pack's in-place clips by file name (the root-motion "_RM" ones are left out).</summary>
        internal static Dictionary<string, string> UacClips()
        {
            var clips = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!AssetDatabase.IsValidFolder(UacPath)) return clips;
            foreach (var guid in AssetDatabase.FindAssets("Humanoid@ t:Model", new[] { UacPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.EndsWith("_RM", StringComparison.Ordinal) && !clips.ContainsKey(name))
                    clips[name] = path;
            }

            return clips;
        }

        private static List<(GameObject Prefab, MonsterBiome Biome, MonsterRank Rank)> FindPrefabs()
        {
            var result = new List<(GameObject, MonsterBiome, MonsterRank)>();
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { MonsterArtPath }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                // Art/Monster/<Biome>/<Rank>/<name>.prefab
                var parts = path.Substring(MonsterArtPath.Length + 1).Split('/');
                if (parts.Length != 3) continue;
                if (!Enum.TryParse(parts[0], out MonsterBiome biome))
                {
                    Debug.LogWarning($"[Purge] Monster '{path}': unknown biome folder '{parts[0]}' — skipped.");
                    continue;
                }

                if (!TryRank(parts[1], out var rank))
                {
                    Debug.LogWarning($"[Purge] Monster '{path}': unknown rank folder '{parts[1]}' — skipped.");
                    continue;
                }

                result.Add((AssetDatabase.LoadAssetAtPath<GameObject>(path), biome, rank));
            }

            return result;
        }

        private static bool TryRank(string folder, out MonsterRank rank)
        {
            switch (folder)
            {
                case "Mass": rank = MonsterRank.Mass; return true;
                case "Officer": rank = MonsterRank.Officer; return true;
                case "Special": rank = MonsterRank.Elite; return true;
                case "Boss": rank = MonsterRank.Boss; return true;
                default: rank = MonsterRank.Mass; return false;
            }
        }

        private static MonsterDefinition EnsureDefinition(GameObject prefab, MonsterBiome biome, MonsterRank rank, out bool created)
        {
            created = false;
            var folder = $"{DataPath}/{biome}";
            EnsureFolder(folder);
            var path = $"{folder}/{prefab.name}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MonsterDefinition>();
                AssetDatabase.CreateAsset(definition, path); // the id comes from the file name (ContentIdAssigner)
                created = true;
            }

            if (definition.Rank != rank || definition.Biome != biome)
                definition.SetPlace(rank, biome);
            if (definition.Drop == null && rank == MonsterRank.Mass)
                definition.SetDrop(AssetDatabase.LoadAssetAtPath<DropTable>(MassDropPath));
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static HordeVisualDefinition BakeMass(GameObject prefab, MonsterDefinition definition, List<string> report)
        {
            EnsureFolder(VisualsPath);
            var visualPath = $"{VisualsPath}/{prefab.name}.asset";
            var visual = AssetDatabase.LoadAssetAtPath<HordeVisualDefinition>(visualPath);
            if (visual == null)
            {
                visual = ScriptableObject.CreateInstance<HordeVisualDefinition>();
                AssetDatabase.CreateAsset(visual, visualPath);
            }

            // The zombie walks of the packs shuffle at 0.2–0.6 cells/s: every chase is a run (~2.2 c/s) or a sprint (~3.4).
            var gait = definition.Stats.Speed >= SprintSpeed ? Gait.Sprint : Gait.Run;
            var entries = new List<ClipEntry>();
            foreach (var entry in MassClips)
                if (entry.Gait == Gait.Any || entry.Gait == gait)
                    entries.Add(entry);

            var artPath = $"{BakedArtPath}/{prefab.name}";
            EnsureFolder(artPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            AnimationMode.StartAnimationMode();
            try
            {
                var scale = prefab.transform.localScale.x;
                var root = instance.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    throw new InvalidOperationException($"'{prefab.name}' has no Humanoid Animator.");
                var renderer = MainRenderer(instance) ??
                               throw new InvalidOperationException($"'{prefab.name}' has no SkinnedMeshRenderer.");

                var source = renderer.sharedMesh;
                var bones = renderer.bones;
                var bindposes = source.bindposes;
                var map = HordeVisualsBuilder.Weld(source, out var welded);

                // Clips: frames of every entry one after another.
                var clips = new List<HordeClip>();
                var parts = new List<AnimationClip[]>();
                var frameCount = 0;
                foreach (var entry in entries)
                {
                    var sources = Array.ConvertAll(entry.Parts, CharacterAnimationUtility.LoadClip);
                    var length = 0f;
                    for (var p = 0; p < sources.Length; p++)
                        length += PartEnd(entry, sources, p) - PartStart(entry, p);
                    // Parts overlap by the blend at every joint.
                    length -= entry.Blend * (sources.Length - 1);
                    if (entry.MaxLength > 0f) length = Mathf.Min(length, entry.MaxLength);

                    var fps = entry.Fps > 0f ? entry.Fps : FrameRate;
                    var count = entry.Frames > 0 ? entry.Frames
                        : entry.Loop ? Mathf.Max(2, Mathf.RoundToInt(length * fps))
                        : Mathf.Max(2, Mathf.CeilToInt(length * fps) + 1);
                    // Loops wrap (count frames span the length); others end on their last frame.
                    var rate = entry.Loop ? count / length : (count - 1) / length;
                    clips.Add(new HordeClip
                    {
                        Name = entry.Name, Kind = entry.Kind, Side = entry.Side, FirstFrame = frameCount, FrameCount = count,
                        Length = length, Loop = entry.Loop, FrameRate = rate,
                    });
                    parts.Add(sources);
                    frameCount += count;
                }

                var boneWidth = bones.Length * 3;
                var boneTexture = new Color[boneWidth * frameCount];
                var sampled = animator.gameObject;
                var toRoot = root.worldToLocalMatrix;
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var pose = new Matrix4x4[bones.Length];
                var next = new Matrix4x4[bones.Length];
                for (var c = 0; c < clips.Count; c++)
                {
                    var info = clips[c];
                    var entry = entries[c];
                    var sources = parts[c];
                    var start = Vector3.zero;
                    for (var f = 0; f < info.FrameCount; f++)
                    {
                        // Time along the parts played one after another (each in its window, overlapping by the
                        // blend) → the part, the time in it, and how far into the next one it blends.
                        var time = Mathf.Min(info.Length, f / info.FrameRate);
                        var p = 0;
                        var blend = 0f;
                        for (; p < sources.Length; p++)
                        {
                            var span = PartEnd(entry, sources, p) - PartStart(entry, p);
                            if (time <= span || p == sources.Length - 1)
                            {
                                if (p < sources.Length - 1 && entry.Blend > 0f && time > span - entry.Blend)
                                    blend = (time - (span - entry.Blend)) / entry.Blend;
                                break;
                            }

                            time -= span - entry.Blend;
                        }

                        Sample(sources[p], PartStart(entry, p) + time, pose, f == 0);
                        if (blend > 0f)
                        {
                            Sample(sources[p + 1], PartStart(entry, p + 1) + (time - (PartEnd(entry, sources, p) - PartStart(entry, p) - entry.Blend)),
                                next, false);
                            var w = Mathf.SmoothStep(0f, 1f, blend);
                            for (var b = 0; b < bones.Length; b++)
                                pose[b] = Lerp(pose[b], next[b], w);
                        }

                        var row = info.FirstFrame + f;
                        for (var b = 0; b < bones.Length; b++)
                        {
                            var m = pose[b];
                            var offset = row * boneWidth + b * 3;
                            boneTexture[offset] = new Color(m.m00, m.m01, m.m02, m.m03);
                            boneTexture[offset + 1] = new Color(m.m10, m.m11, m.m12, m.m13);
                            boneTexture[offset + 2] = new Color(m.m20, m.m21, m.m22, m.m23);
                        }
                    }

                    void Sample(AnimationClip clip, float at, Matrix4x4[] into, bool first)
                    {
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(sampled, clip, Mathf.Clamp(at, 0f, clip.length));
                        AnimationMode.EndSampling();
                        // In place: the gameplay moves the monster. Clips with root motion baked into the pose
                        // (attack lunges ~1.4 m, hit reactions step back, the crawl out of the ground) would carry
                        // the body past the player and snap back on every repeat — their hips' ground drift from
                        // the clip's first frame is taken out. Deaths keep it (the body falls away).
                        var hipsAt = root.InverseTransformPoint(hips.position);
                        if (first) start = hipsAt;
                        var drift = entry.KeepDrift ? Vector3.zero : new Vector3(hipsAt.x - start.x, 0f, hipsAt.z - start.z);
                        var inPlace = Matrix4x4.Translate(-drift) * toRoot;
                        for (var b = 0; b < bones.Length; b++)
                            into[b] = inPlace * bones[b].localToWorldMatrix * bindposes[b];
                    }
                }

                // Ground speed of the gait's run (scale of the level included), cells per second.
                var speed = 0f;
                var gaits = 0;
                foreach (var entry in entries)
                {
                    if (entry.Kind != HordeClipKind.Run) continue;
                    speed += MeasureGroundSpeed(animator, CharacterAnimationUtility.LoadClip(entry.Parts[0])) * scale;
                    gaits++;
                }

                if (gaits > 0) speed /= gaits;

                var boneAsset = HordeVisualsBuilder.SaveTexture($"{artPath}/{prefab.name}_Bones.asset", boneWidth, frameCount, boneTexture);
                var equipment = GlueEquipment(visual.Equipment, animator, renderer, bindposes, out var equipmentMaterials);
                var mesh = SaveMesh($"{artPath}/{prefab.name}_Mesh.asset", source, map, welded, equipment);
                var body = SaveMaterial($"{artPath}/{prefab.name}.mat", renderer.sharedMaterial, boneAsset);
                var extras = new Material[equipmentMaterials.Count];
                for (var i = 0; i < extras.Length; i++)
                    extras[i] = SaveMaterial($"{artPath}/{prefab.name}_Equipment_{i + 1}.mat", equipmentMaterials[i], boneAsset);

                visual.Configure(mesh, new[] { body }, null, boneAsset, bones.Length, FrameRate, clips.ToArray(), scale,
                    source.vertexCount);
                visual.ConfigureMonster(definition.Id, extras, speed, visual.SplashColor);
                EditorUtility.SetDirty(visual);

                var height = source.bounds.size.y * scale;
                report.Add($"  {prefab.name} ({definition.Id}): {gait}, {source.vertexCount} → {mesh.vertexCount} vertices, " +
                           $"{mesh.triangles.Length / 3} triangles, {clips.Count} clips / {frameCount} frames, bones " +
                           $"{boneWidth}x{frameCount} ({boneWidth * frameCount * 8 / 1024} KB), ground speed {speed:0.00} c/s, " +
                           $"height {height:0.00} m, equipment {equipment.Count}.");
                return visual;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The active skinned mesh with the most vertices (the body; capes and hair of other ranks aside).</summary>
        private static SkinnedMeshRenderer MainRenderer(GameObject instance)
        {
            SkinnedMeshRenderer best = null;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.sharedMesh != null && (best == null || renderer.sharedMesh.vertexCount > best.sharedMesh.vertexCount))
                    best = renderer;
            return best;
        }

        /// <summary>
        /// Ground speed of an in-place clip on the sampled model, m/s at scale 1: a foot moves backward only while it
        /// stands on the ground (as <see cref="CharacterAnimationUtility.MeasureGroundSpeed"/>).
        /// </summary>
        private static float MeasureGroundSpeed(Animator animator, AnimationClip clip)
        {
            const int samples = 60;
            var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            var previous = new float[feet.Length];
            var dt = clip.length / samples;
            float travel = 0f, time = 0f;
            for (var i = 0; i <= samples; i++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, i * dt);
                AnimationMode.EndSampling();
                for (var f = 0; f < feet.Length; f++)
                {
                    var z = feet[f].position.z;
                    if (i > 0 && z < previous[f])
                    {
                        travel += previous[f] - z;
                        time += dt;
                    }

                    previous[f] = z;
                }
            }

            return time > 0f ? travel / time : 0f;
        }

        /// <summary>A glued model's triangles: vertices in the body mesh's bind space, all on one bone.</summary>
        private sealed class GluedPart
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
            public int Bone;
        }

        /// <summary>
        /// The equipment in bind pose: model space → <see cref="EquipmentModel.Offset"/> → the bone's bind space →
        /// mesh space (bindpose⁻¹). A part per material and bone (each becomes a submesh drawn with its material).
        /// </summary>
        private static List<GluedPart> GlueEquipment(HordeEquipment[] equipment, Animator animator, SkinnedMeshRenderer body,
            Matrix4x4[] bindposes, out List<Material> materials)
        {
            materials = new List<Material>();
            var parts = new List<GluedPart>();
            foreach (var item in equipment)
            {
                if (item?.Model == null) continue;
                var bone = animator.GetBoneTransform(item.Bone);
                var index = bone != null ? Array.IndexOf(body.bones, bone) : -1;
                if (index < 0)
                {
                    Debug.LogWarning($"[Purge] Equipment '{item.Model.name}': the body has no bone {item.Bone} — skipped.");
                    continue;
                }

                var model = EnsureEquipmentModel(item.Model);
                var toMesh = bindposes[index].inverse * model.Offset;
                foreach (var filter in item.Model.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = filter.sharedMesh;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (mesh == null || renderer == null) continue;
                    var local = toMesh * (item.Model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                    var vertices = mesh.vertices;
                    var uvs = mesh.uv;
                    for (var s = 0; s < mesh.subMeshCount; s++)
                    {
                        var material = s < renderer.sharedMaterials.Length ? renderer.sharedMaterials[s] : renderer.sharedMaterial;
                        var slot = -1;
                        for (var p = 0; p < parts.Count && slot < 0; p++)
                            if (materials[p] == material && parts[p].Bone == index)
                                slot = p;
                        if (slot < 0)
                        {
                            slot = parts.Count;
                            materials.Add(material);
                            parts.Add(new GluedPart { Bone = index });
                        }

                        var part = parts[slot];
                        var remap = new Dictionary<int, int>();
                        foreach (var v in mesh.GetTriangles(s))
                        {
                            if (!remap.TryGetValue(v, out var at))
                            {
                                at = part.Positions.Count;
                                remap.Add(v, at);
                                part.Positions.Add(local.MultiplyPoint3x4(vertices[v]));
                                part.Uvs.Add(uvs.Length > v ? uvs[v] : Vector2.zero);
                            }

                            part.Triangles.Add(at);
                        }

                    }
                }
            }

            return parts;
        }

        /// <summary>The model's <see cref="EquipmentModel"/> (added to the prefab with zero offsets when missing).</summary>
        private static EquipmentModel EnsureEquipmentModel(GameObject model)
        {
            var component = model.GetComponent<EquipmentModel>();
            if (component != null) return component;
            var path = AssetDatabase.GetAssetPath(model);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.AddComponent<EquipmentModel>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EquipmentModel>();
        }

        private static Mesh SaveMesh(string path, Mesh source, int[] map, List<int> welded, List<GluedPart> equipment)
        {
            var vertices = source.vertices;
            var uvs = source.uv;
            var weights = source.boneWeights;
            var positions = new List<Vector3>(welded.Count);
            var uv0 = new List<Vector2>(welded.Count);
            var uv1 = new List<Vector2>(welded.Count);
            var uv2 = new List<Vector4>(welded.Count);
            for (var v = 0; v < welded.Count; v++)
            {
                var original = welded[v];
                positions.Add(vertices[original]);
                uv0.Add(uvs[original]);
                uv1.Add(new Vector2(v, 0f));
                uv2.Add(HordeVisualsBuilder.TopTwoBones(weights[original]));
            }

            var submeshes = new List<List<int>> { new List<int>() };
            for (var s = 0; s < source.subMeshCount; s++)
                foreach (var index in source.GetTriangles(s))
                    submeshes[0].Add(map[index]);

            foreach (var part in equipment)
            {
                var first = positions.Count;
                for (var v = 0; v < part.Positions.Count; v++)
                {
                    positions.Add(part.Positions[v]);
                    uv0.Add(part.Uvs[v]);
                    uv1.Add(new Vector2(positions.Count - 1, 0f));
                    uv2.Add(new Vector4(part.Bone, part.Bone, 1f, 0f));
                }

                var triangles = new List<int>(part.Triangles.Count);
                foreach (var index in part.Triangles)
                    triangles.Add(first + index);
                submeshes.Add(triangles);
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = positions.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.subMeshCount = submeshes.Count;
            for (var s = 0; s < submeshes.Count; s++)
                mesh.SetTriangles(submeshes[s], s);
            // Animated: generous bounds around the standing model (instanced draws use their own bounds anyway).
            mesh.bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(3f, 2.5f, 3f));
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material SaveMaterial(string path, Material source, Texture2D bones)
        {
            var shader = Shader.Find("Purge/Horde") ?? throw new InvalidOperationException("Shader Purge/Horde not found.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            var texture = source != null && source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            if (texture == null && source != null && source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", source != null && source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            material.SetTexture("_BoneTex", bones);
            material.SetTexture("_VatTex", null);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
