using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class AgentHud : MonoBehaviour
{
    private const int AgentCount = 6;

    [Header("Estilo")]
    [SerializeField] private Sprite cardBackground;
    [SerializeField] private Sprite activeCardBackground;
    [SerializeField] private Sprite victoryPanelBackground;
    [SerializeField] private Sprite defeatPanelBackground;
    [SerializeField] private Sprite endScreenButtonBackground;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color cardColor = Color.white;
    [SerializeField] private Color activeCardColor = Color.white;
    [SerializeField, Min(1f)] private float activeCardScale = 1.04f;

    private readonly Dictionary<int, TMP_Text> actionValues = new();
    private readonly Dictionary<int, TMP_Text> victimValues = new();
    private readonly Dictionary<int, RectTransform> cards = new();
    private readonly Dictionary<int, Image> cardBackgrounds = new();
    private readonly Dictionary<int, int> rescuedVictims = new();
    private CanvasGroup canvasGroup;
    private GameObject statsRoot;
    private TMP_Text structuralDamageValue;
    private TMP_Text killedVictimsValue;
    private int structuralDamage;
    private int killedVictims;
    private GameObject endScreenRoot;
    private Image endScreenPanel;
    private TMP_Text endScreenTitle;
    private TMP_Text endScreenSummary;

    public int StructuralDamage => structuralDamage;
    public int KilledVictims => killedVictims;

    private void OnEnable()
    {
        BuildCards();
        BuildStatsHud();
        BuildEndScreen();
        if (!Application.isPlaying)
        {
            SetStructuralDamage(0);
            SetKilledVictims(0);
            SetStatsVisible(true);
            HideEndScreen();
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;
        BuildCards();
        BuildStatsHud();
        BuildEndScreen();
        SetStructuralDamage(0);
        SetKilledVictims(0);
        SetStatsVisible(true);
        HideEndScreen();
    }

    public void Initialize(IEnumerable<int> agentIds)
    {
        BuildCards();
        BuildStatsHud();
        SetStructuralDamage(0);
        SetKilledVictims(0);
        SetStatsVisible(true);
        rescuedVictims.Clear();
        foreach (int id in agentIds)
        {
            if (actionValues.TryGetValue(id, out TMP_Text actionValue))
                actionValue.text = "4";
            if (victimValues.TryGetValue(id, out TMP_Text victimValue))
                victimValue.text = "0";
            rescuedVictims[id] = 0;
        }
        SetActiveAgent(0);
        SetVisible(true);
    }

    public void AddRescue(int agentId)
    {
        rescuedVictims.TryGetValue(agentId, out int rescued);
        rescuedVictims[agentId] = ++rescued;
        if (victimValues.TryGetValue(agentId, out TMP_Text value))
            value.text = rescued.ToString();
    }

    public void SetActionPoints(int agentId, int points)
    {
        if (actionValues.TryGetValue(agentId, out TMP_Text value))
            value.text = Mathf.Clamp(points, 0, 4).ToString();
    }

    public void SetActiveAgent(int agentId)
    {
        foreach (KeyValuePair<int, RectTransform> card in cards)
        {
            bool active = card.Key == agentId;
            card.Value.localScale = Vector3.one * (active ? activeCardScale : 1f);
            if (cardBackgrounds.TryGetValue(card.Key, out Image background))
            {
                background.sprite = cardBackground;
                background.color = Color.white;
            }
        }
    }

    public void SetStructuralDamage(int total)
    {
        structuralDamage = Mathf.Max(0, total);
        if (structuralDamageValue != null)
            structuralDamageValue.text = structuralDamage.ToString();
    }

    public void AddStructuralDamage(int amount) => SetStructuralDamage(structuralDamage + Mathf.Max(0, amount));

    public void SetKilledVictims(int total)
    {
        killedVictims = Mathf.Max(0, total);
        if (killedVictimsValue != null)
            killedVictimsValue.text = killedVictims.ToString();
    }

    public void AddKilledVictim() => SetKilledVictims(killedVictims + 1);

    public void SetStatsVisible(bool visible)
    {
        if (statsRoot != null)
            statsRoot.SetActive(visible);
    }

    public void ShowEndScreen(bool victory, int rescued, int killed, int damage)
    {
        BuildEndScreen();
        if (endScreenRoot == null)
            return;

        endScreenPanel.sprite = victory ? victoryPanelBackground : defeatPanelBackground;
        endScreenPanel.color = endScreenPanel.sprite != null
            ? Color.white
            : victory ? new Color(0.25f, 0.78f, 0.42f, 1f) : new Color(0.94f, 0.25f, 0.3f, 1f);
        endScreenTitle.text = victory ? "VICTORIA" : "DERROTA";
        endScreenSummary.text = $"RESCATADOS  {rescued}\nELIMINADOS  {killed}\nDAÑO ESTRUCTURAL  {damage}/24";
        endScreenRoot.SetActive(true);
        endScreenRoot.transform.SetAsLastSibling();
    }

    public void HideEndScreen()
    {
        if (endScreenRoot != null)
            endScreenRoot.SetActive(false);
    }

    public void SetVisible(bool visible)
    {
        EnsureCanvasGroup();
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private void BuildCards()
    {
        EnsureCanvasGroup();
        ConfigureRoot();
        actionValues.Clear();
        victimValues.Clear();
        cards.Clear();
        cardBackgrounds.Clear();

        for (int id = 1; id <= AgentCount; id++)
        {
            Transform existing = transform.Find($"AgentCard_{id}");
            GameObject card = existing != null ? existing.gameObject : CreateUiObject($"AgentCard_{id}", transform);
            ConfigureCard(card, id);
        }
    }

    private void ConfigureRoot()
    {
        RectTransform rect = GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(16f, -16f);
        rect.sizeDelta = new Vector2(340f, 570f);
        rect.localScale = Vector3.one;

        VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(gameObject);
        layout.spacing = 9f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void ConfigureCard(GameObject card, int id)
    {
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(320f, 84f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.localScale = Vector3.one;

        Image background = GetOrAdd<Image>(card);
        background.sprite = cardBackground;
        background.type = Image.Type.Sliced;
        background.color = cardColor;
        background.raycastTarget = false;
        cards[id] = rect;
        cardBackgrounds[id] = background;

        ConfigureIcon(GetChild(card.transform, "PersonIconSlot"), new Vector2(12f, -12f), new Vector2(44f, 58f));
        ConfigureIcon(GetChild(card.transform, "ActionIconSlot"), new Vector2(218f, -12f), new Vector2(29f, 29f));
        ConfigureIcon(GetChild(card.transform, "VictimIconSlot"), new Vector2(216f, -49f), new Vector2(36f, 24f));

        ConfigureText(GetChild(card.transform, "AgentLabel"), $"AGENTE {id}", new Vector2(64f, -22f), new Vector2(148f, 38f), 21f, TextAlignmentOptions.Left);
        actionValues[id] = ConfigureText(GetChild(card.transform, "ActionValue"), "4", new Vector2(258f, -10f), new Vector2(48f, 34f), 27f);
        victimValues[id] = ConfigureText(GetChild(card.transform, "VictimValue"), "0", new Vector2(258f, -46f), new Vector2(48f, 30f), 25f);
    }

    private void BuildStatsHud()
    {
        Transform canvas = GetComponentInParent<Canvas>()?.transform;
        if (canvas == null)
            return;

        Transform existing = canvas.Find("SimulationStatsHud");
        statsRoot = existing != null ? existing.gameObject : CreateUiObject("SimulationStatsHud", canvas);
        RectTransform root = statsRoot.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(1f, 0f);
        root.anchorMax = new Vector2(1f, 0f);
        root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-16f, 96f);
        root.sizeDelta = new Vector2(180f, 177f);

        VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(statsRoot);
        layout.spacing = 9f;
        layout.childAlignment = TextAnchor.LowerRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        structuralDamageValue = ConfigureStatCard(GetChild(statsRoot.transform, "StructuralDamageCard"));
        killedVictimsValue = ConfigureStatCard(GetChild(statsRoot.transform, "KilledVictimsCard"));
    }

    private TMP_Text ConfigureStatCard(GameObject card)
    {
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180f, 84f);

        Image background = GetOrAdd<Image>(card);
        background.sprite = cardBackground;
        background.type = Image.Type.Sliced;
        background.color = Color.white;
        background.raycastTarget = false;

        GameObject iconSlot = GetChild(card.transform, "IconSlot");
        ConfigureIcon(iconSlot, new Vector2(8f, -4f), new Vector2(76f, 76f));
        Image icon = iconSlot.GetComponent<Image>();
        icon.color = Color.white;

        GetChild(card.transform, "Label").SetActive(false);
        return ConfigureText(GetChild(card.transform, "Value"), "0",
            new Vector2(88f, -10f), new Vector2(76f, 64f), 34f);
    }

    private void BuildEndScreen()
    {
        Transform canvas = GetComponentInParent<Canvas>()?.transform;
        if (canvas == null)
            return;

        Transform existing = canvas.Find("EndScreenHud");
        endScreenRoot = existing != null ? existing.gameObject : CreateUiObject("EndScreenHud", canvas);
        RectTransform root = endScreenRoot.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        Image shade = GetOrAdd<Image>(endScreenRoot);
        shade.sprite = null;
        shade.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        shade.raycastTarget = true;

        GameObject panelObject = GetChild(endScreenRoot.transform, "Panel");
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(620f, 390f);
        endScreenPanel = GetOrAdd<Image>(panelObject);
        endScreenPanel.type = Image.Type.Sliced;
        endScreenPanel.raycastTarget = true;

        endScreenTitle = ConfigureCenteredText(GetChild(panelObject.transform, "Title"),
            new Vector2(0f, 112f), new Vector2(540f, 80f), 54f);
        endScreenSummary = ConfigureCenteredText(GetChild(panelObject.transform, "Summary"),
            new Vector2(0f, 8f), new Vector2(520f, 130f), 25f);
        endScreenSummary.lineSpacing = 12f;

        GameObject buttonObject = GetChild(panelObject.transform, "RestartButton");
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -132f);
        buttonRect.sizeDelta = new Vector2(230f, 68f);
        Image buttonImage = GetOrAdd<Image>(buttonObject);
        buttonImage.sprite = endScreenButtonBackground;
        buttonImage.type = Image.Type.Simple;
        buttonImage.color = Color.white;
        Button button = GetOrAdd<Button>(buttonObject);
        button.targetGraphic = buttonImage;
        if (Application.isPlaying)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(RestartFromEndScreen);
        }
        ConfigureCenteredText(GetChild(buttonObject.transform, "Label"),
            Vector2.zero, new Vector2(210f, 56f), 24f).text = "REINICIAR";
    }

    private TMP_Text ConfigureCenteredText(GameObject target, Vector2 position, Vector2 size, float fontSize)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(target);
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private void RestartFromEndScreen()
    {
        SimulationControls controls = FindFirstObjectByType<SimulationControls>(FindObjectsInactive.Include);
        controls?.Restart();
    }

    private static void ConfigureIcon(GameObject slot, Vector2 position, Vector2 size)
    {
        RectTransform rect = slot.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = GetOrAdd<Image>(slot);
        image.preserveAspect = true;
        if (image.sprite != null && image.color.a <= 0f)
            image.color = Color.white;
        image.raycastTarget = false;
    }

    private TMP_Text ConfigureText(GameObject textObject, string value, Vector2 position,
        Vector2 size, float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(textObject);
        text.text = value;
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private void EnsureCanvasGroup()
    {
        canvasGroup = GetOrAdd<CanvasGroup>(gameObject);
    }

    private static GameObject GetChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        return child != null ? child.gameObject : CreateUiObject(childName, parent);
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject instance = new(objectName, typeof(RectTransform));
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
