using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes the wing beat of the moth fly animation wider.
/// Each frame, after the animation has posed the wing bones, every wing's turn away from its rest pose
/// is multiplied by Amount (1 = as animated, 2 = twice the angle).
/// Added to each moth by OrchidMothExperience; it can also go on any moth model with the Armature wing bones.
/// </summary>
public class WingFlapAmplifier : MonoBehaviour
{
    [Tooltip("How much wider the wing beat is (1 = the animation as made, 1.8 = 80% wider).")]
    [Range(1f, 3f)] public float amount = 1.8f;

    readonly List<Transform> _wings = new List<Transform>();
    readonly List<Quaternion> _rest = new List<Quaternion>();

    // runs as soon as the component is added, before the animation starts, so this is the rest pose
    void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.Contains("Wing")) continue;
            _wings.Add(t);
            _rest.Add(t.localRotation);
        }
    }

    void LateUpdate()
    {
        for (int i = 0; i < _wings.Count; i++)
        {
            if (_wings[i] == null) continue;
            // go further along the rest -> animated rotation (unclamped, so > 1 overshoots it)
            _wings[i].localRotation = Quaternion.SlerpUnclamped(_rest[i], _wings[i].localRotation, amount);
        }
    }
}
