using UnityEngine;

// [Pass 1 – New script] MaskSwapper (OPTIONAL)
// WHY: Lets one Sprite Mask switch between different mask shapes (circle, doorway, slash, etc.)
//      instead of building a separate mask object for every shape.
// SINGLE RESPONSIBILITY: choose which shape this mask uses, and turn it on or off.
[RequireComponent(typeof(SpriteMask))] // [Pass 1] WHY: this script is useless without a Sprite Mask,
                                       //          so Unity adds one automatically.
public class MaskSwapper : MonoBehaviour
{
    [Tooltip("Mask shapes this mask can switch between. Index 0 = first shape.")]
    [SerializeField] private Sprite[] maskShapes;

    private SpriteMask mask; // [Pass 1] Cached once in Awake so we don't search every call.

    private void Awake()
    {
        mask = GetComponent<SpriteMask>();
    }

    // [Pass 1] Switches to the shape at "index" in the maskShapes list.
    public void ShowShape(int index)
    {
        // [Pass 1] WHY: guard against a bad number so the game logs a warning instead of crashing.
        if (index < 0 || index >= maskShapes.Length)
        {
            Debug.LogWarning(name + ": MaskSwapper has no shape at index " + index, this);
            return;
        }

        mask.sprite = maskShapes[index];
    }

    // [Pass 1] Turns the mask on or off without destroying it.
    public void SetMaskEnabled(bool isOn)
    {
        mask.enabled = isOn;
    }
}
