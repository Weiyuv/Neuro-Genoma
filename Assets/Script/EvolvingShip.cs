
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class EvolvingShip : MonoBehaviour
{
    [Header("Movimento")]
    [SerializeField] private float maxSpeed = 7f;
    [SerializeField] private float minimumThrottle = 0.3f;
    [SerializeField] private float turnSpeed = 180f;
    [SerializeField] private float acceleration = 8f;

    [Header("Assistencia de navegacao")]
    [SerializeField, Range(0f, 1f)] private float routeAssist = 0.35f;
    [SerializeField, Range(0f, 2f)] private float obstacleAvoidanceAssist = 0.6f;
    [SerializeField] private float cornerReachDistance = 1.2f;
    [SerializeField] private float goalReachDistance = 1.5f;

    [Header("NavMesh")]
    [SerializeField] private float navMeshSampleDistance = 5f;

    [Header("Sensores")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float sensorDistance = 5f;
    [SerializeField] private float sensorHeight = 0.5f;
    [SerializeField] private float sensorAngle = 40f;

    public bool IsActive => active;
    public bool ReachedGoal { get; private set; }
    public bool Collided { get; private set; }
    public float EpisodeTime { get; private set; }
    public float RealDistance { get; private set; }
    public float Efficiency { get; private set; } = -1f;
    public float Fitness { get; private set; }

    private readonly float[] inputs = new float[NeuralNetwork.InputCount];

    private NeuralNetwork brain;
    private Rigidbody body;
    private EvolutionManager manager;
    private Genome genome;
    private Transform goal;

    private NavMeshPath navPath;
    private Vector3[] corners;
    private int currentCorner;

    private float lastRemainingDistance;
    private float currentSpeed;

    private Vector3 lastPosition;
    private bool active;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        brain = new NeuralNetwork();
        navPath = new NavMeshPath();

        body.useGravity = true;
        body.constraints |= RigidbodyConstraints.FreezeRotationX;
        body.constraints |= RigidbodyConstraints.FreezeRotationZ;

        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void Begin(
        EvolutionManager evolutionManager,
        Genome assignedGenome,
        Transform target,
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        manager = evolutionManager;
        genome = assignedGenome;
        goal = target;

        active = false;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        body.position = spawnPosition;
        body.rotation = spawnRotation;

        transform.SetPositionAndRotation(
            spawnPosition,
            spawnRotation
        );

        currentSpeed = 0f;

        Fitness = 0f;
        EpisodeTime = 0f;
        RealDistance = 0f;

        ReachedGoal = false;
        Collided = false;
        Efficiency = -1f;

        currentCorner = 1;
        lastPosition = spawnPosition;

        corners = null;
        lastRemainingDistance = 0f;

        active = true;

        if (!CalculateRoute())
        {
            Debug.LogWarning(
                "[SHIP] Nao foi possivel calcular a rota NavMesh."
            );

            Collided = true;
            Finish(manager.CollisionPenalty);
            return;
        }

        lastRemainingDistance = CalculateRemainingDistance();
    }

    private bool CalculateRoute()
    {
        if (goal == null)
            return false;

        if (!NavMesh.SamplePosition(
            transform.position,
            out NavMeshHit startHit,
            navMeshSampleDistance,
            NavMesh.AllAreas))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(
            goal.position,
            out NavMeshHit goalHit,
            navMeshSampleDistance,
            NavMesh.AllAreas))
        {
            return false;
        }

        if (!NavMesh.CalculatePath(
            startHit.position,
            goalHit.position,
            NavMesh.AllAreas,
            navPath))
        {
            return false;
        }

        if (navPath.status != NavMeshPathStatus.PathComplete)
            return false;

        corners = navPath.corners;

        if (corners == null || corners.Length < 2)
            return false;

        return true;
    }

    private void FixedUpdate()
    {
        if (!active)
            return;

        float deltaTime = Time.fixedDeltaTime;

        EpisodeTime += deltaTime;

        RealDistance += Vector3.Distance(
            lastPosition,
            body.position
        );

        lastPosition = body.position;

        Fitness -= manager.TimePenalty * deltaTime;

        if (CheckGoalDistance())
        {
            ReachedGoal = true;
            Finish(manager.GoalBonus);
            return;
        }

        UpdateRouteCorner();

        ReadSensors();

        brain.Forward(inputs, genome.Weights);

        float neuralSteering = brain.GetOutput(0);

        float throttleOutput = Mathf.Clamp01(
            (brain.GetOutput(1) + 1f) * 0.5f
        );

        float throttle = Mathf.Lerp(
            minimumThrottle,
            1f,
            throttleOutput
        );

        float steering = CalculateSteering(neuralSteering);

        MoveShip(
            steering,
            throttle,
            deltaTime
        );

        UpdateProgressFitness();
    }

    private bool CheckGoalDistance()
    {
        if (goal == null)
            return false;

        Vector3 shipPosition = body.position;
        Vector3 goalPosition = goal.position;

        shipPosition.y = 0f;
        goalPosition.y = 0f;

        return Vector3.Distance(
            shipPosition,
            goalPosition
        ) <= goalReachDistance;
    }

    private void UpdateRouteCorner()
    {
        if (corners == null || currentCorner >= corners.Length)
            return;

        Vector3 position = body.position;
        Vector3 target = corners[currentCorner];

        position.y = 0f;
        target.y = 0f;

        while (currentCorner < corners.Length - 1 &&
               Vector3.Distance(position, target) <= cornerReachDistance)
        {
            currentCorner++;

            target = corners[currentCorner];
            target.y = 0f;
        }
    }

    private void ReadSensors()
    {
        Vector3 origin = transform.position +
                         Vector3.up * sensorHeight;

        Vector3 targetDirection = GetTargetDirection();

        float routeAngle = Vector3.SignedAngle(
            transform.forward,
            targetDirection,
            Vector3.up
        );

        inputs[0] = Mathf.Clamp(
            routeAngle / 90f,
            -1f,
            1f
        );

        float leftDistance = ReadObstacleSensor(
            Quaternion.Euler(0f, -sensorAngle, 0f) *
            transform.forward,
            origin
        );

        float rightDistance = ReadObstacleSensor(
            Quaternion.Euler(0f, sensorAngle, 0f) *
            transform.forward,
            origin
        );

        float frontDistance = ReadObstacleSensor(
            transform.forward,
            origin
        );

        inputs[1] = Mathf.Max(
            leftDistance,
            frontDistance * 0.5f
        );

        inputs[2] = Mathf.Max(
            rightDistance,
            frontDistance * 0.5f
        );
    }

    private float CalculateSteering(float neuralSteering)
    {
        float routeCorrection = inputs[0] * routeAssist;

        float obstacleCorrection =
            (inputs[1] - inputs[2]) *
            obstacleAvoidanceAssist;

        return Mathf.Clamp(
            neuralSteering +
            routeCorrection +
            obstacleCorrection,
            -1f,
            1f
        );
    }

    private Vector3 GetTargetDirection()
    {
        if (corners == null ||
            corners.Length == 0 ||
            currentCorner >= corners.Length)
        {
            return transform.forward;
        }

        Vector3 direction =
            corners[currentCorner] - body.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return transform.forward;

        return direction.normalized;
    }

    private float ReadObstacleSensor(
        Vector3 direction,
        Vector3 origin)
    {
        if (Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            sensorDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore))
        {
            return Mathf.Clamp01(
                1f - hit.distance / sensorDistance
            );
        }

        return 0f;
    }

    private void MoveShip(
        float steering,
        float throttle,
        float deltaTime)
    {
        float turn = steering * turnSpeed * deltaTime;

        Quaternion rotation = Quaternion.Euler(
            0f,
            turn,
            0f
        );

        Quaternion nextRotation = body.rotation * rotation;

        body.MoveRotation(nextRotation);

        float targetSpeed = throttle * maxSpeed;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            acceleration * deltaTime
        );

        Vector3 velocity =
            nextRotation * Vector3.forward * currentSpeed;

        velocity.y = body.linearVelocity.y;

        body.linearVelocity = velocity;
    }

    private float CalculateRemainingDistance()
    {
        if (corners == null || corners.Length == 0)
            return 0f;

        currentCorner = Mathf.Clamp(
            currentCorner,
            1,
            corners.Length - 1
        );

        float remaining = Vector3.Distance(
            body.position,
            corners[currentCorner]
        );

        for (int i = currentCorner + 1;
             i < corners.Length;
             i++)
        {
            remaining += Vector3.Distance(
                corners[i - 1],
                corners[i]
            );
        }

        return remaining;
    }

    private void UpdateProgressFitness()
    {
        float remaining = CalculateRemainingDistance();

        float progress = lastRemainingDistance - remaining;

        Fitness += progress * manager.ProgressWeight;

        lastRemainingDistance = remaining;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!active)
            return;

        if (((1 << collision.gameObject.layer) &
             obstacleMask.value) != 0)
        {
            Collided = true;

            Finish(manager.CollisionPenalty);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!active || goal == null)
            return;

        if (other.transform == goal ||
            other.transform.IsChildOf(goal) ||
            goal.IsChildOf(other.transform))
        {
            ReachedGoal = true;

            Finish(manager.GoalBonus);
        }
    }

    public void Timeout()
    {
        if (!active)
            return;

        Finish(0f);
    }

    private void Finish(float bonus)
    {
        if (!active)
            return;

        Fitness += bonus;

        active = false;

        if (ReachedGoal &&
            RealDistance > 0f &&
            manager.ReferenceDistance > 0f)
        {
            Efficiency = Mathf.Clamp01(
                manager.ReferenceDistance / RealDistance
            );
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        manager.ReportFinished(this);
    }

    private void OnDrawGizmosSelected()
    {
        if (corners != null && corners.Length > 1)
        {
            Gizmos.color = Color.green;

            for (int i = 1; i < corners.Length; i++)
            {
                Gizmos.DrawLine(
                    corners[i - 1],
                    corners[i]
                );

                Gizmos.DrawSphere(
                    corners[i],
                    0.15f
                );
            }
        }

        Vector3 origin = transform.position +
                         Vector3.up * sensorHeight;

        Gizmos.color = Color.yellow;

        Gizmos.DrawRay(
            origin,
            Quaternion.Euler(0f, -sensorAngle, 0f) *
            transform.forward * sensorDistance
        );

        Gizmos.DrawRay(
            origin,
            transform.forward * sensorDistance
        );

        Gizmos.DrawRay(
            origin,
            Quaternion.Euler(0f, sensorAngle, 0f) *
            transform.forward * sensorDistance
        );
    }
}
