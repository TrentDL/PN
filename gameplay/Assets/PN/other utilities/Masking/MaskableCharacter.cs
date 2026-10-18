using UnityEngine;

// [Pass 1 – New script] MaskableCharacter
// WHY: A PSD-imported character is split into many SpriteRenderers (one per Photoshop layer).
//      A Sprite Mask only affects renderers whose "Mask Interaction" is set, so every body part
//      must be set individually. Missing even one part makes the mask look "broken".
//      This script sets all of them at once, so it can be reused on ANY object in the scene.
// SINGLE RESPONSIBILITY: decide how this object's sprites react to masks. Nothing else.
[DisallowMultipleComponent] // [Pass 1] WHY: two copies would fight over the same setting.
public class MaskableCharacter : MonoBehaviour
{
    [Tooltip("How every body part under this object reacts to Sprite Masks.")]
    [SerializeField] private SpriteMaskInteraction interaction = SpriteMaskInteraction.VisibleInsideMask;
    // [Pass 1] WHY private + [SerializeField]: editable in the Inspector, but other scripts
    //          must go through SetInteraction() (encapsulation).

    private SpriteRenderer[] parts; // [Pass 1] WHY array (not List): the part count does not change
                                    //          during play unless Refresh() is called.

    private void Awake()
    {
        Refresh();
    }

    // [Pass 1] Re-finds all body parts and applies the setting.
    // WHY public: call this if you add parts at runtime (e.g., a weapon sprite parented to a hand bone).
    public void Refresh()
    {
        parts = GetComponentsInChildren<SpriteRenderer>(true); // true = include inactive parts
        ApplyToParts();
    }

    // [Pass 1] Changes the setting at runtime (e.g., turn masking off when leaving a doorway).
    public void SetInteraction(SpriteMaskInteraction newInteraction)
    {
        interaction = newInteraction;
        ApplyToParts();
    }

    // [Pass 1] The one place the setting is written (DRY: Refresh and SetInteraction both reuse it).
    private void ApplyToParts()
    {
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].maskInteraction = interaction;
        }
    }
}
