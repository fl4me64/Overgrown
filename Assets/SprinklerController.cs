using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public interface IMowable
{
    bool isMowed { get; }
    void Regrow();
}

[RequireComponent(typeof(SphereCollider))]
public class SprinklerController : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Tag used by the player object.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Radius of the trigger that watches for the player. Sets the SphereCollider.")]
    [SerializeField] private float detectionRadius = 5f;

    [Header("Effect")]
    [Tooltip("Radius around the sprinkler in which grass regrows.")]
    [SerializeField] private float effectRadius = 3f;

    [Tooltip("Layer(s) your grass patch objects are on.")]
    [SerializeField] private LayerMask grassLayer;

    [Tooltip("Seconds before the sprinkler can trigger again.")]
    [SerializeField] private float cooldown = 4f;

    [Tooltip("Delay after the water effect starts before grass actually regrows, so the droplets visibly land first.")]
    [SerializeField] private float regrowDelay = 1f;

    [Header("Visual Animation")]    
    [SerializeField] private Transform rotatingHead;
    [Tooltip("Time in seconds to complete one 360-degree spin.")]
    [SerializeField] private float spinDuration = 0.5f;

    [Header("Water Droplets (optional)")]
    [Tooltip("Leave empty to use the simple built-in droplet spawner instead.")]
    [SerializeField] private ParticleSystem waterParticles;
    [SerializeField] private int dropletCount = 10;
    [SerializeField] private float dropletArcHeight = 1.2f;
    [SerializeField] private float dropletLifetime = 0.8f;
    [SerializeField] private float dropletScale = 3f;

    public AudioClip waterSoundClip;

    private MowerController playerMower;
    private bool onCooldown;
    private SphereCollider trigger;

    private void Awake()
    {
        trigger = GetComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = detectionRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        playerMower = other.GetComponent<MowerController>();        
    }

    private void OnTriggerStay(Collider other)
    {
        if (playerMower == null || onCooldown) return;
        if (!other.CompareTag(playerTag)) return;               

        if (playerMower.isSprinting)
        {
            Activate();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            playerMower = null;
        }
    }

    private void Activate()
    {        
        StartCoroutine(CooldownRoutine());        
        PlayWaterEffect();

        if (rotatingHead != null)
        {
            StartCoroutine(RotateHeadRoutine());
        }

        StartCoroutine(RegrowAfterDelay());
    }

    private IEnumerator RotateHeadRoutine()
    {
        float elapsedTime = 0f;
        Quaternion startRotation = rotatingHead.localRotation;

        while (elapsedTime < spinDuration)
        {
            elapsedTime += Time.deltaTime;
            float step = (elapsedTime / spinDuration) * 360f;

            // Rotates smoothly around Y-axis relative to its starting orientation
            rotatingHead.localRotation = startRotation * Quaternion.Euler(0f, step, 0f);
            yield return null;
        }

        // Ensure it completes the exact 360-degree loop cleanly
        rotatingHead.localRotation = startRotation;
    }

    private IEnumerator RegrowAfterDelay()
    {
        yield return new WaitForSeconds(regrowDelay);
        RegrowGrassInRange();
    }

    private IEnumerator CooldownRoutine()
    {
        onCooldown = true;
        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    private void RegrowGrassInRange()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, effectRadius, grassLayer);                

        int regrownCount = 0;
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<IMowable>(out var patch))
            {
                if (patch.isMowed)
                {
                    patch.Regrow();
                    regrownCount++;
                }

            }
            else
            {
                Debug.LogWarning($"[Sprinkler] {hit.name} is on grassLayer but has no IMowable component.");
            }
            // If already grown, isMowed is false, so we just skip it — nothing happens.
        }        
    }

    private void PlayWaterEffect()
    {
        if (waterParticles != null)
        {
            waterParticles.Play();
            return;
        }

        StartCoroutine(SpawnDroplets());
    }

    private IEnumerator SpawnDroplets()
    {
        if (waterSoundClip != null)
        {
            GameObject tempAudioObj = new GameObject("TempCutAudio");
            tempAudioObj.transform.position = transform.position;

            AudioSource aSource = tempAudioObj.AddComponent<AudioSource>();
            aSource.clip = waterSoundClip;

            aSource.time = 0.0f;
            aSource.Play();

            Destroy(tempAudioObj, 1.0f);
        }

        for (int i = 0; i < dropletCount; i++)
        {
            Vector2 offset = Random.insideUnitCircle * effectRadius;
            Vector3 target = transform.position + new Vector3(offset.x, 0f, offset.y);
            StartCoroutine(AnimateDroplet(transform.position + Vector3.up * 0.5f, target));
            yield return new WaitForSeconds(0.03f);
        }
    }

    private IEnumerator AnimateDroplet(Vector3 start, Vector3 end)
    {
        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        drop.transform.localScale = Vector3.one * dropletScale;
        Destroy(drop.GetComponent<Collider>());

        var renderer = drop.GetComponent<Renderer>();
        Color waterColor = new Color(0.3f, 0.6f, 1f);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", waterColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", waterColor);
        renderer.material = mat;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dropletLifetime;
            Vector3 flatPos = Vector3.Lerp(start, end, t);
            float arc = Mathf.Sin(t * Mathf.PI) * dropletArcHeight;
            drop.transform.position = flatPos + Vector3.up * arc;
            yield return null;
        }

        Destroy(drop);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, effectRadius);
    }
}
