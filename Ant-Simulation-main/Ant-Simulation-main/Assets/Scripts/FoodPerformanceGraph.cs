using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[AddComponentMenu("Simulation/Food Performance Graph")]
public class FoodPerformanceGraph : MonoBehaviour
{
    public enum GraphMetric
    {
        CumulativeFoodPerAgent,
        SmoothedFoodPerAgentPerSecond,
        FoodGainedPerAgentPerStep
    }

    [Header("References")]
    public AntColony antColony;
    public PathfindingComparisonManager comparisonManager;

    [Header("Measurement")]
    [Tooltip("Length of each measurement interval in SIMULATION seconds.")]
    [Min(0.1f)]
    public float timeStepSeconds = 5f;

    [Tooltip("Trailing window, in seconds, used by the smoothed rate graph.")]
    [Min(1f)]
    public float smoothingWindowSeconds = 30f;

    [Tooltip("Metric shown by the live graph.")]
    public GraphMetric graphMetric = GraphMetric.CumulativeFoodPerAgent;

    [Header("Live Window")]
    public bool showGraph = true;

    [Tooltip("Initial window position and size in the Game view.")]
    public Rect windowRect = new Rect(25f, 25f, 620f, 430f);

    [Min(300f)]
    public float minimumWindowWidth = 400f;

    [Min(220f)]
    public float minimumWindowHeight = 280f;

    [Min(30f)]
    public float minimizedWindowHeight = 34f;

    [Tooltip("Number of recent points displayed live.")]
    [Min(20)]
    public int maxLivePoints = 240;

    [Header("Graph Export / Resolution")]
    [Min(800)]
    public int graphTextureWidth = 1400;

    [Min(500)]
    public int graphTextureHeight = 760;

    [Header("CSV Export")]
    public string exportFilePrefix = "food_performance";

    [Tooltip("False exports a compact analysis CSV. True exports all diagnostic columns.")]
    public bool exportFullCsv = false;

    [Tooltip("Also export a large PNG chart.")]
    public bool exportPng = true;

    private class Sample
    {
        public int timeStep;
        public float timeSeconds;

        public int antTotalFood;
        public int aStarTotalFood;

        public int antFoodGainedThisStep;
        public int aStarFoodGainedThisStep;

        public int antAgentCount;
        public int aStarAgentCount;

        public float antFoodPerAgentThisStep;
        public float aStarFoodPerAgentThisStep;

        public float antFoodPerAgentPerSecond;
        public float aStarFoodPerAgentPerSecond;

        public float antCumulativeFoodPerAgent;
        public float aStarCumulativeFoodPerAgent;

        public float antSmoothedFoodPerAgentPerSecond;
        public float aStarSmoothedFoodPerAgentPerSecond;
    }

    private readonly List<Sample> samples = new List<Sample>();

    private Texture2D graphTexture;
    private Texture2D headerTexture;

    private int runStartAntFood;
    private int runStartAStarFood;

    private int antAgentCountForRun = 1;
    private int aStarAgentCountForRun = 1;

    private int lastObservedAntFood;
    private int lastObservedAStarFood;

    private int currentStep;
    private int currentBucket;
    private bool samplingInitialised;

    private bool minimized;
    private bool resizing;

    private float restoredWindowHeight;

    private Vector2 resizeStartMousePosition;
    private float resizeStartWidth;
    private float resizeStartHeight;

    private GraphMetric lastGraphMetric;
    private float lastTimeStepSeconds;

    private string exportStatus = "";

    private GUIStyle headerTitleStyle;
    private GUIStyle headerSubtitleStyle;
    private GUIStyle labelStyle;
    private GUIStyle smallStyle;
    private GUIStyle buttonStyle;
    private GUIStyle headerBoxStyle;

    private const int WindowId = 438271;

    public static bool IsPointerOverGraphWindow { get; private set; }

    private static readonly Color GraphBackground = new Color(0.07f, 0.07f, 0.08f, 0.98f);
    private static readonly Color GridColor = new Color(0.35f, 0.35f, 0.38f, 0.65f);
    private static readonly Color AntGraphColor = new Color(0.25f, 0.75f, 1f, 1f);
    private static readonly Color AStarGraphColor = new Color(1f, 0.45f, 0.25f, 1f);
    private static readonly Color HeaderBackground = new Color(0.15f, 0.15f, 0.18f, 1f);

