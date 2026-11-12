using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single thin line at the bottom-center of the screen that shrinks toward the middle
/// as stamina drops. Builds its own Canvas at runtime, so no UI setup is needed.
/// Fades out when stamina is full. Turns red while exhausted.
///
/// Put this on the same object as PlayerStamina.
/// </summary>
public class StaminaBar : MonoBehaviour
{
    public PlayerStamina stamina;

    [Header("Look")]
    public float maxWidth = 320f;
    public float height = 3f;
    public float bottomMargin = 28f;
    public Color normalColor = new Color(1f, 1f, 1f, 0.85f);
    public Color exhaustedColor = new Color(1f, 0.35f, 0.3f, 0.9f);

    [Header("Behaviour")]
    public bool hideWhenFull = true;
    public float hideDelay = 1.5f;
    public float fadeSpeed = 3f;
    public float widthSmoothing = 14f;

    RectTransform bar;
    Image image;
    float width, alpha, fullTimer;

    void Awake()
    {
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        BuildUI();
        width = maxWidth;
        alpha = 0f;
    }

    void BuildUI()
    {
        GameObject canvasGO = new GameObject("StaminaCanvas");
        canvasGO.transform.SetParent(transform, false);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject barGO = new GameObject("StaminaLine");
        barGO.transform.SetParent(canvasGO.transform, false);

        image = barGO.AddComponent<Image>();
        image.raycastTarget = false;

        bar = image.rectTransform;
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, bottomMargin);
        bar.sizeDelta = new Vector2(maxWidth, height);
    }

    void Update()
    {
        if (stamina == null) return;
        float dt = Time.deltaTime;

        // Width follows stamina smoothly, shrinking from both ends toward the center.
        float targetWidth = maxWidth * stamina.Value01;
        width = Mathf.Lerp(width, targetWidth, 1f - Mathf.Exp(-widthSmoothing * dt));
        bar.sizeDelta = new Vector2(width, height);

        // Fade out a moment after stamina is full.
        bool full = stamina.Value01 >= 0.999f && !stamina.IsExhausted;
        fullTimer = full ? fullTimer + dt : 0f;
        bool hide = hideWhenFull && fullTimer > hideDelay;

        Color baseColor = stamina.IsExhausted ? exhaustedColor : normalColor;
        alpha = Mathf.MoveTowards(alpha, hide ? 0f : baseColor.a, fadeSpeed * dt);
        image.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
    }
}
