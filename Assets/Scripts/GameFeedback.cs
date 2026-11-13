using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives the player clear feedback for three moments:
/// 1. Clodi gets hit by an enemy   -> red screen edge flash, "-1 Health" popup, effect, sound
/// 2. A mud ball hits an enemy     -> "Stunned!" popup, effect, sound
/// 3. Clodi collects an egg        -> golden screen edge flash, "Egg x/5" popup, effect, sound
///
/// Put this on the GameManager object (only once per scene).
/// The screen flash and the popup texts are created by code, so they work without any setup.
/// Effects (e.g. Cartoon FX prefabs) and sounds are optional: drag them into the slots in the Inspector.
/// </summary>
public class GameFeedback : MonoBehaviour
{
    public static GameFeedback Instance { get; private set; }

    [Header("Screen Flash")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0f, 0f, 0.6f);
    [SerializeField] private Color eggFlashColor = new Color(1f, 0.85f, 0.2f, 0.45f);
    [SerializeField] private float flashDuration = 0.5f;

    [Header("Popup Text")]
    [SerializeField] private Color hitTextColor = new Color(1f, 0.3f, 0.3f);
    [SerializeField] private Color enemyTextColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private Color eggTextColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private int popupFontSize = 34;
    [SerializeField] private float popupDuration = 1f;
    [SerializeField] private float popupRiseSpeed = 80f; // pixels per second

    [Header("Effects (optional, e.g. Cartoon FX prefabs)")]
    [SerializeField] private GameObject playerHitEffect;
    [SerializeField] private GameObject enemyHitEffect;
    [SerializeField] private GameObject eggCollectEffect;
    [SerializeField] private float effectLifetime = 3f;

    [Header("Sounds (optional)")]
    [SerializeField] private AudioClip playerHitSound;
    [SerializeField] private AudioClip enemyHitSound;
    [SerializeField] private AudioClip eggCollectSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.8f;

    private class Popup
    {
        public Text text;
        public Vector3 worldPosition;
        public float age;
    }

    private readonly List<Popup> popups = new List<Popup>();
    private Image flashImage;
    private Color currentFlashColor;
    private float flashTimer;
    private Transform canvasTransform;
    private AudioSource audioSource;
    private Font font;

    private void Awake()
    {
        Instance = this;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D sound, same volume everywhere

        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }

        BuildOverlay();
    }

    private void Update()
    {
        // Unscaled time, so the flash and popups still finish when the game is paused or over
        float deltaTime = Time.unscaledDeltaTime;

        if (flashTimer > 0f)
        {
            flashTimer -= deltaTime;
            Color color = currentFlashColor;
            color.a *= Mathf.Clamp01(flashTimer / flashDuration);
            flashImage.color = color;
        }

        UpdatePopups(deltaTime);
    }

    // ---------- Public calls from other scripts ----------

    /// <summary>Called by PlayerHealth when Clodi loses health.</summary>
    public void PlayerHit(Vector3 position)
    {
        Flash(hitFlashColor);
        SpawnEffect(playerHitEffect, position + Vector3.up * 1f);
        PlaySound(playerHitSound);
        ShowPopup("-1 Health", position + Vector3.up * 2f, hitTextColor);
    }

    /// <summary>Called by MudBall when it hits an enemy.</summary>
    public void EnemyHit(Vector3 position)
    {
        SpawnEffect(enemyHitEffect, position);
        PlaySound(enemyHitSound);
        ShowPopup("Stunned!", position + Vector3.up * 1.5f, enemyTextColor);
    }

    /// <summary>Called by Collectible when Clodi picks up an egg.</summary>
    public void EggCollected(Vector3 position, int collected, int total)
    {
        Flash(eggFlashColor);
        SpawnEffect(eggCollectEffect, position);
        PlaySound(eggCollectSound);

        string message = collected >= total ? "All eggs! Go to the nest!" : $"Egg {collected}/{total}";
        ShowPopup(message, position + Vector3.up * 1f, eggTextColor);
    }

    // ---------- Helpers ----------

    private void Flash(Color color)
    {
        currentFlashColor = color;
        flashTimer = flashDuration;
    }

    private void SpawnEffect(GameObject effectPrefab, Vector3 position)
    {
        if (effectPrefab == null) return;
        GameObject effect = Instantiate(effectPrefab, position, Quaternion.identity);
        Destroy(effect, effectLifetime);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        audioSource.PlayOneShot(clip, soundVolume);
    }

    private void ShowPopup(string message, Vector3 worldPosition, Color color)
    {
        if (font == null) return;

        GameObject textObject = new GameObject("Popup " + message);
        textObject.transform.SetParent(canvasTransform, false);

        Text text = textObject.AddComponent<Text>();
        text.text = message;
        text.font = font;
        text.fontSize = popupFontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        // Black outline so the text is readable on every background
        Outline outline = textObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);

        popups.Add(new Popup { text = text, worldPosition = worldPosition });
        PlacePopup(popups[popups.Count - 1]);
    }

    private void UpdatePopups(float deltaTime)
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            Popup popup = popups[i];
            popup.age += deltaTime;

            if (popup.age >= popupDuration || popup.text == null)
            {
                if (popup.text != null) Destroy(popup.text.gameObject);
                popups.RemoveAt(i);
                continue;
            }

            PlacePopup(popup);

            // Fade out in the second half of its life
            Color color = popup.text.color;
            color.a = Mathf.Clamp01(2f * (1f - popup.age / popupDuration));
            popup.text.color = color;
        }
    }

    private void PlacePopup(Popup popup)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPoint = cam.WorldToScreenPoint(popup.worldPosition);
        bool inFrontOfCamera = screenPoint.z > 0f;
        popup.text.enabled = inFrontOfCamera;

        // Follows the world position and floats upwards
        screenPoint.y += popup.age * popupRiseSpeed;
        screenPoint.z = 0f;
        popup.text.transform.position = screenPoint;
    }

    private void BuildOverlay()
    {
        // Own canvas on top of all other UI
        GameObject canvasObject = new GameObject("Feedback Canvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasTransform = canvasObject.transform;

        // Full screen image that only colours the screen edges (vignette)
        GameObject flashObject = new GameObject("Screen Flash");
        flashObject.transform.SetParent(canvasTransform, false);
        flashImage = flashObject.AddComponent<Image>();
        flashImage.sprite = CreateEdgeSprite();
        flashImage.color = Color.clear;
        flashImage.raycastTarget = false;

        RectTransform rect = flashImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>White texture: transparent in the middle, solid at the edges.</summary>
    private Sprite CreateEdgeSprite()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Distance from the centre: 0 in the middle, about 1 at the edges
                float dx = (x / (float)(size - 1)) * 2f - 1f;
                float dy = (y / (float)(size - 1)) * 2f - 1f;
                float distance = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + new Vector2(dx, dy).magnitude * 0.4f;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, distance));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
