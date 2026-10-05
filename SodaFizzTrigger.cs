using UnityEngine;

/// <summary>
/// Spawns and plays a particle system at a given transform when triggered.
/// Hook TriggerFizz() to a UI Button's OnClick(), or use the optional key trigger.
///
/// SUGGESTED PARTICLE SYSTEM SETTINGS FOR A SODA FIZZ LOOK:
/// - Shape: Circle or Cone, small radius (0.05 - 0.15), spawning upward
/// - Start Lifetime: 0.5 - 1.2s (bubbles pop fast)
/// - Start Speed: 0.5 - 1.5, mostly upward
/// - Start Size: 0.02 - 0.08 (tiny bubbles), enable Random Between Two Constants
/// - Emission: Rate over Time ~5-10 (steady trickle) + a Burst of 30-60 at time 0 (initial fizz)
/// - Velocity over Lifetime: small positive Y to make bubbles rise, slight random X drift
/// - Noise module: low strength (0.1-0.3) for a subtle wobble as bubbles rise
/// - Size over Lifetime: curve that shrinks slightly near the end (bubbles shrink before popping)
/// - Color over Lifetime: alpha fades out near the end of life
/// - Material: use an additive or alpha-blended soft circle/dot sprite (Particles/Standard Unlit works well)
/// - Renderer: Sort in Layer to render above the liquid sprite
/// </summary>
public class SodaFizzTrigger : MonoBehaviour
{
    [Header("Fizz Effect")]
    [Tooltip("The particle system prefab to spawn (not a scene instance).")]
    [SerializeField] private ParticleSystem fizzParticlePrefab;

    [Tooltip("Where to spawn the effect. Defaults to this object's transform if left empty.")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("If true, the spawned particle system is parented under spawnPoint and will move with it.")]
    [SerializeField] private bool parentToSpawnPoint = false;

    [Tooltip("Seconds before the spawned particle GameObject is destroyed. Set to 0 to auto-calculate from the particle system's duration + max lifetime.")]
    [SerializeField] private float destroyAfterSeconds = 3f;

    [Header("Optional Keyboard Trigger")]
    [SerializeField] private bool allowKeyboardTrigger = false;
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;

    [Header("Randomization")]
    [SerializeField] private bool randomizeRotation = true;
    [SerializeField] private bool randomizeScale = false;
    [SerializeField] private Vector2 scaleRange = new Vector2(0.9f, 1.1f);

    private void Update()
    {
        if (allowKeyboardTrigger && Input.GetKeyDown(triggerKey))
        {
            TriggerFizz();
        }
    }

    /// <summary>
    /// Call this from a UI Button's OnClick() event, or from other scripts,
    /// to spawn and play the fizz effect.
    /// </summary>
    public void TriggerFizz()
    {
        if (fizzParticlePrefab == null)
        {
            Debug.LogWarning($"[SodaFizzTrigger] No particle system prefab assigned on '{name}'.");
            return;
        }

        Transform origin = spawnPoint != null ? spawnPoint : transform;

        Quaternion rotation = randomizeRotation
            ? Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))
            : origin.rotation;

        ParticleSystem instance = Instantiate(
            fizzParticlePrefab,
            origin.position,
            rotation,
            parentToSpawnPoint ? origin : null
        );

        if (randomizeScale)
        {
            float s = Random.Range(scaleRange.x, scaleRange.y);
            instance.transform.localScale *= s;
        }

        instance.Play();

        float lifetime = destroyAfterSeconds > 0f
            ? destroyAfterSeconds
            : instance.main.duration + instance.main.startLifetime.constantMax;

        Destroy(instance.gameObject, lifetime);
    }
}
