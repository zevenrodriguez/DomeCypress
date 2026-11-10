using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Cypress > Audio > Add Swamp Audio (open scenes)
/// Adds a "Swamp Audio" object to every open scene with the ambience loop and the six flapping sounds
/// from Assets/Audio, then saves. Run it with the title scene open (and the cypress dome too if you like:
/// the sound carries over from one scene to the next either way).
/// </summary>
static class SwampAudioSetup
{
    const string Folder = "Assets/Audio";

    [MenuItem("Tools/Cypress/Audio/Add Swamp Audio (open scenes)")]
    static void Add()
    {
        var ambience = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Folder}/Ambience Loop 1 (Night).wav");
        var flaps = AssetDatabase.FindAssets("Flapping Sound t:AudioClip", new[] { Folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .Where(c => c != null)
            .ToArray();
        if (ambience == null) Debug.LogWarning($"Cypress: no ambience loop found in {Folder}.");

        // long ambience: stream it instead of loading the whole file into memory
        if (AssetImporter.GetAtPath($"{Folder}/Ambience Loop 1 (Night).wav") is AudioImporter ai)
        {
            var settings = ai.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.Streaming)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                ai.defaultSampleSettings = settings;
                ai.SaveAndReimport();
            }
        }

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            var audio = scene.GetRootGameObjects().Select(g => g.GetComponent<SwampAudio>()).FirstOrDefault(a => a != null);
            if (audio == null)
            {
                var go = new GameObject("Swamp Audio");
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Add Swamp Audio");
                audio = go.AddComponent<SwampAudio>();
            }
            Undo.RecordObject(audio, "Add Swamp Audio");
            audio.ambience = ambience;
            audio.flapSounds = flaps;
            EditorUtility.SetDirty(audio);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log($"Cypress: Swamp Audio added - ambience '{(ambience != null ? ambience.name : "none")}', {flaps.Length} flapping sounds.");
    }
}
