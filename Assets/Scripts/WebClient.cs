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

    private bool requestStarted;
    private float originalFontSize;

    private void Awake()
    {
        if (statusText != null)
            originalFontSize = statusText.fontSize;
    }

    public void RequestSimulation()
    {
        if (requestStarted)
            return;

        requestStarted = true;
        if (statusText != null)
            statusText.fontSize = originalFontSize * 0.5f;
        SetStatus("CARGANDO...");
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
        requestStarted = false;
        if (statusText != null)
        {
            statusText.fontSize = originalFontSize;
            statusText.text = "START";
        }
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
            Debug.LogError($"WebClient: GET {Url} failed: {www.error}", this);
            SetStatus("ERROR DE CONEXION");
            yield break;
        }

        string json = www.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError($"WebClient: GET {Url} returned an empty response.", this);
            SetStatus("RESPUESTA VACIA");
            yield break;
        }

        if (gameManager == null)
        {
            Debug.LogError("WebClient: no GameManager is assigned.", this);
            SetStatus("ERROR INTERNO");
            yield break;
        }

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
}
