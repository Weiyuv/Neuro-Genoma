
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class EvolvingShip : MonoBehaviour
{
    [Header("Sensores")]
    [SerializeField] private float sensorDistance = 8f;
    [SerializeField] private float sensorAngle = 35f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Movimento")]
    [SerializeField] private float maxSpeed = 7f;
    [SerializeField] private float turnSpeed = 180f;

    private readonly float[] inputs = new float[NeuralNetwork.InputCount];
    private readonly NeuralNetwork brain = new();

    private Rigidbody body;
    private EvolutionManager manager;
    private Genome genome;
    private Transform goal;

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Vector3 lastPosition;

    private float initialDistance;
    private bool active;

    public float Fitness { get; private set; }
    public Genome Genome => genome;
    public bool IsActive => active;

    // Métricas do episódio
    public bool ReachedGoal { get; private set; }
    public bool Collided { get; private set; }
    public float EpisodeTime { get; private set; }
    public float RealDistance { get; private set; }

    public float Efficiency
    {
        get
        {
            if (!ReachedGoal || RealDistance <= 0f ||
                manager == null || manager.ReferenceDistance <= 0f)
                return -1f;

            return manager.ReferenceDistance / RealDistance;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();

        body.constraints =
            RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    public void Begin(
        EvolutionManager owner,
        Genome newGenome,
        Transform newGoal,
        Vector3 position,
        Quaternion rotation)
    {
        manager = owner;
        genome = newGenome;
        goal = newGoal;

        spawnPosition = position;
        spawnRotation = rotation;

        gameObject.SetActive(true);

        ResetShip();
    }

    public void ResetShip()
    {
        active = false;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        body.position = spawnPosition;
        body.rotation = spawnRotation;

        transform.SetPositionAndRotation(
            spawnPosition,
            spawnRotation
        );

        Physics.SyncTransforms();

        body.Sleep();
        body.WakeUp();

        initialDistance = Vector3.Distance(
            transform.position,
            goal.position
        );

        lastPosition = transform.position;

        Fitness = 0f;

        ReachedGoal = false;
        Collided = false;
        EpisodeTime = 0f;
        RealDistance = 0f;

        active = true;
    }

    private void FixedUpdate()
    {
        if (!active || genome == null)
            return;

        Vector3 currentPosition = body.position;

        RealDistance += Vector3.Distance(
            lastPosition,
            currentPosition
        );

        lastPosition = currentPosition;

        EpisodeTime += Time.fixedDeltaTime;

        ReadSensors();

        brain.Forward(inputs, genome.Weights);

        float turn = brain.GetOutput(0);

        float throttle = Mathf.Clamp01(
            (brain.GetOutput(1) + 1f) * 0.5f
        );

        Quaternion delta = Quaternion.Euler(
            0f,
            turn * turnSpeed * Time.fixedDeltaTime,
            0f
        );

        body.MoveRotation(body.rotation * delta);

        body.linearVelocity =
            transform.forward * (throttle * maxSpeed);

        Fitness =
            (initialDistance -
            Vector3.Distance(transform.position, goal.position))
            * manager.ProgressWeight;
    }

    private void ReadSensors()
    {
        inputs[0] = ReadSensor(transform.forward);

        inputs[1] = ReadSensor(
            Quaternion.AngleAxis(-sensorAngle, Vector3.up)
            * transform.forward
        );

        inputs[2] = ReadSensor(
            Quaternion.AngleAxis(sensorAngle, Vector3.up)
            * transform.forward
        );
    }

    private float ReadSensor(Vector3 direction)
    {
        Vector3 origin = transform.position + Vector3.up * 0.25f;

        if (Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            sensorDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore))
        {
            return hit.distance / sensorDistance;
        }

        return 1f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!active)
            return;

        if (((1 << collision.gameObject.layer) & obstacleMask.value) != 0)
        {
            Collided = true;
            Finish(manager.CollisionPenalty);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (active && other.transform == goal)
        {
            ReachedGoal = true;
            Finish(manager.GoalBonus);
        }
    }

    public void Timeout()
    {
        if (active)
            Finish(-manager.TimePenalty);
    }

    private void Finish(float bonusOrPenalty)
    {
        if (!active)
            return;

        // Registra o último deslocamento antes de finalizar.
        RealDistance += Vector3.Distance(
            lastPosition,
            body.position
        );

        lastPosition = body.position;

        Fitness += bonusOrPenalty;

        active = false;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        manager.ReportFinished(this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        Vector3 origin = transform.position + Vector3.up * 0.25f;

        Gizmos.DrawRay(
            origin,
            transform.forward * sensorDistance
        );

        Gizmos.DrawRay(
            origin,
            Quaternion.AngleAxis(-sensorAngle, Vector3.up)
            * transform.forward * sensorDistance
        );

        Gizmos.DrawRay(
            origin,
            Quaternion.AngleAxis(sensorAngle, Vector3.up)
            * transform.forward * sensorDistance
        );
    }
}
