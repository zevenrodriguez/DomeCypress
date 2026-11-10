using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Cypress > Look > Create Dusk Variant (CypressDomeDusk)
/// Makes a copy of the CypressDome scene called CypressDomeDusk - same swamp, same Space-bar orchid and
/// moth interaction - and gives the copy a nighttime-dusk look:
///   - the sun just under the horizon, a faint rose glow low in the sky, deep blue overhead
///   - dim, cool ambient light, darker blue-violet haze and mist
///   - a cooler, darker colour grade
///   - ghost orchids that glow softly, with a small light that lights the bark around each flower
/// The look uses its own sky, mist and colour-grade assets ("...Dusk"), so the original scene is untouched.
/// Running it again rebuilds the dusk scene from the current CypressDome.
/// </summary>
static class CypressDuskVariant
{
    const string SourceScene = CypressAssets.ScenePath;                       // Assets/Scenes/CypressDome.unity
    const string DuskScene = "Assets/Scenes/CypressDomeDusk.unity";
    const string DuskSky = CypressAssets.MaterialFolder + "/Cypress_SkyDusk.mat";
    const string DuskMist = CypressAssets.MaterialFolder + "/Cypress_MistDusk.mat";
    const string DuskProfile = CypressAssets.MaterialFolder + "/CypressDusk_Profile.asset";
    const string DuskReflection = CypressAssets.MaterialFolder + "/CypressReflectionDusk.exr";

    static Color Lin(float r, float g, float b) => new Color(r, g, b).gamma;

    [MenuItem("Tools/Cypress/Look/Create Dusk Variant (CypressDomeDusk)")]
    static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
        {
            Debug.LogError($"Cypress: {SourceScene} not found.");
            return;
        }

        // 1. copy the scene and open the copy
        AssetDatabase.DeleteAsset(DuskScene);
        AssetDatabase.CopyAsset(SourceScene, DuskScene);
        var scene = EditorSceneManager.OpenScene(DuskScene, OpenSceneMode.Single);

        // 2. sun just below the horizon: very little direct light, warm rose colour
        var sun = scene.GetRootGameObjects().Select(g => g.GetComponent<Light>())
                       .FirstOrDefault(l => l != null && l.type == LightType.Directional);
        if (sun != null)
        {
            var toSun = new Vector3(-0.35f, Mathf.Sin(3f * Mathf.Deg2Rad), -0.92f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-toSun);
            sun.useColorTemperature = false;
            sun.color = Lin(1.0f, 0.48f, 0.36f);
            sun.intensity = 0.2f;
            sun.shadowStrength = 0.4f;
            RenderSettings.sun = sun;
        }

