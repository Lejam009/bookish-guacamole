#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds GameObject > UI > Interaction Prompt to the Unity menu. It builds a Canvas with a TextMeshPro
/// label and the InteractionPrompt component, ready for you to restyle. Safe to leave in the project:
/// it's editor-only and does nothing in builds.
/// </summary>
public static class InteractionPromptCreator
{
    [MenuItem("GameObject/UI/Interaction Prompt", false, 10)]
    static void Create(MenuCommand command)
    {
        GameObject canvasGO = new GameObject("InteractionCanvas", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Interaction Prompt");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject textGO = new GameObject("InteractionPrompt", typeof(RectTransform));
        textGO.transform.SetParent(canvasGO.transform, false);

        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "[E] Pick up Item  (1.0 kg)";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 34f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(1f, 1f, 1f, 0.95f);
        tmp.raycastTarget = false;

        RectTransform rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -140f);
        rt.sizeDelta = new Vector2(900f, 60f);

        textGO.AddComponent<CanvasGroup>();
        InteractionPrompt prompt = textGO.AddComponent<InteractionPrompt>();
        prompt.label = tmp;

        Selection.activeGameObject = textGO;
    }
}
#endif
