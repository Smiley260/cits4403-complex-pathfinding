using UnityEngine;

public class GlobalSettings : MonoBehaviour
{
    // ==================================================
    // GLOBAL SIMULATION SPEED
    // ==================================================

    [Header("GLOBAL SIMULATION SPEED")]

    [Tooltip("1 = normal speed, 2 = twice as fast, 5 = five times as fast, etc.")]
    [Range(0.1f, 20f)]
    public float simulationSpeed = 1f;


    // ==================================================
    // ANT SETTINGS
    // ==================================================

    [Header("ANT SETTINGS")]

    [Tooltip("AntSettings asset used by the simulation.")]
    public AntSettings antSettings;

    [Tooltip("Multiplier applied to ant movement speed.")]
    [Range(0f, 5f)]
    public float antSpeedMultiplier = 1f;

    [Tooltip("Multiplier applied to ant acceleration.")]
    [Range(0f, 5f)]
    public float antAccelerationMultiplier = 1f;


    // ==================================================
    // PHEROMONE SETTINGS
    // ==================================================

    [Header("PHEROMONE SETTINGS")]

    [Tooltip("Multiplier applied to pheromone lifetime.")]
    [Range(0.1f, 5f)]
    public float pheromoneLifetimeMultiplier = 1f;

    [Tooltip("Multiplier applied to pheromone run-out time.")]
    [Range(0.1f, 5f)]
    public float pheromoneRunOutMultiplier = 1f;

    [Tooltip("Multiplier applied to pheromone following strength.")]
    [Range(0f, 5f)]
    public float pheromoneWeightMultiplier = 1f;

    [Tooltip("Multiplier applied to distance between pheromone markers.")]
    [Range(0.25f, 3f)]
    public float pheromoneSpacingMultiplier = 1f;

    [Tooltip("Multiplier applied to pheromone sensor size.")]
    [Range(0.25f, 3f)]
    public float pheromoneSensorSizeMultiplier = 1f;

    [Tooltip("Multiplier applied to pheromone sensor distance.")]
    [Range(0.25f, 3f)]
    public float pheromoneSensorDistanceMultiplier = 1f;


    // ==================================================
    // PHEROMONE PARTICLE SETTINGS
    // ==================================================

    [Header("PHEROMONE PARTICLE SETTINGS")]

    [Tooltip("Maximum number of visible pheromone particles.")]
    [Range(10000, 2000000)]
    public int maxPheromoneParticles = 1000000;

    [Tooltip("Pheromone particle size.")]
    [Range(0.01f, 0.2f)]
    public float pheromoneParticleSize = 0.05f;


    // ==================================================
    // STORM SETTINGS
    // ==================================================

    [Header("STORM SETTINGS")]

    public Storm storm;

    [Tooltip("Multiplier applied to storm movement speed.")]
    [Range(0f, 5f)]
    public float stormSpeedMultiplier = 1f;

    [Tooltip("Multiplier applied to storm traversal cost.")]
    [Range(0f, 5f)]
    public float stormCostMultiplier = 1f;


    // ==================================================
    // ORIGINAL ANT VALUES
    // ==================================================

    private float originalAntSpeed;
    private float originalAntAcceleration;

    private float originalPheromoneLifetime;
    private float originalPheromoneRunOut;
    private float originalPheromoneWeight;
    private float originalPheromoneSpacing;
    private float originalPheromoneSensorSize;
    private float originalPheromoneSensorDistance;


    // ==================================================
    // ORIGINAL STORM VALUES
    // ==================================================

    private float originalStormSpeed;
    private float originalStormCost;


    // ==================================================
    // CACHED OBJECTS
    // ==================================================

    private PerceptionMap[] perceptionMaps;


    // ==================================================
    // START
    // ==================================================

    void Start()
    {
        // --------------------------------------------------
        // Find perception maps
        // --------------------------------------------------

        perceptionMaps =
            FindObjectsOfType<PerceptionMap>();


        // --------------------------------------------------
        // Find the AntSettings actually being used
        // --------------------------------------------------

        FindAntSettings();


        // --------------------------------------------------
        // Store original AntSettings values
        // --------------------------------------------------

        StoreOriginalAntSettings();


        // --------------------------------------------------
        // Store original Storm values
        // --------------------------------------------------

        if (storm != null)
        {
            originalStormSpeed =
                storm.movementSpeed;

            originalStormCost =
                storm.maxCostMultiplier;
        }


        // Apply immediately.
        ApplySettings();
    }


