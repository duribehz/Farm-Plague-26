using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class SimulationControls : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private WebClient webClient;
    [SerializeField] private Sprite pauseButtonBackground;
    [SerializeField] private Sprite pauseIcon;
    [SerializeField] private Sprite restartButtonBackground;
    [SerializeField] private Sprite restartIcon;
    [SerializeField] private TMP_FontAsset font;

    private Button pauseButton;
    private Button restartButton;
    private TMP_Text pauseText;
    private bool paused;

    private void OnEnable()
    {
        BuildControls();
        if (Application.isPlaying)
        {
            pauseButton.onClick.RemoveListener(TogglePause);
            pauseButton.onClick.AddListener(TogglePause);
            restartButton.onClick.RemoveListener(Restart);
            restartButton.onClick.AddListener(Restart);
        }
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        if (visible)
            ResetPauseState();
    }

    public void TogglePause()
    {
        paused = !paused;
        if (paused)
            gameManager?.Pause();
        else
            gameManager?.Resume();
        if (pauseText != null)
            pauseText.text = paused ? "CONTINUAR" : "PAUSA";
    }

    public void Restart()
    {
        gameManager?.ReturnToStart();
        webClient?.ResetToStart();
    }

    private void ResetPauseState()
    {
        paused = false;
        gameManager?.Resume();
        if (pauseText != null)
            pauseText.text = "PAUSA";
    }

    private void BuildControls()
    {
        RectTransform root = GetComponent<RectTransform>();
        root.anchorMin = new Vector2(1f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-16f, -16f);
        root.sizeDelta = new Vector2(350f, 68f);
        root.localScale = Vector3.one;

        HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(gameObject);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        pauseButton = ConfigureButton(GetChild("PauseButton"), "PAUSA", pauseButtonBackground, pauseIcon, out pauseText);
        restartButton = ConfigureButton(GetChild("RestartButton"), "REINICIAR", restartButtonBackground, restartIcon, out _);
    }

    private Button ConfigureButton(GameObject target, string label, Sprite background, Sprite icon, out TMP_Text labelText)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(170f, 64f);

        Image image = GetOrAdd<Image>(target);
        image.sprite = background;
        image.type = Image.Type.Simple;
        image.color = Color.white;

        Button button = GetOrAdd<Button>(target);
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        button.colors = colors;

        GameObject textObject = GetChild(target.transform, "Label");
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(icon != null ? 43f : 8f, 7f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(textObject);
        text.text = label;
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = 19f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        ConfigureButtonIcon(target.transform, icon);
        labelText = text;
        return button;
    }

    private static void ConfigureButtonIcon(Transform parent, Sprite sprite)
    {
        GameObject iconObject = GetChild(parent, "Icon");
        RectTransform rect = iconObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(16f, 2f);
        rect.sizeDelta = new Vector2(28f, 28f);
        Image image = GetOrAdd<Image>(iconObject);
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        iconObject.SetActive(sprite != null);
    }

    private GameObject GetChild(string childName) => GetChild(transform, childName);

    private static GameObject GetChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child.gameObject;
        GameObject instance = new(childName, typeof(RectTransform));
        instance.layer = LayerMask.NameToLayer("UI");
        instance.transform.SetParent(parent, false);
        return instance;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
}
