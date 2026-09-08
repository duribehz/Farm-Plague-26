using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class AnimalWanderController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float wanderRadius = 1.5f;
    [SerializeField, Min(0.01f)] private float minimumSpeed = 0.3f;
    [SerializeField, Min(0.01f)] private float maximumSpeed = 0.7f;
    [SerializeField, Min(0f)] private float turnSpeed = 4f;
    [SerializeField, Min(0f)] private float stepHeight = 0.08f;
    [SerializeField, Min(0f)] private float waddleAngle = 4f;
    [SerializeField, Min(0.1f)] private float stepFrequency = 5f;
    [SerializeField, Min(0f)] private float minimumWait = 0.3f;
    [SerializeField, Min(0f)] private float maximumWait = 1.2f;

    private readonly List<AnimalState> animals = new();
    private bool running;
    private bool paused;

    private void Awake()
    {
        CacheAnimals();
    }

    public void StartWalking()
    {
        if (animals.Count == 0)
            CacheAnimals();
        running = true;
        paused = false;
        foreach (AnimalState animal in animals)
            ChooseDestination(animal);
    }

    public void SetPaused(bool value)
    {
        paused = value;
    }

    public void StopWalking(bool resetPositions)
    {
        running = false;
        paused = false;
        foreach (AnimalState animal in animals)
        {
            animal.Transform.localPosition = resetPositions ? animal.Origin : animal.GroundPosition;
            animal.Transform.localRotation = resetPositions ? animal.OriginRotation : animal.FacingRotation;
        }
    }

    private void Update()
    {
        if (!running || paused)
            return;

        float deltaTime = Time.deltaTime;
        foreach (AnimalState animal in animals)
            UpdateAnimal(animal, deltaTime);
    }

    private void UpdateAnimal(AnimalState animal, float deltaTime)
    {
        if (animal.WaitRemaining > 0f)
        {
            animal.WaitRemaining -= deltaTime;
            animal.Transform.localPosition = animal.GroundPosition;
            animal.Transform.localRotation = animal.FacingRotation;
            return;
        }

        Vector3 offset = animal.Destination - animal.GroundPosition;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.01f)
        {
            animal.WaitRemaining = RandomRange(animal.Random, minimumWait, maximumWait);
            ChooseDestination(animal);
            return;
        }

        Vector3 direction = offset.normalized;
        animal.GroundPosition = Vector3.MoveTowards(
            animal.GroundPosition, animal.Destination, animal.Speed * deltaTime);
        Quaternion targetFacing = Quaternion.LookRotation(direction, Vector3.up);
        animal.FacingRotation = Quaternion.Slerp(
            animal.FacingRotation, targetFacing, turnSpeed * deltaTime);

        animal.StepPhase += deltaTime * stepFrequency * animal.Speed;
        float step = Mathf.Sin(animal.StepPhase * Mathf.PI * 2f);
        Vector3 displayedPosition = animal.GroundPosition + Vector3.up * (Mathf.Abs(step) * stepHeight);
        animal.Transform.localPosition = displayedPosition;
        animal.Transform.localRotation = animal.FacingRotation * Quaternion.Euler(0f, 0f, step * waddleAngle);
    }

    private void CacheAnimals()
    {
        animals.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform animal = transform.GetChild(i);
            animals.Add(new AnimalState
            {
                Transform = animal,
                Origin = animal.localPosition,
                GroundPosition = animal.localPosition,
                OriginRotation = animal.localRotation,
                FacingRotation = animal.localRotation,
                Random = new System.Random(1709 + i * 397)
            });
        }
    }

    private void ChooseDestination(AnimalState animal)
    {
        float angle = RandomRange(animal.Random, 0f, Mathf.PI * 2f);
        float distance = RandomRange(animal.Random, wanderRadius * 0.35f, wanderRadius);
        animal.Destination = animal.Origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
        animal.Destination.y = animal.Origin.y;
        animal.Speed = RandomRange(animal.Random, minimumSpeed, maximumSpeed);
    }

    private static float RandomRange(System.Random random, float minimum, float maximum) =>
        minimum + (float)random.NextDouble() * (maximum - minimum);

    private sealed class AnimalState
    {
        public Transform Transform;
        public Vector3 Origin;
        public Vector3 GroundPosition;
        public Quaternion OriginRotation;
        public Quaternion FacingRotation;
        public Vector3 Destination;
        public float Speed;
        public float StepPhase;
        public float WaitRemaining;
        public System.Random Random;
    }
}
