using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class MowerController : MonoBehaviour
{
    [Header("Move Settings")]
    [Tooltip("Base movement speed.")]
    public float baseSpeed = 50f;
    [Tooltip("Sprinting speed.")]
    public float sprintSpeed = 100f;
    [Tooltip("Fuel consumption multiplier when sprinting.")]
    public float sprintFuelMultiplier = 2f;

    private float currentSpeed;

    // isSprinting is true only when the player is sprinting
    // This is read by the DogFSM script to determine whether a sleeping dog should wake up
    private bool _isSprinting;
    public bool isSprinting => _isSprinting;

    [Header("Mud Path Speed Modifiers")]
    [Tooltip("Multiplier applied by mud paths.")]
    private float mudSpeedMultiplier = 1f;
    private readonly Dictionary<int, float> activeSpeedModifiers = new Dictionary<int, float>();
    public float exitGracePeriod = 0.1f;
    private float emptySince = -1f;

    [Header("Audio Settings")]
    [Tooltip("Target volume when the mower is fully up to speed.")]
    [Range(0f, 1f)]
    public float maxVolume = 1f;
    [Tooltip("How fast the audio fades in or out (higher numbers = faster fade).")]
    public float fadeSpeed = 5f;
    [Tooltip("Timestamp in seconds where the smooth engine engine running loop begins.")]
    public float loopStartTime = 3.0f; // Adjust based on your audio file!
    [Tooltip("Timestamp in seconds where the engine running loop ends.")]
    public float loopEndTime = 65.0f;
    public AudioClip sparksSoundClip;
    [Range(0f, 3f)]
    public float sparksVolume = 2f;

    public AudioClip takeDamageSoundClip;

    [Header("Health Settings")]
    public int maxHealth = 3;
    private int currentHealth;
    public TextMeshProUGUI healthText;

    [Header("Hit Settings")]
    // public GameObject hitTextBox;
    // public GameObject outOfFuelTextBox;
    public HPHitEffect hpHitEffect;
    // private bool playerWasHit = false;
    public float freezeDuration = 1.0f;
    private bool isFreezing = false;

    [Header("Invincibility and Blink Settings")]
    public float invincibilityDuration = 2.0f;
    public float blinkInterval = 0.15f;
    private bool isInvulnerable = false;
    // private MeshRenderer meshRenderer;
    private Renderer[] childRenderers;

    [Header("UI Screens")]
    public GameObject gameOver;

    [Header("Fuel Settings")]
    public float maxFuel = 100f;
    private float currentFuel;
    public float fuelUseRate = 5f;
    public Image fuelFilledImage;
    public TextMeshProUGUI fuelPercentText;

    [Header("Death Sequence")]
    public ParticleSystem sparkParticles;
    public ParticleSystem smokeParticles;
    public float smokeDelay = 0.4f;
    public float smokeDuration = 1.5f;

    [Header("Iris Wipe: Game Over Sequence")]    
    public Image irisBackground;    
    [Tooltip("Seconds for the iris to open (converge to circle around player).")]
    [SerializeField] private float irisCloseDuration = 0.15f;
    [Tooltip("Seconds to hold the first iris circle.")]
    [SerializeField] private float irisHoldDuration = 5f;
    [Tooltip("The in-between radius after the first shrink (not zero).")]
    public float irisIntermediateRadius = 0.08f;
    [Tooltip("Seconds for the first shrink, down to the intermediate radius.")]
    public float irisPartialShrinkDuration = 0.4f;
    [Tooltip("Seconds to hold at the intermediate radius before vanishing completely.")]
    public float irisSecondHoldDuration = 1f;
    [Tooltip("Seconds for the iris to shrink completely to black.")]
    [SerializeField] private float irisShrinkDuration = 0.4f;
    [SerializeField] private Camera sceneCamera;
    [SerializeField] private float irisStartRadius = 1.4f;
    [SerializeField] private float irisModerateRadius = 0.18f;
    [SerializeField] private AnimationCurve irisEasing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Material irisMaterialInstance;
    private static readonly int CenterID = Shader.PropertyToID("_Center");
    private static readonly int RadiusID = Shader.PropertyToID("_Radius");
    private static readonly int AspectID = Shader.PropertyToID("_Aspect");

    [Header("References")]
    public LevelTimer levelTimer;
    private AudioSource mowerAudioSource;
    public ParticleSystem grassParticles;

    private Rigidbody rb;
    private Vector3 movement;
    private bool _gameOverTriggered = false;

    public AudioClip gameOverSoundClip;
    public AudioSource sfxSource;

    void Awake()
    {
        if (gameOver != null)
        {
            gameOver.SetActive(false);
        }

        if (irisBackground != null)
        {
            irisBackground.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        rb = GetComponent<Rigidbody>();
        mowerAudioSource = GetComponent<AudioSource>();

        // Safely find the MeshRenderer on this object or its visual children
        /*meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }*/

        if (mowerAudioSource != null)
        {
            mowerAudioSource.volume = 0f;
        }

        childRenderers = GetComponentsInChildren<Renderer>();

        // Initialize health, fuel, and default speed
        currentHealth = maxHealth;
        UpdateHealthUI();

        currentFuel = maxFuel;
        UpdateFuelUI();

        currentSpeed = baseSpeed;

        if (gameOver != null)
        {
            gameOver.SetActive(false);
        }

        // Hide the iris entirely at start
        if (irisBackground != null) irisBackground.gameObject.SetActive(false);

        if (irisBackground == null) Debug.LogWarning("[MowerController] irisBackground not assigned!");

        /*if (hitTextBox != null)
        {
            hitTextBox.SetActive(false);
        }

        if (outOfFuelTextBox != null)
        {
            outOfFuelTextBox.SetActive(false);
        }*/
    }

    void Update()
    {
        if (currentHealth <= 0)
        {            
            return;
        }

        bool isSprinting = false;
        if(Keyboard.current != null)
        {
            isSprinting = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
        }

        UpdateMudPathSpeedMultiplier();

        currentSpeed = (isSprinting ? sprintSpeed : baseSpeed) * mudSpeedMultiplier;
        
        // MAY NEED TO UPDATE TO INPUT SYSTEM/ACTIONS
        // The modern way to read WASD / Arrow keys
        if (Keyboard.current != null)
        {
            float moveX = 0f;
            float moveZ = 0f;

            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveZ = 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveZ = -1f;
            if ((Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) & (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)) moveZ = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveX = -1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveX = 1f;
            if ((Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) & (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)) moveX = 0f;

            movement = new Vector3(moveX, 0f, moveZ);
        }

        // The sprinting bool is set if the player is sprinting
        _isSprinting = isSprinting && movement != Vector3.zero;        

        if (movement != Vector3.zero && currentFuel > 0 && IsActuallyMoving())
        {
            UseFuel();
        }

        HandleEngineAudio();

        UpdateFuelWarning();
    }

    // ─── Mud Path Speed Modifier ─────────────────────────────
    
    public void ApplyMudPathSpeedModifier(int sourceId, float multiplier)
    {
        activeSpeedModifiers[sourceId] = multiplier;
        emptySince = -1f;
    }

    public void RemoveMudPathSpeedModifier(int sourceId)
    {
        if (activeSpeedModifiers.Remove(sourceId) && activeSpeedModifiers.Count == 0)
        {
            emptySince = Time.time;
        }
    }

    private void UpdateMudPathSpeedMultiplier()
    {
        if (activeSpeedModifiers.Count > 0)
        {
            // If standing in multiple slow zones at once, apply the strongest
            // (lowest) slow rather than multiplying them together.
            float strongest = 1f;
            foreach (var m in activeSpeedModifiers.Values)
            {
                if (m < strongest) strongest = m;
            }
            mudSpeedMultiplier = strongest;
            return;
        }

        if (emptySince >= 0f && Time.time - emptySince >= exitGracePeriod)
        {            
            mudSpeedMultiplier = 1f;
            emptySince = -1f;
        }
    }

    private void HandleEngineAudio()
    {
        if (mowerAudioSource == null) return;

        // Is the player actively inputting directions and carrying speed?
        bool isMoving = (movement != Vector3.zero && IsActuallyMoving() && currentFuel > 0);

        if (isMoving)
        {
            // 1. If it's not playing at all, start it from 0 (the ignition startup sound)
            if (!mowerAudioSource.isPlaying)
            {
                mowerAudioSource.time = loopStartTime;
                mowerAudioSource.Play();
            }

            // Smoothly lerp volume up to maximum intensity
            mowerAudioSource.volume = Mathf.MoveTowards(mowerAudioSource.volume, maxVolume, fadeSpeed * Time.deltaTime);

            // 2. THE INDEFINITE LOOP SNAP:
            // If the track reaches the end of the steady engine hum, snap it back to where the hum *started*
            if (mowerAudioSource.time >= loopEndTime)
            {
                mowerAudioSource.time = loopStartTime;
            }
        }
        else
        {
            // Smoothly decrease volume down to dead silence when stopping
            if (mowerAudioSource.isPlaying)
            {
                mowerAudioSource.volume = Mathf.MoveTowards(mowerAudioSource.volume, 0f, fadeSpeed * Time.deltaTime);

                // Completely stop playback once it reaches complete silence
                if (mowerAudioSource.volume <= 0f)
                {
                    mowerAudioSource.Stop();
                }
            }
        }
    }

    public void EmitGrassParticles(int count = 15)
    {
        if (grassParticles != null && currentHealth > 0)
        {
            grassParticles.Emit(count);
        }
    }

    private bool IsActuallyMoving()
    {
        if (rb == null) return false;

        // Calculate velocity strictly on the horizontal X and Z plane (ignoring any minor Y gravity bumps)
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // Returns true if your actual movement speed is greater than a tiny threshold (0.1)
        return horizontalVelocity.magnitude > 0.1f;
    }

    void FixedUpdate()
    {
        if (currentHealth <= 0)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        if (currentFuel <= 0)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        Vector3 targetVelocity = movement.normalized * currentSpeed;

        rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);

        // Rotate the mower to face the direction it's driving
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            rb.MoveRotation(targetRotation);
        }
        else
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        }
    }

    /*private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Enemy"))
        {
            // playerWasHit = true;
            TakeDamage(1);
        }
    }*/

    public void TakeDamage(int damageAmount)
    {
        if (currentHealth <= 0 || isFreezing || isInvulnerable) return;        
        
        if (takeDamageSoundClip != null)
        {
            AudioSource.PlayClipAtPoint(takeDamageSoundClip, Camera.main.transform.position);
        }
        
        currentHealth -= damageAmount;        
        if (currentHealth < 0) currentHealth = 0;

        UpdateHealthUI();

        if (currentHealth >= 0)
        {
            isFreezing = true;
            Time.timeScale = 0f;
            if (mowerAudioSource != null) mowerAudioSource.Pause();

            if (hpHitEffect != null)
            {
                hpHitEffect.PlayHitEffect(currentHealth, this);
            }
            else
            {
                // Safety fallback if no UI script is attached
                StartCoroutine(FallbackFreezeRoutine());
            }
        }
        else
        {
            if (hpHitEffect != null)
                hpHitEffect.StopAllCoroutines();

            Debug.Log($"[MowerController] Health reached 0. timeScale={Time.timeScale}, isFreezing={isFreezing}");

            // Cancel any active freeze coroutine before triggering game over
            StopAllCoroutines();
            Time.timeScale = 1f;
            isFreezing = false;

            Debug.Log($"[MowerController] After cleanup: timeScale={Time.timeScale}");

            TriggerGameOver();
        }
    }

    public void OnShakeComplete()
    {
        if (!isFreezing) return;

        Time.timeScale = 1f;
        isFreezing = false;
        
        if (currentHealth > 0)
        {
            if (mowerAudioSource != null) mowerAudioSource.UnPause();
            // Start blinking immediately as the game unfreezes
            StartCoroutine(InvincibilityBlinkRoutine());
        }      
        else
        {
            StartCoroutine(DeathSequenceRoutine());
        }        
    }

    private IEnumerator DeathSequenceRoutine()
    {
        // Make sure mower stays still during the effects
        if (rb != null)
            rb.linearVelocity = Vector3.zero;

        // 1. Wait for HPHitEffect to finish sliding back home (pauseAfterShake + slideBackDuration)
        if (hpHitEffect != null)
        {
            float waitTime = hpHitEffect.pauseAfterShake + hpHitEffect.slideBackDuration;
            yield return new WaitForSeconds(waitTime);
        }
                
        // 2. Play sparks
        if (sparkParticles != null)
        {            
            if (sparksSoundClip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(sparksSoundClip, sparksVolume);
            }            
            
            sparkParticles.Play();            
        }

        // Brief pause before smoke starts
        yield return new WaitForSeconds(smokeDelay);
                
        // 3. Play smoke
        if (smokeParticles != null)
        {
            Debug.Log("Smoke playing now.");
            if (Camera.main != null)
            {
                // Align particle rotation to Camera UP (screen top) regardless of mower facing direction
                smokeParticles.transform.rotation = Quaternion.LookRotation(Camera.main.transform.up, -Camera.main.transform.forward);
            }

            smokeParticles.Simulate(0, true, true);
            smokeParticles.Play();            
        }

        // 4. Let smoke puff for a moment
        yield return new WaitForSeconds(smokeDuration);

        if (sparkParticles != null)
        {
            var main = sparkParticles.main;
            main.loop = false;
        }

        // 5. Trigger existing Game Over logic unchanged
        TriggerGameOver();
    }

    private IEnumerator FallbackFreezeRoutine()
    {
        yield return new WaitForSecondsRealtime(freezeDuration);
        OnShakeComplete();
    }

    /*private IEnumerator FreezeGameRoutine()
    {
        isFreezing = true;

        // HP circle animation fires for both hit types
        if (hpHitEffect != null)
            hpHitEffect.PlayHitEffect(currentHealth);

        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(freezeDuration);
        Time.timeScale = 1f;

        isFreezing = false;

        StartCoroutine(InvincibilityBlinkRoutine());

        /*isFreezing = true;

        // If the player was hit, show the "Hit!" text box.
        if (condition)
        {
            // Show the "Hit!" text box
            if (hitTextBox != null)
            {
                hitTextBox.SetActive(true);
            }
        }
        // Otherwise, show the "Out of Fuel!" text box
        else
        {
            if (outOfFuelTextBox != null)
            {
                outOfFuelTextBox.SetActive(true);
            }
        }
        
        // Freeze the game world execution
        Time.timeScale = 0f;

        // Wait for real-world seconds (unaffected by Time.timeScale)
        yield return new WaitForSecondsRealtime(freezeDuration);

        // Unfreeze the game world
        Time.timeScale = 1f;

        if (condition)
        {
            // Hide the "Hit!" text box
            if (hitTextBox != null)
            {
                hitTextBox.SetActive(false);
            }
        }
        else
        {
            if (outOfFuelTextBox != null)
            {
                outOfFuelTextBox.SetActive(false);
            }
        }
        

        isFreezing = false;
    }*/

    // ─── Game Over ────────────────────────────────────────────────────────────

    private void TriggerGameOver()
    {
        if (_gameOverTriggered) return;
        _gameOverTriggered = true;

        if (gameOverSoundClip != null && sfxSource != null)
        {
            try
            {
                sfxSource.ignoreListenerPause = true;
                sfxSource.PlayOneShot(gameOverSoundClip);
                AudioListener.pause = true;
                StartCoroutine(ResumeListenerAfterClip(gameOverSoundClip.length));                
            }
            catch (System.Exception e)
            {                
                AudioListener.pause = true;
            }
        }
        else
        {
            AudioListener.pause = true;
        }        

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;        

        // Make sure timeScale is normal so our unscaled coroutine runs cleanly
        Time.timeScale = 1f;
        isFreezing = false;

        // Stop the timer
        if (levelTimer != null)
            levelTimer.SetTimerRunning(false);       

        // Kill player movement
        if (rb != null)
            rb.linearVelocity = Vector3.zero;

        // Push tracked time and mowed score stats into Game Over UI panel variables
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TrackGameOverStats();
        }

        StartCoroutine(IrisWipeSequence());        
    }

    private IEnumerator ResumeListenerAfterClip(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        sfxSource.ignoreListenerPause = false;
    }

    private IEnumerator IrisWipeSequence()
    {
        if (gameOver != null) gameOver.SetActive(false);

        // Brief pause so death doesn't feel instant
        yield return new WaitForSecondsRealtime(0.5f);

        // Fade black overlay in
        /*if (irisBackground != null)
        {            
            irisBackground.gameObject.SetActive(true);
            Color c = irisBackground.color;
            c.a = 0f;
            irisBackground.color = c;

            float elapsed = 0f;
            float fadeDuration = 0.8f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(elapsed / fadeDuration);
                irisBackground.color = c;
                yield return null;
            }
            c.a = 1f;
            irisBackground.color = c;
        }*/

        if (irisBackground != null)
        {
            // Instance the material once so we don't edit the shared asset
            if (irisMaterialInstance == null)
            {
                irisMaterialInstance = new Material(irisBackground.material);
                irisBackground.material = irisMaterialInstance;
            }

            irisBackground.gameObject.SetActive(true);

            // The shader's radius controls visibility now, not alpha - keep it fully opaque
            Color c = irisBackground.color;
            c.a = 1f;
            irisBackground.color = c;

            Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
            Vector3 viewportPos = cam.WorldToViewportPoint(transform.position);
            irisMaterialInstance.SetVector(CenterID, new Vector2(viewportPos.x, viewportPos.y));
            irisMaterialInstance.SetFloat(AspectID, (float)Screen.width / Screen.height);
            irisMaterialInstance.SetFloat(RadiusID, irisStartRadius);

            // Beat 1: quickly settle into a moderate circle around the player
            yield return AnimateIrisRadius(irisStartRadius, irisModerateRadius, irisCloseDuration);

            // Beat 2: hold at the moderate circle
            yield return new WaitForSecondsRealtime(irisHoldDuration);

            // Beat 3: first shrink - down to a small but non-zero circle
            yield return AnimateIrisRadius(irisModerateRadius, irisIntermediateRadius, irisPartialShrinkDuration);

            // Beat 4: hold at that small circle
            yield return new WaitForSecondsRealtime(irisSecondHoldDuration);

            // Beat 3: one decisive shrink down to full black
            yield return AnimateIrisRadius(irisIntermediateRadius, 0f, irisShrinkDuration);
        }

        // Stop dogs
        foreach (DogFSM dog in FindObjectsByType<DogFSM>(FindObjectsSortMode.None))
            dog.SetDogActive(false);

        // Bounce in game over panel
        if (gameOver != null)
        {
            gameOver.SetActive(true);
            StartCoroutine(BounceInPanel(gameOver.transform));
        }        
    }

    private IEnumerator AnimateIrisRadius(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            irisMaterialInstance.SetFloat(RadiusID, to);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = irisEasing.Evaluate(Mathf.Clamp01(elapsed / duration));
            irisMaterialInstance.SetFloat(RadiusID, Mathf.Lerp(from, to, t));
            yield return null;
        }
        irisMaterialInstance.SetFloat(RadiusID, to);
    }

    /// <summary>
    /// Scales the game over panel from 0 up to slightly over 1 then settles at 1,
    /// giving a satisfying bounce-in feel.
    /// </summary>
    private IEnumerator BounceInPanel(Transform panel)
    {
        float duration = 0.5f;
        float overshoot = 1.15f;  // how much it overshoots before settling
        float elapsed = 0f;

        panel.localScale = Vector3.zero;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Bounce curve: grows past 1, then snaps back
            float scale;
            if (t < 0.7f)
            {
                // Grow to overshoot
                scale = Mathf.Lerp(0f, overshoot, t / 0.7f);
            }
            else
            {
                // Snap back to 1
                scale = Mathf.Lerp(overshoot, 1f, (t - 0.7f) / 0.3f);
            }

            panel.localScale = new Vector3(scale, scale, scale);
            yield return null;
        }

        panel.localScale = Vector3.one;        
    }

    private IEnumerator InvincibilityBlinkRoutine()
    {
        isInvulnerable = true;
        float elapsed = 0f;

        // Ensure we actually have a renderer to flash
        if (childRenderers != null && childRenderers.Length > 0)
        {
            bool isVisible = false;

            while (elapsed < invincibilityDuration)
            {
                // Toggle the state flag
                isVisible = !isVisible;

                // Apply the visibility state across all parts of the lawn mower model
                foreach (Renderer ren in childRenderers)
                {
                    if (ren != null) ren.enabled = isVisible;
                }

                yield return new WaitForSeconds(blinkInterval);
                elapsed += blinkInterval;
            }

            // Always force the player back to visible when the sequence ends
            foreach (Renderer ren in childRenderers)
            {
                if (ren != null) ren.enabled = true;
            }
        }
        else
        {
            // Fallback timer if no renderer found
            yield return new WaitForSeconds(invincibilityDuration);
        }

        isInvulnerable = false;
    }

    /*void GameOver()
    {
        Debug.Log("Game Over!");
        
        if (gameOver != null)
        {
            gameOver.SetActive(true);
        }
    }*/

    void UseFuel()
    {
        // Check if the player is currently sprinting
        bool isSprinting = currentSpeed == sprintSpeed;
        float dynamicFuelUseRate = fuelUseRate * (isSprinting ? sprintFuelMultiplier : 1f);
        
        // Subtract fuel in real time
        currentFuel -= dynamicFuelUseRate * Time.deltaTime;
        
        if (currentFuel <= 0)
        {
            currentFuel = 0;
            UpdateFuelUI();
            // playerWasHit = false;
            TakeDamage(1);

            if (currentHealth > 0)
            {
                ReplenishFuel(maxFuel);
            }
        }
        else
        {
            UpdateFuelUI();
        }           
    }

    void UpdateHealthUI()
    {
        if (healthText != null)
        {
            healthText.text = currentHealth.ToString();
        }
    }

    void UpdateFuelUI()
    {
        if (fuelFilledImage != null)
        {
            fuelFilledImage.fillAmount = currentFuel / maxFuel;

            if (currentFuel <= 10f)
            {
                fuelFilledImage.color = Color.red;
            }
            else if (currentFuel <= 20f)
            {
                fuelFilledImage.color = new Color(1f, 0.5f, 0f);                
            }
            else
            {
                fuelFilledImage.color = Color.blue;
            }
        }

        if (fuelPercentText != null)
        {
            fuelPercentText.text = Mathf.CeilToInt(currentFuel) + "%";
        }
    }

    private void UpdateFuelWarning()
    {
        if (fuelPercentText == null) return;

        int percent = Mathf.CeilToInt(currentFuel);

        // Warning pulse below 20% fuel (and above 0%)
        if (percent <= 20 && percent > 0)
        {
            // 10% or below: Fast warning (~4Hz, toggles every 0.125s)
            // 11% to 20%: Standard warning (~2Hz, toggles every 0.25s)
            float cycleDuration = (percent <= 10) ? 0.25f : 0.5f;
            float toggleInterval = cycleDuration / 2f;

            bool showRed = (Time.time % cycleDuration) < toggleInterval;
            fuelPercentText.color = showRed ? Color.red : Color.black;
        }
        else
        {
            fuelPercentText.color = Color.black;
        }
    }

    public void ReplenishFuel(float amount)
    {
        currentFuel += amount;

        if (currentFuel > maxFuel)
        {
            currentFuel = maxFuel;
        }

        UpdateFuelUI();

        Debug.Log("Fuel replenished by " + amount + "! Current fuel: " + currentFuel + ".");
    }

    private void OnDisable()
    {
        // Safety check in case the component gets disabled mid-run
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
        }
    }
}