// TC2008B Modelacion de Sistemas Multiagentes con graficas computacionales
// C# client to interact with a Python server via GET.

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public sealed class WebClient : MonoBehaviour
{
    private const string Url = "http://localhost:8000/";
    private const float MinimumLoadingTime = 1f;

    [SerializeField] private GameManager gameManager;
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Sprite startButtonBackground;
    [SerializeField] private Sprite playIcon;
    [SerializeField] private TMP_FontAsset font;

    private bool requestStarted;
    private float originalFontSize;
    private float originalCharacterSpacing;
    private GameObject playIconObject;
    private Coroutine loadingAnimation;

    private void Awake()
    {
        ConfigureStartButton();
        if (statusText != null)
        {
            originalFontSize = statusText.fontSize;
            originalCharacterSpacing = statusText.characterSpacing;
        }
    }

    private void OnValidate()
    {
        ConfigureStartButton();
    }

    public void RequestSimulation()
    {
        if (requestStarted)
            return;

        requestStarted = true;
        if (statusText != null)
        {
            statusText.fontSize = originalFontSize * 0.88f;
            statusText.characterSpacing = 2f;
            SetTextInsets(24f, 24f);
        }
        if (playIconObject != null)
            playIconObject.SetActive(false);
        loadingAnimation = StartCoroutine(AnimateLoadingText());
        if (startButton != null)
        {
            ColorBlock colors = startButton.colors;
            colors.disabledColor = colors.normalColor;
            startButton.colors = colors;
            startButton.interactable = false;
        }
        StartCoroutine(GetSimulation());
    }

    public void ResetToStart()
    {
        StopAllCoroutines();
        loadingAnimation = null;
        requestStarted = false;
        if (statusText != null)
        {
            statusText.fontSize = originalFontSize;
            statusText.characterSpacing = originalCharacterSpacing;
            statusText.text = "INICIAR SIMULACION";
            SetTextInsets(116f, 32f);
        }
        if (playIconObject != null)
            playIconObject.SetActive(true);
        if (startButton != null)
        {
            startButton.gameObject.SetActive(true);
            startButton.interactable = true;
        }
    }

    private IEnumerator GetSimulation()
    {
        float loadingStartedAt = Time.realtimeSinceStartup;
        using UnityWebRequest www = UnityWebRequest.Get(Url);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Accept", "application/json");

        yield return www.SendWebRequest();

        float remainingLoadingTime = MinimumLoadingTime - (Time.realtimeSinceStartup - loadingStartedAt);
        if (remainingLoadingTime > 0f)
            yield return new WaitForSecondsRealtime(remainingLoadingTime);

        if (www.result != UnityWebRequest.Result.Success)
        {
            StopLoadingAnimation();
            Debug.LogError($"WebClient: GET {Url} failed: {www.error}", this);
            SetStatus("ERROR DE CONEXION");
            yield break;
        }

        string json = www.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(json))
        {
            StopLoadingAnimation();
            Debug.LogError($"WebClient: GET {Url} returned an empty response.", this);
            SetStatus("RESPUESTA VACIA");
            yield break;
        }

        if (gameManager == null)
        {
            StopLoadingAnimation();
            Debug.LogError("WebClient: no GameManager is assigned.", this);
            SetStatus("ERROR INTERNO");
            yield break;
        }

        StopLoadingAnimation();
        if (gameManager.PlayJson(json))
        {
            if (startButton != null)
                startButton.gameObject.SetActive(false);
        }
        else
        {
            SetStatus("JSON INVALIDO");
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private IEnumerator AnimateLoadingText()
    {
        int dots = 0;
        while (requestStarted)
        {
            SetStatus("CARGANDO" + new string('.', dots));
            dots = (dots + 1) % 4;
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }

    private void StopLoadingAnimation()
    {
        if (loadingAnimation != null)
            StopCoroutine(loadingAnimation);
        loadingAnimation = null;
    }

    private void SetTextInsets(float left, float right)
    {
        if (statusText == null)
            return;
        RectTransform textRect = statusText.rectTransform;
        textRect.offsetMin = new Vector2(left, 14f);
        textRect.offsetMax = new Vector2(-right, -8f);
    }

    private void ConfigureStartButton()
    {
        if (startButton == null)
            return;

        Image background = startButton.GetComponent<Image>();
        if (background != null)
        {
            background.sprite = startButtonBackground;
            background.type = Image.Type.Simple;
            background.color = Color.white;
            startButton.targetGraphic = background;
        }
        ColorBlock colors = startButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        startButton.colors = colors;

        if (statusText != null)
        {
            RectTransform textRect = statusText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            SetTextInsets(requestStarted ? 24f : 116f, requestStarted ? 24f : 32f);
            statusText.text = requestStarted ? statusText.text : "INICIAR SIMULACION";
            statusText.font = font != null ? font : TMP_Settings.defaultFontAsset;
            statusText.fontSize = 31f;
            statusText.fontStyle = FontStyles.Bold;
            statusText.color = Color.white;
            statusText.alignment = TextAlignmentOptions.Center;
        }

        Transform existingIcon = startButton.transform.Find("PlayIcon");
        GameObject iconObject = existingIcon != null
            ? existingIcon.gameObject
            : new GameObject("PlayIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.layer = LayerMask.NameToLayer("UI");
        playIconObject = iconObject;
        iconObject.transform.SetParent(startButton.transform, false);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(48f, 3f);
        iconRect.sizeDelta = new Vector2(60f, 60f);
        Image iconImage = iconObject.GetComponent<Image>();
        iconImage.sprite = playIcon;
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconObject.SetActive(!requestStarted);
    }
}
