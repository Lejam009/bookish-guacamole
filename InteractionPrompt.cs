using TMPro;
using UnityEngine;

public enum PromptKind { Pickup, Swap, TooHeavy }

/// <summary>
/// The on-screen text shown when you look at an item. This lives on a normal UI Canvas object, so
/// you style it in the editor: font, size, colour, outline, position, anchors, an icon next to it, anything.
/// Create the whole thing with GameObject > UI > Interaction Prompt.
///
/// The wording is editable below. Placeholders: {name} = item name, {weight} = item weight.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class InteractionPrompt : MonoBehaviour
{
    [Header("Text Element")]
    [Tooltip("The TextMeshPro text to fill in. Found automatically in children if left empty.")]
    public TMP_Text label;

    [Header("Wording ({name} and {weight} are replaced)")]
    public string pickupText = "[E] Pick up {name}  ({weight})";
    public string swapText = "[E] Swap for {name}  ({weight})";
    public string tooHeavyText = "{name}  ({weight}) - too heavy";

    [Header("Weight Display")]
    [Tooltip("Number format. \"0.0\" gives 3.6, \"0.00\" gives 3.60.")]
    public string weightFormat = "0.0";
    public string weightSuffix = " kg";

    [Header("Fade")]
    public float fadeSpeed = 10f;

    CanvasGroup group;
    bool visible;

    // Cache so we only rebuild the string when something actually changed (no per-frame garbage)
    bool hasShown;
    PromptKind lastKind;
    string lastName;
    float lastMass;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (label == null) label = GetComponentInChildren<TMP_Text>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    /// <summary>Called every frame by PlayerInteractor while you look at an item.</summary>
    public void Show(PromptKind kind, string itemName, float massKg)
    {
        visible = true;
        if (label == null) return;
        if (hasShown && kind == lastKind && itemName == lastName && Mathf.Approximately(massKg, lastMass)) return;

        hasShown = true;
        lastKind = kind;
        lastName = itemName;
        lastMass = massKg;

        string template = kind == PromptKind.TooHeavy ? tooHeavyText
                        : kind == PromptKind.Swap ? swapText
                        : pickupText;

        label.text = template
            .Replace("{name}", itemName)
            .Replace("{weight}", massKg.ToString(weightFormat) + weightSuffix);
    }

    public void Hide() => visible = false;

    void Update()
    {
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
    }
}
