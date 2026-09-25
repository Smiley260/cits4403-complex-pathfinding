using UnityEngine;

public class AStarAgent2D : MonoBehaviour
{
    [Header("Movement")]

    [Min(0.1f)]
    public float speed = 2f;

    [Min(0.1f)]
    public float acceleration = 3f;

    [Min(0.01f)]
    public float waypointDistance = 0.10f;

    [Min(0.01f)]
    public float arrivalDistance = 0.35f;


    [Header("Movement Collision")]

    [Min(0.01f)]
    public float collisionRadius = 0.2f;

    public LayerMask collisionMask;


    [Header("Dynamic Repathing")]

    [Tooltip(
        "Each A* agent independently recalculates its own path this often."
    )]
    [Min(0.1f)]
    public float dynamicRepathInterval = 1f;


    [Tooltip(
        "Minimum time between collision-triggered path calculations."
    )]
    [Min(0.01f)]
    public float collisionRepathCooldown = 0.05f;


    [Tooltip(
        "Retry delay when a path cannot currently be found."
    )]
    [Min(0.01f)]
    public float failedPathRetryDelay = 0.15f;


    [Header("Stuck Detection")]

    [Tooltip(
        "How often the agent checks whether it is actually moving."
    )]
    [Min(0.05f)]
    public float stuckCheckInterval = 0.25f;


    [Tooltip(
        "Minimum movement during one stuck-check interval."
    )]
    [Min(0f)]
    public float stuckMinimumMovement = 0.02f;


    [Tooltip(
        "How long the agent can remain effectively stationary before recovery."
    )]
    [Min(0.05f)]
    public float stuckTime = 0.75f;


    [Header("Wall Recovery")]

    [Tooltip(
        "Maximum distance used when escaping from a wall."
    )]
    [Min(0.05f)]
    public float recoveryDistance = 0.75f;


    [Min(8)]
    public int recoveryDirections = 24;


    [Tooltip(
        "Small extra gap placed between the agent and the wall after recovery."
    )]
    [Min(0.001f)]
    public float recoveryMargin = 0.03f;


    [Header("References")]

    public AntSettings movementSettings;


    private AStarPathfinder2D pathfinder;

    private AntColony colony;

    private Storm storm;


    private Vector2 homePosition;

    private Vector2 foodPosition;


    private Vector2[] currentPath;

    private int pathIndex;


    private bool goingToFood = true;


    private Vector2 currentVelocity;


    private float nextDynamicRepathTime;

    private float nextCollisionRepathTime;

    private float nextFailedPathRetryTime;


    private float stuckTimer;

    private float stuckCheckTimer;

    private Vector2 stuckCheckStartPosition;


    private SpriteRenderer spriteRenderer;

    private static Sprite sharedSprite;


    public bool HasPath
    {
        get
        {
            return
                currentPath != null &&
                currentPath.Length > 0 &&
                pathIndex <
                currentPath.Length;
        }
    }


    // ============================================================
    // INITIALISE
    // ============================================================

    public void Initialise(
        AStarPathfinder2D pathfinder,
        Vector2 homePosition,
        Vector2 foodPosition,
        AntColony colony
    )
    {
        this.pathfinder =
            pathfinder;


        this.homePosition =
            homePosition;


        this.foodPosition =
            foodPosition;


        this.colony =
            colony;

        storm = (Storm)GameObject.FindWithTag("Storm").GetComponent("Storm");


        if (
            movementSettings == null &&
            colony != null
        )
        {
            movementSettings =
                colony.settings;
        }


        if (
            pathfinder != null &&
            pathfinder.grid != null
        )
        {
            collisionRadius =
                pathfinder.grid.agentRadius;


            collisionMask =
                pathfinder.grid.unwalkableMask;
        }


        goingToFood =
            true;


        CreateVisual();


        currentVelocity =
            Vector2.zero;


        stuckTimer =
            0f;


        stuckCheckTimer =
            0f;


        stuckCheckStartPosition =
            transform.position;


        nextDynamicRepathTime =
            Time.time +
            dynamicRepathInterval;


        nextCollisionRepathTime =
            0f;


        nextFailedPathRetryTime =
            0f;


        // ========================================================
        // THIS AGENT'S OWN INITIAL A*
        // ========================================================

        RecalculatePathImmediately();


        if (
            HasPath
        )
        {
            InitialiseVelocityFromPath();
        }
    }


