using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click looks for the cypress dome. These only run when you pick them from the menu, so the
/// "keep my tweaks" rule of the scene builder still holds; afterwards every value is yours to adjust.
/// Menu: Tools > Cypress > Look > ...
/// </summary>
static class CypressLookPresets
{
    const string MorningSky = CypressAssets.MaterialFolder + "/Cypress_SkyMorning.mat";
    const string MistMaterial = CypressAssets.MaterialFolder + "/Cypress_Mist.mat";
    const string MistRoot = "Ground Mist";

    static Color Lin(float r, float g, float b, float a = 1f) => new Color(r, g, b, a).gamma;

    [MenuItem("Tools/Cypress/Look/Early Morning Mist (dusky, cloudy)")]
    static void EarlyMorning()
    {
        var scene = OpenCypressScene();
        if (!scene.IsValid()) return;

        // --- low, warm sun rising behind the far side of the dome (backlights the mist and Spanish moss)
        var sun = FindOrCreateSun(scene);
        var toSun = new Vector3(-0.35f, Mathf.Sin(9f * Mathf.Deg2Rad), -0.92f).normalized;
        sun.transform.rotation = Quaternion.LookRotation(-toSun);
        sun.useColorTemperature = false;
        sun.color = Lin(1.0f, 0.62f, 0.40f);
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.55f;          // thin cloud: soft, weak shadows
        RenderSettings.sun = sun;

        // --- cloudy dawn sky
        var sky = GetOrCreateMaterial(MorningSky, "Cypress/CloudySky");
        if (sky != null)
        {
            sky.SetColor("_ZenithColor", Lin(0.16f, 0.20f, 0.30f));
            sky.SetColor("_HorizonColor", Lin(0.80f, 0.62f, 0.52f));
            sky.SetColor("_GroundColor", Lin(0.25f, 0.25f, 0.24f));
            sky.SetFloat("_HorizonSharpness", 2.2f);
            sky.SetColor("_SunColor", Lin(1.0f, 0.75f, 0.55f));
            sky.SetFloat("_SunGlow", 1.4f);
            sky.SetFloat("_SunGlowPower", 10f);
            sky.SetFloat("_CloudCoverage", 0.68f);
            sky.SetFloat("_CloudSoftness", 0.2f);
            sky.SetFloat("_CloudScale", 1.3f);
            sky.SetColor("_CloudLit", Lin(0.62f, 0.58f, 0.58f));
            sky.SetColor("_CloudShadow", Lin(0.22f, 0.24f, 0.30f));
            sky.SetColor("_CloudSunTint", Lin(1.0f, 0.60f, 0.40f));
            sky.SetFloat("_Exposure", 1.0f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        // --- ambient + distance haze (built-in fog: costs almost nothing, every Cypress shader supports it)
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.022f;
        RenderSettings.fogColor = Lin(0.52f, 0.50f, 0.50f);
        DynamicGI.UpdateEnvironment();

        // --- soft, muted grade
        var profile = FindOrCreateProfile(scene);
        if (profile != null)
        {
            var tone = Get<Tonemapping>(profile); tone.mode.Override(TonemappingMode.Neutral);
            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0.1f);
            color.contrast.Override(-8f);
            color.saturation.Override(-18f);
            color.colorFilter.Override(new Color(1.0f, 0.97f, 0.93f));
            var wb = Get<WhiteBalance>(profile);
            wb.temperature.Override(6f);
            wb.tint.Override(4f);
            var lgg = Get<LiftGammaGain>(profile);
            lgg.lift.Override(new Vector4(0.97f, 0.99f, 1.04f, -0.02f));   // cool, lifted shadows
            lgg.gain.Override(new Vector4(1.03f, 1.0f, 0.96f, 0f));        // warm highlights
            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1.0f, 0.86f, 0.72f));
            var vig = Get<Vignette>(profile);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.5f);
            EditorUtility.SetDirty(profile);
        }

        // --- low-lying mist over the water
        BuildMist(scene);

        // --- the water mirrors the new sky
        RebakeReflectionProbe(scene);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Cypress: applied the Early Morning Mist look. Tweak 'Sun', 'Global Volume', 'Ground Mist' and Lighting > Environment freely.");
    }

    [MenuItem("Tools/Cypress/Look/Remove Ground Mist")]
    static void RemoveMist()
    {
        var scene = SceneManager.GetActiveScene();
        var mist = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MistRoot);
        if (mist != null)
        {
            Undo.DestroyObjectImmediate(mist);
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    // ------------------------------------------------------------------ mist
    static void BuildMist(Scene scene)
    {
        var mat = GetOrCreateMaterial(MistMaterial, "Cypress/Mist");
        if (mat == null) return;
        mat.SetColor("_MistColor", Lin(0.75f, 0.74f, 0.76f));
        mat.SetFloat("_NoiseScale", 0.06f);
        mat.SetFloat("_Breakup", 0.6f);
        mat.SetVector("_DriftSpeed", new Vector4(0.10f, 0.04f, 0, 0));
        mat.SetFloat("_SoftDistance", 1.2f);
        mat.SetFloat("_CameraFade", 2.5f);
        mat.SetFloat("_EdgeRadius", 55f);
        mat.SetFloat("_SunScatter", 1.2f);
        EditorUtility.SetDirty(mat);

        var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MistRoot);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(MistRoot);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = DomeCenter(scene);

        // height above the water, density: thick on the water, thinning upward
        var layers = new (float y, float density)[] { (0.12f, 0.40f), (0.45f, 0.28f), (0.95f, 0.18f), (1.7f, 0.10f) };
        var block = new MaterialPropertyBlock();
        foreach (var (y, density) in layers)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = $"Mist {y:0.00}m";
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0, y, 0);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = new Vector3(120f, 120f, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            block.Clear();
            block.SetFloat("_Density", density);
            r.SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------------ helpers
    static Scene OpenCypressScene()
    {
        var scene = SceneManager.GetSceneByPath(CypressAssets.ScenePath);
        if (scene.isLoaded)
        {
            SceneManager.SetActiveScene(scene);
            return scene;
        }
        if (!File.Exists(CypressAssets.ScenePath))
        {
            Debug.LogError("Cypress: build the scene first (Tools > Cypress > Build Cypress Dome Scene).");
            return default;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return default;
        return EditorSceneManager.OpenScene(CypressAssets.ScenePath, OpenSceneMode.Single);
    }

    static Light FindOrCreateSun(Scene scene)
    {
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Sun");
        if (go != null && go.TryGetComponent(out Light light)) return light;
        go = new GameObject("Sun");
        SceneManager.MoveGameObjectToScene(go, scene);
        light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        return light;
    }

    static VolumeProfile FindOrCreateProfile(Scene scene)
    {
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Global Volume");
        if (go == null) { go = new GameObject("Global Volume"); SceneManager.MoveGameObjectToScene(go, scene); }
        var volume = go.GetComponent<Volume>();
        if (volume == null) volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        if (volume.sharedProfile == null)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(CypressAssets.VolumeProfile);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, CypressAssets.VolumeProfile);
            }
            volume.sharedProfile = profile;
        }
        return volume.sharedProfile;
    }

    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
        {
            component.active = true;
            return component;
        }
        component = profile.Add<T>(true);
        if (AssetDatabase.Contains(profile))
            AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    static Material GetOrCreateMaterial(string path, string shaderName)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        var shader = Shader.Find(shaderName);
        if (shader == null) { Debug.LogError($"Cypress: shader '{shaderName}' not found."); return null; }
        mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Vector3 DomeCenter(Scene scene)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "CypressDome");
        var water = root != null ? root.transform.Find("Water") : null;
        return water != null ? new Vector3(water.position.x, 0f, water.position.z) : Vector3.zero;
    }

    static void RebakeReflectionProbe(Scene scene)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "CypressDome");
        var probe = root != null ? root.GetComponentInChildren<ReflectionProbe>() : null;
        if (probe == null) return;
        string path = CypressAssets.MaterialFolder + "/CypressReflection.exr";
        if (Lightmapping.BakeReflectionProbe(probe, path))
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }
    }
}
