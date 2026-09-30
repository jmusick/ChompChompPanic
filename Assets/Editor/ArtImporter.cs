using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace ChompChompPanic.Editor
{
    /// <summary>
    /// Brings the procedurally generated art (ArtSource/*/build_*.py) into the game:
    /// 1. Slices every strip listed in an Assets/Art/*/sprite_frames.json into frames of the listed size
    ///    (point filtered, uncompressed, 64 px per unit), keeping sprite IDs stable across re-imports.
    /// 2. Fills the scene's GameManager: each prey type's and the rivals' sprite variants from files named
    ///    &lt;prefix&gt;[_&lt;variant&gt;]_&lt;clip&gt;.png, weapon projectiles and weapon effects by file name.
    /// Clips: idle, run/walk/side/fly (walk), up, down, shoot (attack), chomp, death.
    /// </summary>
    static class ArtImporter
    {
        const string ArtRoot = "Assets/Art";
        const int PixelsPerUnit = 64;

        static readonly Regex ManifestEntry = new("\"([^\"]+)\"\\s*:\\s*\\[\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\]");

        [MenuItem("Chomp Chomp Panic/Import Art")]
        static void ImportArt()
        {
            int sliced = SliceAll();
            AssignSprites();
            Debug.Log($"Import Art: sliced {sliced} sprite sheets and assigned sprites to the GameManager.");
        }

        // ------------------------------------------------------------------ slicing

        static int SliceAll()
        {
            int count = 0;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            foreach (var manifest in Directory.GetFiles(ArtRoot, "sprite_frames.json", SearchOption.AllDirectories))
            {
                string folder = Path.GetDirectoryName(manifest)!.Replace('\\', '/');
                foreach (Match m in ManifestEntry.Matches(File.ReadAllText(manifest)))
                {
                    string path = $"{folder}/{m.Groups[1].Value}";
                    if (Slice(factory, path, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)))
                        count++;
                }
            }
            return count;
        }

        /// <summary>Slice one strip into frames of width x height. Returns false when it's already set up.</summary>
        static bool Slice(SpriteDataProviderFactories factory, string path, int width, int height)
        {
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (importer == null || texture == null)
            {
                Debug.LogWarning($"Import Art: {path} is listed but missing.");
                return false;
            }

            string baseName = Path.GetFileNameWithoutExtension(path);
            int frames = texture.width / width;
            var wanted = Enumerable.Range(0, frames).Select(i => new Rect(i * width, 0, width, height)).ToArray();

            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects();
            bool upToDate = importer.spriteImportMode == SpriteImportMode.Multiple
                && Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)
                && importer.filterMode == FilterMode.Point
                && existing.Length == frames
                && existing.Select(r => r.rect).SequenceEqual(wanted);
            if (upToDate)
                return false;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            // Reuse IDs by name so scene references survive a re-slice.
            var ids = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = wanted.Select((rect, i) =>
            {
                string name = $"{baseName}_{i}";
                return new SpriteRect
                {
                    name = name,
                    rect = rect,
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = ids.TryGetValue(name, out var id) ? id : GUID.Generate(),
                };
            }).ToArray();
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(
                rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            provider.Apply();
            importer.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------ assigning

        static void AssignSprites()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();
            if (manager == null)
            {
                Debug.LogWarning("Import Art: no GameManager in the open scene; sprites not assigned.");
                return;
            }

            var so = new SerializedObject(manager);
            var prey = so.FindProperty("preyTypes");
            for (int i = 0; i < prey.arraySize; i++)
            {
                var type = prey.GetArrayElementAtIndex(i);
                AssignVariants(type.FindPropertyRelative("Variants"), type.FindPropertyRelative("SpritePrefix").stringValue);
                var weapon = type.FindPropertyRelative("Weapon");
                string projectile = weapon.FindPropertyRelative("ProjectileArt").stringValue;
                if (!string.IsNullOrEmpty(projectile))
                    SetSprites(weapon.FindPropertyRelative("Projectile"), SpritesNamed(projectile));
            }
            var rivals = so.FindProperty("rivals");
            AssignVariants(rivals.FindPropertyRelative("Variants"), rivals.FindPropertyRelative("SpritePrefix").stringValue);
            SetSprites(so.FindProperty("muzzleFlash"), SpritesNamed("fx_muzzle"));
            SetSprites(so.FindProperty("explosion"), SpritesNamed("fx_explosion"));
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
        }

        static void AssignVariants(SerializedProperty variants, string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return;

            // variant -> clip -> frames
            var found = new SortedDictionary<string, Dictionary<string, Sprite[]>>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith(prefix + "_"))
                    continue;
                string rest = name.Substring(prefix.Length + 1);
                int split = rest.LastIndexOf('_');
                string variant = split < 0 ? "" : rest.Substring(0, split);
                string clip = split < 0 ? rest : rest.Substring(split + 1);
                if (!found.TryGetValue(variant, out var clips))
                    found[variant] = clips = new Dictionary<string, Sprite[]>();
                clips[clip] = LoadSprites(path);
            }
            if (found.Count == 0)
            {
                Debug.LogWarning($"Import Art: no sprites found for prefix '{prefix}'.");
                return;
            }

            variants.arraySize = found.Count;
            int index = 0;
            foreach (var clips in found.Values)
            {
                var entry = variants.GetArrayElementAtIndex(index++);
                Sprite[] Clip(params string[] names) =>
                    names.Select(n => clips.TryGetValue(n, out var s) ? s : null).FirstOrDefault(s => s != null) ?? new Sprite[0];
                var walk = Clip("run", "walk", "side", "fly");
                // Fliers are rotated to their heading rather than walked, so the fly loop is also their idle.
                var idle = Clip("idle", "fly");
                if (idle.Length == 0 && walk.Length > 0)
                    idle = new[] { walk[0] };
                SetSprites(entry.FindPropertyRelative("Idle"), idle);
                SetSprites(entry.FindPropertyRelative("Walk"), walk);
                SetSprites(entry.FindPropertyRelative("WalkUp"), Clip("up"));
                SetSprites(entry.FindPropertyRelative("WalkDown"), Clip("down"));
                SetSprites(entry.FindPropertyRelative("Attack"), Clip("shoot"));
                SetSprites(entry.FindPropertyRelative("Chomp"), Clip("chomp"));
                SetSprites(entry.FindPropertyRelative("Death"), Clip("death"));
                // Short idle loops (breathing) play slowly; a fly loop is the afterburner flicker.
                entry.FindPropertyRelative("IdleFps").floatValue = idle.Length <= 2 ? 3f : clips.ContainsKey("fly") ? 12f : 8f;
                entry.FindPropertyRelative("WalkFps").floatValue = 12f;
                entry.FindPropertyRelative("AttackFps").floatValue = 14f;
                entry.FindPropertyRelative("ChompFps").floatValue = 14f;
                entry.FindPropertyRelative("DeathFps").floatValue = 10f;
            }
        }

        static Sprite[] SpritesNamed(string fileName)
        {
            var guid = AssetDatabase.FindAssets($"{fileName} t:Texture2D", new[] { ArtRoot })
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == fileName);
            if (guid == null)
            {
                Debug.LogWarning($"Import Art: no sprite sheet named '{fileName}'.");
                return new Sprite[0];
            }
            return LoadSprites(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>A sheet's frames, left to right.</summary>
        static Sprite[] LoadSprites(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.rect.x).ToArray();

        static void SetSprites(SerializedProperty property, Sprite[] sprites)
        {
            property.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }
    }
}
