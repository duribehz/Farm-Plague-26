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
    [SerializeField] private AgentHud agentHud;
    [SerializeField] private SimulationControls simulationControls;
    [SerializeField] private AnimalWanderController animalWander;

    [Header("Playback")]
    [SerializeField, Min(0.05f)] private float moveDuration = 0.8f;
    [SerializeField, Min(0f)] private float eventDelay = 0.35f;
    [SerializeField, Min(0.05f)] private float playbackSpeed = 1f;
    [SerializeField, Min(0f)] private float agentHeight = 0.75f;
    [SerializeField, Min(0f)] private float markerHeight = 0.35f;
    [SerializeField, Min(0.1f)] private float hazardSize = 1.5f;
    [SerializeField, Min(0.05f)] private float doorAnimationDuration = 0.65f;
    [SerializeField, Min(0.05f)] private float wallBreakDuration = 0.7f;
    [SerializeField, Min(0.5f)] private float infirmaryFlightHeight = 5f;
    [SerializeField, Min(0.1f)] private float infirmaryGardenClearance = 0.5f;
    [SerializeField, Min(0.25f)] private float infirmaryAgentSpacing = 0.75f;

    private readonly Dictionary<int, GameObject> agents = new();
    private readonly Dictionary<Vector2Int, Transform> tiles = new();
    private readonly Dictionary<Vector2Int, GameObject> pointsOfInterest = new();
    private readonly Dictionary<Vector2Int, GameObject> occupants = new();
    private readonly Dictionary<Vector2Int, GameObject> hazards = new();
    private readonly Dictionary<Vector2Int, string> hazardStates = new();
    private readonly Dictionary<int, GameObject> carriedVictims = new();
    private readonly List<GameObject> runtimeObjects = new();
    private readonly List<Material> runtimeParticleMaterials = new();
    private readonly Dictionary<string, Transform> doors = new();
    private readonly Dictionary<string, WallState> walls = new();
    private readonly Dictionary<Transform, Quaternion> initialDoorRotations = new();
    private readonly Dictionary<int, int> infirmarySlots = new();

    private Transform infirmaryAnchor;
    private Vector3 infirmaryBasePosition;
    private Vector3 infirmaryRowDirection = Vector3.forward;
    private Vector3 infirmaryColumnDirection = Vector3.right;

    private Coroutine playback;
    private JObject simulation;
    private bool paused;
    private bool collapseReached;
    private bool victoryReached;
    private int rescuedTotal;

    private readonly struct WallState
    {
        public WallState(Transform transform)
        {
            Transform = transform;
            Position = transform.localPosition;
            Rotation = transform.localRotation;
            Scale = transform.localScale;
            Active = transform.gameObject.activeSelf;
        }

        public Transform Transform { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public bool Active { get; }
    }

    private void Start()
    {
        CacheTiles();
        CacheBoardGeometry();
        CacheInfirmary();
        agentHud?.SetVisible(false);
        agentHud?.SetStatsVisible(false);
        agentHud?.HideEndScreen();
        simulationControls?.SetVisible(false);
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
        simulationControls?.SetVisible(true);
        animalWander?.StartWalking();
        return true;
    }

    public void Pause()
    {
        paused = true;
        animalWander?.SetPaused(true);
    }

    public void Resume()
    {
        paused = false;
        animalWander?.SetPaused(false);
    }

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
        animalWander?.StopWalking(false);
    }

    public void ReturnToStart()
    {
        StopPlayback();
        ResetSimulation();
        agentHud?.SetVisible(false);
        agentHud?.SetStatsVisible(false);
        agentHud?.HideEndScreen();
        simulationControls?.SetVisible(false);
        animalWander?.StopWalking(true);
    }

    private void InitializeSimulation()
    {
        collapseReached = false;
        victoryReached = false;
        rescuedTotal = 0;
        agentHud?.HideEndScreen();
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
        List<int> agentIds = new();
        foreach (JToken agent in initialAgents)
        {
            int id = (int)agent["id"];
            EnsureAgent(id, ReadCell(agent["pos"]));
            agentIds.Add(id);
        }
        agentHud?.Initialize(agentIds);
        UpdateHazardCounts();
    }

    private IEnumerator PlaybackRoutine()
    {
        foreach (JToken step in (JArray)simulation["steps"])
        {
            int activeAgentId = 0;
            foreach (JToken evt in step["events"] as JArray ?? new JArray())
            {
                int eventAgentId = (int?)evt["agent"] ?? 0;
                if (eventAgentId != 0 && eventAgentId != activeAgentId)
                {
                    activeAgentId = eventAgentId;
                    agentHud?.SetActiveAgent(activeAgentId);
                }
                yield return WaitWhilePaused();
                yield return PlayEvent(evt);
                if (collapseReached || victoryReached)
                    break;
            }
            agentHud?.SetActiveAgent(0);
            if (collapseReached || victoryReached)
                break;
        }

        ApplyResultStats();
        playback = null;
        animalWander?.StopWalking(false);
    }

    private IEnumerator PlayEvent(JToken evt)
    {
        string type = (string)evt["type"];
        int agentId = (int?)evt["agent"] ?? 0;
        if (agentId != 0 && evt["ap_remaining"] != null)
            agentHud?.SetActionPoints(agentId, (int)evt["ap_remaining"]);

        switch (type)
        {
            case "move":
                GameObject agent = EnsureAgent(agentId, ReadCell(evt["from"]));
                if (agent != null && TryGetTile(ReadCell(evt["to"]), out Transform tile))
                    yield return MoveAgent(agent, TilePosition(tile, agentHeight));
                yield break;
            case "cell_change":
                if ((string)evt["from"] == "fire" && (string)evt["to"] == "none")
                    yield break;
                ApplyCellChange(ReadCell(evt["pos"]), (string)evt["to"]);
                break;
            case "pickup":
                Pickup(agentId, ReadCell(evt["pos"]), (string)evt["kind"]);
                break;
            case "rescue":
                Rescue(agentId);
                rescuedTotal = (int?)evt["total"] ?? rescuedTotal + 1;
                if (rescuedTotal >= 7)
                {
                    victoryReached = true;
                    agentHud?.ShowEndScreen(
                        true, rescuedTotal, agentHud?.KilledVictims ?? 0, agentHud?.StructuralDamage ?? 0);
                    yield break;
                }
                break;
            case "knockdown":
                agentHud?.SetAgentInfirmary(agentId, true);
                DropCarriedVictim(agentId);
                yield return MoveAgentToInfirmary(agentId, ReadCell(evt["pos"]));
                yield break;
            case "ambulance_hold":
                agentHud?.SetAgentInfirmary(agentId, true);
                if (!infirmarySlots.ContainsKey(agentId))
                    yield return MoveAgentToInfirmary(agentId, ReadCell(evt["pos"]));
                yield break;
            case "respawn":
                agentHud?.SetAgentInfirmary(agentId, false);
                yield return RespawnAgent(agentId, ReadCell(evt["spawn"] ?? evt["pos"]));
                yield break;
            case "extinguish":
                yield return ExtinguishFire(agentId, ReadCell(evt["pos"]));
                yield break;
            case "fire_spread":
                if (evt["cells"] is JArray spreadCells)
                    foreach (JToken target in spreadCells)
                        ApplyCellChange(ReadCell(target), "fire");
                break;
            case "kill":
                RemoveFrom(occupants, ReadCell(evt["pos"]));
                RemoveFrom(pointsOfInterest, ReadCell(evt["pos"]));
                agentHud?.AddKilledVictim();
                break;
            case "door_open":
                yield return OpenDoor(ReadCell(evt["from"]), ReadCell(evt["to"]));
                yield break;
            case "wall_chop":
                yield return BreakWall(agentId, ReadCell(evt["from"]), ReadCell(evt["to"]));
                yield break;
            case "wall_damaged":
                yield return DamageWall(evt["edge"], (int?)evt["hp"] ?? 1);
                yield break;
            case "wall_destroyed":
                yield return DestroyWall(evt["edge"]);
                yield break;
            case "structural_damage":
                if (evt["total"] != null)
                    agentHud?.SetStructuralDamage((int)evt["total"]);
                else
                    agentHud?.AddStructuralDamage((int?)evt["amount"] ?? 1);
                break;
            case "collapse":
                agentHud?.SetStructuralDamage((int?)evt["total"] ?? 24);
                collapseReached = true;
                yield break;
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

    private IEnumerator FlyAgent(GameObject agent, Vector3 destination)
    {
        Vector3 start = agent.transform.position;
        AgentAnimation agentAnimation = agent.GetComponent<AgentAnimation>();
        agentAnimation?.BeginMove(destination - start);
        float duration = moveDuration * 1.35f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return WaitWhilePaused();
            float scaledDeltaTime = Time.deltaTime * playbackSpeed;
            elapsed += scaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            Vector3 position = Vector3.Lerp(start, destination, easedProgress);
            position.y += Mathf.Sin(progress * Mathf.PI) * infirmaryFlightHeight;
            agent.transform.position = position;
            agentAnimation?.AnimateMove(progress, scaledDeltaTime);
            yield return null;
        }
        agent.transform.position = destination;
        agentAnimation?.EndMove();
    }

    private IEnumerator MoveAgentToInfirmary(int agentId, Vector2Int lastCell)
    {
        GameObject agent = EnsureAgent(agentId, lastCell);
        if (agent == null)
            yield break;
        if (infirmaryAnchor == null)
        {
            Debug.LogWarning("GameManager: Vegetable garden was not found; infirmary movement was skipped.", this);
            yield break;
        }

        if (!infirmarySlots.TryGetValue(agentId, out int slot))
        {
            slot = Mathf.Max(0, agentId - 1) % 6;
            infirmarySlots[agentId] = slot;
        }
        int row = slot / 3;
        int column = slot % 3 - 1;
        Vector3 destination = infirmaryBasePosition +
            infirmaryColumnDirection * (column * infirmaryAgentSpacing) +
            infirmaryRowDirection * (row * infirmaryAgentSpacing);
        yield return FlyAgent(agent, destination);
    }

    private IEnumerator RespawnAgent(int agentId, Vector2Int spawn)
    {
        infirmarySlots.Remove(agentId);
        if (!agents.TryGetValue(agentId, out GameObject agent))
            agent = EnsureAgent(agentId, spawn);
        if (agent == null || !TryGetTile(spawn, out Transform tile))
            yield break;
        yield return FlyAgent(agent, TilePosition(tile, agentHeight));
    }

    private void DropCarriedVictim(int agentId)
    {
        if (carriedVictims.Remove(agentId, out GameObject victim))
            DestroyRuntime(victim);
        if (agents.TryGetValue(agentId, out GameObject agent))
            agent.GetComponent<AgentAnimation>()?.StopCarrying();
    }

    private IEnumerator OpenDoor(Vector2Int from, Vector2Int to)
    {
        string key = EdgeKey(from, to);
        if (!doors.TryGetValue(key, out Transform leaf))
        {
            Debug.LogWarning($"GameManager: door {key} was not found.", this);
            yield break;
        }

        foreach (MonoBehaviour behaviour in leaf.GetComponents<MonoBehaviour>())
            if (behaviour.GetType().FullName == "DoorScript.Door")
                behaviour.enabled = false;

        Quaternion start = leaf.localRotation;
        Quaternion target = Quaternion.Euler(0f, -90f, 0f);
        float elapsed = 0f;
        while (elapsed < doorAnimationDuration)
        {
            yield return WaitWhilePaused();
            elapsed += Time.deltaTime * playbackSpeed;
            leaf.localRotation = Quaternion.Slerp(start, target,
                Mathf.SmoothStep(0f, 1f, elapsed / doorAnimationDuration));
            yield return null;
        }
        leaf.localRotation = target;
    }

    private IEnumerator BreakWall(int agentId, Vector2Int from, Vector2Int to)
    {
        string wallName = WallName(from, to);
        if (wallName == null || !walls.TryGetValue(wallName, out WallState state))
        {
            Debug.LogWarning($"GameManager: wall between {from} and {to} was not found.", this);
            yield break;
        }

        Transform wall = state.Transform;
        AgentAnimation agentAnimation = agents.TryGetValue(agentId, out GameObject agent)
            ? agent.GetComponent<AgentAnimation>()
            : null;
        agentAnimation?.BeginChop(wall.position);
        yield return AnimateWallDestruction(wall, agentAnimation);
    }

    private IEnumerator DamageWall(JToken edge, int hp)
    {
        if (!TryGetWall(edge, out Transform wall) || !wall.gameObject.activeSelf)
            yield break;

        ParticleSystem dust = CreateWallDust(wall);
        Vector3 startPosition = wall.localPosition;
        Quaternion startRotation = wall.localRotation;
        const float duration = 0.38f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return WaitWhilePaused();
            elapsed += Time.deltaTime * playbackSpeed;
            float progress = Mathf.Clamp01(elapsed / duration);
            float shake = Mathf.Sin(progress * Mathf.PI * 12f) * (1f - progress) * 0.1f;
            wall.localPosition = startPosition + Vector3.right * shake;
            wall.localRotation = startRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(progress * Mathf.PI) * 6f);
            yield return null;
        }
        wall.localPosition = startPosition;
        wall.localRotation = startRotation * Quaternion.Euler(0f, 0f, hp <= 1 ? 4f : 2f);
        AddWallCracks(wall);
        StopAndReleaseEffect(dust, 1.5f);
    }

    private IEnumerator DestroyWall(JToken edge)
    {
        if (!TryGetWall(edge, out Transform wall) || !wall.gameObject.activeSelf)
            yield break;
        yield return AnimateWallDestruction(wall, null);
    }

    private IEnumerator AnimateWallDestruction(Transform wall, AgentAnimation agentAnimation)
    {
        ParticleSystem dust = CreateWallDust(wall);
        SpawnWallFragments(wall);
        Vector3 startPosition = wall.localPosition;
        Quaternion startRotation = wall.localRotation;
        Vector3 startScale = wall.localScale;
        float elapsed = 0f;
        while (elapsed < wallBreakDuration)
        {
            yield return WaitWhilePaused();
            elapsed += Time.deltaTime * playbackSpeed;
            float progress = Mathf.Clamp01(elapsed / wallBreakDuration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            agentAnimation?.AnimateChop(progress);
            float shake = Mathf.Sin(progress * Mathf.PI * 12f) * (1f - progress) * 0.12f;
            wall.localPosition = startPosition + Vector3.down * (startScale.y * 0.42f * easedProgress) +
                Vector3.right * shake;
            wall.localRotation = startRotation * Quaternion.Euler(82f * easedProgress, 0f, 5f * easedProgress);
            wall.localScale = Vector3.Scale(startScale, new Vector3(1f, 1f - 0.08f * easedProgress, 1f));
            yield return null;
        }
        agentAnimation?.EndChop();
        StopAndReleaseEffect(dust, 1.5f);
        wall.gameObject.SetActive(false);
    }

    private bool TryGetWall(JToken edge, out Transform wall)
    {
        wall = null;
        if (edge is not JArray cells || cells.Count < 2)
            return false;
        string wallName = WallName(ReadCell(cells[0]), ReadCell(cells[1]));
        return wallName != null && walls.TryGetValue(wallName, out WallState state) &&
            (wall = state.Transform) != null;
    }

    private void AddWallCracks(Transform wall)
    {
        if (wall.Find("DamageCrack_0") != null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        Material crackMaterial = shader != null
            ? new Material(shader) { color = new Color(0.12f, 0.08f, 0.05f) }
            : null;
        if (crackMaterial != null)
            runtimeParticleMaterials.Add(crackMaterial);

        for (int i = 0; i < 3; i++)
        {
            GameObject crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crack.name = $"DamageCrack_{i}";
            crack.transform.SetParent(wall, false);
            crack.transform.localPosition = new Vector3((i - 1) * 0.12f, (i - 1) * 0.14f, -0.54f);
            crack.transform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 32f : -32f);
            crack.transform.localScale = new Vector3(0.025f, 0.24f, 0.025f);
            Collider crackCollider = crack.GetComponent<Collider>();
            if (crackCollider != null)
                Destroy(crackCollider);
            if (crackMaterial != null)
                crack.GetComponent<Renderer>().sharedMaterial = crackMaterial;
            runtimeObjects.Add(crack);
        }
    }

    private IEnumerator ExtinguishFire(int agentId, Vector2Int cell)
    {
        if (!TryGetTile(cell, out Transform tile))
            yield break;

        GameObject agent = agents.TryGetValue(agentId, out GameObject foundAgent) ? foundAgent : null;
        Vector3 target = tile.position + Vector3.up * 0.55f;
        AgentAnimation agentAnimation = agent != null ? agent.GetComponent<AgentAnimation>() : null;
        agentAnimation?.BeginExtinguish(target);

        Vector3 origin = agent != null
            ? agent.transform.position + Vector3.up * 1.05f + agent.transform.forward * 0.3f
            : target + Vector3.up;
        ParticleSystem water = CreateWaterStream(origin, target);
        ParticleSystem impact = CreateWaterImpact(target);
        hazards.TryGetValue(cell, out GameObject fire);
        Vector3 fireScale = fire != null ? fire.transform.localScale : Vector3.one;

        const float duration = 1f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return WaitWhilePaused();
            elapsed += Time.deltaTime * playbackSpeed;
            float progress = Mathf.Clamp01(elapsed / duration);
            agentAnimation?.AnimateExtinguish(progress);
            if (fire != null)
                fire.transform.localScale = fireScale * Mathf.Lerp(1f, 0.08f, progress);
            yield return null;
        }

        agentAnimation?.EndExtinguish();
        StopAndReleaseEffect(water, 0.75f);
        StopAndReleaseEffect(impact, 0.75f);
        RemoveFrom(hazards, cell);
        hazardStates.Remove(cell);
        UpdateHazardCounts();
    }

    private ParticleSystem CreateWaterStream(Vector3 origin, Vector3 target)
    {
        GameObject effect = new("ExtinguishWaterStream");
        effect.transform.SetParent(transform, true);
        effect.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(target - origin));
        runtimeObjects.Add(effect);
        ParticleSystem water = effect.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = water.main;
        main.loop = true;
        main.startLifetime = Vector3.Distance(origin, target) / 7f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(6.5f, 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.08f, 0.45f, 1f, 0.9f),
            new Color(0.25f, 0.85f, 1f, 0.95f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = water.emission;
        emission.rateOverTime = 100f;
        ParticleSystem.ShapeModule shape = water.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 3f;
        shape.radius = 0.025f;
        ConfigureParticleMaterial(effect, new Color(0.12f, 0.55f, 1f));
        water.Play();
        return water;
    }

    private ParticleSystem CreateWaterImpact(Vector3 target)
    {
        GameObject effect = new("ExtinguishWaterImpact");
        effect.transform.SetParent(transform, true);
        effect.transform.position = target;
        runtimeObjects.Add(effect);
        ParticleSystem impact = effect.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = impact.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.2f, 0.7f, 1f, 0.8f),
            new Color(0.75f, 0.9f, 1f, 0.5f));
        main.gravityModifier = 0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = impact.emission;
        emission.rateOverTime = 55f;
        ParticleSystem.ShapeModule shape = impact.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.25f;
        ConfigureParticleMaterial(effect, new Color(0.45f, 0.8f, 1f));
        impact.Play();
        return impact;
    }

    private void ConfigureParticleMaterial(GameObject effect, Color color)
    {
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return;
        Material material = new(shader) { color = color };
        effect.GetComponent<ParticleSystemRenderer>().material = material;
        runtimeParticleMaterials.Add(material);
    }

    private ParticleSystem CreateWallDust(Transform wall)
    {
        GameObject effect = new($"{wall.name}_Dust");
        effect.transform.SetParent(transform, true);
        runtimeObjects.Add(effect);
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Bounds bounds = wallRenderer != null ? wallRenderer.bounds : new Bounds(wall.position, Vector3.one);
        effect.transform.position = bounds.center;

        ParticleSystem dust = effect.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = dust.main;
        main.loop = false;
        main.duration = wallBreakDuration;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.24f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.3f, 0.22f, 0.14f, 0.75f),
            new Color(0.65f, 0.58f, 0.48f, 0.6f));
        main.gravityModifier = 0.15f;
        main.maxParticles = 90;

        ParticleSystem.EmissionModule emission = dust.emission;
        emission.rateOverTime = 70f;
        ParticleSystem.ShapeModule shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = bounds.size * 0.75f;

        ParticleSystemRenderer particleRenderer = effect.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            Material material = new(shader);
            particleRenderer.material = material;
            runtimeParticleMaterials.Add(material);
        }
        dust.Play();
        return dust;
    }

    private void StopAndReleaseEffect(ParticleSystem particles, float delay)
    {
        if (particles == null)
            return;

        GameObject effect = particles.gameObject;
        ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
        Material material = renderer != null ? renderer.sharedMaterial : null;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        runtimeObjects.Remove(effect);
        if (material != null && runtimeParticleMaterials.Remove(material))
            Destroy(material, delay);
        Destroy(effect, delay);
    }

    private void SpawnWallFragments(Transform wall)
    {
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Bounds bounds = wallRenderer != null ? wallRenderer.bounds : new Bounds(wall.position, Vector3.one);
        for (int i = 0; i < 7; i++)
        {
            GameObject fragment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fragment.name = $"{wall.name}_Fragment_{i}";
            fragment.transform.SetParent(transform, true);
            fragment.transform.position = bounds.center + new Vector3(
                UnityEngine.Random.Range(-bounds.extents.x, bounds.extents.x),
                UnityEngine.Random.Range(-bounds.extents.y * 0.5f, bounds.extents.y),
                UnityEngine.Random.Range(-bounds.extents.z, bounds.extents.z));
            fragment.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.08f, 0.2f);
            if (wallRenderer != null && fragment.TryGetComponent(out Renderer fragmentRenderer))
                fragmentRenderer.sharedMaterial = wallRenderer.sharedMaterial;
            Rigidbody body = fragment.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.AddForce((Vector3.up + UnityEngine.Random.insideUnitSphere) * 1.5f, ForceMode.Impulse);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * 4f, ForceMode.Impulse);
            Destroy(fragment, 2.5f);
        }
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
        hazardStates.Remove(cell);
        UpdateHazardCounts();
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
        if (carriedVictims.Remove(agentId, out GameObject victim))
        {
            DestroyRuntime(victim);
            if (agents.TryGetValue(agentId, out GameObject agent))
                agent.GetComponent<AgentAnimation>()?.StopCarrying();
        }
        agentHud?.AddRescue(agentId);
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
        hazardStates.Remove(cell);
        if (state == "none" || state == "exit")
        {
            UpdateHazardCounts();
            return;
        }

        GameObject prefab = state == "fire" ? firePrefab : smokePrefab;
        GameObject instance = prefab != null
            ? Create(prefab, TilePosition(tile, markerHeight), $"{state}_{cell.x}_{cell.y}")
            : CreateFallbackHazard(tile, state, cell);
        if (instance == null)
        {
            UpdateHazardCounts();
            return;
        }
        if (prefab != null)
        {
            FitHazardToTile(instance, tile);
            try
            {
                instance.AddComponent<HazardAnimation>().Configure(state == "fire");
            }
            catch (Exception exception)
            {
                Debug.LogError($"GameManager: could not configure {instance.name}. {exception.Message}", instance);
            }
        }
        hazards[cell] = instance;
        hazardStates[cell] = state;
        UpdateHazardCounts();
    }

    private void UpdateHazardCounts()
    {
        int fireCount = 0;
        int smokeCount = 0;
        foreach (string state in hazardStates.Values)
        {
            if (state == "fire")
                fireCount++;
            else if (state == "smoke")
                smokeCount++;
        }
        agentHud?.SetFireCount(fireCount);
        agentHud?.SetSmokeCount(smokeCount);
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

    private void CacheBoardGeometry()
    {
        doors.Clear();
        walls.Clear();
        initialDoorRotations.Clear();

        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.name.StartsWith("Wall_", StringComparison.Ordinal))
            {
                walls[candidate.name] = new WallState(candidate);
                continue;
            }
            if (!candidate.name.StartsWith("Door_Interior_", StringComparison.Ordinal))
                continue;

            string endpoints = candidate.name.Substring("Door_Interior_".Length);
            string[] cells = endpoints.Split(new[] { "__" }, StringSplitOptions.None);
            if (cells.Length != 2 || !TryParseNamedCell(cells[0], out Vector2Int a) ||
                !TryParseNamedCell(cells[1], out Vector2Int b))
                continue;

            Transform leaf = candidate.Find("Door");
            if (leaf == null)
                continue;
            doors[EdgeKey(a, b)] = leaf;
            initialDoorRotations[leaf] = leaf.localRotation;
        }
    }

    private void CacheInfirmary()
    {
        infirmaryAnchor = null;
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.name == "Vegetable garden")
            {
                infirmaryAnchor = candidate;
                break;
            }
        }
        if (infirmaryAnchor == null)
            return;

        Renderer[] renderers = infirmaryAnchor.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers.Length > 0
            ? renderers[0].bounds
            : new Bounds(infirmaryAnchor.position, Vector3.one * 2f);
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 boardCenter = Vector3.zero;
        foreach (Transform tile in tiles.Values)
            boardCenter += tile.position;
        if (tiles.Count > 0)
            boardCenter /= tiles.Count;
        infirmaryRowDirection = Vector3.ProjectOnPlane(bounds.center - boardCenter, Vector3.up).normalized;
        if (infirmaryRowDirection.sqrMagnitude < 0.01f)
            infirmaryRowDirection = Vector3.forward;
        infirmaryColumnDirection = Vector3.Cross(Vector3.up, infirmaryRowDirection).normalized;
        Vector3 gardenEdge = bounds.ClosestPoint(bounds.center + infirmaryRowDirection * 1000f);
        infirmaryBasePosition = gardenEdge + infirmaryRowDirection * infirmaryGardenClearance;
        infirmaryBasePosition.y = bounds.min.y + agentHeight;
    }

    private static bool TryParseNamedCell(string value, out Vector2Int cell)
    {
        string[] coordinates = value.Split('_');
        if (coordinates.Length == 2 && int.TryParse(coordinates[0], out int x) &&
            int.TryParse(coordinates[1], out int y))
        {
            cell = new Vector2Int(x, y);
            return true;
        }
        cell = default;
        return false;
    }

    private static string EdgeKey(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || a.x == b.x && a.y > b.y)
            (a, b) = (b, a);
        return $"{a.x}_{a.y}__{b.x}_{b.y}";
    }

    private static string WallName(Vector2Int a, Vector2Int b)
    {
        if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1)
            return $"Wall_V_F{a.y}_C{Mathf.Max(a.x, b.x)}";
        if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1)
            return $"Wall_H_F{Mathf.Max(a.y, b.y)}_C{a.x}";
        return null;
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
        collapseReached = false;
        victoryReached = false;
        rescuedTotal = 0;
        agentHud?.SetStructuralDamage(0);
        agentHud?.SetKilledVictims(0);
        agentHud?.SetFireCount(0);
        agentHud?.SetSmokeCount(0);
        foreach (GameObject instance in runtimeObjects)
            if (instance != null)
                Destroy(instance);
        runtimeObjects.Clear();
        foreach (Material material in runtimeParticleMaterials)
            if (material != null)
                Destroy(material);
        runtimeParticleMaterials.Clear();
        agents.Clear();
        pointsOfInterest.Clear();
        occupants.Clear();
        hazards.Clear();
        hazardStates.Clear();
        carriedVictims.Clear();
        infirmarySlots.Clear();

        foreach (KeyValuePair<Transform, Quaternion> door in initialDoorRotations)
            if (door.Key != null)
                door.Key.localRotation = door.Value;
        foreach (WallState wall in walls.Values)
        {
            if (wall.Transform == null)
                continue;
            wall.Transform.localPosition = wall.Position;
            wall.Transform.localRotation = wall.Rotation;
            wall.Transform.localScale = wall.Scale;
            wall.Transform.gameObject.SetActive(wall.Active);
        }
    }

    private void ApplyResultStats()
    {
        JToken result = simulation?["result"];
        int rescued = (int?)result?["rescued"] ?? rescuedTotal;
        int killed = (int?)result?["killed"] ?? agentHud?.KilledVictims ?? 0;
        int damage = (int?)result?["structural_damage"] ?? agentHud?.StructuralDamage ?? 0;
        agentHud?.SetStructuralDamage(damage);
        agentHud?.SetKilledVictims(killed);

        string endReason = ((string)result?["end_reason"])?.Trim();
        bool victory = victoryReached || rescued >= 7 ||
            string.Equals(endReason, "7 victims rescued", StringComparison.OrdinalIgnoreCase);
        if (victory)
        {
            agentHud?.ShowEndScreen(true, Mathf.Max(rescued, rescuedTotal), killed, damage);
            return;
        }

        if (string.Equals(endReason, "max steps reached", StringComparison.OrdinalIgnoreCase))
        {
            agentHud?.HideEndScreen();
            return;
        }

        if (killed >= 4 || damage >= 24 || collapseReached)
            agentHud?.ShowEndScreen(false, rescued, killed, damage);
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
