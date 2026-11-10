using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

static class CypressAssets
{
    public const string Root = "Assets/Cypress";
    public const string ModelFolder = Root + "/Models";
    public const string TextureFolder = Root + "/Textures";
    public const string MaterialFolder = Root + "/Materials";
    public const string ScenePath = "Assets/Scenes/CypressDome.unity";
    public const string SceneDataPath = Root + "/cypress_scene.json";
    public const string DomeModel = ModelFolder + "/CypressDome.fbx";
    public const string GrassModel = ModelFolder + "/CypressGrass.fbx";
    public const string LeafAtlas = TextureFolder + "/cypress_leaf_atlas.png";
    public const string UnderstoryAtlas = TextureFolder + "/swamp_understory_atlas.png";
    public const string LushModel = ModelFolder + "/CypressLush.fbx";
    public const string FloatingLitterAtlas = TextureFolder + "/floating_litter_atlas.png";
    public const string PondBedTexture = TextureFolder + "/pondbed_litter.png";

    public const string BarkBaldMaterial = MaterialFolder + "/Cypress_BarkBald.mat";
    public const string BarkPondMaterial = MaterialFolder + "/Cypress_BarkPond.mat";
    public const string BarkLogMaterial = MaterialFolder + "/Cypress_BarkLog.mat";
    public const string LeafMaterial = MaterialFolder + "/Cypress_LeafCards.mat";
    public const string UnderstoryMaterial = MaterialFolder + "/Cypress_Understory.mat";
    public const string LitterCardMaterial = MaterialFolder + "/Cypress_LitterCards.mat";
    public const string GrassMaterial = MaterialFolder + "/Cypress_Grass.mat";
    public const string LitterMaterial = MaterialFolder + "/Cypress_Litter.mat";
    public const string GroundMaterial = MaterialFolder + "/Cypress_Ground.mat";
    public const string WaterMaterial = MaterialFolder + "/Cypress_Water.mat";
    public const string SkyMaterial = MaterialFolder + "/Cypress_Sky.mat";
    public const string VolumeProfile = MaterialFolder + "/CypressDay_Profile.asset";
}

/// <summary>
/// Builds Assets/Scenes/CypressDome.unity from the export of BlenderTools/export_cypress_unity.py:
/// the cypress dome (trunks, leaf cards, ground cover, peat hummocks), tannin-stained water with
/// duckweed scum, the instanced sawgrass ring with LODs, daylight, and cameras.
/// Menu: Tools > Cypress > Build Cypress Dome Scene.
/// </summary>
static class CypressDomeBuilder
{
    const string RootName = "CypressDome";
    const string SunName = "Sun";
    const string VolumeName = "Global Volume";
    const string PreviewCameraName = "Preview Camera";
    const float WaterSize = 240f;
    const int WaterResolution = 240;

    [Serializable] class SceneFile { public float waterLevel; public float[] domeCenter; public CameraInfo camera; public SunInfo sun; public MurkInfo murk; public GrassInfo grass; }
    [Serializable] class CameraInfo { public float[] position; public float[] forward; public float fov; public float tiltDegrees; }
    [Serializable] class SunInfo { public float[] direction; public float intensity; }
    [Serializable] class MurkInfo { public float[] center; public float[] radii; }
    [Serializable] class GrassInfo { public float lod1Distance; public float lod2Distance; }

    /// <summary>
    /// Rebuilds the dome geometry (trees, plants, water, grass, reflection probe) from the latest export.
    /// Your look is kept: the Sun, Global Volume (and its profile), Preview Camera, Lighting/Environment
    /// settings (skybox, ambient, fog) and every Cypress material are only created if they don't exist yet.
    /// </summary>
    [MenuItem("Tools/Cypress/Build Cypress Dome Scene")]
    static void BuildMenu() => BuildScene(false);

    /// <summary>Same as Build, but throws away all look tweaks and restores the default sun, sky, fog, post-processing and materials.</summary>
    [MenuItem("Tools/Cypress/Reset Look to Defaults and Rebuild")]
    static void ResetMenu()
    {
        if (EditorUtility.DisplayDialog("Reset Cypress look?",
                "This replaces the Sun, Global Volume profile, sky, fog, ambient light, Preview Camera and all Cypress materials " +
                "with the defaults, discarding your tweaks. Continue?", "Reset", "Cancel"))
            BuildScene(true);
    }