    // ==================================================
    // FIND ANT SETTINGS
    // ==================================================

    void FindAntSettings()
    {
        // If an AntSettings asset has already been assigned,
        // use it.

        if (antSettings != null)
        {
            return;
        }


        // Otherwise, look at an existing Ant in the scene.

        Ant ant =
            FindObjectOfType<Ant>();


        if (ant != null && ant.settings != null)
        {
            antSettings =
                ant.settings;

            return;
        }


        // If there are no spawned ants yet, look for an
        // AntColony.

        AntColony colony =
            FindObjectOfType<AntColony>();


        if (colony != null && colony.settings != null)
        {
            antSettings =
                colony.settings;

            return;
        }


        Debug.LogWarning(
            "GlobalSettings could not find an AntSettings asset. " +
            "Assign the same AntSettings asset used by the Ant prefab."
        );
    }


    // ==================================================
    // STORE ORIGINAL ANT SETTINGS
    // ==================================================

    void StoreOriginalAntSettings()
    {
        if (antSettings == null)
        {
            return;
        }


        originalAntSpeed =
            antSettings.maxSpeed;

        originalAntAcceleration =
            antSettings.acceleration;


        originalPheromoneLifetime =
            antSettings.pheromoneEvaporateTime;

        originalPheromoneRunOut =
            antSettings.pheromoneRunOutTime;

        originalPheromoneWeight =
            antSettings.pheromoneWeight;

        originalPheromoneSpacing =
            antSettings.dstBetweenMarkers;

        originalPheromoneSensorSize =
            antSettings.sensorSize;

        originalPheromoneSensorDistance =
            antSettings.sensorDst;
    }


    // ==================================================
    // UPDATE
    // ==================================================

    void Update()
    {
        ApplySettings();
    }


    // ==================================================
    // APPLY SETTINGS
    // ==================================================

    void ApplySettings()
    {
        // ==================================================
        // GLOBAL TIME
        // ==================================================

        Time.timeScale =
            simulationSpeed;


        // ==================================================
        // ANT SETTINGS
        // ==================================================

        if (antSettings != null)
        {
            antSettings.maxSpeed =
                originalAntSpeed *
                antSpeedMultiplier;

            antSettings.acceleration =
                originalAntAcceleration *
                antAccelerationMultiplier;


            // ==================================================
            // PHEROMONES
            // ==================================================

            antSettings.pheromoneEvaporateTime =
                originalPheromoneLifetime *
                pheromoneLifetimeMultiplier;

            antSettings.pheromoneRunOutTime =
                originalPheromoneRunOut *
                pheromoneRunOutMultiplier;

            antSettings.pheromoneWeight =
                originalPheromoneWeight *
                pheromoneWeightMultiplier;

            antSettings.dstBetweenMarkers =
                originalPheromoneSpacing *
                pheromoneSpacingMultiplier;

            antSettings.sensorSize =
                originalPheromoneSensorSize *
                pheromoneSensorSizeMultiplier;

            antSettings.sensorDst =
                originalPheromoneSensorDistance *
                pheromoneSensorDistanceMultiplier;
        }


        // ==================================================
        // STORM
        // ==================================================

        if (storm != null)
        {
            storm.movementSpeed =
                originalStormSpeed *
                stormSpeedMultiplier;

            storm.maxCostMultiplier =
                originalStormCost *
                stormCostMultiplier;
        }


        // ==================================================
        // PHEROMONE PARTICLES
        // ==================================================

        ApplyParticleSettings();
    }


    // ==================================================
    // PARTICLE SETTINGS
    // ==================================================

    void ApplyParticleSettings()
    {
        if (perceptionMaps == null)
        {
            return;
        }

        if (maxPheromoneParticles < 1)
        {
            return;
        }


        foreach (PerceptionMap map in perceptionMaps)
        {
            if (map == null)
            {
                continue;
            }

            if (map.particleDisplay == null)
            {
                continue;
            }


            var main =
                map.particleDisplay.main;


            main.maxParticles =
                maxPheromoneParticles;


            main.startSize =
                pheromoneParticleSize;
        }
    }


    // ==================================================
    // RESET TIME SCALE
    // ==================================================

    void OnDestroy()
    {
        Time.timeScale = 1f;
    }
}