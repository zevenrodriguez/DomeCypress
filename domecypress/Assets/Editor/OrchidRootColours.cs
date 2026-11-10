using UnityEditor;
using UnityEngine;

/// <summary>
/// Brightens or darkens every colour on the orchid root material in one step, keeping your own
/// adjustments (it works on the material as it is in Unity right now).
///   Tools > Orchids > Root Colours: Brighter   (+0.05 on every colour channel)
///   Tools > Orchids > Root Colours: Darker     (-0.05)
/// Ctrl+Z undoes a step.
/// </summary>
public static class OrchidRootColours
{
    const string MaterialPath = "Assets/OrchidRoots/Resources/OrchidRoots/OrchidRoot.mat";
    const float Step = 0.05f;
    static readonly string[] Colours = { "_GreenDark", "_GreenLight", "_TipColor", "_BrownDark", "_BrownLight" };

    [MenuItem("Tools/Orchids/Root Colours: Brighter")]
    static void Brighter() => Shift(Step);

    [MenuItem("Tools/Orchids/Root Colours: Darker")]
    static void Darker() => Shift(-Step);

    static void Shift(float amount)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null) { Debug.LogWarning($"Orchid root material not found at {MaterialPath}."); return; }

        Undo.RecordObject(mat, amount > 0 ? "Brighter roots" : "Darker roots");
        foreach (var name in Colours)
        {
            if (!mat.HasProperty(name)) continue;
            Color c = mat.GetColor(name);
            mat.SetColor(name, new Color(Mathf.Clamp01(c.r + amount), Mathf.Clamp01(c.g + amount),
                                         Mathf.Clamp01(c.b + amount), c.a));
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
        Color g = mat.GetColor("_GreenDark");
        Debug.Log($"Orchid roots {(amount > 0 ? "brighter" : "darker")} by {Mathf.Abs(amount)} - Green Dark is now " +
                  $"({g.r:0.00}, {g.g:0.00}, {g.b:0.00}).");
    }
}
