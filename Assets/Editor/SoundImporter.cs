using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChompChompPanic.Editor
{
    /// <summary>
    /// Brings the procedurally generated sound effects (ArtSource/Audio/build_sfx.py) and the music into the game:
    /// 1. Sets every sfx_*.wav in Assets/Audio to mono, decompressed on load (they're all short one-shots),
    ///    and every music_*.wav to stereo Vorbis, streamed from disk (they're minutes long).
    /// 2. Assigns sfx_&lt;name&gt;.wav to the &lt;name&gt;Sound field (snake_case to camelCase) of the
    ///    GameManager and TitleScreen in every scene of the build profile, saving the scenes it changes.
    ///    Numbered variants, sfx_&lt;name&gt;_&lt;n&gt;.wav, fill the &lt;name&gt;Sounds array in order.
    ///    Music tracks, music_&lt;map&gt;_&lt;n&gt;.wav, fill the &lt;map&gt;Music array in order.
    /// </summary>
    static class SoundImporter
    {
        const string AudioRoot = "Assets/Audio";
        const string Prefix = "sfx_";
        const string MusicPrefix = "music_";

        static readonly Regex Variant = new(@"^(.+)_(\d+)$");

        [MenuItem("Chomp Chomp Panic/Import Audio")]
        static void ImportAudio()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // field -> clips; a single sound is a one-clip list.
            var clips = new Dictionary<string, List<(int Order, AudioClip Clip)>>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                string field;
                int order;
                if (name.StartsWith(Prefix))
                {
                    Configure(path);
                    name = name.Substring(Prefix.Length);
                    var variant = Variant.Match(name);
                    field = variant.Success ? FieldName(variant.Groups[1].Value, "Sound") + "s" : FieldName(name, "Sound");
                    order = variant.Success ? int.Parse(variant.Groups[2].Value) : 0;
                }
                else if (name.StartsWith(MusicPrefix))
                {
                    ConfigureMusic(path);
                    name = name.Substring(MusicPrefix.Length);
                    var variant = Variant.Match(name);
                    field = FieldName(variant.Success ? variant.Groups[1].Value : name, "Music");
                    order = variant.Success ? int.Parse(variant.Groups[2].Value) : 0;
                }
                else
                    continue;
                if (!clips.TryGetValue(field, out var list))
                    clips[field] = list = new List<(int, AudioClip)>();
                list.Add((order, AssetDatabase.LoadAssetAtPath<AudioClip>(path)));
            }
            if (clips.Count == 0)
            {
                Debug.LogWarning($"Import Audio: no {Prefix}*.wav or {MusicPrefix}*.wav files in {AudioRoot}. Run ArtSource/Audio/build_sfx.py first.");
                return;
            }

            var assigned = new HashSet<string>();
            foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.GetSceneByPath(buildScene.path);
                bool wasOpen = scene.isLoaded;
                if (!wasOpen)
                    scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);

                bool changed = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (component is not (GameManager or TitleScreen))
                            continue;
                        var so = new SerializedObject(component);
                        foreach (var (field, list) in clips)
                        {
                            var property = so.FindProperty(field);
                            if (property == null)
                                continue;
                            var ordered = list.OrderBy(c => c.Order).Select(c => c.Clip).ToArray();
                            if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
                            {
                                property.arraySize = ordered.Length;
                                for (int i = 0; i < ordered.Length; i++)
                                    property.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
                            }
                            else if (property.propertyType == SerializedPropertyType.ObjectReference)
                                property.objectReferenceValue = ordered[0];
                            else
                                continue;
                            assigned.Add(field);
                        }
                        changed |= so.ApplyModifiedProperties();
                    }
                }

                if (changed)
                    EditorSceneManager.SaveScene(scene);
                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }

            foreach (var field in clips.Keys.Where(f => !assigned.Contains(f)))
                Debug.LogWarning($"Import Audio: no GameManager or TitleScreen has a '{field}' field.");
            Debug.Log($"Import Audio: assigned {assigned.Count} of {clips.Count} sounds.");
        }

        /// <summary>Short one-shots: mono, uncompressed in memory, so playing them costs nothing.</summary>
        static void Configure(string path)
        {
            if (AssetImporter.GetAtPath(path) is not UnityEditor.AudioImporter importer)
                return;
            var settings = importer.defaultSampleSettings;
            if (importer.forceToMono && settings.loadType == AudioClipLoadType.DecompressOnLoad
                && settings.compressionFormat == AudioCompressionFormat.ADPCM)
                return;
            importer.forceToMono = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        /// <summary>Long music tracks: stereo, Vorbis-compressed and streamed, so they don't sit in memory.</summary>
        static void ConfigureMusic(string path)
        {
            if (AssetImporter.GetAtPath(path) is not UnityEditor.AudioImporter importer)
                return;
            var settings = importer.defaultSampleSettings;
            if (!importer.forceToMono && settings.loadType == AudioClipLoadType.Streaming
                && settings.compressionFormat == AudioCompressionFormat.Vorbis)
                return;
            importer.forceToMono = false;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        /// <summary>("menu_move", "Sound") -> "menuMoveSound".</summary>
        static string FieldName(string snake, string suffix)
        {
            var parts = snake.Split('_');
            return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1))) + suffix;
        }
    }
}
