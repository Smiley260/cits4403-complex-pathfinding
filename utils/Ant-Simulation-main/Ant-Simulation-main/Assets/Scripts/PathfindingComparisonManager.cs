using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathfindingComparisonManager :
    MonoBehaviour
{
    public PathfindingMode mode =
        PathfindingMode.Both;


    [Header("Existing Ant Simulation")]

    public AntColony antColony;


    [Header("A*")]

    public AStarPathfinder2D aStarPathfinder;

    public Transform home;

    public Transform food;


    [Tooltip(
        "Number of A* agents when matching is disabled."
    )]
    [Min(1)]
    public int aStarAgentCount = 20;


    [Tooltip(
        "Fallback speed only used if AntSettings is unavailable."
    )]
    [Min(0.1f)]
    public float aStarSpeed = 2f;


    [Tooltip(
        "When enabled, A* count follows the normal ant count."
    )]
    public bool matchAntColonyCount = false;


    [Header("Initial A* Spawn")]

    [Tooltip(
        "Maximum number of agents spawned before yielding to the next frame."
    )]
    [Min(1)]
    public int initialAgentsPerFrame = 10;


    [Tooltip(
        "Startup budget. This only controls how many agents are created "
        +
        "per frame; it does NOT limit the work of an individual A* search."
    )]
    [Min(0.1f)]
    public float initialPathfindingMillisecondsPerFrame = 20f;


    [Tooltip(
        "Maximum distance around Home where A* agents may spawn."
    )]
    [Min(0f)]
    public float spawnRadius = 1f;


    [Tooltip(
        "Minimum separation between A* spawn positions."
    )]
    [Min(0f)]
    public float spawnSeparation = 0.2f;


    [Header("Legacy Spawn Setting")]

    [Min(0f)]
    public float spawnInterval = 0.005f;


    [Header("Visual")]

    public GameObject aStarAgentPrefab;


    private GameObject aStarParent;


    // ============================================================
    // START
    // ============================================================

    private IEnumerator Start()
    {
        // MapGenerator creates its EdgeCollider2D objects during
        // Start(), so allow that to complete first.
        yield return null;


        if (
            aStarPathfinder != null &&
            aStarPathfinder.grid != null
        )
        {
            aStarPathfinder.grid.CreateGrid();
        }


        ApplyMode();
    }


    // ============================================================
    // APPLY MODE
    // ============================================================

    public void ApplyMode()
    {
        if (
            antColony != null
        )
        {
            antColony.enabled =
                mode !=
                PathfindingMode.AStarOnly;
        }


        bool useAStar =
            mode !=
            PathfindingMode.AntsOnly;


        if (
            useAStar
        )
        {
            StartCoroutine(
                SpawnAStarAgentsRoutine()
            );
        }
    }


    // ============================================================
    // SPAWN A* AGENTS
    // ============================================================

    private IEnumerator SpawnAStarAgentsRoutine()
    {
        if (
            aStarPathfinder == null ||
            home == null ||
            food == null
        )
        {
            Debug.LogError(
                "A* comparison setup is missing references."
            );


            yield break;
        }


        if (
            aStarParent != null
        )
        {
            Destroy(
                aStarParent
            );
        }


        aStarParent =
            new GameObject(
                "A* Agents"
            );


        int numberToSpawn =
            matchAntColonyCount &&
            antColony != null
                ? antColony.numToSpawn
                : aStarAgentCount;


        if (
            numberToSpawn < 1
        )
        {
            yield break;
        }


        List<Vector2> usedSpawnPositions =
            new List<Vector2>();


        int agentsThisFrame =
            0;


        float frameStart =
            Time.realtimeSinceStartup;


        for (
            int i = 0;
            i < numberToSpawn;
            i++
        )
        {
            Vector2 spawnPosition =
                FindSafeSpawnPosition(
                    usedSpawnPositions
                );


            usedSpawnPositions.Add(
                spawnPosition
            );


            SpawnSingleAStarAgent(
                i,
                spawnPosition
            );


            agentsThisFrame++;


            float elapsedMilliseconds =
                (
                    Time.realtimeSinceStartup -
                    frameStart
                ) *
                1000f;


            if (
                agentsThisFrame >=
                initialAgentsPerFrame
                ||
                elapsedMilliseconds >=
                initialPathfindingMillisecondsPerFrame
            )
            {
                agentsThisFrame =
                    0;


                yield return null;


                frameStart =
                    Time.realtimeSinceStartup;
            }
        }
    }


    // ============================================================
    // SPAWN ONE AGENT
    // ============================================================

    private void SpawnSingleAStarAgent(
        int index,
        Vector2 spawnPosition
    )
    {
        GameObject agentObject;


        // --------------------------------------------------------
        // Create object.
        // --------------------------------------------------------

        if (
            aStarAgentPrefab != null
        )
        {
            agentObject =
                Instantiate(
                    aStarAgentPrefab
                );
        }
        else
        {
            agentObject =
                new GameObject();
        }


        agentObject.name =
            "A* Agent " +
            index;


        agentObject.transform.SetParent(
            aStarParent.transform
        );


        // Don't let the agent move while its first path is calculated.
        agentObject.SetActive(
            false
        );


        // --------------------------------------------------------
        // Place at its safe starting position.
        // --------------------------------------------------------

        agentObject.transform.position =
            new Vector3(
                spawnPosition.x,
                spawnPosition.y,
                -0.5f
            );


        // --------------------------------------------------------
        // Find/add AStarAgent2D.
        // --------------------------------------------------------

        AStarAgent2D agent =
            agentObject.GetComponent<
                AStarAgent2D
            >();


        if (
            agent == null
        )
        {
            agent =
                agentObject.AddComponent<
                    AStarAgent2D
                >();
        }


        // --------------------------------------------------------
        // EXACT SAME ANT SETTINGS
        // --------------------------------------------------------

        if (
            antColony != null &&
            antColony.settings != null
        )
        {
            agent.movementSettings =
                antColony.settings;
        }
        else
        {
            agent.speed =
                aStarSpeed;
        }


        // --------------------------------------------------------
        // Collision settings.
        // --------------------------------------------------------

        if (
            aStarPathfinder.grid != null
        )
        {
            agent.collisionRadius =
                aStarPathfinder.grid.agentRadius;


            agent.collisionMask =
                aStarPathfinder.grid.unwalkableMask;
        }


        // --------------------------------------------------------
        // INITIALISE
        //
        // This performs THIS agent's own A* calculation.
        //
        // Initialise no longer overwrites spawnPosition.
        // --------------------------------------------------------

        agent.Initialise(
            aStarPathfinder,
            home.position,
            food.position,
            antColony
        );


        // --------------------------------------------------------
        // Activate only after the initial path exists.
        // --------------------------------------------------------

        agentObject.SetActive(
            true
        );
    }


    // ============================================================
    // SAFE SPAWN
    // ============================================================

    private Vector2 FindSafeSpawnPosition(
        List<Vector2> usedSpawnPositions
    )
    {
        if (
            aStarPathfinder == null ||
            aStarPathfinder.grid == null
        )
        {
            return home.position;
        }


        AStarGrid2D grid =
            aStarPathfinder.grid;


        // --------------------------------------------------------
        // Try many random positions.
        // --------------------------------------------------------

        for (
            int attempt = 0;
            attempt < 100;
            attempt++
        )
        {
            Vector2 candidate =
                (Vector2)home.position +
                Random.insideUnitCircle *
                spawnRadius;


            if (
                IsSafeSpawnPosition(
                    candidate,
                    grid,
                    usedSpawnPositions
                )
            )
            {
                return candidate;
            }
        }


        // --------------------------------------------------------
        // Try the exact home position.
        // --------------------------------------------------------

        if (
            IsSafeSpawnPosition(
                home.position,
                grid,
                usedSpawnPositions
            )
        )
        {
            return home.position;
        }


        // --------------------------------------------------------
        // Last resort: nearest reachable grid node.
        // --------------------------------------------------------

        Node2D nearest =
            grid.GetNearestReachableNode(
                home.position
            );


        if (
            nearest != null
        )
        {
            return nearest.worldPosition;
        }


        return home.position;
    }


    // ============================================================
    // SPAWN VALIDATION
    // ============================================================

    private bool IsSafeSpawnPosition(
        Vector2 position,
        AStarGrid2D grid,
        List<Vector2> usedSpawnPositions
    )
    {
        if (
            !grid.IsPositionWalkable(
                position
            )
        )
        {
            return false;
        }


        if (
            usedSpawnPositions == null
        )
        {
            return true;
        }


        for (
            int i = 0;
            i < usedSpawnPositions.Count;
            i++
        )
        {
            if (
                Vector2.Distance(
                    position,
                    usedSpawnPositions[i]
                ) <
                spawnSeparation
            )
            {
                return false;
            }
        }


        return true;
    }
}