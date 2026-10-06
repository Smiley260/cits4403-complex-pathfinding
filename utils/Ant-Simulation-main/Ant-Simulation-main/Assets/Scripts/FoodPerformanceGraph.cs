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
    public Rect windowRect = new Rect(25f, 25f, 1000f, 520f);

    [Min(700f)]
    public float minimumWindowWidth = 900f;

    [Min(400f)]
    public float minimumWindowHeight = 480f;

    [Min(34f)]
    public float minimizedWindowHeight = 40f;

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
    private GUIStyle statsHeaderStyle;
    private GUIStyle statsLabelStyle;
    private GUIStyle statsValueStyle;
    private GUIStyle statsNoteStyle;
    private GUIStyle reopenTabStyle;

    private const int WindowId = 438271;

    // Small persistent tab shown in the bottom-left when the graph is closed.
    [Header("Reopen Tab")]
    [Tooltip("Show a small tab in the bottom-left corner when the graph is closed.")]
    public bool showReopenTab = true;

    [Min(20f)]
    public float reopenTabWidth = 44f;

    [Min(20f)]
    public float reopenTabHeight = 26f;

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
        // OnGUI can be invoked during Unity layout/repaint before every Start
        // detail has been initialised. Initialise each style independently so
        // a partially-initialised GUI can never leave a null style behind.
        if (graphTexture == null || headerTexture == null)
        {
            CreateGraphTexture();
        }

        if (headerTitleStyle == null)
        {
            headerTitleStyle =
                new GUIStyle(GUI.skin.label);
        }

        headerTitleStyle.fontSize = 16;
        headerTitleStyle.fontStyle = FontStyle.Bold;
        headerTitleStyle.alignment = TextAnchor.MiddleLeft;
        headerTitleStyle.normal.textColor = Color.white;

        if (headerSubtitleStyle == null)
        {
            headerSubtitleStyle =
                new GUIStyle(GUI.skin.label);
        }

        headerSubtitleStyle.fontSize = 10;
        headerSubtitleStyle.alignment = TextAnchor.MiddleLeft;
        headerSubtitleStyle.normal.textColor = Color.white;

        if (labelStyle == null)
        {
            labelStyle =
                new GUIStyle(GUI.skin.label);
        }

        labelStyle.fontSize = 12;
        labelStyle.normal.textColor = Color.white;

        if (smallStyle == null)
        {
            smallStyle =
                new GUIStyle(GUI.skin.label);
        }

        smallStyle.fontSize = 10;
        smallStyle.normal.textColor = Color.white;

        if (buttonStyle == null)
        {
            buttonStyle =
                new GUIStyle(GUI.skin.button);
        }

        buttonStyle.fontSize = 10;

        if (headerBoxStyle == null)
        {
            headerBoxStyle =
                new GUIStyle(GUI.skin.box);
        }

        headerBoxStyle.normal.background = headerTexture;

        if (statsHeaderStyle == null)
        {
            statsHeaderStyle =
                new GUIStyle(GUI.skin.label);
        }

        statsHeaderStyle.fontSize = 14;
        statsHeaderStyle.fontStyle = FontStyle.Bold;
        statsHeaderStyle.alignment = TextAnchor.MiddleLeft;
        statsHeaderStyle.normal.textColor = Color.white;

        if (statsLabelStyle == null)
        {
            statsLabelStyle =
                new GUIStyle(GUI.skin.label);
        }

        statsLabelStyle.fontSize = 10;
        statsLabelStyle.alignment = TextAnchor.MiddleLeft;
        statsLabelStyle.normal.textColor = Color.white;
        statsLabelStyle.wordWrap = false;
        statsLabelStyle.padding = new RectOffset(4, 2, 0, 0);

        if (statsValueStyle == null)
        {
            statsValueStyle =
                new GUIStyle(GUI.skin.label);
        }

        statsValueStyle.fontSize = 10;
        statsValueStyle.alignment = TextAnchor.MiddleRight;
        statsValueStyle.normal.textColor = Color.white;
        statsValueStyle.padding = new RectOffset(2, 5, 0, 0);

        if (statsNoteStyle == null)
        {
            statsNoteStyle =
                new GUIStyle(GUI.skin.label);
        }

        statsNoteStyle.fontSize = 9;
        statsNoteStyle.alignment = TextAnchor.UpperLeft;
        statsNoteStyle.normal.textColor = new Color(0.82f, 0.82f, 0.84f, 1f);
        statsNoteStyle.wordWrap = true;
        statsNoteStyle.padding = new RectOffset(3, 3, 0, 0);

        if (reopenTabStyle == null)
        {
            reopenTabStyle =
                new GUIStyle(GUI.skin.button);
        }

        reopenTabStyle.fontSize = 11;
        reopenTabStyle.fontStyle = FontStyle.Bold;
        reopenTabStyle.alignment = TextAnchor.MiddleCenter;
        reopenTabStyle.padding = new RectOffset(2, 2, 2, 2);
    }

    private void OnGUI()
    {
        InitialiseGuiStyles();

        // Never let stale IMGUI hot-control state prevent the reopen tab from
        // being usable after closing or minimising the window.
        if (!showGraph && resizing)
        {
            resizing = false;
            GUIUtility.hotControl = 0;
        }

        // Keep a small persistent tab visible when the graph is closed.
        if (!showGraph)
        {
            IsPointerOverGraphWindow = false;
            DrawReopenTab();
            return;
        }

        ClampWindowToScreen();

        windowRect = GUI.Window(
            WindowId,
            windowRect,
            DrawWindow,
            "FOOD PERFORMANCE"
        );
    }

    private void DrawReopenTab()
    {
        if (!showReopenTab)
        {
            return;
        }

        float width = Mathf.Max(20f, reopenTabWidth);
        float height = Mathf.Max(20f, reopenTabHeight);

        Rect tabRect = new Rect(
            8f,
            Screen.height - height - 8f,
            width,
            height
        );

        // Simple text icon for reliable rendering with Unity's IMGUI font.
        GUIStyle safeReopenTabStyle =
            reopenTabStyle != null
                ? reopenTabStyle
                : GUI.skin.button;

        if (GUI.Button(tabRect, "FP", safeReopenTabStyle))
        {
            showGraph = true;
            minimized = false;

            // Restore the normal graph size when reopening.
            windowRect.height = Mathf.Max(
                minimumWindowHeight,
                restoredWindowHeight
            );

            ClampWindowToScreen();
        }
    }

    private void DrawWindow(int id)
    {
        Event e = Event.current;
        float width = windowRect.width;

        // The two buttons live in the title bar.
        Rect minimizeRect =
            new Rect(width - 58f, 3f, 25f, 20f);

        if (GUI.Button(minimizeRect, minimized ? "+" : "-"))
        {
            ToggleMinimized();
        }

        Rect closeRect =
            new Rect(width - 30f, 3f, 25f, 20f);

        if (GUI.Button(closeRect, "x"))
        {
            showGraph = false;
            IsPointerOverGraphWindow = false;
        }

        // Only the title-bar area is draggable. The buttons are outside it.
        GUI.DragWindow(
            new Rect(
                0f,
                0f,
                Mathf.Max(0f, width - 62f),
                24f
            )
        );

        // A minimised window intentionally contains only the Unity title bar.
        // The minimum height is kept comfortably above Unity's title-bar height
        // so the title and buttons cannot be vertically clipped.
        if (minimized)
        {
            return;
        }

        Rect headerRect =
            new Rect(
                8f,
                27f,
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
                30f,
                width - 80f,
                21f
            ),
            "Food Performance",
            headerTitleStyle
        );

        GUI.Label(
            new Rect(
                16f,
                51f,
                width - 80f,
                20f
            ),
            GetMetricTitle() +
            "  |  " +
            timeStepSeconds.ToString("F1") +
            " s intervals",
            headerSubtitleStyle
        );

        // Main graph and statistics panel.
        float contentTop = 82f;
        float bottomReserved = 102f;
        float contentHeight =
            Mathf.Max(
                200f,
                windowRect.height - contentTop - bottomReserved
            );

        // Give the statistics table enough width that labels do not collide
        // with the Ant/A* value columns.
        float statsWidth =
            Mathf.Min(
                340f,
                Mathf.Max(
                    300f,
                    width * 0.34f
                )
            );

        float graphWidth =
            Mathf.Max(
                340f,
                width - statsWidth - 30f
            );

        Rect liveGraphRect =
            new Rect(
                10f,
                contentTop,
                graphWidth,
                contentHeight
            );

        Rect statsRect =
            new Rect(
                graphWidth + 20f,
                contentTop,
                statsWidth,
                contentHeight
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
        GUIStyle antStyle = new GUIStyle(labelStyle);
        antStyle.normal.textColor = AntGraphColor;

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

        GUIStyle aStarStyle = new GUIStyle(labelStyle);
        aStarStyle.normal.textColor = AStarGraphColor;

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

        DrawStatisticsPanel(statsRect);

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
                windowRect.height - 94f,
                width - 24f,
                20f
            ),
            "Current: Ant " +
            antValue.ToString("F4") +
            "   A* " +
            aStarValue.ToString("F4"),
            labelStyle
        );

        GUI.Label(
            new Rect(
                12f,
                windowRect.height - 73f,
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

        float buttonY =
            windowRect.height - 48f;

        GUIStyle safeButtonStyle =
            buttonStyle != null
                ? buttonStyle
                : GUI.skin.button;

        if (GUI.Button(
            new Rect(10f, buttonY, 118f, 22f),
            "Metric: " + GetMetricShortName(),
            safeButtonStyle
        ))
        {
            CycleMetric();
        }

        if (GUI.Button(
            new Rect(134f, buttonY, 100f, 22f),
            "Reset Data",
            safeButtonStyle
        ))
        {
            ResetGraph();
        }

        if (GUI.Button(
            new Rect(240f, buttonY, 100f, 22f),
            "Export CSV",
            safeButtonStyle
        ))
        {
            ExportCsv();
        }

        if (!string.IsNullOrEmpty(exportStatus))
        {
            GUI.Label(
                new Rect(
                    350f,
                    buttonY,
                    Mathf.Max(100f, width - 430f),
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

        HandleResize(resizeRect, e);
    }

    private void DrawStatisticsPanel(Rect rect)
    {
        GUI.Box(rect, GUIContent.none);

        const float outerPadding = 8f;
        const float titleHeight = 24f;
        const float noteHeight = 22f;
        const float columnHeaderHeight = 24f;
        const float rowHeight = 22f;

        Rect inner = new Rect(
            rect.x + outerPadding,
            rect.y + outerPadding,
            rect.width - outerPadding * 2f,
            rect.height - outerPadding * 2f
        );

        GUI.Label(
            new Rect(
                inner.x,
                inner.y,
                inner.width,
                titleHeight
            ),
            "Statistics",
            statsHeaderStyle
        );

        GUI.Label(
            new Rect(
                inner.x,
                inner.y + titleHeight - 1f,
                inner.width,
                noteHeight
            ),
            "Key summary measures for efficiency and consistency.",
            statsNoteStyle
        );

        float tableTop =
            inner.y +
            titleHeight +
            noteHeight +
            4f;

        // Fixed columns keep every value aligned and prevent long labels
        // from colliding with the Ant and A* columns.
        float measureWidth = inner.width - 142f;
        float valueWidth = 67f;
        float gap = 4f;

        float antX = inner.x + measureWidth + gap;
        float aStarX = antX + valueWidth + gap;

        DrawStatisticsCell(
            new Rect(inner.x, tableTop, measureWidth, columnHeaderHeight),
            "Measure",
            statsLabelStyle,
            false
        );

        GUIStyle antHeader = new GUIStyle(statsValueStyle);
        antHeader.normal.textColor = AntGraphColor;
        antHeader.fontStyle = FontStyle.Bold;
        antHeader.alignment = TextAnchor.MiddleCenter;

        GUI.Label(
            new Rect(antX, tableTop, valueWidth, columnHeaderHeight),
            "Ant",
            antHeader
        );

        GUIStyle aStarHeader = new GUIStyle(statsValueStyle);
        aStarHeader.normal.textColor = AStarGraphColor;
        aStarHeader.fontStyle = FontStyle.Bold;
        aStarHeader.alignment = TextAnchor.MiddleCenter;

        GUI.Label(
            new Rect(aStarX, tableTop, valueWidth, columnHeaderHeight),
            "A*",
            aStarHeader
        );

        if (samples.Count == 0)
        {
            GUI.Label(
                new Rect(
                    inner.x,
                    tableTop + columnHeaderHeight + 8f,
                    inner.width,
                    55f
                ),
                "Statistics will populate after the first measurement interval.",
                statsNoteStyle
            );
            return;
        }

        float y = tableTop + columnHeaderHeight;

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Final cumulative / agent",
            GetFinalCumulative(true),
            GetFinalCumulative(false),
            "F4"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Mean cumulative / agent",
            GetMeanCumulative(true),
            GetMeanCumulative(false),
            "F4"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Mean return rate / agent / s",
            GetMeanRate(true),
            GetMeanRate(false),
            "F4"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Mean smoothed rate / agent / s",
            GetMeanSmoothedRate(true),
            GetMeanSmoothedRate(false),
            "F4"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Rate variability (SD)",
            GetRateStandardDeviation(true),
            GetRateStandardDeviation(false),
            "F4"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Rate variability (CV %)",
            GetRateCoefficientOfVariation(true),
            GetRateCoefficientOfVariation(false),
            "F1"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Peak return rate / agent / s",
            GetPeakRate(true),
            GetPeakRate(false),
            "F4"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "Total food returned",
            GetTotalFoodGained(true),
            GetTotalFoodGained(false),
            "F0"
        );

        DrawStatisticRow(
            rect, ref y, measureWidth, antX, aStarX, valueWidth, rowHeight,
            "First return (s)",
            GetFirstFoodReturnTime(true),
            GetFirstFoodReturnTime(false),
            "F1",
            true
        );
    }

    private void DrawStatisticsCell(
        Rect rect,
        string text,
        GUIStyle style,
        bool centered
    )
    {
        GUIStyle drawStyle = new GUIStyle(style);

        if (centered)
        {
            drawStyle.alignment = TextAnchor.MiddleCenter;
        }

        GUI.Label(
            rect,
            text,
            drawStyle
        );
    }

    private void DrawStatisticRow(
        Rect rect,
        ref float y,
        float measureWidth,
        float antX,
        float aStarX,
        float valueWidth,
        float rowHeight,
        string label,
        float antValue,
        float aStarValue,
        string format,
        bool secondsValue = false
    )
    {
        // Subtle alternating rows make the table much easier to read.
        Color previousColor = GUI.color;

        GUI.color = new Color(1f, 1f, 1f, 0.035f);
        GUI.Box(
            new Rect(
                rect.x + 8f,
                y,
                rect.width - 16f,
                rowHeight
            ),
            GUIContent.none
        );

        GUI.color = previousColor;

        GUI.Label(
            new Rect(
                rect.x + 12f,
                y,
                measureWidth - 4f,
                rowHeight
            ),
            label,
            statsLabelStyle
        );

        GUI.Label(
            new Rect(
                antX,
                y,
                valueWidth,
                rowHeight
            ),
            FormatStatisticValue(antValue, format, secondsValue),
            statsValueStyle
        );

        GUI.Label(
            new Rect(
                aStarX,
                y,
                valueWidth,
                rowHeight
            ),
            FormatStatisticValue(aStarValue, format, secondsValue),
            statsValueStyle
        );

        y += rowHeight;
    }

    private string FormatStatisticValue(
        float value,
        string format,
        bool secondsValue
    )
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return "N/A";
        }

        if (secondsValue && value < 0f)
        {
            return "N/A";
        }

        if (format == "F0")
        {
            return Mathf.RoundToInt(value).ToString();
        }

        return value.ToString(
            format,
            CultureInfo.InvariantCulture
        );
    }

    private float GetFinalCumulative(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        Sample latest = samples[samples.Count - 1];

        return ant
            ? latest.antCumulativeFoodPerAgent
            : latest.aStarCumulativeFoodPerAgent;
    }

    private float GetMeanCumulative(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        float total = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            total += ant
                ? samples[i].antCumulativeFoodPerAgent
                : samples[i].aStarCumulativeFoodPerAgent;
        }

        return total / samples.Count;
    }

    private float GetMeanRate(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        float total = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            total += ant
                ? samples[i].antFoodPerAgentPerSecond
                : samples[i].aStarFoodPerAgentPerSecond;
        }

        return total / samples.Count;
    }

    private float GetMeanSmoothedRate(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        float total = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            total += ant
                ? samples[i].antSmoothedFoodPerAgentPerSecond
                : samples[i].aStarSmoothedFoodPerAgentPerSecond;
        }

        return total / samples.Count;
    }

    private float GetRateStandardDeviation(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        float mean = GetMeanRate(ant);
        float squaredDifferenceTotal = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            float value = ant
                ? samples[i].antFoodPerAgentPerSecond
                : samples[i].aStarFoodPerAgentPerSecond;

            float difference = value - mean;
            squaredDifferenceTotal += difference * difference;
        }

        return Mathf.Sqrt(
            squaredDifferenceTotal / samples.Count
        );
    }

    private float GetRateCoefficientOfVariation(bool ant)
    {
        float mean = GetMeanRate(ant);

        if (mean <= 0.000001f)
        {
            return float.NaN;
        }

        return (GetRateStandardDeviation(ant) / mean) * 100f;
    }

    private float GetPeakRate(bool ant)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        float peak = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            peak = Mathf.Max(
                peak,
                ant
                    ? samples[i].antFoodPerAgentPerSecond
                    : samples[i].aStarFoodPerAgentPerSecond
            );
        }

        return peak;
    }

    private float GetTotalFoodGained(bool ant)
    {
        return ant
            ? Mathf.Max(0, antColony.numFoodCollected - runStartAntFood)
            : Mathf.Max(0, antColony.numAStarFoodCollected - runStartAStarFood);
    }

    private float GetFirstFoodReturnTime(bool ant)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            if (ant)
            {
                if (samples[i].antFoodGainedThisStep > 0)
                {
                    return samples[i].timeSeconds;
                }
            }
            else if (samples[i].aStarFoodGainedThisStep > 0)
            {
                return samples[i].timeSeconds;
            }
        }

        return float.NaN;
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
                Mathf.Max(
                    40f,
                    minimizedWindowHeight
                );

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
                ? Mathf.Max(40f, minimizedWindowHeight)
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
            exportStatus = "No samples recorded.";
            return;
        }

        string timestamp =
            DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string path =
            System.IO.Path.Combine(
                "..\\data\\Storm_Statistics",
                exportFilePrefix +
                "_" +
                timestamp +
                ".csv"
            );

        // Excel-friendly CSV layout:
        // A:G = chart-ready time-series data.
        // H    = spacer column.
        // I:K = summary statistics table.
        // This keeps the first three columns immediately usable for a line graph.
        System.Text.StringBuilder csv =
            new System.Text.StringBuilder();

        csv.AppendLine(
            "time_seconds," +
            "ant_cumulative_food_per_agent," +
            "astar_cumulative_food_per_agent," +
            "ant_food_gained_this_step," +
            "astar_food_gained_this_step," +
            "ant_smoothed_food_per_agent_per_second," +
            "astar_smoothed_food_per_agent_per_second," +
            "," +
            "STATISTICS SUMMARY,,"
        );

        string[,] stats = BuildExportStatistics();

        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i];

            csv.Append(
                FormatNumber(sample.timeSeconds) +
                "," +
                FormatNumber(sample.antCumulativeFoodPerAgent) +
                "," +
                FormatNumber(sample.aStarCumulativeFoodPerAgent) +
                "," +
                sample.antFoodGainedThisStep +
                "," +
                sample.aStarFoodGainedThisStep +
                "," +
                FormatNumber(sample.antSmoothedFoodPerAgentPerSecond) +
                "," +
                FormatNumber(sample.aStarSmoothedFoodPerAgentPerSecond) +
                ","
            );

            int statsRow = i;

            if (statsRow < stats.GetLength(0))
            {
                csv.Append(
                    CsvEscape(stats[statsRow, 0]) +
                    "," +
                    CsvEscape(stats[statsRow, 1]) +
                    "," +
                    CsvEscape(stats[statsRow, 2])
                );
            }
            else
            {
                csv.Append(",,");
            }

            csv.AppendLine();
        }

        try
        {
            // UTF-8 BOM helps Excel recognise the CSV correctly on Windows.
            byte[] utf8Bom =
            {
                0xEF,
                0xBB,
                0xBF
            };

            byte[] csvBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    csv.ToString()
                );

            byte[] output =
                new byte[utf8Bom.Length + csvBytes.Length];

            System.Buffer.BlockCopy(
                utf8Bom,
                0,
                output,
                0,
                utf8Bom.Length
            );

            System.Buffer.BlockCopy(
                csvBytes,
                0,
                output,
                utf8Bom.Length,
                csvBytes.Length
            );

            System.IO.File.WriteAllBytes(
                path,
                output
            );

            exportStatus = "CSV + statistics exported.";

            Debug.Log(
                "Food performance CSV exported to:\n" +
                System.IO.Path.GetFullPath(path)
            );
        }
        catch (Exception exception)
        {
            exportStatus = "Export failed.";

            Debug.LogError(
                "FoodPerformanceGraph CSV export failed:\n" +
                exception
            );
        }
    }

    private string[,] BuildExportStatistics()
    {
        // Row 0 is the table header. Rows 1-9 are the nine summary measures.
        string[,] table = new string[10, 3];

        table[0, 0] = "Statistic";
        table[0, 1] = "Ant";
        table[0, 2] = "A*";

        table[1, 0] = "Final cumulative / agent";
        table[1, 1] = FormatStatisticCsv(GetFinalCumulative(true), "F4", false);
        table[1, 2] = FormatStatisticCsv(GetFinalCumulative(false), "F4", false);

        table[2, 0] = "Mean cumulative / agent";
        table[2, 1] = FormatStatisticCsv(GetMeanCumulative(true), "F4", false);
        table[2, 2] = FormatStatisticCsv(GetMeanCumulative(false), "F4", false);

        table[3, 0] = "Mean return rate / agent / s";
        table[3, 1] = FormatStatisticCsv(GetMeanRate(true), "F4", false);
        table[3, 2] = FormatStatisticCsv(GetMeanRate(false), "F4", false);

        table[4, 0] = "Mean smoothed rate / agent / s";
        table[4, 1] = FormatStatisticCsv(GetMeanSmoothedRate(true), "F4", false);
        table[4, 2] = FormatStatisticCsv(GetMeanSmoothedRate(false), "F4", false);

        table[5, 0] = "Rate variability (SD)";
        table[5, 1] = FormatStatisticCsv(GetRateStandardDeviation(true), "F4", false);
        table[5, 2] = FormatStatisticCsv(GetRateStandardDeviation(false), "F4", false);

        table[6, 0] = "Rate variability (CV %)";
        table[6, 1] = FormatStatisticCsv(GetRateCoefficientOfVariation(true), "F1", false);
        table[6, 2] = FormatStatisticCsv(GetRateCoefficientOfVariation(false), "F1", false);

        table[7, 0] = "Peak return rate / agent / s";
        table[7, 1] = FormatStatisticCsv(GetPeakRate(true), "F4", false);
        table[7, 2] = FormatStatisticCsv(GetPeakRate(false), "F4", false);

        table[8, 0] = "Total food returned";
        table[8, 1] = FormatStatisticCsv(GetTotalFoodGained(true), "F0", false);
        table[8, 2] = FormatStatisticCsv(GetTotalFoodGained(false), "F0", false);

        table[9, 0] = "First return (s)";
        table[9, 1] = FormatStatisticCsv(GetFirstFoodReturnTime(true), "F1", true);
        table[9, 2] = FormatStatisticCsv(GetFirstFoodReturnTime(false), "F1", true);

        return table;
    }

    private string FormatStatisticCsv(
        float value,
        string format,
        bool secondsValue
    )
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return "N/A";
        }

        if (secondsValue && value < 0f)
        {
            return "N/A";
        }

        if (format == "F0")
        {
            return Mathf.RoundToInt(value).ToString();
        }

        return value.ToString(
            format,
            CultureInfo.InvariantCulture
        );
    }

    private string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (
            value.IndexOf(',') >= 0 ||
            value.IndexOf('"') >= 0 ||
            value.IndexOf('\n') >= 0 ||
            value.IndexOf('\r') >= 0
        )
        {
            return "\"" +
                   value.Replace("\"", "\"\"") +
                   "\"";
        }

        return value;
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