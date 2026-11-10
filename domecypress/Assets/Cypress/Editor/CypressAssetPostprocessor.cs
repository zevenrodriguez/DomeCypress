using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the Blender exports in Assets/Cypress: keeps Blender's normals, UVs and vertex
/// colors, skips cameras/lights/animation, and assigns the cypress materials by object name.
/// The leaf atlas keeps its alpha coverage across mip levels so distant leaf cards don't vanish.
/// </summary>
class CypressAssetPostprocessor : AssetPostprocessor
{
    bool IsCypressModel => assetPath.StartsWith(CypressAssets.ModelFolder + "/");
    bool IsCypressTexture => assetPath.StartsWith(CypressAssets.TextureFolder + "/");

    void OnPreprocessModel()
    {
        if (!IsCypressModel) return;
        var importer = (ModelImporter)assetImporter;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.importBlendShapes = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.generateSecondaryUV = false;
        importer.indexFormat = ModelImporterIndexFormat.Auto;   // big merged meshes need 32-bit indices
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!IsCypressModel) return;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var path = MaterialPathFor(renderer.name);
            if (path == null) continue;
            context.DependsOnSourceAsset(path);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var filter = renderer.GetComponent<MeshFilter>();
            int count = filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 1;
            var materials = new Material[count];
            for (int i = 0; i < count; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    void OnPreprocessTexture()
    {
        if (!IsCypressTexture) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.5f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        if (assetPath.EndsWith("pondbed_litter.png"))
        {
            // seamless ground texture: tiles, no alpha
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.alphaIsTransparency = false;
            importer.mipMapsPreserveCoverage = false;
            importer.anisoLevel = 4;
        }
    }

    static string MaterialPathFor(string objectName)
    {
        switch (objectName)
        {
            case "Cypress_BaldTrunks":
            case "Cypress_Branches":
            case "Cypress_Knees":
            case "Cypress_KneesExtra":
            case "Cypress_YoungBranches":
            case "Cypress_Vines":
                return CypressAssets.BarkBaldMaterial;
            case "Cypress_PondTrunks":
            case "Cypress_YoungTrunks":
                return CypressAssets.BarkPondMaterial;
            case "Cypress_Logs":
            case "Cypress_Sticks":
                return CypressAssets.BarkLogMaterial;
            case "Cypress_LeafCards":
            case "Cypress_CanopyExtra":
            case "Cypress_YoungFoliage":
                return CypressAssets.LeafMaterial;
            case "Cypress_SpanishMoss":
            case "Cypress_Shrubs":
            case "Cypress_FernCards":
            case "Cypress_Pickerelweed":
                return CypressAssets.UnderstoryMaterial;
            case "Cypress_FloatingLitter":
            case "Cypress_SunkenLitter":
                return CypressAssets.LitterCardMaterial;
            case "Cypress_ShortGrass":
            case "Cypress_Sawgrass":
            case "Cypress_SawgrassTufts":
            case "Cypress_Ferns":
            case "Cypress_AirPlants":
            case "Grass_L0":
            case "Grass_L1":
            case "Grass_L2":
                return CypressAssets.GrassMaterial;
            case "Cypress_FloatingLeaves":
                return CypressAssets.LitterMaterial;
            case "Cypress_Ground":
                return CypressAssets.GroundMaterial;
            default:
                return null;
        }
    }
}