    // ============================================================
    // VISUAL
    // ============================================================

    private void CreateVisual()
    {
        spriteRenderer =
            GetComponent<
                SpriteRenderer
            >();


        if (
            spriteRenderer == null
        )
        {
            spriteRenderer =
                gameObject.AddComponent<
                    SpriteRenderer
                >();
        }


        if (
            sharedSprite == null
        )
        {
            Texture2D texture =
                new Texture2D(
                    16,
                    16
                );


            Color[] pixels =
                new Color[
                    16 * 16
                ];


            for (
                int i = 0;
                i < pixels.Length;
                i++
            )
            {
                pixels[i] =
                    Color.white;
            }


            texture.SetPixels(
                pixels
            );


            texture.Apply();


            texture.filterMode =
                FilterMode.Point;


            sharedSprite =
                Sprite.Create(
                    texture,
                    new Rect(
                        0,
                        0,
                        16,
                        16
                    ),
                    new Vector2(
                        0.5f,
                        0.5f
                    ),
                    16f
                );
        }


        spriteRenderer.sprite =
            sharedSprite;


        spriteRenderer.sortingOrder =
            20;


        spriteRenderer.transform.localScale =
            Vector3.one *
            0.35f;
    }


    // ============================================================
    // UPDATE
    // ============================================================

