using System.Collections;
using UnityEngine;

/// <summary>
/// Nitro object in the world. Needs a Collider with "Is Trigger" on.
/// Works for the player and AI (any vehicle with a VehicleController).
/// </summary>
[RequireComponent(typeof(Collider))]
public class NitroPickup : MonoBehaviour
{
    public float amount = 35f;
    public float respawnTime = 8f;
    public float spinSpeed = 120f;

    Collider col;
    Renderer[] renderers;

    void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true;
        renderers = GetComponentsInChildren<Renderer>();
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }

    void OnTriggerEnter(Collider other)
    {
        VehicleController car = other.GetComponentInParent<VehicleController>();
        if (car == null) return;

        car.AddNitro(amount);
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        SetVisible(false);
        yield return new WaitForSeconds(respawnTime);
        SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        col.enabled = visible;
        foreach (Renderer r in renderers) r.enabled = visible;
    }
}