    private void Start()
    {
        ResolveReferences();
        CreateGraphTexture();
        InitialiseSampling();

        restoredWindowHeight = Mathf.Max(minimumWindowHeight, windowRect.height);

        lastGraphMetric = graphMetric;
        lastTimeStepSeconds = timeStepSeconds;

        RebuildGraphTexture();
    }

    private void Update()
    {
        UpdatePointerState();
        ResolveReferences();

        if (Input.GetKeyDown(KeyCode.G))
        {
            showGraph = !showGraph;
        }

        if (antColony == null)
        {
            return;
        }

        if (graphMetric != lastGraphMetric ||
            !Mathf.Approximately(timeStepSeconds, lastTimeStepSeconds))
        {
            lastGraphMetric = graphMetric;
            lastTimeStepSeconds = timeStepSeconds;
            RebuildGraphTexture();
        }

        DetectCounterReset();

        int newBucket = Mathf.FloorToInt(
            Time.timeSinceLevelLoad /
            Mathf.Max(0.1f, timeStepSeconds)
        );

        if (!samplingInitialised)
        {
            currentBucket = newBucket;
            samplingInitialised = true;
            return;
        }

        if (newBucket <= currentBucket)
        {
            return;
        }

        while (currentBucket < newBucket)
        {
            currentBucket++;

            RecordSample(
                currentBucket *
                Mathf.Max(0.1f, timeStepSeconds)
            );
        }
    }

    private void UpdatePointerState()
    {
        if (!showGraph)
        {
            IsPointerOverGraphWindow = false;
            return;
        }

        Vector2 guiMousePosition = new Vector2(
            Input.mousePosition.x,
            Screen.height - Input.mousePosition.y
        );

        IsPointerOverGraphWindow =
            windowRect.Contains(guiMousePosition);
    }

    private void ResolveReferences()
    {
        if (antColony == null)
        {
            antColony = FindObjectOfType<AntColony>();
        }

        if (comparisonManager == null)
        {
            comparisonManager =
                FindObjectOfType<PathfindingComparisonManager>();
        }

        if (graphTexture == null)
        {
            CreateGraphTexture();
        }
    }

    private void InitialiseSampling()
    {
        runStartAntFood =
            antColony != null
                ? antColony.numFoodCollected
                : 0;

        runStartAStarFood =
            antColony != null
                ? antColony.numAStarFoodCollected
                : 0;

        lastObservedAntFood =
            runStartAntFood;

        lastObservedAStarFood =
            runStartAStarFood;

        antAgentCountForRun =
            GetConfiguredAntAgentCount();

        aStarAgentCountForRun =
            GetConfiguredAStarAgentCount();

        currentStep = 0;

        currentBucket = Mathf.FloorToInt(
            Time.timeSinceLevelLoad /
            Mathf.Max(0.1f, timeStepSeconds)
        );

        samplingInitialised = true;
    }

    private int GetConfiguredAntAgentCount()
    {
        if (antColony == null)
        {
            return 1;
        }

        return Mathf.Max(1, antColony.numToSpawn);
    }

    private int GetConfiguredAStarAgentCount()
    {
        if (comparisonManager == null)
        {
            return 1;
        }

        if (comparisonManager.matchAntColonyCount &&
            comparisonManager.antColony != null)
        {
            return Mathf.Max(
                1,
                comparisonManager.antColony.numToSpawn
            );
        }

        return Mathf.Max(
            1,
            comparisonManager.aStarAgentCount
        );
    }

    private void DetectCounterReset()
    {
        int currentAntFood = antColony.numFoodCollected;
        int currentAStarFood = antColony.numAStarFoodCollected;

        if (currentAntFood < lastObservedAntFood ||
            currentAStarFood < lastObservedAStarFood)
        {
            ResetGraph();
        }

        lastObservedAntFood = antColony.numFoodCollected;
        lastObservedAStarFood = antColony.numAStarFoodCollected;
    }