    private void Update()
    {
        if (
            pathfinder == null ||
            !pathfinder.IsReady()
        )
        {
            return;
        }


        // --------------------------------------------------------
        // Only consider the agent to be physically inside a wall
        // if the ACTUAL physical radius overlaps the wall.
        //
        // We no longer use the larger planning clearance here.
        // --------------------------------------------------------

        if (
            IsPhysicallyInsideWall()
        )
        {
            currentVelocity =
                Vector2.zero;


            TryEscapeFromWall(
                Vector2.zero,
                Vector2.zero,
                GetDirectionToCurrentTarget()
            );


            RecalculatePathImmediately();
        }


        // --------------------------------------------------------
        // No current path.
        // --------------------------------------------------------

        if (
            !HasPath
        )
        {
            if (
                Time.time >=
                nextFailedPathRetryTime
            )
            {
                RecalculatePathImmediately();
            }


            if (
                !HasPath
            )
            {
                return;
            }
        }


        // --------------------------------------------------------
        // Dynamic replanning.
        //
        // Every agent performs its OWN search.
        // --------------------------------------------------------

        if (
            Time.time >=
            nextDynamicRepathTime
        )
        {
            RecalculatePathImmediately();


            nextDynamicRepathTime =
                Time.time +
                dynamicRepathInterval;
        }


        if (
            !HasPath
        )
        {
            return;
        }


        Vector2 currentPosition =
            transform.position;


        // --------------------------------------------------------
        // Advance only through waypoints that have actually been
        // reached.
        //
        // We deliberately do not jump several waypoints ahead.
        // --------------------------------------------------------

        while (
            pathIndex <
            currentPath.Length - 1
        )
        {
            float distance =
                Vector2.Distance(
                    currentPosition,
                    currentPath[pathIndex]
                );


            if (
                distance >
                waypointDistance
            )
            {
                break;
            }


            pathIndex++;
        }


        if (
            pathIndex >=
            currentPath.Length
        )
        {
            ArrivedAtDestination();
            return;
        }


        Vector2 target =
            currentPath[pathIndex];


        Vector2 offset =
            target -
            currentPosition;


        float distanceToTarget =
            offset.magnitude;


        // --------------------------------------------------------
        // FINAL TARGET
        // --------------------------------------------------------

        if (
            pathIndex ==
            currentPath.Length - 1
            &&
            distanceToTarget <=
            arrivalDistance
        )
        {
            ArrivedAtDestination();
            return;
        }


        if (
            distanceToTarget <=
            0.0001f
        )
        {
            return;
        }


        Vector2 desiredDirection =
            offset /
            distanceToTarget;


        // --------------------------------------------------------
        // Check the next waypoint using PHYSICAL clearance.
        //
        // The A* route itself was planned with extra clearance,
        // but the moving agent only needs its actual body to fit.
        // --------------------------------------------------------

        if (
            !pathfinder.grid.IsPhysicalSegmentClear(
                currentPosition,
                target
            )
        )
        {
            currentVelocity =
                Vector2.zero;


            RecalculatePathImmediately();


            if (
                !HasPath
            )
            {
                return;
            }


            currentPosition =
                transform.position;


            if (
                pathIndex >=
                currentPath.Length
            )
            {
                return;
            }


            target =
                currentPath[pathIndex];


            offset =
                target -
                currentPosition;


            if (
                offset.sqrMagnitude <=
                0.0001f
            )
            {
                return;
            }


            desiredDirection =
                offset.normalized;
        }


        // ========================================================
        // SAME MOVEMENT MODEL AS ANT.CS
        // ========================================================

        Vector2 desiredVelocity =
            desiredDirection *
            GetMaxSpeed();


        SteerTowards(
            desiredVelocity
        );


        float moveDistance =
            currentVelocity.magnitude *
            Time.deltaTime;


        if (
            moveDistance <=
            0.000001f
        )
        {
            UpdateStuckDetection();
            return;
        }


        Vector2 movementDirection =
            currentVelocity.normalized;


        float physicalRadius =
            GetPhysicalCollisionRadius();


        // --------------------------------------------------------
        // FULL PHYSICAL COLLISION TEST
        // --------------------------------------------------------

        RaycastHit2D hit =
            Physics2D.CircleCast(
                currentPosition,
                physicalRadius,
                movementDirection,
                moveDistance +
                recoveryMargin,
                collisionMask
            );


        // ========================================================
        // WALL HIT
        // ========================================================

        if (
            hit.collider != null
        )
        {
            currentVelocity =
                Vector2.zero;


            TryEscapeFromWall(
                hit.normal,
                hit.point,
                desiredDirection
            );


            RequestCollisionRepath();


            UpdateStuckDetection();


            return;
        }


        // --------------------------------------------------------
        // SECOND SAFETY CHECK
        // --------------------------------------------------------

        Vector2 newPosition =
            currentPosition +
            currentVelocity *
            Time.deltaTime;


        if (
            !pathfinder.grid.IsPhysicalPositionWalkable(
                newPosition
            )
        )
        {
            currentVelocity =
                Vector2.zero;


            TryEscapeFromWall(
                Vector2.zero,
                Vector2.zero,
                desiredDirection
            );


            RequestCollisionRepath();


            UpdateStuckDetection();


            return;
        }


        // --------------------------------------------------------
        // APPLY MOVEMENT
        // --------------------------------------------------------

        transform.SetPositionAndRotation(
            new Vector3(
                newPosition.x,
                newPosition.y,
                -0.5f
            ),
            Quaternion.FromToRotation(
                Vector3.right,
                movementDirection
            )
        );


        UpdateStuckDetection();
    }


    // ============================================================
    // PHYSICAL WALL TEST
    // ============================================================

    private bool IsPhysicallyInsideWall()
    {
        if (
            pathfinder == null ||
            pathfinder.grid == null
        )
        {
            return false;
        }


        // Use slightly less than the physical radius.
        //
        // This avoids treating an agent that is merely touching
        // a wall as being "inside" it.
        float checkRadius =
            GetPhysicalCollisionRadius() *
            0.9f;


        return
            Physics2D.OverlapCircle(
                transform.position,
                checkRadius,
                collisionMask
            ) != null;
    }


    // ============================================================
    // STUCK DETECTION
    // ============================================================

