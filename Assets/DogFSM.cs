using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

/// <summary>
/// Dog enemy FSM: Idle, Patrol, Chase.
/// Requires a NavMeshAgent on the same GameObject.
/// Detection is distance-based — no line-of-sight needed.
/// Handles player collision via OnTriggerEnter on a child trigger collider.
/// </summary>

[RequireComponent(typeof(NavMeshAgent))]
public class DogFSM : MonoBehaviour
{
    // ─── Inspector fields ────────────────────────────────────────────────────

    [Header("Detection Range")]
    [Tooltip("The dog's detection range.")]
    public float detectionRange = 40f;

    [Header("Movement Settings")]
    public float patrolSpeed = 30f;
    public float chaseSpeed = 30f;
    [Tooltip("How close the dog must get to a waypoint before moving to the next one.")]
    public float waypointTolerance = 0.3f;

    [Header("Patrol Settings")]
    [Tooltip("The number of seconds to walk the patrol route before resting.")]
    public float patrolDuration = 10f;

    [Header("Random Waypoint Generation")]
    [Tooltip("How many waypoints to generate for this dog's patrol route.")]
    public int waypointCount = 4;
    [Tooltip("How far to search on the NavMesh when sampling a random point.")]
    public float navMeshSampleDistance = 5f;
    [Tooltip("Max attempts to find a valid NavMesh point before skipping a waypoint.")]
    public int maxSampleAttempts = 20;

    [Header("Fuel Can Bias")]
    [Tooltip("0 = fully random patrol, 1 = always target a fuel can. 0.5 is a good moderate starting point.")]
    [Range(0f, 1f)]
    public float fuelCanBiasChance = 0.5f;
    [Tooltip("How close a waypoint must snap to a fuel can position.")]
    public float fuelCanSnapRadius = 3f;

    [Header("Idle")]
    [Tooltip("The minimum number of seconds to rest before patrolling again.")]
    public float idleTimeMin = 2f;
    [Tooltip("The maximum number of seconds to rest before patrolling again.")]
    public float idleTimeMax = 5f;

    [Header("Chase/Give Up")]
    [Tooltip("The number of seconds the player must stay out of range before the dog gives up.")]
    public float giveUpDelay = 2f;

    [Header("Damage")]
    [Tooltip("How much damage the dog deals when it touches the player.")]
    public int damageAmount = 1;

    [Header("Chase Audio")]
    [Tooltip("The looping bark sound that plays while the dog is chasing the player.")]
    public AudioClip barkSoundClip;
    [Range(0f, 1f)]
    public float barkVolume = 0.5f;

    private AudioSource _audioSource;

    [Header("Bark Visual")]
    public GameObject bark;
    public Vector3 barkOffset = new Vector3(0f, 1.4f, 0f);
    public Vector3 barkRotation = new Vector3(60f, 0f, 0f);
    public float barkInterval = 2.2f;

    private float _barkTimer;

    [Header("Doghouse")]
    [Tooltip("Doghouse dogs sleep here and only wake up when the player sprints nearby.")]
    public Transform doghouse;
    [Tooltip("How close the player must be while sprinting to wake a sleeping dog.")]
    public float sprintDetectionRange = 15f;
    [Tooltip("How close the dog must get to the doghouse before going back to sleep.")]
    public float doghouseArrivalTolerance = 0.5f;

    [Header("Mud Path Speed Modifiers")]
    private float mudSpeedMultiplier = 1f;
    private readonly Dictionary<int, float> activeSpeedModifiers = new Dictionary<int, float>();
    public float exitGracePeriod = 0.1f;
    private float emptySince = -1f;

    private float _baseSpeed = 0f;

    // ─── State enum ──────────────────────────────────────────────────────────

    public enum DogState { Idle, Patrol, Chase, Sleeping, Return }
    public DogState CurrentState { get; private set; } = DogState.Idle;

    // ─── Private fields ───────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Transform _player;
    private MowerController _playerMower;
    private DogState _stateBeforeChase = DogState.Idle;

    public List<Vector3> _waypoints = new();
    private int _waypointIndex;

    private float _idleTimer;
    private float _patrolTimer;
    private float _giveUpTimer;

    // Cached NavMesh bounds: computed once and reused for all waypoint rolls
    private Vector3 _navMeshCenter;
    private Vector3 _navMeshExtents;

    // Cached fuel can positions: refreshed each time waypoints are generated
    // so the list stays accurate as cans are collected during gameplay
    private Vector3[] _fuelCanPositions = new Vector3[0];

    // Controls if the FSM runs.
    // It is set to false during the level intro sequence and after a game over.
    private bool _active = false;