    private void RecordSample(float simulationTime)
    {
        int antTotal = antColony.numFoodCollected;
        int aStarTotal = antColony.numAStarFoodCollected;

        int previousAntTotal =
            samples.Count > 0
                ? samples[samples.Count - 1].antTotalFood
                : runStartAntFood;

        int previousAStarTotal =
            samples.Count > 0
                ? samples[samples.Count - 1].aStarTotalFood
                : runStartAStarFood;

        int antGain = Mathf.Max(
            0,
            antTotal - previousAntTotal
        );

        int aStarGain = Mathf.Max(
            0,
            aStarTotal - previousAStarTotal
        );

        int antSinceStart = Mathf.Max(
            0,
            antTotal - runStartAntFood
        );

        int aStarSinceStart = Mathf.Max(
            0,
            aStarTotal - runStartAStarFood
        );

        float stepSeconds =
            Mathf.Max(0.1f, timeStepSeconds);

        Sample sample = new Sample();

        currentStep++;

        sample.timeStep = currentStep;
        sample.timeSeconds = simulationTime;

        sample.antTotalFood = antTotal;
        sample.aStarTotalFood = aStarTotal;

        sample.antFoodGainedThisStep = antGain;
        sample.aStarFoodGainedThisStep = aStarGain;

        sample.antAgentCount = antAgentCountForRun;
        sample.aStarAgentCount = aStarAgentCountForRun;

        sample.antFoodPerAgentThisStep =
            (float)antGain /
            Mathf.Max(1, antAgentCountForRun);

        sample.aStarFoodPerAgentThisStep =
            (float)aStarGain /
            Mathf.Max(1, aStarAgentCountForRun);

        sample.antFoodPerAgentPerSecond =
            sample.antFoodPerAgentThisStep /
            stepSeconds;

        sample.aStarFoodPerAgentPerSecond =
            sample.aStarFoodPerAgentThisStep /
            stepSeconds;

        sample.antCumulativeFoodPerAgent =
            (float)antSinceStart /
            Mathf.Max(1, antAgentCountForRun);

        sample.aStarCumulativeFoodPerAgent =
            (float)aStarSinceStart /
            Mathf.Max(1, aStarAgentCountForRun);

        samples.Add(sample);

        // Calculate smoothing AFTER adding the newest sample.
        sample.antSmoothedFoodPerAgentPerSecond =
            CalculateSmoothedRate(true);

        sample.aStarSmoothedFoodPerAgentPerSecond =
            CalculateSmoothedRate(false);

        lastObservedAntFood = antTotal;
        lastObservedAStarFood = aStarTotal;

        RebuildGraphTexture();
    }

    private float CalculateSmoothedRate(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        float cutoffTime =
            samples[samples.Count - 1].timeSeconds -
            Mathf.Max(1f, smoothingWindowSeconds);

        float total = 0f;
        int count = 0;

        for (int i = samples.Count - 1; i >= 0; i--)
        {
            Sample sample = samples[i];

            if (sample.timeSeconds < cutoffTime)
            {
                break;
            }

            total +=
                ant
                    ? sample.antFoodPerAgentPerSecond
                    : sample.aStarFoodPerAgentPerSecond;

            count++;
        }

        return count > 0
            ? total / count
            : 0f;
    }

    private float GetAntValue(Sample sample)
    {
        switch (graphMetric)
        {
            case GraphMetric.SmoothedFoodPerAgentPerSecond:
                return sample.antSmoothedFoodPerAgentPerSecond;

            case GraphMetric.FoodGainedPerAgentPerStep:
                return sample.antFoodPerAgentThisStep;

            case GraphMetric.CumulativeFoodPerAgent:
            default:
                return sample.antCumulativeFoodPerAgent;
        }
    }

    private float GetAStarValue(Sample sample)
    {
        switch (graphMetric)
        {
            case GraphMetric.SmoothedFoodPerAgentPerSecond:
                return sample.aStarSmoothedFoodPerAgentPerSecond;

            case GraphMetric.FoodGainedPerAgentPerStep:
                return sample.aStarFoodPerAgentThisStep;

            case GraphMetric.CumulativeFoodPerAgent:
            default:
                return sample.aStarCumulativeFoodPerAgent;
        }
    }

    private string GetMetricTitle()
    {
        switch (graphMetric)
        {
            case GraphMetric.SmoothedFoodPerAgentPerSecond:
                return "Smoothed food gained per agent per second";

            case GraphMetric.FoodGainedPerAgentPerStep:
                return "Food gained per agent during each time step";

            case GraphMetric.CumulativeFoodPerAgent:
            default:
                return "Cumulative food returns per agent";
        }
    }

    private string GetMetricShortName()
    {
        switch (graphMetric)
        {
            case GraphMetric.SmoothedFoodPerAgentPerSecond:
                return "Smoothed rate";

            case GraphMetric.FoodGainedPerAgentPerStep:
                return "Per-step gain";

            case GraphMetric.CumulativeFoodPerAgent:
            default:
                return "Cumulative";
        }
    }

