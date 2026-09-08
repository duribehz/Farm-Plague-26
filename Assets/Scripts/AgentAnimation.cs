using System;
using UnityEngine;

public sealed class AgentAnimation : MonoBehaviour
{
    [Header("Walk")]
    [SerializeField] private float legSwingAngle = 28f;
    [SerializeField] private float armSwingAngle = 16f;
    [SerializeField] private float walkCyclesPerMove = 2f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float modelForwardOffset;

    [Header("Carry")]
    [SerializeField] private float carryingArmAngle = -70f;
    [SerializeField] private Vector3 carryPosition = new(0f, 1.35f, 0.45f);
    [SerializeField] private Vector3 carriedVictimRotation = new(0f, 0f, 90f);
    [SerializeField] private float carryBodyClearance;

    private Transform leftLeg;
    private Transform rightLeg;
    private Transform leftArm;
    private Transform rightArm;
    private Transform carryAnchor;
    private Quaternion leftLegRest;
    private Quaternion rightLegRest;
    private Quaternion leftArmRest;
    private Quaternion rightArmRest;
    private Quaternion targetRotation;
    private bool carrying;

    private void Awake()
    {
        leftLeg = FindPart("leg-left");
        rightLeg = FindPart("leg-right");
        leftArm = FindPart("arm-left");
        rightArm = FindPart("arm-right");

        if (leftLeg != null)
            leftLegRest = leftLeg.localRotation;
        if (rightLeg != null)
            rightLegRest = rightLeg.localRotation;
        if (leftArm != null)
            leftArmRest = leftArm.localRotation;
        if (rightArm != null)
            rightArmRest = rightArm.localRotation;

        carryAnchor = new GameObject("CarryAnchor").transform;
        carryAnchor.SetParent(transform, false);
        carryAnchor.localPosition = carryPosition;
        targetRotation = transform.rotation;
    }

    public void BeginMove(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up) *
            Quaternion.Euler(0f, modelForwardOffset, 0f);
    }

    public void AnimateMove(float progress, float deltaTime)
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * deltaTime);
        float swing = Mathf.Sin(progress * walkCyclesPerMove * Mathf.PI * 2f);
        SetLocalXRotation(leftLeg, leftLegRest, swing * legSwingAngle);
        SetLocalXRotation(rightLeg, rightLegRest, -swing * legSwingAngle);

        if (carrying)
        {
            SetCarryingArms();
        }
        else
        {
            SetLocalXRotation(leftArm, leftArmRest, -swing * armSwingAngle);
            SetLocalXRotation(rightArm, rightArmRest, swing * armSwingAngle);
        }
    }

    public void EndMove()
    {
        transform.rotation = targetRotation;
        SetLocalXRotation(leftLeg, leftLegRest, 0f);
        SetLocalXRotation(rightLeg, rightLegRest, 0f);
        if (carrying)
            SetCarryingArms();
        else
            ResetArms();
    }

    public void AttachVictim(GameObject victim)
    {
        carrying = true;
        SetCarryingArms();
        if (victim == null)
            return;

        victim.transform.SetParent(carryAnchor, true);
        victim.transform.position = carryAnchor.position;
        victim.transform.rotation = transform.rotation * Quaternion.Euler(carriedVictimRotation);
        CenterVisualAt(victim, carryAnchor.position);
        MoveVisualInFrontOfBody(victim);
    }

    public void StopCarrying()
    {
        carrying = false;
        ResetArms();
    }

    private void SetCarryingArms()
    {
        SetLocalXRotation(leftArm, leftArmRest, carryingArmAngle);
        SetLocalXRotation(rightArm, rightArmRest, carryingArmAngle);
    }

    private void ResetArms()
    {
        SetLocalXRotation(leftArm, leftArmRest, 0f);
        SetLocalXRotation(rightArm, rightArmRest, 0f);
    }

    private static void SetLocalXRotation(Transform part, Quaternion rest, float angle)
    {
        if (part != null)
            part.localRotation = rest * Quaternion.AngleAxis(angle, Vector3.right);
    }

    private static void CenterVisualAt(GameObject instance, Vector3 target)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        instance.transform.position += target - bounds.center;
    }

    private void MoveVisualInFrontOfBody(GameObject victim)
    {
        Renderer[] victimRenderers = victim.GetComponentsInChildren<Renderer>(true);
        if (victimRenderers.Length == 0)
            return;

        Bounds victimBounds = CombinedBounds(victimRenderers);
        Vector3 forward = transform.forward;
        float bodyFront = float.NegativeInfinity;

        foreach (Renderer bodyRenderer in GetComponentsInChildren<Renderer>(true))
        {
            if (bodyRenderer.transform.IsChildOf(victim.transform) ||
                leftArm != null && bodyRenderer.transform.IsChildOf(leftArm) ||
                rightArm != null && bodyRenderer.transform.IsChildOf(rightArm))
                continue;
            Bounds bounds = bodyRenderer.bounds;
            bodyFront = Mathf.Max(bodyFront,
                Vector3.Dot(bounds.center, forward) + ProjectedExtent(bounds, forward));
        }

        if (float.IsNegativeInfinity(bodyFront))
            return;

        float victimCenter = Vector3.Dot(victimBounds.center, forward);
        float victimBackOffset = ProjectedExtent(victimBounds, forward);
        float requiredShift = bodyFront + victimBackOffset + carryBodyClearance - victimCenter;
        if (requiredShift > 0f)
            victim.transform.position += forward * requiredShift;
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static float ProjectedExtent(Bounds bounds, Vector3 direction)
    {
        Vector3 extents = bounds.extents;
        return Mathf.Abs(direction.x) * extents.x +
               Mathf.Abs(direction.y) * extents.y +
               Mathf.Abs(direction.z) * extents.z;
    }

    private Transform FindPart(string partName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, partName, StringComparison.OrdinalIgnoreCase))
                return child;

        Debug.LogWarning($"AgentAnimation: '{partName}' was not found in {name}.", this);
        return null;
    }
}