    private void UpdateStuckDetection()
    {
        stuckCheckTimer +=
            Time.deltaTime;


        if (
            stuckCheckTimer <
            stuckCheckInterval
        )
        {
            return;
        }


        float movement =
            Vector2.Distance(
                transform.position,
                stuckCheckStartPosition
            );


        bool tryingToMove =
            HasPath &&
            currentVelocity.magnitude >
            0.1f;


        stuckCheckTimer =
            0f;


        stuckCheckStartPosition =
            transform.position;


        if (
            tryingToMove &&
            movement <=
            stuckMinimumMovement
        )
        {
            stuckTimer +=
                stuckCheckInterval;
        }
        else
        {
            stuckTimer =
                0f;
        }


        if (
            stuckTimer >=
            stuckTime
        )
        {
            stuckTimer =
                0f;


            currentVelocity =
                Vector2.zero;


            TryEscapeFromWall(
                Vector2.zero,
                Vector2.zero,
                GetDirectionToCurrentTarget()
            );


            RecalculatePathImmediately();
        }
    }


    // ============================================================
    // CURRENT TARGET DIRECTION
    // ============================================================

    private Vector2 GetDirectionToCurrentTarget()
    {
        if (
            HasPath
        )
        {
            Vector2 pathDirection =
                currentPath[pathIndex] -
                (Vector2)transform.position;


            if (
                pathDirection.sqrMagnitude >
                0.0001f
            )
            {
                return
                    pathDirection.normalized;
            }
        }


        Vector2 finalTarget =
            goingToFood
                ? foodPosition
                : homePosition;


        Vector2 targetDirection =
            finalTarget -
            (Vector2)transform.position;


        if (
            targetDirection.sqrMagnitude <=
            0.0001f
        )
        {
            return Vector2.zero;
        }


        return
            targetDirection.normalized;
    }


    // ============================================================
    // COLLISION SETTINGS
    // ============================================================

    private float GetPhysicalCollisionRadius()
    {
        if (
            pathfinder != null &&
            pathfinder.grid != null
        )
        {
            return
                pathfinder.grid.agentRadius;
        }


        return collisionRadius;
    }


    // ============================================================
    // SPEED
    // ============================================================

    private float GetMaxSpeed()
    {
        float stormMult = storm.GetSpeedReduction(transform.position);
        if (
            movementSettings != null
        )
        {
            return
                movementSettings.maxSpeed*stormMult;
        }


        return speed*stormMult;
    }


    private float GetAcceleration()
    {
        if (
            movementSettings != null
        )
        {
            return
                movementSettings.acceleration;
        }


        return acceleration;
    }


    // ============================================================
    // STEERING
    // ============================================================

    private void SteerTowards(
        Vector2 desiredVelocity
    )
    {
        Vector2 steeringForce =
            desiredVelocity -
            currentVelocity;


        Vector2 accelerationVector =
            Vector2.ClampMagnitude(
                steeringForce *
                GetAcceleration(),
                GetAcceleration()
            );


        currentVelocity +=
            accelerationVector *
            Time.deltaTime;


        currentVelocity =
            Vector2.ClampMagnitude(
                currentVelocity,
                GetMaxSpeed()
            );
    }


    // ============================================================
    // INITIAL VELOCITY
    // ============================================================

    private void InitialiseVelocityFromPath()
    {
        if (
            !HasPath
        )
        {
            currentVelocity =
                Vector2.zero;


            return;
        }


        Vector2 direction =
            currentPath[pathIndex] -
            (Vector2)transform.position;


        if (
            direction.sqrMagnitude <=
            0.0001f
        )
        {
            currentVelocity =
                Vector2.zero;


            return;
        }


        currentVelocity =
            direction.normalized *
            GetMaxSpeed();
    }


    // ============================================================
    // INDEPENDENT A* SEARCH
    // ============================================================

