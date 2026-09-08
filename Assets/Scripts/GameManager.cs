using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class GameManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject agentPrefab;
    [SerializeField] private GameObject victimPrefab;
    [SerializeField] private GameObject pointOfInterestPrefab;
    [SerializeField] private GameObject fakePrefab;
    [SerializeField] private GameObject smokePrefab;
    [SerializeField] private GameObject firePrefab;

    [Header("Playback")]
    [SerializeField, Min(0.05f)] private float moveDuration = 0.8f;
    [SerializeField, Min(0f)] private float eventDelay = 0.35f;
    [SerializeField, Min(0.05f)] private float playbackSpeed = 1f;
    [SerializeField, Min(0f)] private float agentHeight = 0.75f;
    [SerializeField, Min(0f)] private float markerHeight = 0.35f;
    [SerializeField, Min(0.1f)] private float hazardSize = 1.5f;

    private readonly Dictionary<int, GameObject> agents = new();
    private readonly Dictionary<Vector2Int, Transform> tiles = new();
    private readonly Dictionary<Vector2Int, GameObject> pointsOfInterest = new();
    private readonly Dictionary<Vector2Int, GameObject> occupants = new();
    private readonly Dictionary<Vector2Int, GameObject> hazards = new();
    private readonly Dictionary<int, GameObject> carriedVictims = new();
    private readonly List<GameObject> runtimeObjects = new();

    private Coroutine playback;
    private JObject simulation;
    private bool paused;

    private void Start()
    {
        CacheTiles();
    }

    public bool PlayJson(string json)
    {
        StopPlayback();
        ResetSimulation();

        try
        {
            simulation = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"GameManager: invalid simulation JSON. {exception.Message}", this);
            return false;
        }

        if (simulation["map"] == null || simulation["steps"] is not JArray)
        {
            Debug.LogError("GameManager: the JSON must contain map and steps.", this);
            return false;
        }

        InitializeSimulation();
        playback = StartCoroutine(PlaybackRoutine());
        return true;
    }

    public void Pause() => paused = true;
    public void Resume() => paused = false;

    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Max(0.05f, speed);
    }

    public void StopPlayback()
    {
        if (playback != null)
            StopCoroutine(playback);
        playback = null;
        paused = false;
    }

    private void InitializeSimulation()
    {
        if (simulation["map"]?["cells"] is JArray rows)
        {
            for (int y = 0; y < rows.Count; y++)
            {
                if (rows[y] is not JArray row)
                    continue;
                for (int x = 0; x < row.Count; x++)
                    ApplyCellChange(new Vector2Int(x, y), (string)row[x]);
            }
        }

        if (simulation["agents"] is not JArray initialAgents)
            return;
        foreach (JToken agent in initialAgents)
            EnsureAgent((int)agent["id"], ReadCell(agent["pos"]));
    }

    private IEnumerator PlaybackRoutine()
    {
        foreach (JToken step in (JArray)simulation["steps"])
        {
            foreach (JToken evt in step["events"] as JArray ?? new JArray())
            {
                yield return WaitWhilePaused();
                yield return PlayEvent(evt);
            }
        }

        playback = null;
    }

    private IEnumerator PlayEvent(JToken evt)
    {
        string type = (string)evt["type"];
        int agentId = (int?)evt["agent"] ?? 0;

        switch (type)
        {
            case "move":
                GameObject agent = EnsureAgent(agentId, ReadCell(evt["from"]));
                if (agent != null && TryGetTile(ReadCell(evt["to"]), out Transform tile))
                    yield return MoveAgent(agent, TilePosition(tile, agentHeight));
                yield break;
            case "cell_change":
                ApplyCellChange(ReadCell(evt["pos"]), (string)evt["to"]);
                break;
            case "pickup":
                Pickup(agentId, ReadCell(evt["pos"]), (string)evt["kind"]);
                break;
            case "rescue":
                Rescue(agentId);
                break;
            case "extinguish":
                RemoveFrom(hazards, ReadCell(evt["pos"]));
                break;
            case "fire_spread":
                if (evt["cells"] is JArray spreadCells)
                    foreach (JToken target in spreadCells)
                        ApplyCellChange(ReadCell(target), "fire");
                break;
            case "kill":
                RemoveFrom(occupants, ReadCell(evt["pos"]));
                RemoveFrom(pointsOfInterest, ReadCell(evt["pos"]));
                break;
            case "door_open":
            case "wall_chop":
                // The board geometry is static; these events retain their place in playback timing.
                break;
            default:
                Debug.LogWarning($"GameManager: unsupported event '{type}'.", this);
                break;
        }

        yield return WaitSeconds(eventDelay);
    }

    private IEnumerator MoveAgent(GameObject agent, Vector3 destination)
    {
        Vector3 start = agent.transform.position;
        AgentAnimation agentAnimation = agent.GetComponent<AgentAnimation>();
        agentAnimation?.BeginMove(destination - start);
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            yield return WaitWhilePaused();
            float scaledDeltaTime = Time.deltaTime * playbackSpeed;
            elapsed += scaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / moveDuration);
            agent.transform.position = Vector3.Lerp(start, destination, progress);
            agentAnimation?.AnimateMove(progress, scaledDeltaTime);
            yield return null;
        }
        agent.transform.position = destination;
        agentAnimation?.EndMove();
    }

    private GameObject EnsureAgent(int id, Vector2Int position)
    {
        if (agents.TryGetValue(id, out GameObject existing))
            return existing;
        if (!TryGetTile(position, out Transform tile))
        {
            Debug.LogWarning($"GameManager: no valid initial cell for agent {id}.", this);
            return null;
        }

        GameObject instance = Create(agentPrefab, TilePosition(tile, agentHeight), $"Agent_{id}");
        if (instance != null)
        {
            if (instance.GetComponent<AgentAnimation>() == null)
                instance.AddComponent<AgentAnimation>();
            agents[id] = instance;
        }
        return instance;
    }

    private void ApplyCellChange(Vector2Int cell, string state)
    {
        RemoveFrom(pointsOfInterest, cell);
        RemoveFrom(occupants, cell);

        if (state == "fire" || state == "smoke" || state == "none" || state == "exit")
        {
            SetHazard(cell, state);
            return;
        }

        RemoveFrom(hazards, cell);
        if (state == "unknown")
        {
            SpawnPointOfInterest(cell);
            return;
        }

        if ((state == "victim" || state == "fake") && TryGetTile(cell, out Transform tile))
        {
            GameObject prefab = state == "victim" ? victimPrefab : fakePrefab;
            GameObject instance = Create(prefab, TilePosition(tile, markerHeight), $"{state}_{cell.x}_{cell.y}");
            if (instance != null)
                occupants[cell] = instance;
        }
    }

    private void Pickup(int agentId, Vector2Int cell, string kind)
    {
        RemoveFrom(pointsOfInterest, cell);
        occupants.Remove(cell, out GameObject marker);
        if (kind != "victim")
        {
            DestroyRuntime(marker);
            return;
        }
        if (!agents.TryGetValue(agentId, out GameObject agent))
            return;

        marker ??= Create(victimPrefab, agent.transform.position, $"CarriedVictim_{agentId}");
        if (marker == null)
            return;
        agent.GetComponent<AgentAnimation>()?.AttachVictim(marker);
        carriedVictims[agentId] = marker;
    }

    private void Rescue(int agentId)
    {
        if (!carriedVictims.Remove(agentId, out GameObject victim))
            return;
        DestroyRuntime(victim);
        if (agents.TryGetValue(agentId, out GameObject agent))
            agent.GetComponent<AgentAnimation>()?.StopCarrying();
    }

    private void SpawnPointOfInterest(Vector2Int cell)
    {
        if (pointsOfInterest.ContainsKey(cell) || !TryGetTile(cell, out Transform tile))
            return;
        GameObject instance = Create(pointOfInterestPrefab, TilePosition(tile, markerHeight), $"POI_{cell.x}_{cell.y}");
        if (instance != null)
            pointsOfInterest[cell] = instance;
    }

    private void SetHazard(Vector2Int cell, string state)
    {
        if (!TryGetTile(cell, out Transform tile))
            return;

        RemoveFrom(hazards, cell);
        if (state == "none" || state == "exit")
            return;

        GameObject prefab = state == "fire" ? firePrefab : smokePrefab;
        GameObject instance = prefab != null
            ? Create(prefab, TilePosition(tile, markerHeight), $"{state}_{cell.x}_{cell.y}")
            : CreateFallbackHazard(tile, state, cell);
        if (instance == null)
            return;
        if (prefab != null)
            FitHazardToTile(instance, tile);
        hazards[cell] = instance;
    }

    private void FitHazardToTile(GameObject instance, Transform tile)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"GameManager: {instance.name} has no Renderer.", instance);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        float currentSize = Mathf.Max(bounds.size.x, bounds.size.z);
        if (currentSize > 0.001f)
            instance.transform.localScale *= hazardSize / currentSize;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        instance.transform.position += new Vector3(
            tile.position.x - bounds.center.x,
            tile.position.y + 0.1f - bounds.min.y,
            tile.position.z - bounds.center.z);
    }

    private GameObject CreateFallbackHazard(Transform tile, string state, Vector2Int cell)
    {
        bool isFire = state == "fire";
        GameObject instance = GameObject.CreatePrimitive(isFire ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
        instance.name = $"{state}_{cell.x}_{cell.y}_Fallback";
        instance.transform.SetParent(transform, true);
        instance.transform.position = TilePosition(tile, isFire ? 0.9f : 0.45f);
        instance.transform.localScale = isFire
            ? new Vector3(1.2f, 1.8f, 1.2f)
            : new Vector3(1.5f, 0.25f, 1.5f);

        Renderer hazardRenderer = instance.GetComponent<Renderer>();
        if (hazardRenderer != null)
            hazardRenderer.material.color = isFire
                ? new Color(1f, 0.18f, 0.02f)
                : new Color(0.35f, 0.35f, 0.35f);
        Collider hazardCollider = instance.GetComponent<Collider>();
        if (hazardCollider != null)
            Destroy(hazardCollider);

        runtimeObjects.Add(instance);
        return instance;
    }

    private void CacheTiles()
    {
        tiles.Clear();
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!candidate.name.StartsWith("Tile_", StringComparison.Ordinal))
                continue;
            string[] parts = candidate.name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int row) && int.TryParse(parts[2], out int column))
                tiles[new Vector2Int(column, row)] = candidate;
        }
    }

    private bool TryGetTile(Vector2Int cell, out Transform tile)
    {
        if (tiles.TryGetValue(cell, out tile))
            return true;
        Debug.LogWarning($"GameManager: Tile_{cell.y}_{cell.x} was not found.", this);
        return false;
    }

    private static Vector2Int ReadCell(JToken token) =>
        token is JArray values && values.Count >= 2
            ? new Vector2Int((int)values[0], (int)values[1])
            : default;

    private static Vector3 TilePosition(Transform tile, float height) => tile.position + Vector3.up * height;

    private GameObject Create(GameObject prefab, Vector3 position, string objectName)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"GameManager: prefab missing for {objectName}.", this);
            return null;
        }
        GameObject instance = Instantiate(prefab, position, prefab.transform.rotation, transform);
        instance.name = objectName;
        runtimeObjects.Add(instance);
        return instance;
    }

    private void RemoveFrom(Dictionary<Vector2Int, GameObject> collection, Vector2Int cell)
    {
        if (collection.Remove(cell, out GameObject instance))
            DestroyRuntime(instance);
    }

    private void DestroyRuntime(GameObject instance)
    {
        if (instance == null)
            return;
        runtimeObjects.Remove(instance);
        Destroy(instance);
    }

    private void ResetSimulation()
    {
        foreach (GameObject instance in runtimeObjects)
            if (instance != null)
                Destroy(instance);
        runtimeObjects.Clear();
        agents.Clear();
        pointsOfInterest.Clear();
        occupants.Clear();
        hazards.Clear();
        carriedVictims.Clear();
    }

    private IEnumerator WaitSeconds(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            yield return WaitWhilePaused();
            elapsed += Time.deltaTime * playbackSpeed;
            yield return null;
        }
    }

    private IEnumerator WaitWhilePaused()
    {
        while (paused)
            yield return null;
    }
}
