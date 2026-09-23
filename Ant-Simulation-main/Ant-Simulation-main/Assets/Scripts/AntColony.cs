using System.Collections;
using UnityEngine;

public class AntColony :
    MonoBehaviour
{
    public AntSettings settings;

    public Ant antPrefab;


    [Header("Population")]

    public int numToSpawn = 10;

    public Transform antHolder;

    public bool replenishDead;


    public PerceptionMap homeMarkers;

    public PerceptionMap foodMarkers;


    [Header("Food Counters")]

    [Tooltip(
        "Completed food trips by normal ants."
    )]
    public int numFoodCollected;


    [Tooltip(
        "Completed food trips by A* agents."
    )]
    public int numAStarFoodCollected;


    [Header("Food Counter Appearance")]

    [Range(8, 256)]
    [Tooltip(
        "Font resolution used by the TextMesh. " +
        "The visible world size is controlled mainly by Character Size."
    )]
    public int foodCounterFontSize = 96;


    [Min(0.01f)]
    [Tooltip(
        "Actual visible size of the food counter in world space."
    )]
    public float foodCounterCharacterSize = 0.09f;


    [Tooltip(
        "Spacing between the Ant and A* counter lines."
    )]
    public float foodCounterLineSpacing = 0.8f;


    [Tooltip(
        "Automatically reduce the character size if the counter " +
        "would extend outside the colony."
    )]
    public bool autoFitFoodCounter = true;


    [Tooltip(
        "Small delay between normal ant spawns during startup."
    )]
    [Min(0f)]
    public float antSpawnInterval = 0.005f;


    [Header("Colony")]

    public float radius;

    public Transform graphic;


    [Header("Statistics")]

    public float timePassed;


    [Tooltip(
        "TextMesh used for the Ant and A* counters."
    )]
    public TextMesh numFoodUI;


    private float nextPossibleRespawnTime;


    private bool hasPrinted10MinMark;


    private bool foodUIIsDirty =
        true;


    private float nextFoodUIUpdateTime;


    private const float foodUIUpdateInterval =
        0.1f;


    private Coroutine spawnCoroutine;


    // ==================================================
    // START
    // ==================================================

    private void Start()
    {
        ConfigureFoodUI();


        spawnCoroutine =
            StartCoroutine(
                SpawnInitialAntsRoutine()
            );


        UpdateFoodUI(
            true
        );
    }


    // ==================================================
    // UPDATE
    // ==================================================

    private void Update()
    {
        timePassed =
            Time.timeSinceLevelLoad;


        // ------------------------------------------------
        // 10 MINUTE STATISTIC
        // ------------------------------------------------

        if (
            !hasPrinted10MinMark &&
            timePassed >
            60f * 10f
        )
        {
            hasPrinted10MinMark =
                true;


            Debug.Log(
                "Food collected after 10 minutes - " +
                "Ants: " +
                numFoodCollected +
                " | A*: " +
                numAStarFoodCollected
            );
        }


        // ------------------------------------------------
        // BATCH UI UPDATES
        // ------------------------------------------------

        if (
            foodUIIsDirty &&
            Time.unscaledTime >=
            nextFoodUIUpdateTime
        )
        {
            UpdateFoodUI(
                false
            );
        }


        // ------------------------------------------------
        // REPLENISH ANTS
        // ------------------------------------------------

        if (
            antHolder != null
        )
        {
            int numDead =
                numToSpawn -
                antHolder.childCount;


            if (
                Time.time >
                nextPossibleRespawnTime
            )
            {
                nextPossibleRespawnTime =
                    Time.time;


                if (
                    numDead > 0 &&
                    replenishDead
                )
                {
                    SpawnAnt();
                }
            }
        }
    }


    // ==================================================
    // INITIAL ANT SPAWN
    // ==================================================

    private IEnumerator SpawnInitialAntsRoutine()
    {
        for (
            int i = 0;
            i < numToSpawn;
            i++
        )
        {
            SpawnAnt();


            if (
                antSpawnInterval > 0f
            )
            {
                yield return new WaitForSecondsRealtime(
                    antSpawnInterval
                );
            }
            else
            {
                yield return null;
            }
        }


        spawnCoroutine =
            null;
    }


    // ==================================================
    // SPAWN ANT
    // ==================================================

    private void SpawnAnt()
    {
        if (
            antPrefab == null ||
            antHolder == null
        )
        {
            return;
        }


        Ant ant =
            Instantiate(
                antPrefab,
                transform.position,
                Quaternion.identity,
                antHolder
            );


        ant.settings =
            settings;


        ant.SetColony(
            this
        );
    }


    // ==================================================
    // NORMAL ANT FOOD
    // ==================================================

    public void FoodCollected()
    {
        numFoodCollected++;


        foodUIIsDirty =
            true;
    }


    // ==================================================
    // A* FOOD
    // ==================================================

    public void AStarFoodCollected()
    {
        numAStarFoodCollected++;


        foodUIIsDirty =
            true;
    }


    // ==================================================
    // CONFIGURE FOOD UI
    // ==================================================

    private void ConfigureFoodUI()
    {
        if (
            numFoodUI == null
        )
        {
            return;
        }


        numFoodUI.alignment =
            TextAlignment.Center;


        numFoodUI.anchor =
            TextAnchor.MiddleCenter;


        // ----------------------------------------------
        // FONT SIZE
        // ----------------------------------------------

        numFoodUI.fontSize =
            foodCounterFontSize;


        // ----------------------------------------------
        // VISIBLE SIZE
        // ----------------------------------------------

        numFoodUI.characterSize =
            foodCounterCharacterSize;


        numFoodUI.lineSpacing =
            foodCounterLineSpacing;


        numFoodUI.fontStyle =
            FontStyle.Normal;


        numFoodUI.transform.localPosition =
            Vector3.zero;


        numFoodUI.transform.localRotation =
            Quaternion.identity;


        // ----------------------------------------------
        // RENDERER
        // ----------------------------------------------

        MeshRenderer meshRenderer =
            numFoodUI.GetComponent<
                MeshRenderer
            >();


        if (
            meshRenderer != null
        )
        {
            meshRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;


            meshRenderer.receiveShadows =
                false;
        }


        FitFoodUIToColony();
    }


    // ==================================================
    // UPDATE FOOD UI
    // ==================================================

    private void UpdateFoodUI(
        bool force
    )
    {
        if (
            numFoodUI == null
        )
        {
            foodUIIsDirty =
                false;


            return;
        }


        if (
            !force &&
            !foodUIIsDirty
        )
        {
            return;
        }


        numFoodUI.text =
            "Ant: " +
            numFoodCollected +
            "\n" +
            "A*: " +
            numAStarFoodCollected;


        numFoodUI.fontSize =
            foodCounterFontSize;


        foodUIIsDirty =
            false;


        nextFoodUIUpdateTime =
            Time.unscaledTime +
            foodUIUpdateInterval;


        FitFoodUIToColony();
    }


    // ==================================================
    // FIT FOOD UI
    // ==================================================

    private void FitFoodUIToColony()
    {
        if (
            numFoodUI == null
        )
        {
            return;
        }


        // The colony is currently approximately radius 2,
        // so this keeps the text comfortably inside it.
        float maximumWidth =
            Mathf.Max(
                0.5f,
                radius * 1.35f
            );


        float maximumHeight =
            Mathf.Max(
                0.5f,
                radius * 1.0f
            );


        numFoodUI.characterSize =
            foodCounterCharacterSize;


        if (
            !autoFitFoodCounter
        )
        {
            return;
        }


        Renderer renderer =
            numFoodUI.GetComponent<
                Renderer
            >();


        if (
            renderer == null
        )
        {
            return;
        }


        Bounds bounds =
            renderer.bounds;


        float currentCharacterSize =
            foodCounterCharacterSize;


        // Only shrink the text if it is actually too large.
        for (
            int i = 0;
            i < 8;
            i++
        )
        {
            if (
                bounds.size.x <=
                maximumWidth
                &&
                bounds.size.y <=
                maximumHeight
            )
            {
                break;
            }


            currentCharacterSize *=
                0.9f;


            numFoodUI.characterSize =
                currentCharacterSize;


            bounds =
                renderer.bounds;
        }
    }


    // ==================================================
    // VALIDATION
    // ==================================================

    private void OnValidate()
    {
        radius =
            Mathf.Max(
                0f,
                radius
            );


        foodCounterFontSize =
            Mathf.Clamp(
                foodCounterFontSize,
                8,
                256
            );


        foodCounterCharacterSize =
            Mathf.Max(
                0.01f,
                foodCounterCharacterSize
            );


        if (
            graphic != null
        )
        {
            graphic.transform.localScale =
                Vector3.one *
                radius *
                2f;
        }
    }
}