    private void RecalculatePathImmediately()
    {
        if (
            pathfinder == null ||
            !pathfinder.IsReady()
        )
        {
            return;
        }


        Vector2 target =
            goingToFood
                ? foodPosition
                : homePosition;


        // ========================================================
        // THIS IS A COMPLETELY SEPARATE A* SEARCH.
        //
        // No route is shared between agents.
        // ========================================================

        Vector2[] newPath =
            pathfinder.FindPathImmediate(
                transform.position,
                target
            );


        if (
            newPath == null ||
            newPath.Length == 0
        )
        {
            // IMPORTANT:
            //
            // If there is already a valid path, don't throw it away
            // just because a transient replan failed.
            //
            // The agent can continue using the existing route until
            // another successful recalculation becomes available.
            if (
                HasPath
            )
            {
                return;
            }


            currentPath =
                null;


            pathIndex =
                0;


            currentVelocity =
                Vector2.zero;


            nextFailedPathRetryTime =
                Time.time +
                failedPathRetryDelay;


            return;
        }


        SetPath(
            newPath
        );


        nextFailedPathRetryTime =
            0f;
    }


    // ============================================================
    // COLLISION REPAT
    // ============================================================

    private void RequestCollisionRepath()
    {
        if (
            Time.time <
            nextCollisionRepathTime
        )
        {
            return;
        }


        nextCollisionRepathTime =
            Time.time +
            collisionRepathCooldown;


        RecalculatePathImmediately();
    }


    // ============================================================
    // SET PATH
    // ============================================================

    private void SetPath(
        Vector2[] path
    )
    {
        currentPath =
            path;


        if (
            currentPath == null ||
            currentPath.Length == 0
        )
        {
            pathIndex =
                0;


            currentVelocity =
                Vector2.zero;


            return;
        }


        Vector2 currentPosition =
            transform.position;


        pathIndex =
            0;


        // Discard only waypoints that are genuinely already
        // reached.
        while (
            pathIndex <
            currentPath.Length - 1
        )
        {
            float distance =
                Vector2.Distance(
                    currentPosition,
                    currentPath[pathIndex]
                );


            if (
                distance >
                waypointDistance
            )
            {
                break;
            }


            pathIndex++;
        }


        if (
            currentVelocity.sqrMagnitude <=
            0.0001f
        )
        {
            InitialiseVelocityFromPath();
        }
    }


    // ============================================================
    // WALL RECOVERY
    // ============================================================

