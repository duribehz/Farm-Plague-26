using System.Collections.Generic;
using UnityEngine;

public sealed class HazardAnimation : MonoBehaviour
{
    private Transform visualPivot;
    private ParticleSystem particles;
    private Material particleMaterial;
    private Material fallbackMaterial;
    private readonly List<Material> visualMaterials = new();
    private Vector3 baseScale;
    private bool isFire;

    public void Configure(bool fire)
    {
        isFire = fire;
        CenterVisualPivot();
        ConfigureVisualAppearance();
        CreateParticles();
        baseScale = visualPivot.localScale;
    }

    private void Update()
    {
        if (visualPivot == null)
            return;

        float speed = isFire ? 55f : 24f;
        visualPivot.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
        if (isFire)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 7f) * 0.045f;
            visualPivot.localScale = baseScale * pulse;
        }
    }

    private void CenterVisualPivot()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            renderers = CreateFallbackVisual();

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        visualPivot = new GameObject("VisualPivot").transform;
        visualPivot.SetParent(transform, true);
        visualPivot.position = bounds.center;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != visualPivot)
                child.SetParent(visualPivot, true);
        }
    }

    private void ConfigureVisualAppearance()
    {
        Renderer[] renderers = visualPivot.GetComponentsInChildren<Renderer>(true);
        Color visualColor = isFire
            ? new Color(1f, 0.18f, 0.015f, 1f)
            : new Color(0.92f, 0.96f, 1f, 1f);
        foreach (Renderer modelRenderer in renderers)
        {
            modelRenderer.enabled = true;
            Material[] sourceMaterials = modelRenderer.sharedMaterials;
            Material[] replacements = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                if (sourceMaterials[i] == null)
                    continue;
                Material material = new(sourceMaterials[i]) { color = visualColor };
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", visualColor);
                if (isFire && material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", visualColor * 1.5f);
                }
                replacements[i] = material;
                visualMaterials.Add(material);
            }
            modelRenderer.sharedMaterials = replacements;
        }
    }

    private Renderer[] CreateFallbackVisual()
    {
        GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fallback.name = "FallbackVisual";
        fallback.transform.SetParent(transform, false);
        fallback.transform.localPosition = Vector3.up * 0.45f;
        fallback.transform.localScale = isFire
            ? new Vector3(0.75f, 1.1f, 0.75f)
            : new Vector3(0.9f, 0.55f, 0.9f);

        Collider fallbackCollider = fallback.GetComponent<Collider>();
        if (fallbackCollider != null)
            Destroy(fallbackCollider);
        Renderer fallbackRenderer = fallback.GetComponent<Renderer>();
        fallbackMaterial = fallbackRenderer.material;
        fallbackMaterial.color = isFire
            ? new Color(1f, 0.12f, 0.01f)
            : new Color(0.38f, 0.42f, 0.45f);
        return new[] { fallbackRenderer };
    }

    private void CreateParticles()
    {
        GameObject effect = new(isFire ? "FireParticles" : "SmokeParticles");
        effect.transform.SetParent(transform, false);
        effect.transform.localPosition = Vector3.up * 0.35f;
        particles = effect.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = isFire ? new ParticleSystem.MinMaxCurve(0.35f, 0.8f) :
            new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = isFire ? new ParticleSystem.MinMaxCurve(0.35f, 0.9f) :
            new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSize = isFire ? new ParticleSystem.MinMaxCurve(0.12f, 0.3f) :
            new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.startColor = isFire
            ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.16f, 0.01f, 0.95f), new Color(1f, 0.82f, 0.08f, 1f))
            : new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.35f), new Color(0.82f, 0.9f, 1f, 0.65f));
        main.maxParticles = isFire ? 70 : 45;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = isFire ? 32f : 14f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = isFire ? 0.38f : 0.5f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new();
        GradientColorKey[] colors = isFire
            ? new[]
            {
                new GradientColorKey(new Color(1f, 0.9f, 0.2f), 0f),
                new GradientColorKey(new Color(1f, 0.12f, 0.01f), 1f)
            }
            : new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.82f, 0.9f, 1f), 1f)
            };
        gradient.SetKeys(colors,
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        ParticleSystemRenderer particleRenderer = effect.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            particleMaterial = new Material(shader);
            particleRenderer.material = particleMaterial;
        }
        particles.Play();
    }

    private void OnDestroy()
    {
        if (particleMaterial != null)
            Destroy(particleMaterial);
        if (fallbackMaterial != null)
            Destroy(fallbackMaterial);
        foreach (Material material in visualMaterials)
            if (material != null)
                Destroy(material);
    }
}