    static void BuildScene(bool resetLook)
    {
        if (!File.Exists(CypressAssets.SceneDataPath))
        {
            Debug.LogError($"Cypress: {CypressAssets.SceneDataPath} not found. Run BlenderTools/export_cypress_unity.py in Blender first.");
            return;
        }
        var data = JsonUtility.FromJson<SceneFile>(File.ReadAllText(CypressAssets.SceneDataPath));

        EnsureRenderPipelineFeatures();
        EnsureMaterials(data, resetLook);
        ReimportModels();   // lets the postprocessor pick up materials created just now

        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(CypressAssets.ScenePath);
        bool wasOpen = scene.isLoaded;
        if (!wasOpen)
            scene = File.Exists(CypressAssets.ScenePath)
                ? EditorSceneManager.OpenScene(CypressAssets.ScenePath, OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            // Only the generated dome is replaced; look objects survive unless a reset was asked for.
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName ||
                    (resetLook && (go.name == SunName || go.name == VolumeName || go.name == PreviewCameraName)))
                    UnityEngine.Object.DestroyImmediate(go);

            var root = BuildDome(data);
            if (root == null) return;
            SetupDaylight(scene, data, resetLook);
            SetupCameras(scene, root, data);
            SetupReflectionProbe(root, data);
            EditorSceneManager.SaveScene(scene, CypressAssets.ScenePath);
            Debug.Log($"Cypress: saved {CypressAssets.ScenePath}. Open it to see the dome (Preview Camera = the Blender view).");
        }
        finally
        {
            if (previous.IsValid() && previous != scene) SceneManager.SetActiveScene(previous);
            if (!wasOpen && previous.IsValid() && previous != scene) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static GameObject BuildDome(SceneFile data)
    {
        var root = new GameObject(RootName);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CypressAssets.DomeModel);
        if (model == null)
        {
            Debug.LogError($"Cypress: {CypressAssets.DomeModel} not found.");
            UnityEngine.Object.DestroyImmediate(root);
            return null;
        }
        var dome = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        var ground = dome.transform.Find("Cypress_Ground");
        if (ground != null) ground.gameObject.AddComponent<MeshCollider>();
        foreach (var name in new[] { "Cypress_FloatingLeaves", "Cypress_ShortGrass", "Cypress_AirPlants" })
        {
            var t = dome.transform.Find(name);   // small detail: skip shadow casting to save draw cost
            if (t != null) t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Lush layer (BlenderTools: build_lush_layer.py): denser canopy, Spanish moss, shrubs, ferns, pickerelweed
        var lush = AssetDatabase.LoadAssetAtPath<GameObject>(CypressAssets.LushModel);
        if (lush != null)
        {
            var lushGo = (GameObject)PrefabUtility.InstantiatePrefab(lush, root.transform);
            foreach (var name in new[] { "Cypress_Pickerelweed", "Cypress_FloatingLitter", "Cypress_SunkenLitter" })
            {
                var t = lushGo.transform.Find(name);
                if (t != null) t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            // the textured litter replaces the old untextured floating leaves
            if (lushGo.transform.Find("Cypress_FloatingLitter") != null)
            {
                var oldLeaves = dome.transform.Find("Cypress_FloatingLeaves");
                if (oldLeaves != null) oldLeaves.gameObject.SetActive(false);
            }
        }

        // Water: the project's Custom/Water shader, tannin-tinted murk zone over the whole dome
        var water = new GameObject("Water", typeof(MeshFilter), typeof(MeshRenderer), typeof(WaterSurface));
        water.transform.SetParent(root.transform, false);
        var c = ToVector(data.domeCenter);
        water.transform.localPosition = new Vector3(c.x, data.waterLevel, c.z);
        var surface = water.GetComponent<WaterSurface>();
        surface.size = WaterSize;
        surface.resolution = WaterResolution;
        surface.Rebuild();
        var waterRenderer = water.GetComponent<MeshRenderer>();
        waterRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CypressAssets.WaterMaterial);
        waterRenderer.shadowCastingMode = ShadowCastingMode.Off;

        // Sawgrass ring: GPU instanced, three LODs picked per camera
        var grassGo = new GameObject("MarshGrass", typeof(GrassInstancer));
        grassGo.transform.SetParent(root.transform, false);
        var grass = grassGo.GetComponent<GrassInstancer>();
        grass.sceneData = AssetDatabase.LoadAssetAtPath<TextAsset>(CypressAssets.SceneDataPath);
        var meshes = AssetDatabase.LoadAllAssetsAtPath(CypressAssets.GrassModel).OfType<Mesh>().ToArray();
        grass.lodMeshes = new Mesh[3];
        for (int i = 0; i < 3; i++)
            grass.lodMeshes[i] = meshes.FirstOrDefault(m => m.name == $"Grass_L{i}");
        grass.material = AssetDatabase.LoadAssetAtPath<Material>(CypressAssets.GrassMaterial);
        if (data.grass != null && data.grass.lod1Distance > 0)
        {
            grass.lod1Distance = data.grass.lod1Distance;
            grass.lod2Distance = data.grass.lod2Distance;
        }
        grass.Load();
        if (grass.lodMeshes.Any(m => m == null))
            Debug.LogWarning("Cypress: some grass LOD meshes (Grass_L0..L2) are missing from CypressGrass.fbx.");

        Debug.Log($"Cypress: built dome with {grass.InstanceCount} instanced grass clumps.");
        return root;
    }

    static GameObject FindRootObject(Scene scene, string name) =>
        scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

    static void SetupDaylight(Scene scene, SceneFile data, bool resetLook)
    {
        var sunGo = FindRootObject(scene, SunName);
        bool firstTime = sunGo == null;      // no Sun yet = new scene or a reset: apply the default environment too
        Light sun;
        if (sunGo != null && sunGo.TryGetComponent(out sun))
        {
            // keep the user's sun as it is
        }
        else
        {
            sun = new GameObject(SunName).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.6f;
            sun.shadowStrength = 0.85f;
            sun.shadows = LightShadows.Soft;
            var dir = data.sun?.direction?.Length == 3 ? ToVector(data.sun.direction) : new Vector3(0.3f, -0.7f, 0.6f);
            sun.transform.rotation = Quaternion.LookRotation(dir.normalized);
        }
        RenderSettings.sun = sun;

        var sky = (resetLook ? (Func<string, string, Action<Material>, Material>)Recreate : GetOrCreate)(
            CypressAssets.SkyMaterial, "Skybox/Procedural", m =>
        {
            m.SetFloat("_SunSize", 0.04f);
            m.SetFloat("_SunSizeConvergence", 5f);
            m.SetFloat("_AtmosphereThickness", 0.9f);
            m.SetColor("_SkyTint", new Color(0.45f, 0.55f, 0.7f));
            m.SetColor("_GroundColor", new Color(0.3f, 0.32f, 0.26f));
            m.SetFloat("_Exposure", 1.2f);
        });
        if (firstTime || RenderSettings.skybox == null)
            RenderSettings.skybox = sky;
        if (firstTime)
        {
            // Lighting window > Environment defaults; afterwards they're yours to change
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.35f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            // humid swamp haze, slightly green
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = Lin(0.62f, 0.70f, 0.78f);
        }
        DynamicGI.UpdateEnvironment();

        var volumeGo = FindRootObject(scene, VolumeName);
        if (volumeGo == null || !volumeGo.TryGetComponent(out Volume volume))
        {
            volume = (volumeGo != null ? volumeGo : new GameObject(VolumeName)).AddComponent<Volume>();
            volume.isGlobal = true;
        }
        if (volume.sharedProfile == null || resetLook)
            volume.sharedProfile = GetOrCreateProfile(resetLook);
    }

    static VolumeProfile GetOrCreateProfile(bool reset)
    {
        var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(CypressAssets.VolumeProfile);
        if (existing != null && !reset) return existing;
        if (existing != null)
            AssetDatabase.DeleteAsset(CypressAssets.VolumeProfile);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, CypressAssets.VolumeProfile);
        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);   // gentler than ACES, closer to Blender's AgX
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.15f);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.15f);
        color.saturation.Override(0f);
        foreach (var component in profile.components)
            AssetDatabase.AddObjectToAsset(component, profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    /// <summary>
    /// Bakes a box-projected reflection probe in the middle of the dome so the water mirrors the
    /// trunks and canopy (URP has no screen-space reflections) instead of only the bright sky.
    /// </summary>
    static void SetupReflectionProbe(GameObject root, SceneFile data)
    {
        var c = ToVector(data.domeCenter);
        var go = new GameObject("Reflection Probe");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(c.x, 2.0f, c.z);
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.size = new Vector3(90f, 40f, 90f);
        probe.center = new Vector3(0f, 16f, 0f);
        probe.boxProjection = true;
        probe.resolution = 512;
        probe.hdr = true;
        probe.nearClipPlane = 0.3f;
        probe.farClipPlane = 300f;
        probe.importance = 1;
        string path = CypressAssets.MaterialFolder + "/CypressReflection.exr";
        if (Lightmapping.BakeReflectionProbe(probe, path))
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }
        else Debug.LogWarning("Cypress: reflection probe bake failed; water will reflect only the sky.");
    }

    static void SetupCameras(Scene scene, GameObject root, SceneFile data)
    {
        if (data.camera?.position?.Length != 3) return;
        var pos = ToVector(data.camera.position);
        var forward = ToVector(data.camera.forward).normalized;
        bool keepPreview = FindRootObject(scene, PreviewCameraName) != null;   // keep the user's framing / camera settings
        if (!keepPreview)
            CreatePreviewCamera(pos, forward, data);
        CreateDomeRig(root, pos, forward, data);
    }

    static void CreatePreviewCamera(Vector3 pos, Vector3 forward, SceneFile data)
    {
        // Regular camera matching the Blender render view
        var preview = new GameObject(PreviewCameraName, typeof(Camera), typeof(AudioListener));
        preview.tag = "MainCamera";
        preview.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, Vector3.up));
        var cam = preview.GetComponent<Camera>();
        cam.fieldOfView = data.camera.fov > 1 ? data.camera.fov : 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1000f;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    }

    static void CreateDomeRig(GameObject root, Vector3 pos, Vector3 forward, SceneFile data)
    {

        // Fulldome rig (same convention as the mangrove scene): forward = dome zenith tilted toward the view.
        // Disabled by default - enable "DomeCameraMarker" to render the domemaster in Play mode.
        var marker = new GameObject("DomeCameraMarker").transform;
        marker.SetParent(root.transform, false);
        marker.localPosition = pos;
        var flat = forward; flat.y = 0; flat.Normalize();
        float elevation = (90f - data.camera.tiltDegrees) * Mathf.Deg2Rad;
        var zenith = flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);
        var up = -flat * Mathf.Sin(elevation) + Vector3.up * Mathf.Cos(elevation);
        marker.localRotation = Quaternion.LookRotation(zenith, up);
        var domeCam = new GameObject("DomeCamera").AddComponent<DomeMasterCamera>();
        domeCam.transform.SetParent(marker, false);
        domeCam.domeShader = Shader.Find("Hidden/Mangrove/DomeMaster");
        marker.gameObject.SetActive(false);
    }

    /// <summary>The water shader reads the camera depth and opaque textures; switch them on in every URP asset.</summary>
    static void EnsureRenderPipelineFeatures()
    {
        var assets = new System.Collections.Generic.HashSet<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) assets.Add(d);
        for (int i = 0; i < QualitySettings.count; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q) assets.Add(q);
        foreach (var urp in assets)
        {
            if (urp.supportsCameraDepthTexture && urp.supportsCameraOpaqueTexture) continue;
            urp.supportsCameraDepthTexture = true;
            urp.supportsCameraOpaqueTexture = true;
            EditorUtility.SetDirty(urp);
            Debug.Log($"Cypress: enabled Depth Texture and Opaque Texture on {urp.name} (needed by the water shader).");
        }
    }

    // Blender colours are linear; Unity's SetColor expects sRGB (gamma) values in a linear project.
    static Color Lin(float r, float g, float b, float a = 1f) => new Color(r, g, b, a).gamma;

    /// <summary>
    /// Creates any missing Cypress material with its defaults (all colours set here: shader defaults
    /// would be read as sRGB and come out too dark). With reset = true every material is recreated.
    /// </summary>
    static void EnsureMaterials(SceneFile data, bool reset)
    {
        // Existing materials keep your edits; a reset recreates them with the defaults below.
        Func<string, string, Action<Material>, Material> make = reset
            ? (Func<string, string, Action<Material>, Material>)Recreate
            : GetOrCreate;
        if (!AssetDatabase.IsValidFolder(CypressAssets.MaterialFolder))
            AssetDatabase.CreateFolder(CypressAssets.Root, "Materials");

        Action<Material, float, Color> bark = (m, pale, grey) =>
        {
            m.SetFloat("_PaleAmount", pale);
            m.SetColor("_GreyColor", grey);
            m.SetColor("_CinnamonColor", Lin(0.17f, 0.085f, 0.05f));
            m.SetColor("_CrackColor", Lin(0.09f, 0.07f, 0.05f));
            m.SetColor("_WetColor", Lin(0.012f, 0.011f, 0.008f));
            m.SetColor("_MossColorA", Lin(0.05f, 0.07f, 0.015f));
            m.SetColor("_MossColorB", Lin(0.22f, 0.28f, 0.04f));
        };
        make(CypressAssets.BarkBaldMaterial, "Cypress/Bark", m => bark(m, 0.5f, Lin(0.28f, 0.25f, 0.21f)));
        make(CypressAssets.BarkPondMaterial, "Cypress/Bark", m =>
        {
            bark(m, 0.75f, Lin(0.30f, 0.28f, 0.25f));
            m.SetVector("_WetBand", new Vector4(0.2f, 0.6f, 0, 0));
            m.SetVector("_MossBand", new Vector4(0.8f, 1.5f, 0, 0));
        });
        make(CypressAssets.BarkLogMaterial, "Cypress/Bark", m =>
        {
            bark(m, 0.3f, Lin(0.28f, 0.25f, 0.21f));
            m.SetFloat("_MossTop", 0.9f);
            m.SetVector("_WetBand", new Vector4(-10f, -9f, 0, 0));    // no waterline bands on logs
            m.SetVector("_MossBand", new Vector4(-10f, -9f, 0, 0));
        });
        make(CypressAssets.LeafMaterial, "Cypress/LeafCard", m =>
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CypressAssets.LeafAtlas));
            m.SetColor("_TintA", Lin(0.70f, 0.80f, 0.60f));
            m.SetColor("_TintB", Lin(1.0f, 0.98f, 0.82f));
            m.SetFloat("_Translucency", 0.35f);
            m.SetColor("_TranslucencyColor", Lin(0.45f, 0.55f, 0.2f));
            m.SetFloat("_Smoothness", 0.3f);
        });
        make(CypressAssets.UnderstoryMaterial, "Cypress/LeafCard", m =>
        {
            // Spanish moss, shrubs, ferns, pickerelweed: colour comes from the atlas, only light variation here
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CypressAssets.UnderstoryAtlas));
            m.SetColor("_TintA", Lin(0.80f, 0.85f, 0.75f));
            m.SetColor("_TintB", Lin(1.0f, 1.0f, 0.92f));
            m.SetFloat("_TintScale", 0.5f);
            m.SetFloat("_Translucency", 0.3f);
            m.SetColor("_TranslucencyColor", Lin(0.45f, 0.55f, 0.2f));
            m.SetFloat("_Smoothness", 0.35f);
        });
        make(CypressAssets.LitterCardMaterial, "Cypress/LeafCard", m =>
        {
            // fallen leaves on / under the water: wet and glossy, barely translucent
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CypressAssets.FloatingLitterAtlas));
            m.SetColor("_TintA", Lin(0.70f, 0.66f, 0.60f));
            m.SetColor("_TintB", Lin(1.0f, 0.95f, 0.88f));
            m.SetFloat("_TintScale", 0.8f);
            m.SetFloat("_Translucency", 0.1f);
            m.SetFloat("_Smoothness", 0.65f);
        });
        make(CypressAssets.GrassMaterial, "Cypress/Foliage", m =>
        {
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Translucency", 0.4f);
            m.SetColor("_TranslucencyColor", Lin(0.5f, 0.6f, 0.2f));
        });
        make(CypressAssets.LitterMaterial, "Cypress/Foliage", m =>
        {
            m.SetFloat("_Translucency", 0.15f);
            m.SetFloat("_Smoothness", 0.5f);
        });
        make(CypressAssets.GroundMaterial, "Cypress/Ground", m =>
        {
            m.SetColor("_MudDark", Lin(0.018f, 0.013f, 0.009f));
            m.SetColor("_MudMid", Lin(0.045f, 0.03f, 0.018f));
            m.SetColor("_MudLight", Lin(0.07f, 0.045f, 0.028f));
            m.SetColor("_SpeckColor", Lin(0.30f, 0.13f, 0.04f));
        });
        make(CypressAssets.WaterMaterial, "Custom/Water", m =>
        {
            // still, dark, tannin-stained swamp water that mirrors the trees (via the reflection probe)
            m.SetColor("_ShallowColor", Lin(0.03f, 0.018f, 0.008f, 0.75f));
            m.SetColor("_DeepColor", Lin(0.012f, 0.008f, 0.004f, 0.97f));
            m.SetFloat("_DepthMaxDistance", 1.5f);
            m.SetVector("_WaveA", new Vector4(1f, 0.2f, 0.01f, 40f));
            m.SetVector("_WaveB", new Vector4(0.3f, 1f, 0.01f, 20f));
            m.SetVector("_WaveC", new Vector4(-0.6f, 0.8f, 0.01f, 10f));
            m.SetFloat("_WaveSpeed", 0.5f);
            m.SetFloat("_RippleScale", 0.35f);
            m.SetFloat("_RippleSpeed", 0.2f);
            m.SetFloat("_RippleStrength", 0.12f);
            m.SetFloat("_Smoothness", 0.95f);
            m.SetFloat("_SpecularIntensity", 0.6f);
            m.SetFloat("_FresnelPower", 2.5f);
            m.SetFloat("_ReflectionStrength", 0.85f);
            m.SetFloat("_RefractionStrength", 0.01f);
            m.SetColor("_FoamColor", new Color(1f, 1f, 1f, 0f));        // no shore foam in a swamp
            m.SetFloat("_CrestFoam", 0f);
            m.SetFloat("_MurkStrength", 1f);
            m.SetColor("_MurkColor", Lin(0.025f, 0.009f, 0.003f, 0.97f));   // tannin tea
            m.SetColor("_ScumColor", Lin(0.13f, 0.33f, 0.035f));            // duckweed
            m.SetFloat("_ScumScale", 0.9f);
            m.SetFloat("_ScumCoverage", 0.22f);
            m.SetVector("_MurkEdge", new Vector4(0.7f, 1.05f, 0, 0));
            m.SetFloat("_MurkWaveScale", 0.02f);
            if (data.murk?.center?.Length == 3)
                m.SetVector("_MurkCenter", new Vector4(data.murk.center[0], data.murk.center[2], 0, 0));
            if (data.murk?.radii?.Length == 2)
                m.SetVector("_MurkRadii", new Vector4(data.murk.radii[0], data.murk.radii[1], 0, 0));
        });
        UpgradeMaterials();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Fills in settings added after a material was first created (kept materials would otherwise
    /// miss them). Only touches values that are still at their "unset" state, so your edits stay.
    /// </summary>
    static void UpgradeMaterials()
    {
        var ground = AssetDatabase.LoadAssetAtPath<Material>(CypressAssets.GroundMaterial);
        if (ground != null && ground.HasProperty("_LitterTex") && ground.GetTexture("_LitterTex") == null)
        {
            ground.SetTexture("_LitterTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CypressAssets.PondBedTexture));
            ground.SetFloat("_LitterScale", 0.6f);
            ground.SetColor("_LitterTint", Lin(0.75f, 0.7f, 0.65f));
            ground.SetVector("_LitterBand", new Vector4(0.03f, -0.12f, 0, 0));
            EditorUtility.SetDirty(ground);
        }
        var water = AssetDatabase.LoadAssetAtPath<Material>(CypressAssets.WaterMaterial);
        if (water != null && water.HasProperty("_MurkClarity") && water.GetFloat("_MurkClarity") <= 0f)
        {
            water.SetFloat("_MurkClarity", 0.35f);                       // ~35 cm before the tea colour wins
            water.SetColor("_MurkTint", Lin(0.45f, 0.25f, 0.10f));      // tannin absorption of what's below
            water.SetColor("_MurkColor", Lin(0.025f, 0.009f, 0.003f, 0.97f));
            EditorUtility.SetDirty(water);
        }
    }

    static Material Recreate(string path, string shaderName, Action<Material> init)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);
        return GetOrCreate(path, shaderName, init);
    }

    static Material GetOrCreate(string path, string shaderName, Action<Material> init)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"Cypress: shader '{shaderName}' not found.");
            return null;
        }
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path), enableInstancing = true };
        init?.Invoke(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void ReimportModels()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CypressAssets.ModelFolder }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
    }

    static Vector3 ToVector(float[] v) => v != null && v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;
}