    private bool hasDoghouse => doghouse != null;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            _player = playerObject.transform;
            _playerMower = playerObject.GetComponent<MowerController>();
        }                    

        CacheNavMeshBounds();

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        _audioSource.clip = barkSoundClip;
        _audioSource.volume = barkVolume;
        _audioSource.loop = false;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        if (hasDoghouse)
        {
            EnterSleeping();
        }
        else
        {
            EnterIdle();
        }
    }

    private void Update()
    {
        if (!_active || _player == null) return;

        UpdateMudPathSpeedMultiplier();
        _agent.speed = _baseSpeed * mudSpeedMultiplier;

        switch (CurrentState)
        {
            case DogState.Idle: UpdateIdle(); break;
            case DogState.Patrol: UpdatePatrol(); break;
            case DogState.Chase: UpdateChase(); break;
            case DogState.Sleeping: UpdateSleeping(); break;
            case DogState.Return: UpdateReturn(); break;
        }
    }

    // Mud Methods
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

    // ─── Public control ───────────────────────────────────────────────────────

    /// <summary>
    /// Called by LevelIntroSequence and MowerController to start or stop the dogs.
    /// False: The dogs don't move. True: The FSM runs normally.
    /// </summary>
    public void SetDogActive(bool active)
    {
        _active = active;

        if (!active)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
        else
        {
            _agent.isStopped = false;
        }
    }

    // ─── NavMesh bounds ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the bounding box of all NavMesh triangles so waypoints can be
    /// sampled from anywhere on the walkable surface, not just near the dog.
    /// </summary>
    private void CacheNavMeshBounds()
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        if (triangulation.vertices.Length == 0)
        {
            Debug.LogWarning("[DogFSM] NavMesh has no triangles. Has it been baked?");
            _navMeshCenter = Vector3.zero;
            _navMeshExtents = new Vector3(100f, 0f, 100f);
            return;
        }

        Bounds bounds = new Bounds(triangulation.vertices[0], Vector3.zero);
        foreach (Vector3 v in triangulation.vertices)
            bounds.Encapsulate(v);

        _navMeshCenter = bounds.center;
        _navMeshExtents = bounds.extents;

        Debug.Log($"[DogFSM] NavMesh bounds cached: center = {_navMeshCenter}, extents = {_navMeshExtents}");
    }

    // ─── Fuel can cache ───────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes the list of fuel can positions from all active "FuelCan" tagged
    /// objects. Called each time waypoints are generated so collected cans are
    /// excluded automatically.
    /// </summary>
    private void RefreshFuelCanPositions()
    {
        GameObject[] cans = GameObject.FindGameObjectsWithTag("FuelCan");
        _fuelCanPositions = new Vector3[cans.Length];
        for (int i = 0; i < cans.Length; i++)
            _fuelCanPositions[i] = cans[i].transform.position;
    }

    // ─── Waypoint generation ──────────────────────────────────────────────────

    private void GenerateWaypoints()
    {
        RefreshFuelCanPositions();

        _waypoints.Clear();

        for (int i = 0; i < waypointCount; i++)
        {
            // Bias roll: try to place this waypoint near a fuel can
            if (_fuelCanPositions.Length > 0 && Random.value < fuelCanBiasChance)
            {
                Vector3 canPos = _fuelCanPositions[Random.Range(0, _fuelCanPositions.Length)];
                if (TryGetNavMeshPointNear(canPos, fuelCanSnapRadius, out Vector3 canWaypoint))
                {
                    _waypoints.Add(canWaypoint);
                    continue;
                }
                // If snap failed, fall through to random
            }

            if (TryGetRandomNavMeshPoint(out Vector3 randomPoint))
                _waypoints.Add(randomPoint);
        }

        if (_waypoints.Count == 0)
            Debug.LogWarning($"[DogFSM] {name} failed to generate any waypoints. " +
                             $"Try increasing Nav Mesh Sample Distance in the Inspector.");
        else
            Debug.Log($"[DogFSM] {name} generated {_waypoints.Count} waypoints " +
                      $"({_fuelCanPositions.Length} fuel cans active).");
    }

    /// <summary>
    /// Snaps a given world position to the nearest NavMesh point within snapRadius.
    /// Used to place waypoints near fuel cans.
    /// </summary>
    private bool TryGetNavMeshPointNear(Vector3 origin, float snapRadius, out Vector3 result)
    {
        if (NavMesh.SamplePosition(origin, out NavMeshHit hit, snapRadius, NavMesh.AllAreas))
        {
            result = hit.position;
            return true;
        }
        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Picks a random point anywhere inside the NavMesh bounding box (XZ only)
    /// and snaps it to the nearest NavMesh position. Returns false if no valid
    /// point is found within maxSampleAttempts tries.
    /// </summary>
    private bool TryGetRandomNavMeshPoint(out Vector3 result)
    {
        for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(_navMeshCenter.x - _navMeshExtents.x, _navMeshCenter.x + _navMeshExtents.x),
                _navMeshCenter.y,
                Random.Range(_navMeshCenter.z - _navMeshExtents.z, _navMeshCenter.z + _navMeshExtents.z)
            );

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    // ─── Collision/Trigger ──────────────────────────────────────────────────

    // Called by child trigger collider via DogHitbox.cs
    public void OnPlayerContact(MowerController mower)
    {
        mower.TakeDamage(damageAmount);

        // A doghouse dog returns home after attacking the player
        if (hasDoghouse && CurrentState == DogState.Chase)
        {
            EnterReturn();
        }
    }

    // ─── State entry ──────────────────────────────────────────────────────────

    private void EnterIdle()
    {
        CurrentState = DogState.Idle;
        _idleTimer = Random.Range(idleTimeMin, idleTimeMax);
        _agent.isStopped = true;

        _audioSource.Stop();
    }

    private void EnterPatrol()
    {
        GenerateWaypoints();
        _waypointIndex = 0;

        CurrentState = DogState.Patrol;
        _patrolTimer = patrolDuration;
        _baseSpeed = patrolSpeed;
        _agent.isStopped = false;
        SetDestinationToWaypoint();

        _audioSource.Stop();
    }

    private void EnterChase()
    {        
        if (CurrentState != DogState.Chase)
            _stateBeforeChase = CurrentState;

        CurrentState = DogState.Chase;
        _giveUpTimer = giveUpDelay;
        _baseSpeed = chaseSpeed;
        _agent.isStopped = false;

        PlayBark();
        _barkTimer = barkInterval;        
    }

    private void EnterSleeping()
    {
        CurrentState = DogState.Sleeping;
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;

        _audioSource.Stop();
    }

    private void EnterReturn()
    {
        CurrentState = DogState.Return;
        _baseSpeed = patrolSpeed;
        _agent.isStopped = false;
        _agent.SetDestination(doghouse.position);

        _audioSource.Stop();
    }

    // ─── State update ─────────────────────────────────────────────────────────

    private void UpdateIdle()
    {
        if (PlayerInRange()) { EnterChase(); return; }

        _idleTimer -= Time.deltaTime;
        if (_idleTimer <= 0f)
            EnterPatrol();
    }

    private void UpdatePatrol()
    {
        if (PlayerInRange()) { EnterChase(); return; }

        if (_waypoints.Count == 0)
        {
            EnterIdle();
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= waypointTolerance)
        {
            _waypointIndex = (_waypointIndex + 1) % _waypoints.Count;
            SetDestinationToWaypoint();
        }

        _patrolTimer -= Time.deltaTime;
        if (_patrolTimer <= 0f)
            EnterIdle();
    }

    private void UpdateChase()
    {
        if (Time.timeScale == 0f) return;

        _barkTimer -= Time.deltaTime;
        if (_barkTimer <= 0f)
        {
            PlayBark();
            _barkTimer = barkInterval;
        }

        if (PlayerInRange())
        {
            _giveUpTimer = giveUpDelay;
            _agent.SetDestination(_player.position);
        }
        else
        {
            _giveUpTimer -= Time.deltaTime;
            if (_giveUpTimer <= 0f)
            {
                if (hasDoghouse)
                {
                    EnterReturn();
                }
                else if (_stateBeforeChase == DogState.Patrol)
                {
                    EnterPatrol();
                }                    
                else
                {
                    EnterIdle();
                }                    
            }
            else
            {
                _agent.SetDestination(_player.position);
            }
        }
    }

    private void UpdateSleeping()
    {
        if (PlayerSprintingInRange())
        {
            EnterChase();
        }
    }

    private void UpdateReturn()
    {
        if (!_agent.pathPending && _agent.remainingDistance <= doghouseArrivalTolerance)
        {
            EnterSleeping();
        }
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    private void PlayBark()
    {
        if (barkSoundClip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(barkSoundClip, barkVolume);
        }

        if (bark != null)
        {
            GameObject popup = Instantiate(bark, transform);
            popup.transform.localPosition = barkOffset;
            popup.transform.localRotation = Quaternion.identity;            

            Destroy(popup, 1.0f);
        }
    }

    private bool PlayerInRange()
    {
        return Vector3.Distance(transform.position, _player.position) <= detectionRange;
    }

    private bool PlayerSprintingInRange()
    {
        if (_playerMower == null || !_playerMower.isSprinting) return false;

        return Vector3.Distance(transform.position, _player.position) <= sprintDetectionRange;
    }

    private void SetDestinationToWaypoint()
    {
        if (_waypoints.Count == 0) return;
        _agent.SetDestination(_waypoints[_waypointIndex]);
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = CurrentState == DogState.Chase ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        if (hasDoghouse)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, sprintDetectionRange);
            Gizmos.DrawLine(transform.position, doghouse.position);
            Gizmos.DrawWireCube(doghouse.position, Vector3.one * 0.5f);
        }

        if (Application.isPlaying)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
            Gizmos.DrawWireCube(_navMeshCenter, _navMeshExtents * 2f);
        }

        if (_waypoints == null || _waypoints.Count < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < _waypoints.Count; i++)
        {
            Gizmos.DrawSphere(_waypoints[i], 0.25f);
            int next = (i + 1) % _waypoints.Count;
            Gizmos.DrawLine(_waypoints[i], _waypoints[next]);
        }
    }
}