        // 3. dusk sky (its own material, copied from the scene's current sky)
        var sky = CopyMaterial(RenderSettings.skybox, DuskSky, "Cypress/CloudySky");
        if (sky != null)
        {
            sky.SetColor("_ZenithColor", Lin(0.035f, 0.045f, 0.11f));
            sky.SetColor("_HorizonColor", Lin(0.50f, 0.28f, 0.32f));
            sky.SetColor("_GroundColor", Lin(0.05f, 0.05f, 0.07f));
            sky.SetFloat("_HorizonSharpness", 2.8f);
            sky.SetColor("_SunColor", Lin(1.0f, 0.45f, 0.32f));
            sky.SetFloat("_SunGlow", 0.9f);
            sky.SetFloat("_SunGlowPower", 7f);
            sky.SetFloat("_CloudCoverage", 0.55f);
            sky.SetColor("_CloudLit", Lin(0.32f, 0.22f, 0.28f));
            sky.SetColor("_CloudShadow", Lin(0.05f, 0.06f, 0.11f));
            sky.SetColor("_CloudSunTint", Lin(0.95f, 0.42f, 0.34f));
            sky.SetFloat("_Exposure", 0.7f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        // 4. dim, cool ambient and blue-violet haze
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 0.45f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.03f;
        RenderSettings.fogColor = Lin(0.11f, 0.10f, 0.16f);
        DynamicGI.UpdateEnvironment();

        // 5. darker, cooler colour grade (its own profile, copied from the scene's)
        var volume = scene.GetRootGameObjects().Select(g => g.GetComponent<Volume>()).FirstOrDefault(v => v != null);
        if (volume != null && volume.sharedProfile != null)
        {
            AssetDatabase.DeleteAsset(DuskProfile);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile), DuskProfile);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DuskProfile);
            volume.sharedProfile = profile;
            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(-0.85f);
            color.contrast.Override(6f);
            color.saturation.Override(-12f);
            color.colorFilter.Override(new Color(0.86f, 0.89f, 1.0f));
            var wb = Get<WhiteBalance>(profile);
            wb.temperature.Override(-14f);
            wb.tint.Override(6f);
            var lgg = Get<LiftGammaGain>(profile);
            lgg.lift.Override(new Vector4(0.95f, 0.97f, 1.07f, -0.04f));
            lgg.gain.Override(new Vector4(1.0f, 0.98f, 1.0f, 0f));
            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(0.7f);
            bloom.intensity.Override(0.8f);
            bloom.tint.Override(new Color(0.88f, 0.95f, 1.0f));
            var vig = Get<Vignette>(profile);
            vig.intensity.Override(0.32f);
            EditorUtility.SetDirty(profile);
        }

        // 6. mist: its own darker blue material on the mist layers
        var mistRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Ground Mist");
        if (mistRoot != null)
        {
            var renderers = mistRoot.GetComponentsInChildren<MeshRenderer>(true);
            var source = renderers.Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            var mist = CopyMaterial(source, DuskMist, "Cypress/Mist");
            if (mist != null)
            {
                mist.SetColor("_MistColor", Lin(0.22f, 0.22f, 0.31f));
                mist.SetFloat("_SunScatter", 0.6f);
                EditorUtility.SetDirty(mist);
                foreach (var r in renderers) r.sharedMaterial = mist;
            }
        }

        // 7. glowing ghost orchids
        var orchids = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<OrchidMothExperience>(true))
                           .FirstOrDefault(o => o != null);
        if (orchids != null)
        {
            orchids.orchidGlow = new Color(0.38f, 0.42f, 0.36f);   // brighter emission
            orchids.orchidLightIntensity = 1.6f;
            orchids.orchidLightRange = 2.5f;
            orchids.orchidLightColor = new Color(0.82f, 0.95f, 0.85f);
            EditorUtility.SetDirty(orchids);
        }

        // 8. water reflections of the dusk sky
        var probe = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ReflectionProbe>(true)).FirstOrDefault();
        if (probe != null && Lightmapping.BakeReflectionProbe(probe, DuskReflection))
        {
            AssetDatabase.ImportAsset(DuskReflection, ImportAssetOptions.ForceUpdate);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(DuskReflection);
        }

        // 9. save, and add it to the build's scene list (after CypressDome)
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        var list = EditorBuildSettings.scenes.ToList();
        if (!list.Any(s => s.path == DuskScene))
        {
            list.Add(new EditorBuildSettingsScene(DuskScene, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
        Debug.Log("Cypress: created CypressDomeDusk (dusk sky, haze, mist, grade and glowing orchids) and added it to the build list. " +
                  "To play it after the title, set Next Scene on 'Orchid Title Ending' (title scene) to CypressDomeDusk.");
    }

    /// <summary>
    /// One step darker for the open dusk scene, keeping your other tweaks: exposure -0.25, ambient -15%,
    /// sky -10%. Run it again for darker still; Lighter goes the other way.
    /// </summary>
    [MenuItem("Tools/Cypress/Look/Dusk: Darker")]
    static void Darker() => Step(-1);

    [MenuItem("Tools/Cypress/Look/Dusk: Lighter")]
    static void Lighter() => Step(+1);

    static void Step(int dir)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != DuskScene) { Debug.LogWarning("Cypress: open CypressDomeDusk first."); return; }
        var volume = scene.GetRootGameObjects().Select(g => g.GetComponent<Volume>()).FirstOrDefault(v => v != null);
        if (volume != null && volume.sharedProfile != null)
        {
            var color = Get<ColorAdjustments>(volume.sharedProfile);
            color.postExposure.Override(color.postExposure.value + 0.25f * dir);
            EditorUtility.SetDirty(volume.sharedProfile);
        }
        RenderSettings.ambientIntensity *= dir < 0 ? 0.85f : 1f / 0.85f;
        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            var sky = RenderSettings.skybox;
            sky.SetFloat("_Exposure", sky.GetFloat("_Exposure") * (dir < 0 ? 0.9f : 1f / 0.9f));
            EditorUtility.SetDirty(sky);
        }
        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Cypress: dusk one step {(dir < 0 ? "darker" : "lighter")} - ambient {RenderSettings.ambientIntensity:0.00}.");
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>A copy of `source` saved at `path` (or a new material with `shaderName` if there's no source).</summary>
    static Material CopyMaterial(Material source, string path, string shaderName)
    {
        AssetDatabase.DeleteAsset(path);
        Material mat;
        if (source != null) mat = new Material(source);
        else
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogError($"Cypress: shader '{shaderName}' not found."); return null; }
            mat = new Material(shader);
        }
        mat.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) { component.active = true; return component; }
        component = profile.Add<T>(true);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }
}
