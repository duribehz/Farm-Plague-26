using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class UiThemeSetup
{
    private const string ScenePath = "Assets/Scenes/Demo.unity";
    private const string BlueButton = "Assets/UI/PNG/Blue/Double/button_square_gloss.png";
    private const string GreyButton = "Assets/UI/PNG/Grey/Double/button_square_gloss.png";
    private const string OrangeButton = "Assets/UI/PNG/Yellow/Double/button_square_gloss.png";
    private const string YellowButton = "Assets/UI/PNG/Yellow/Double/button_rectangle_depth_gradient.png";
    private const string GreenButton = "Assets/UI/PNG/Green/Double/button_rectangle_depth_gradient.png";
    private const string RedButton = "Assets/UI/PNG/Red/Double/button_rectangle_depth_gradient.png";
    private const string PlayIcon = "Assets/UI/PNG/Extra/Double/icon_play_light.png";
    private const string PauseIcon = "Assets/UI/PNG/Yellow/Double/check_round_grey_circle.png";
    private const string RepeatIcon = "Assets/UI/PNG/Extra/Double/icon_repeat_light.png";
    private const string FirePrefab = "Assets/Prefabs/Virus/Virus2_Fire.prefab";
    private const string SmokePrefab = "Assets/Prefabs/Virus/Virus1_Smoke.prefab";

    [MenuItem("Tools/Farm Plague/Apply Kenney UI Theme")]
    private static void ApplyTheme()
    {
        string[] textures = { BlueButton, GreyButton, OrangeButton, YellowButton, GreenButton, RedButton, PlayIcon, PauseIcon, RepeatIcon };
        bool changedImporter = false;
        foreach (string path in textures)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                continue;
            bool needsBorder = path == BlueButton || path == GreyButton || path == OrangeButton;
            Vector4 border = needsBorder ? new Vector4(20f, 20f, 20f, 20f) : Vector4.zero;
            if (importer.textureType == TextureImporterType.Sprite &&
                !importer.mipmapEnabled && importer.spriteBorder == border)
                continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
            changedImporter = true;
        }
        if (changedImporter)
        {
            EditorApplication.delayCall += ApplyTheme;
            return;
        }

        TMP_FontAsset tmpFont = TMP_Settings.defaultFontAsset;
        if (tmpFont == null)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != ScenePath)
            return;

        Sprite blue = AssetDatabase.LoadAssetAtPath<Sprite>(BlueButton);
        Sprite grey = AssetDatabase.LoadAssetAtPath<Sprite>(GreyButton);
        Sprite orange = AssetDatabase.LoadAssetAtPath<Sprite>(OrangeButton);
        Sprite yellow = AssetDatabase.LoadAssetAtPath<Sprite>(YellowButton);
        Sprite green = AssetDatabase.LoadAssetAtPath<Sprite>(GreenButton);
        Sprite red = AssetDatabase.LoadAssetAtPath<Sprite>(RedButton);
        Sprite play = AssetDatabase.LoadAssetAtPath<Sprite>(PlayIcon);
        Sprite pause = AssetDatabase.LoadAssetAtPath<Sprite>(PauseIcon);
        Sprite repeat = AssetDatabase.LoadAssetAtPath<Sprite>(RepeatIcon);
        GameObject firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FirePrefab);
        GameObject smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmokePrefab);

        AgentHud hud = Object.FindFirstObjectByType<AgentHud>(FindObjectsInactive.Include);
        SetProperties(hud,
            ("cardBackground", blue),
            ("activeCardBackground", blue),
            ("infirmaryCardBackground", grey),
            ("statsCardBackground", orange),
            ("fireIconPrefab", firePrefab),
            ("smokeIconPrefab", smokePrefab),
            ("victoryPanelBackground", green),
            ("defeatPanelBackground", red),
            ("endScreenButtonBackground", red),
            ("font", tmpFont));
        SetColor(hud, "cardColor", Color.white);
        SetColor(hud, "activeCardColor", Color.white);

        SimulationControls controls = Object.FindFirstObjectByType<SimulationControls>(FindObjectsInactive.Include);
        SetProperties(controls,
            ("pauseButtonBackground", yellow),
            ("pauseIcon", pause),
            ("restartButtonBackground", red),
            ("restartIcon", repeat),
            ("font", tmpFont));

        WebClient webClient = Object.FindFirstObjectByType<WebClient>(FindObjectsInactive.Include);
        SetProperties(webClient,
            ("startButtonBackground", green),
            ("playIcon", play),
            ("font", tmpFont));

        CanvasScaler scaler = Object.FindFirstObjectByType<CanvasScaler>(FindObjectsInactive.Include);
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            EditorUtility.SetDirty(scaler);
        }

        hud?.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        controls?.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        webClient?.SendMessage("ConfigureStartButton", SendMessageOptions.DontRequireReceiver);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("UiThemeSetup: Kenney UI theme applied. Save the scene to keep the changes.");
    }

    private static void SetProperties(Object target, params (string name, Object value)[] properties)
    {
        if (target == null)
            return;
        SerializedObject serialized = new(target);
        foreach ((string name, Object value) in properties)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null)
                property.objectReferenceValue = value;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetColor(Object target, string propertyName, Color value)
    {
        if (target == null)
            return;
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.colorValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
