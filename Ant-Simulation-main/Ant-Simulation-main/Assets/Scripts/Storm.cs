using UnityEngine;

public class Storm : MonoBehaviour
{
    [Header("Map")]
    public float mapWidth = 60f;
    public float mapHeight = 30f;
    public Vector2 mapCenter = Vector2.zero;


    [Header("Noise / Cloud Shape")]
    [Tooltip("Controls the size of the storm regions in world space. Lower values create larger, smoother clouds.")]
    [Min(0.01f)]
    public float noiseScale = 0.08f;

    [Tooltip("How quickly the weather pattern moves.")]
    [Min(0f)]
    public float movementSpeed = 0.1f;

    [Tooltip("Controls the direction the weather moves.")]
    public Vector2 movementDirection = new Vector2(1f, 0.3f);

    [Tooltip("Controls how much of the map becomes storm.")]
    [Range(0f, 1f)]
    public float stormThreshold = 0.55f;

    [Tooltip("Controls how gradual the transition from clear to storm is.")]
    [Range(0.01f, 0.5f)]
    public float transitionWidth = 0.12f;


    [Header("Randomisation")]
    [Tooltip("Randomise the starting position of the Perlin noise every time the game starts.")]
    public bool randomiseOnStart = true;

    [Tooltip("How far the random starting point can be from the origin of the noise field.")]
    [Min(1f)]
    public float randomOffsetRange = 1000f;


    [Header("Traversal Cost")]
    [Min(1f)]
    public float maxCostMultiplier = 3f;


    [Header("Visual")]
    [Range(32, 256)]
    public int textureWidth = 256;

    [Range(32, 128)]
    public int textureHeight = 128;

    [Tooltip("How often the visual is updated.")]
    [Min(0.01f)]
    public float visualUpdateInterval = 0.05f;


    [Header("Colours")]
    [Tooltip("Colour of areas with little/no storm.")]
    public Color clearColour = new Color(0.2f, 0.6f, 1f, 0.08f);

    [Tooltip("Colour of the strongest storm areas.")]
    public Color stormColour = new Color(0.1f, 0.1f, 0.25f, 0.55f);


    private Texture2D noiseTexture;
    private SpriteRenderer spriteRenderer;

    private float visualTimer;

    // Position within the infinite Perlin noise field.
    private Vector2 currentNoiseOffset;


    void Start()
    {
        // Give the storm a different starting pattern each time.
        if (randomiseOnStart)
        {
            currentNoiseOffset = new Vector2(
                Random.Range(-randomOffsetRange, randomOffsetRange),
                Random.Range(-randomOffsetRange, randomOffsetRange)
            );
        }
        else
        {
            currentNoiseOffset = Vector2.zero;
        }

        CreateVisual();
        UpdateVisual();
    }


    void Update()
    {
        // Move through the infinite Perlin noise field.
        currentNoiseOffset +=
            movementDirection.normalized *
            movementSpeed *
            Time.deltaTime;

        // Don't regenerate the texture every frame.
        visualTimer += Time.deltaTime;

        if (visualTimer >= visualUpdateInterval)
        {
            visualTimer = 0f;
            UpdateVisual();
        }
    }


    // ==================================================
    // CREATE VISUAL
    // ==================================================

    void CreateVisual()
    {
        noiseTexture = new Texture2D(
            textureWidth,
            textureHeight,
            TextureFormat.RGBA32,
            false
        );

        noiseTexture.filterMode = FilterMode.Bilinear;
        noiseTexture.wrapMode = TextureWrapMode.Clamp;

        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        Sprite sprite = Sprite.Create(
            noiseTexture,
            new Rect(
                0,
                0,
                textureWidth,
                textureHeight
            ),
            new Vector2(0.5f, 0.5f),
            1f
        );

        spriteRenderer.sprite = sprite;

        // Make sure the weather appears above the map.
        spriteRenderer.sortingOrder = 10;

        // Put the centre of the storm field at the centre
        // of the map.
        transform.position = new Vector3(
            mapCenter.x,
            mapCenter.y,
            0f
        );

        // Padding allows the visual to extend slightly beyond
        // the map so there are no visible gaps at the edges.
        float horizontalPadding = 0f;
        float verticalPadding = 5f;

        transform.localScale = new Vector3(
            (mapWidth + horizontalPadding) / textureWidth,
            (mapHeight + verticalPadding) / textureHeight,
            1f
        );
    }


    // ==================================================
    // GENERATE VISUAL
    // ==================================================

    void UpdateVisual()
    {
        for (int x = 0; x < textureWidth; x++)
        {
            for (int y = 0; y < textureHeight; y++)
            {
                // Convert each pixel into an actual world position.
                //
                // This means the noise itself is NOT based on
                // texture coordinates or map-normalised coordinates.
                float worldX =
                    mapCenter.x
                    - mapWidth / 2f
                    + ((float)x / (textureWidth - 1))
                    * mapWidth;

                float worldY =
                    mapCenter.y
                    - mapHeight / 2f
                    + ((float)y / (textureHeight - 1))
                    * mapHeight;

                float intensity =
                    GetWeatherIntensity(
                        new Vector2(worldX, worldY)
                    );

                // Blend between clear and storm.
                Color colour = Color.Lerp(
                    clearColour,
                    stormColour,
                    intensity
                );

                noiseTexture.SetPixel(
                    x,
                    y,
                    colour
                );
            }
        }

        noiseTexture.Apply();
    }


    // ==================================================
    // WEATHER INTENSITY
    // ==================================================

    public float GetWeatherIntensity(Vector2 worldPosition)
    {
        // WORLD-SPACE PERLIN NOISE
        //
        // The actual world position is used directly.
        // The map dimensions do not determine the shape
        // of the noise.
        float sampleX =
            worldPosition.x * noiseScale
            + currentNoiseOffset.x;

        float sampleY =
            worldPosition.y * noiseScale
            + currentNoiseOffset.y;

        float noiseValue =
            Mathf.PerlinNoise(
                sampleX,
                sampleY
            );


        // Create a soft transition around the storm threshold.
        float lowerBound =
            stormThreshold - transitionWidth;

        float upperBound =
            stormThreshold + transitionWidth;

        float intensity =
            Mathf.InverseLerp(
                lowerBound,
                upperBound,
                noiseValue
            );


        // Smooth the result so cloud boundaries aren't harsh.
        intensity =
            Mathf.SmoothStep(
                0f,
                1f,
                intensity
            );


        return intensity;
    }


    // ==================================================
    // TRAVERSAL COST
    // ==================================================

    
    public float GetSpeedReduction(Vector2 worldPosition)
    {
		float stormCost = Mathf.Max(1f,GetTraversalCost(worldPosition)) -1f;
		float stormMult = stormCost/(stormCost+ maxCostMultiplier);
        return stormMult;
    }

    public float GetTraversalCost(Vector2 worldPosition)
    {
        float intensity =
            GetWeatherIntensity(worldPosition);

        return Mathf.Lerp(
            1f,
            maxCostMultiplier,
            intensity
        );
    }
}