    private bool TryEscapeFromWall(
        Vector2 hitNormal,
        Vector2 hitPoint,
        Vector2 desiredDirection
    )
    {
        if (
            pathfinder == null ||
            pathfinder.grid == null
        )
        {
            return false;
        }


        Vector2 currentPosition =
            transform.position;


        float physicalRadius =
            GetPhysicalCollisionRadius();


        // ========================================================
        // METHOD 1:
        // Use the actual collision normal.
        // ========================================================

        if (
            hitNormal.sqrMagnitude >
            0.0001f
        )
        {
            Vector2 normal =
                hitNormal.normalized;


            // First try placing the agent just outside the
            // actual collision point.
            if (
                hitPoint != Vector2.zero
            )
            {
                Vector2 candidate =
                    hitPoint +
                    normal *
                    (
                        physicalRadius +
                        recoveryMargin
                    );


                if (
                    pathfinder.grid.IsPhysicalPositionWalkable(
                        candidate
                    )
                )
                {
                    MoveToRecoveryPosition(
                        candidate
                    );


                    return true;
                }
            }


            // Otherwise push directly along the collision normal.
            for (
                int i = 1;
                i <= 10;
                i++
            )
            {
                float distance =
                    recoveryDistance *
                    i /
                    10f;


                Vector2 candidate =
                    currentPosition +
                    normal *
                    distance;


                if (
                    pathfinder.grid.IsPhysicalPositionWalkable(
                        candidate
                    )
                )
                {
                    MoveToRecoveryPosition(
                        candidate
                    );


                    return true;
                }
            }
        }


        // ========================================================
        // METHOD 2:
        // Derive a normal from the closest overlapping collider.
        // ========================================================

        Collider2D wall =
            Physics2D.OverlapCircle(
                currentPosition,
                physicalRadius *
                1.1f,
                collisionMask
            );


        if (
            wall != null
        )
        {
            Vector2 closestPoint =
                wall.ClosestPoint(
                    currentPosition
                );


            Vector2 normal =
                currentPosition -
                closestPoint;


            if (
                normal.sqrMagnitude >
                0.0001f
            )
            {
                normal.Normalize();


                for (
                    int i = 1;
                    i <= 10;
                    i++
                )
                {
                    float distance =
                        recoveryDistance *
                        i /
                        10f;


                    Vector2 candidate =
                        currentPosition +
                        normal *
                        distance;


                    if (
                        pathfinder.grid.IsPhysicalPositionWalkable(
                            candidate
                        )
                    )
                    {
                        MoveToRecoveryPosition(
                            candidate
                        );


                        return true;
                    }
                }
            }
        }


        // ========================================================
        // METHOD 3:
        // Search around the agent for the safest nearby position.
        // ========================================================

        int directionCount =
            Mathf.Max(
                8,
                recoveryDirections
            );


        float bestScore =
            float.MinValue;


        Vector2 bestPosition =
            currentPosition;


        bool found =
            false;


        Vector2 desired =
            desiredDirection;


        if (
            desired.sqrMagnitude >
            0.0001f
        )
        {
            desired.Normalize();
        }


        for (
            int distanceStep = 1;
            distanceStep <= 12;
            distanceStep++
        )
        {
            float distance =
                recoveryDistance *
                distanceStep /
                12f;


            for (
                int i = 0;
                i < directionCount;
                i++
            )
            {
                float angle =
                    i *
                    360f /
                    directionCount *
                    Mathf.Deg2Rad;


                Vector2 direction =
                    new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)
                    );


                Vector2 candidate =
                    currentPosition +
                    direction *
                    distance;


                if (
                    !pathfinder.grid.IsPhysicalPositionWalkable(
                        candidate
                    )
                )
                {
                    continue;
                }


                float score =
                    0f;


                if (
                    desired.sqrMagnitude >
                    0.0001f
                )
                {
                    score +=
                        Vector2.Dot(
                            direction,
                            desired
                        ) *
                        3f;
                }


                // Prefer smaller escape distances.
                score -=
                    distance *
                    0.5f;


                if (
                    !found ||
                    score >
                    bestScore
                )
                {
                    found =
                        true;


                    bestScore =
                        score;


                    bestPosition =
                        candidate;
                }
            }


            if (
                found &&
                distance >=
                recoveryDistance *
                0.5f
            )
            {
                break;
            }
        }


        if (
            !found
        )
        {
            return false;
        }


        MoveToRecoveryPosition(
            bestPosition
        );


        return true;
    }


    private void MoveToRecoveryPosition(
        Vector2 position
    )
    {
        transform.position =
            new Vector3(
                position.x,
                position.y,
                -0.5f
            );


        currentVelocity =
            Vector2.zero;


        stuckTimer =
            0f;


        stuckCheckTimer =
            0f;


        stuckCheckStartPosition =
            position;
    }


    // ============================================================
    // DESTINATION
    // ============================================================

    private void ArrivedAtDestination()
    {
        currentVelocity =
            Vector2.zero;


        currentPath =
            null;


        pathIndex =
            0;


        if (
            goingToFood
        )
        {
            goingToFood =
                false;
        }
        else
        {
            goingToFood =
                true;


            if (
                colony != null
            )
            {
                colony.AStarFoodCollected();
            }
        }


        nextFailedPathRetryTime =
            Time.time;


        // Start the next completely independent A* search.
        RecalculatePathImmediately();


        nextDynamicRepathTime =
            Time.time +
            dynamicRepathInterval;
    }


    // ============================================================
    // DEBUG PATH
    // ============================================================

    private void OnDrawGizmos()
    {
        if (
            currentPath == null ||
            currentPath.Length == 0
        )
        {
            return;
        }


        Gizmos.color =
            Color.red;


        Vector3 previous =
            transform.position;


        foreach (
            Vector2 point
            in currentPath
        )
        {
            Vector3 current =
                new Vector3(
                    point.x,
                    point.y,
                    -0.5f
                );


            Gizmos.DrawLine(
                previous,
                current
            );


            previous =
                current;
        }
    }
}