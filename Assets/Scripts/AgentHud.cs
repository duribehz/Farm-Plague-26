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
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color cardColor = new(0.93f, 0.04f, 0.08f, 1f);
    [SerializeField] private Color activeCardColor = new(0.65f, 0.01f, 0.03f, 1f);
    [SerializeField, Min(1f)] private float activeCardScale = 1.12f;

    private readonly Dictionary<int, TMP_Text> actionValues = new();
    private readonly Dictionary<int, TMP_Text> victimValues = new();
    private readonly Dictionary<int, RectTransform> cards = new();
    private readonly Dictionary<int, Image> cardBackgrounds = new();
    private readonly Dictionary<int, int> rescuedVictims = new();
    private CanvasGroup canvasGroup;

    private void OnEnable()
    {
        BuildCards();
    }

    public void Initialize(IEnumerable<int> agentIds)
    {
        BuildCards();
        rescuedVictims.Clear();
        foreach (int id in agentIds)
        {
            if (actionValues.TryGetValue(id, out TMP_Text actionValue))
                actionValue.text = "-";
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

    public void SetActiveAgent(int agentId)
    {
        foreach (KeyValuePair<int, RectTransform> card in cards)
        {
            bool active = card.Key == agentId;
            card.Value.localScale = Vector3.one * (active ? activeCardScale : 1f);
            if (cardBackgrounds.TryGetValue(card.Key, out Image background))
                background.color = active ? activeCardColor : cardColor;
        }
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
        rect.sizeDelta = new Vector2(175f, 328f);
        rect.localScale = Vector3.one * 2f;

        VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(gameObject);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void ConfigureCard(GameObject card, int id)
    {
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(175f, 48f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.localScale = Vector3.one;

        Image background = GetOrAdd<Image>(card);
        background.sprite = cardBackground;
        background.type = Image.Type.Sliced;
        background.color = cardColor;
        background.raycastTarget = false;
        cards[id] = rect;
        cardBackgrounds[id] = background;

        ConfigureIcon(GetChild(card.transform, "PersonIconSlot"), new Vector2(8f, -6f), new Vector2(36f, 36f));
        ConfigureIcon(GetChild(card.transform, "ActionIconSlot"), new Vector2(68f, -3f), new Vector2(20f, 20f));
        ConfigureIcon(GetChild(card.transform, "VictimIconSlot"), new Vector2(118f, -3f), new Vector2(28f, 20f));

        actionValues[id] = ConfigureText(GetChild(card.transform, "ActionValue"), "-", new Vector2(61f, -23f));
        victimValues[id] = ConfigureText(GetChild(card.transform, "VictimValue"), "0", new Vector2(111f, -23f));
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

    private TMP_Text ConfigureText(GameObject textObject, string value, Vector2 position)
    {
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(34f, 22f);

        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(textObject);
        text.text = value;
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = 20f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
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
