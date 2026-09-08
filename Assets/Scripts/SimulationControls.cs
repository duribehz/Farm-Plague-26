using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class SimulationControls : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private WebClient webClient;
    [SerializeField] private Sprite buttonBackground;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color buttonColor = new(0.93f, 0.04f, 0.08f, 1f);

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
        root.sizeDelta = new Vector2(270f, 48f);
        root.localScale = Vector3.one * 1.5f;

        HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(gameObject);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        pauseButton = ConfigureButton(GetChild("PauseButton"), "PAUSA", out pauseText);
        restartButton = ConfigureButton(GetChild("RestartButton"), "RESTART", out _);
    }

    private Button ConfigureButton(GameObject target, string label, out TMP_Text labelText)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(130f, 48f);

        Image image = GetOrAdd<Image>(target);
        image.sprite = buttonBackground;
        image.type = Image.Type.Sliced;
        image.color = buttonColor;

        Button button = GetOrAdd<Button>(target);
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        button.colors = colors;

        GameObject textObject = GetChild(target.transform, "Label");
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(textObject);
        text.text = label;
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = 19f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        labelText = text;
        return button;
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
