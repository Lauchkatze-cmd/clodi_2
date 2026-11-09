using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small stamina bar for the sprint ability, shown at the bottom of the screen.
/// It builds itself at runtime on the first Canvas in the scene, nothing to set up.
/// Added automatically by the PlayerController.
/// </summary>
public class StaminaBar : MonoBehaviour
{
    [SerializeField] private Vector2 size = new Vector2(320f, 18f);
    [SerializeField] private Color fullColor = new Color(0.55f, 0.9f, 0.35f);
    [SerializeField] private Color lowColor = new Color(0.95f, 0.45f, 0.2f);
    [Tooltip("Seconds the bar stays visible after it is full again.")]
    [SerializeField] private float hideDelay = 1.2f;

    private PlayerController player;
    private RectTransform fill;
    private Image fillImage;
    private CanvasGroup group;
    private float fullSince;

    private void Start()
    {
        player = GetComponent<PlayerController>();
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (player == null || canvas == null)
        {
            enabled = false;
            return;
        }

        GameObject root = new GameObject("StaminaBar", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 70f);
        rootRect.sizeDelta = size;
        group = root.GetComponent<CanvasGroup>();

        Image background = CreateImage("Background", root.transform, new Color(0f, 0f, 0f, 0.55f));
        Stretch((RectTransform)background.transform, 0f);

        fillImage = CreateImage("Fill", root.transform, fullColor);
        fill = (RectTransform)fillImage.transform;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(-3f, -3f);
    }

    private void Update()
    {
        if (fill == null) return;

        float value = player.Stamina01;
        fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        fillImage.color = Color.Lerp(lowColor, fullColor, value);

        // Only show the bar while it matters
        if (value < 0.999f || player.IsSprinting) fullSince = Time.time;
        float targetAlpha = Time.time - fullSince < hideDelay ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.deltaTime * 3f);
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }
}