    private void ResetGraph()
    {
        samples.Clear();

        currentStep = 0;

        runStartAntFood =
            antColony != null
                ? antColony.numFoodCollected
                : 0;

        runStartAStarFood =
            antColony != null
                ? antColony.numAStarFoodCollected
                : 0;

        lastObservedAntFood =
            runStartAntFood;

        lastObservedAStarFood =
            runStartAStarFood;

        antAgentCountForRun =
            GetConfiguredAntAgentCount();

        aStarAgentCountForRun =
            GetConfiguredAStarAgentCount();

        currentBucket = Mathf.FloorToInt(
            Time.timeSinceLevelLoad /
            Mathf.Max(0.1f, timeStepSeconds)
        );

        samplingInitialised = true;
        exportStatus = "";

        RebuildGraphTexture();
    }

    private void CreateGraphTexture()
    {
        if (graphTexture != null)
        {
            Destroy(graphTexture);
        }

        graphTexture = new Texture2D(
            Mathf.Max(800, graphTextureWidth),
            Mathf.Max(500, graphTextureHeight),
            TextureFormat.RGBA32,
            false
        );

        graphTexture.filterMode = FilterMode.Bilinear;
        graphTexture.wrapMode = TextureWrapMode.Clamp;

        if (headerTexture != null)
        {
            Destroy(headerTexture);
        }

        headerTexture = new Texture2D(
            1,
            1,
            TextureFormat.RGBA32,
            false
        );

        headerTexture.SetPixel(
            0,
            0,
            HeaderBackground
        );

        headerTexture.Apply();

        headerTexture.hideFlags =
            HideFlags.HideAndDontSave;
    }

    private void RebuildGraphTexture()
    {
        if (graphTexture == null)
        {
            return;
        }

        int width = graphTexture.width;
        int height = graphTexture.height;

        Color[] pixels =
            new Color[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = GraphBackground;
        }

        graphTexture.SetPixels(pixels);

        int left = Mathf.RoundToInt(width * 0.065f);
        int right = width - Mathf.RoundToInt(width * 0.025f);
        int top = Mathf.RoundToInt(height * 0.06f);
        int bottom = height - Mathf.RoundToInt(height * 0.10f);

        // No data yet.
        if (samples.Count == 0)
        {
            DrawLine(
                graphTexture,
                left,
                bottom,
                right,
                bottom,
                GridColor
            );

            DrawLine(
                graphTexture,
                left,
                bottom,
                left,
                top,
                GridColor
            );

            graphTexture.Apply();
            return;
        }

        int firstVisible =
            Mathf.Max(
                0,
                samples.Count -
                Mathf.Max(20, maxLivePoints)
            );

        float maxValue = 0f;

        for (int i = firstVisible; i < samples.Count; i++)
        {
            maxValue = Mathf.Max(
                maxValue,
                GetAntValue(samples[i])
            );

            maxValue = Mathf.Max(
                maxValue,
                GetAStarValue(samples[i])
            );
        }

        maxValue = GetNiceUpperBound(maxValue);

        // Horizontal grid.
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f;

            int y = Mathf.RoundToInt(
                Mathf.Lerp(
                    bottom,
                    top,
                    t
                )
            );

            DrawLine(
                graphTexture,
                left,
                y,
                right,
                y,
                GridColor
            );
        }

        // Axes.
        DrawLine(
            graphTexture,
            left,
            bottom,
            right,
            bottom,
            Color.white
        );

        DrawLine(
            graphTexture,
            left,
            bottom,
            left,
            top,
            Color.white
        );

        if (samples.Count <= 1)
        {
            graphTexture.Apply();
            return;
        }

        float minTime =
            samples[firstVisible].timeSeconds;

        float maxTime =
            samples[samples.Count - 1].timeSeconds;

        float timeRange =
            Mathf.Max(
                1f,
                maxTime - minTime
            );

        for (
            int i = firstVisible + 1;
            i < samples.Count;
            i++
        )
        {
            Sample previous =
                samples[i - 1];

            Sample current =
                samples[i];

            DrawSampleLine(
                previous,
                current,
                GetAntValue(previous),
                GetAntValue(current),
                left,
                right,
                bottom,
                top,
                minTime,
                timeRange,
                maxValue,
                AntGraphColor
            );

            DrawSampleLine(
                previous,
                current,
                GetAStarValue(previous),
                GetAStarValue(current),
                left,
                right,
                bottom,
                top,
                minTime,
                timeRange,
                maxValue,
                AStarGraphColor
            );
        }

