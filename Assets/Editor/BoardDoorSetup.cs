using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class BoardDoorSetup
{
    private const string ScenePath = "Assets/Scenes/Demo.unity";
    private const string DoorPrefabPath = "Assets/Free Wood Door Pack/Prefab/Wood/Door_1/Door_1_Brown.prefab";

    private readonly struct DoorPlacement
    {
        public DoorPlacement(string wallName, string doorName, bool open)
        {
            WallName = wallName;
            DoorName = doorName;
            Open = open;
        }

        public string WallName { get; }
        public string DoorName { get; }
        public bool Open { get; }
    }

    private static readonly DoorPlacement[] Placements =
    {
        new("Wall_V_F0_C3", "Door_Interior_2_0__3_0", false),
        new("Wall_V_F2_C2", "Door_Interior_1_2__2_2", false),
        new("Wall_H_F4_C3", "Door_Interior_3_3__3_4", false),
        new("Wall_V_F5_C5", "Door_Interior_4_5__5_5", false),
        new("Wall_V_F5_C7", "Door_Interior_6_5__7_5", false),
        new("Wall_H_F2_C7", "Door_Interior_7_1__7_2", false),
        new("Wall_V_F3_C6", "Door_Interior_5_3__6_3", false),
        new("Wall_V_F1_C5", "Door_Interior_4_1__5_1", false),
        new("Wall_H_F0_C5", "Door_Exterior_5_0", true),
        new("Wall_V_F2_C0", "Door_Exterior_0_2", true),
        new("Wall_V_F3_C8", "Door_Exterior_7_3", true),
        new("Wall_H_F6_C2", "Door_Exterior_2_5", true),
    };

    static BoardDoorSetup()
    {
        EditorApplication.delayCall += SetupOpenDemoScene;
    }

    [MenuItem("Tools/Farm Plague/Setup Board Doors")]
    private static void SetupOpenDemoScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || !scene.isLoaded)
            return;

        GameObject walls = GameObject.Find("Board/Walls");
        if (walls == null || walls.transform.Find("Doors") != null)
            return;

        GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
        if (doorPrefab == null)
        {
            Debug.LogError($"BoardDoorSetup: prefab not found at {DoorPrefabPath}.");
            return;
        }

        GameObject container = new("Doors");
        container.transform.SetParent(walls.transform, false);

        foreach (DoorPlacement placement in Placements)
        {
            Transform wall = walls.transform.Find(placement.WallName);
            if (wall == null)
            {
                Debug.LogError($"BoardDoorSetup: wall {placement.WallName} was not found.");
                continue;
            }

            wall.gameObject.SetActive(false);
            GameObject door = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, container.transform);
            door.name = placement.DoorName;
            door.transform.SetPositionAndRotation(
                new Vector3(wall.position.x, wall.position.y - wall.localScale.y * 0.5f, wall.position.z),
                wall.rotation);
            door.transform.localScale = new Vector3(2.2f, 1.19f, 1f);
            ConfigureDoorLeaf(door, placement.Open);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"BoardDoorSetup: placed {Placements.Length} doors in {ScenePath}.");
    }

    private static void ConfigureDoorLeaf(GameObject door, bool open)
    {
        Transform leaf = door.transform.Find("Door");
        if (leaf == null)
            throw new InvalidOperationException($"{door.name} has no Door child.");

        leaf.localRotation = Quaternion.Euler(0f, open ? -90f : 0f, 0f);
        foreach (MonoBehaviour component in leaf.GetComponents<MonoBehaviour>())
        {
            SerializedObject serialized = new(component);
            SerializedProperty openProperty = serialized.FindProperty("open");
            if (openProperty == null)
                continue;
            openProperty.boolValue = open;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            break;
        }
    }
}