        graphTexture.Apply();
    }

    private float GetNiceUpperBound(float value)
    {
        if (value <= 0.0001f)
        {
            return 1f;
        }

        float exponent =
            Mathf.Floor(
                Mathf.Log10(value)
            );

        float fraction =
            value /
            Mathf.Pow(10f, exponent);

        float niceFraction;

        if (fraction <= 1f)
        {
            niceFraction = 1f;
        }
        else if (fraction <= 2f)
        {
            niceFraction = 2f;
        }
        else if (fraction <= 5f)
        {
            niceFraction = 5f;
        }
        else
        {
            niceFraction = 10f;
        }

        return
            niceFraction *
            Mathf.Pow(
                10f,
                exponent
            ) *
            1.05f;
    }

    private void DrawSampleLine(
        Sample previous,
        Sample current,
        float previousValue,
        float currentValue,
        int left,
        int right,
        int bottom,
        int top,
        float minTime,
        float timeRange,
        float maxValue,
        Color color
    )
    {
        float x1 =
            Mathf.Clamp01(
                (
                    previous.timeSeconds -
                    minTime
                ) /
                timeRange
            );

        float x2 =
            Mathf.Clamp01(
                (
                    current.timeSeconds -
                    minTime
                ) /
                timeRange
            );

        float y1 =
            Mathf.Clamp01(
                previousValue /
                maxValue
            );

        float y2 =
            Mathf.Clamp01(
                currentValue /
                maxValue
            );

        int px1 =
            Mathf.RoundToInt(
                Mathf.Lerp(
                    left,
                    right,
                    x1
                )
            );

        int px2 =
            Mathf.RoundToInt(
                Mathf.Lerp(
                    left,
                    right,
                    x2
                )
            );

        int py1 =
            Mathf.RoundToInt(
                Mathf.Lerp(
                    bottom,
                    top,
                    y1
                )
            );

        int py2 =
            Mathf.RoundToInt(
                Mathf.Lerp(
                    bottom,
                    top,
                    y2
                )
            );

        DrawLine(
            graphTexture,
            px1,
            py1,
            px2,
            py2,
            color
        );
    }

    private void DrawLine(
        Texture2D texture,
        int x0,
        int y0,
        int x1,
        int y1,
        Color color
    )
    {
        int dx = Mathf.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;

        int dy = -Mathf.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;

        int error = dx + dy;

        while (true)
        {
            if (
                x0 >= 0 &&
                x0 < texture.width &&
                y0 >= 0 &&
                y0 < texture.height
            )
            {
                texture.SetPixel(
                    x0,
                    y0,
                    color
                );
            }

            if (
                x0 == x1 &&
                y0 == y1
            )
            {
                break;
            }

            int e2 =
                2 * error;

            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private void InitialiseGuiStyles()
    {
        if (headerTitleStyle != null)
        {
            return;
        }

        headerTitleStyle =
            new GUIStyle(GUI.skin.label);

        headerTitleStyle.fontSize = 16;
        headerTitleStyle.fontStyle =
            FontStyle.Bold;

        headerTitleStyle.alignment =
            TextAnchor.MiddleLeft;

        headerTitleStyle.normal.textColor =
            Color.white;

        headerSubtitleStyle =
            new GUIStyle(GUI.skin.label);

        headerSubtitleStyle.fontSize = 10;

        headerSubtitleStyle.alignment =
            TextAnchor.MiddleLeft;

        headerSubtitleStyle.normal.textColor =
            Color.white;

        labelStyle =
            new GUIStyle(GUI.skin.label);

        labelStyle.fontSize = 12;
        labelStyle.normal.textColor =
            Color.white;

        smallStyle =
            new GUIStyle(GUI.skin.label);

        smallStyle.fontSize = 10;
        smallStyle.normal.textColor =
            Color.white;

        buttonStyle =
            new GUIStyle(GUI.skin.button);

        buttonStyle.fontSize = 10;

        headerBoxStyle =
            new GUIStyle(GUI.skin.box);

        headerBoxStyle.normal.background =
            headerTexture;
    }

    private void OnGUI()
    {
        if (!showGraph)
        {
            return;
        }

        InitialiseGuiStyles();
        ClampWindowToScreen();

        windowRect = GUI.Window(
            WindowId,
            windowRect,
            DrawWindow,
            "FOOD PERFORMANCE"
        );
    }

    private void DrawWindow(int id)
    {
        Event e = Event.current;

        float width =
            windowRect.width;

        // Minimise.
        Rect minimizeRect =
            new Rect(
                width - 29f,
                4f,
                23f,
                20f
            );

        if (
            GUI.Button(
                minimizeRect,
                minimized ? "+" : "-"
            )
        )
        {
            ToggleMinimized();
        }

        // Hide.
        Rect closeRect =
            new Rect(
                width - 56f,
                4f,
                23f,
                20f
            );

        if (
            GUI.Button(
                closeRect,
                "x"
            )
        )
        {
            showGraph = false;
            IsPointerOverGraphWindow = false;
        }

        // Drag.
        GUI.DragWindow(
            new Rect(
                0f,
                0f,
                width - 60f,
                27f
            )
        );

        if (minimized)
        {
            GUI.Label(
                new Rect(
                    10f,
                    28f,
                    width - 75f,
                    22f
                ),
                "Press G to show the graph again.",
                smallStyle
            );

            return;
        }

        // Readable header.
        Rect headerRect =
            new Rect(
                8f,
                29f,
                width - 16f,
                50f
            );

        GUI.Box(
            headerRect,
            GUIContent.none,
            headerBoxStyle
        );

        GUI.Label(
            new Rect(
                16f,
                31f,
                width - 80f,
                22f
            ),
            "Food Performance",
            headerTitleStyle
        );

        GUI.Label(
            new Rect(
                16f,
                52f,
                width - 80f,
                20f
            ),
            GetMetricTitle() +
            "  |  " +
            timeStepSeconds.ToString("F1") +
            " s intervals",
            headerSubtitleStyle
        );

        // Graph.
        Rect liveGraphRect =
            new Rect(
                10f,
                84f,
                width - 20f,
                windowRect.height - 175f
            );

        if (graphTexture != null)
        {
            GUI.DrawTexture(
                liveGraphRect,
                graphTexture,
                ScaleMode.StretchToFill,
                false
            );
        }

        // Legend.
        GUIStyle antStyle =
            new GUIStyle(labelStyle);

        antStyle.normal.textColor =
            AntGraphColor;

        GUI.Label(
            new Rect(
                liveGraphRect.x + 8f,
                liveGraphRect.y + 5f,
                55f,
                20f
            ),
            "ANT",
            antStyle
        );

        GUIStyle aStarStyle =
            new GUIStyle(labelStyle);

        aStarStyle.normal.textColor =
            AStarGraphColor;

        GUI.Label(
            new Rect(
                liveGraphRect.x + 55f,
                liveGraphRect.y + 5f,
                55f,
                20f
            ),
            "A*",
            aStarStyle
        );

        // Current values.
        Sample latest =
            samples.Count > 0
                ? samples[samples.Count - 1]
                : null;

        float antValue =
            latest != null
                ? GetAntValue(latest)
                : 0f;

        float aStarValue =
            latest != null
                ? GetAStarValue(latest)
                : 0f;

        GUI.Label(
            new Rect(
                12f,
                windowRect.height - 84f,
                width - 24f,
                20f
            ),
            "Current: Ant " +
            antValue.ToString("F4") +
            "   A* " +
            aStarValue.ToString("F4"),
            labelStyle
        );

        // Run information.
        GUI.Label(
            new Rect(
                12f,
                windowRect.height - 63f,
                width - 24f,
                18f
            ),
            "Time: " +
            Time.timeSinceLevelLoad.ToString("F1") +
            " s | Step: " +
            currentStep +
            " | Agents: " +
            antAgentCountForRun +
            " / " +
            aStarAgentCountForRun,
            smallStyle
        );

        // Metric selector.
        float buttonWidth = 118f;
        float buttonY =
            windowRect.height - 35f;

        if (
            GUI.Button(
                new Rect(
                    10f,
                    buttonY,
                    buttonWidth,
                    22f
                ),
                "Metric: " +
                GetMetricShortName(),
                buttonStyle
            )
        )
        {
            CycleMetric();
        }

        if (
            GUI.Button(
                new Rect(
                    134f,
                    buttonY,
                    100f,
                    22f
                ),
                "Reset Data",
                buttonStyle
            )
        )
        {
            ResetGraph();
        }

        if (
            GUI.Button(
                new Rect(
                    240f,
                    buttonY,
                    100f,
                    22f
                ),
                "Export CSV",
                buttonStyle
            )
        )
        {
            ExportCsv();
        }

        if (
            !string.IsNullOrEmpty(exportStatus)
        )
        {
            GUI.Label(
                new Rect(
                    350f,
                    buttonY,
                    width - 430f,
                    22f
                ),
                exportStatus,
                smallStyle
            );
        }

        // Resize handle.
        Rect resizeRect =
            new Rect(
                width - 20f,
                windowRect.height - 20f,
                20f,
                20f
            );

        GUI.Label(
            resizeRect,
            "↘",
            smallStyle
        );

        HandleResize(
            resizeRect,
            e
        );
    }

    private void CycleMetric()
    {
        int next =
            ((int)graphMetric + 1) % 3;

        graphMetric =
            (GraphMetric)next;

        lastGraphMetric =
            graphMetric;

        RebuildGraphTexture();
    }

    private void ToggleMinimized()
    {
        if (!minimized)
        {
            restoredWindowHeight =
                Mathf.Max(
                    minimumWindowHeight,
                    windowRect.height
                );

            windowRect.height =
                minimizedWindowHeight;

            minimized = true;
        }
        else
        {
            windowRect.height =
                Mathf.Max(
                    minimumWindowHeight,
                    restoredWindowHeight
                );

            minimized = false;
        }

        ClampWindowToScreen();
    }

    private void HandleResize(
        Rect resizeRect,
        Event e
    )
    {
        if (minimized)
        {
            return;
        }

        if (
            e.type == EventType.MouseDown &&
            e.button == 0 &&
            resizeRect.Contains(e.mousePosition)
        )
        {
            resizing = true;

            resizeStartMousePosition =
                GUIUtility.GUIToScreenPoint(
                    e.mousePosition
                );

            resizeStartWidth =
                windowRect.width;

            resizeStartHeight =
                windowRect.height;

            GUIUtility.hotControl =
                GUIUtility.GetControlID(
                    FocusType.Passive
                );

            e.Use();
        }

        if (
            resizing &&
            e.type == EventType.MouseDrag &&
            e.button == 0
        )
        {
            Vector2 currentMousePosition =
                GUIUtility.GUIToScreenPoint(
                    e.mousePosition
                );

            Vector2 delta =
                currentMousePosition -
                resizeStartMousePosition;

            windowRect.width =
                Mathf.Max(
                    minimumWindowWidth,
                    resizeStartWidth +
                    delta.x
                );

            windowRect.height =
                Mathf.Max(
                    minimumWindowHeight,
                    resizeStartHeight +
                    delta.y
                );

            ClampWindowToScreen();

            e.Use();
        }

        if (
            resizing &&
            (
                e.type == EventType.MouseUp ||
                e.rawType == EventType.MouseUp
            )
        )
        {
            resizing = false;

            GUIUtility.hotControl = 0;

            e.Use();
        }
    }

    private void ClampWindowToScreen()
    {
        windowRect.width =
            Mathf.Min(
                windowRect.width,
                Mathf.Max(
                    minimumWindowWidth,
                    Screen.width
                )
            );

        if (!minimized)
        {
            windowRect.height =
                Mathf.Min(
                    windowRect.height,
                    Mathf.Max(
                        minimumWindowHeight,
                        Screen.height
                    )
                );
        }

        windowRect.x =
            Mathf.Clamp(
                windowRect.x,
                0f,
                Mathf.Max(
                    0f,
                    Screen.width -
                    windowRect.width
                )
            );

        float visibleHeight =
            minimized
                ? minimizedWindowHeight
                : windowRect.height;

        windowRect.y =
            Mathf.Clamp(
                windowRect.y,
                0f,
                Mathf.Max(
                    0f,
                    Screen.height -
                    visibleHeight
                )
            );
    }

    private void ExportCsv()
    {
        if (samples.Count == 0)
        {
            exportStatus =
                "No samples recorded.";

            return;
        }

        string timestamp =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss"
            );

        string path =
            System.IO.Path.Combine(
                Application.persistentDataPath,
                exportFilePrefix +
                "_" +
                timestamp +
                ".csv"
            );

        System.Text.StringBuilder csv =
            new System.Text.StringBuilder();

        if (!exportFullCsv)
        {
            csv.AppendLine(
                "time_step," +
                "time_seconds," +
                "ant_food_gained_this_step," +
                "astar_food_gained_this_step," +
                "ant_cumulative_food_per_agent," +
                "astar_cumulative_food_per_agent," +
                "ant_smoothed_food_per_agent_per_second," +
                "astar_smoothed_food_per_agent_per_second"
            );
        }
        else
        {
            csv.AppendLine(
                "time_step," +
                "time_seconds," +
                "ant_total_food," +
                "astar_total_food," +
                "ant_food_gained_this_step," +
                "astar_food_gained_this_step," +
                "ant_agent_count," +
                "astar_agent_count," +
                "ant_food_per_agent_this_step," +
                "astar_food_per_agent_this_step," +
                "ant_food_per_agent_per_second," +
                "astar_food_per_agent_per_second," +
                "ant_cumulative_food_per_agent," +
                "astar_cumulative_food_per_agent," +
                "ant_smoothed_food_per_agent_per_second," +
                "astar_smoothed_food_per_agent_per_second"
            );
        }

        foreach (Sample sample in samples)
        {
            if (!exportFullCsv)
            {
                csv.AppendLine(
                    sample.timeStep +
                    "," +
                    FormatNumber(sample.timeSeconds) +
                    "," +
                    sample.antFoodGainedThisStep +
                    "," +
                    sample.aStarFoodGainedThisStep +
                    "," +
                    FormatNumber(
                        sample.antCumulativeFoodPerAgent
                    ) +
                    "," +
                    FormatNumber(
                        sample.aStarCumulativeFoodPerAgent
                    ) +
                    "," +
                    FormatNumber(
                        sample.antSmoothedFoodPerAgentPerSecond
                    ) +
                    "," +
                    FormatNumber(
                        sample.aStarSmoothedFoodPerAgentPerSecond
                    )
                );
            }
            else
            {
                csv.AppendLine(
                    sample.timeStep +
                    "," +
                    FormatNumber(sample.timeSeconds) +
                    "," +
                    sample.antTotalFood +
                    "," +
                    sample.aStarTotalFood +
                    "," +
                    sample.antFoodGainedThisStep +
                    "," +
                    sample.aStarFoodGainedThisStep +
                    "," +
                    sample.antAgentCount +
                    "," +
                    sample.aStarAgentCount +
                    "," +
                    FormatNumber(
                        sample.antFoodPerAgentThisStep
                    ) +
                    "," +
                    FormatNumber(
                        sample.aStarFoodPerAgentThisStep
                    ) +
                    "," +
                    FormatNumber(
                        sample.antFoodPerAgentPerSecond
                    ) +
                    "," +
                    FormatNumber(
                        sample.aStarFoodPerAgentPerSecond
                    ) +
                    "," +
                    FormatNumber(
                        sample.antCumulativeFoodPerAgent
                    ) +
                    "," +
                    FormatNumber(
                        sample.aStarCumulativeFoodPerAgent
                    ) +
                    "," +
                    FormatNumber(
                        sample.antSmoothedFoodPerAgentPerSecond
                    ) +
                    "," +
                    FormatNumber(
                        sample.aStarSmoothedFoodPerAgentPerSecond
                    )
                );
            }
        }

        try
        {
            System.IO.File.WriteAllText(
                path,
                csv.ToString()
            );

            if (exportPng)
            {
                string pngPath =
                    System.IO.Path.Combine(
                        Application.persistentDataPath,
                        exportFilePrefix +
                        "_" +
                        timestamp +
                        ".png"
                    );

                System.IO.File.WriteAllBytes(
                    pngPath,
                    graphTexture.EncodeToPNG()
                );

                exportStatus =
                    "CSV + PNG exported.";

                Debug.Log(
                    "Food performance CSV exported to:\n" +
                    path +
                    "\n\nPNG exported to:\n" +
                    pngPath
                );
            }
            else
            {
                exportStatus =
                    "CSV exported.";

                Debug.Log(
                    "Food performance CSV exported to:\n" +
                    path
                );
            }
        }
        catch (Exception exception)
        {
            exportStatus =
                "Export failed.";

            Debug.LogError(
                "FoodPerformanceGraph export failed:\n" +
                exception
            );
        }
    }

    private string FormatNumber(float value)
    {
        return value.ToString(
            "F6",
            CultureInfo.InvariantCulture
        );
    }

    private void OnDisable()
    {
        IsPointerOverGraphWindow = false;
    }

    private void OnDestroy()
    {
        IsPointerOverGraphWindow = false;

        if (graphTexture != null)
        {
            Destroy(graphTexture);
            graphTexture = null;
        }

        if (headerTexture != null)
        {
            Destroy(headerTexture);
            headerTexture = null;
        }
    }
}