using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    [Header("Size settings")]

    public int chunkResolution = 10;
    public int numChunksX = 16;
    public int numChunksY = 9;
    public float mapWidth;
    public bool colliderAroundBorder;


    [Header("Render settings")]

    public float iso;
    public float borderIso;
    public float isoClamp = 1;
    public Material mapMaterial;
    public bool smooth;
    public bool border;


    [Header("Noise settings")]

    public float noiseScale = 1;
    public int numLayers = 1;
    public float lacunarity = 2;
    public float persistence = 0.5f;
    public Vector2 noiseOffset;
    public int seed;
    public bool autoRefreshNoise;


    [Header("Brush settings")]

    public bool useBrush;
    public float brushWorldRadius;
    public Vector2 brushMinMax =
        new Vector2(0.5f, 1);

    public float brushStrength;
    public Transform brush;
    public float brushRadiusInputSensitivity;

    float targetBrushRadius;

    public TextAsset loadFile;
    public bool loadOnStart;


    private AStarGrid2D aStarGrid;


    float[,] fullMap;
    Chunk[,] chunks;

    Vector2 brushPosLastFrame;


    // ============================================================
    // START
    // ============================================================

    void Start()
    {
        Init();


        if (loadOnStart)
        {
            if (loadFile == null)
            {
                Debug.LogError(
                    "MapGenerator: Load On Start is enabled, " +
                    "but no Load File is assigned."
                );
            }
            else
            {
                SaveData loadedData =
                    JsonUtility.FromJson<SaveData>(
                        loadFile.text
                    );


                for (
                    int y = 0;
                    y < loadedData.height;
                    y++
                )
                {
                    for (
                        int x = 0;
                        x < loadedData.width;
                        x++
                    )
                    {
                        fullMap[x, y] =
                            loadedData.map[
                                y *
                                loadedData.width +
                                x
                            ];
                    }
                }
            }
        }
        else
        {
            Generate();
        }


        DrawChunks();


        // The A* manager normally creates the grid after this
        // Start() method has completed.
        //
        // Only mark it dirty here if an A* grid has already been
        // created.
        aStarGrid =
            FindObjectOfType<AStarGrid2D>();


        if (
            aStarGrid != null &&
            aStarGrid.IsGridCreated()
        )
        {
            aStarGrid.MarkGridDirty();
        }
    }


    // ============================================================
    // SAVE
    // ============================================================

    [ContextMenu("Save")]
    void Save()
    {
        SaveData saveData =
            new SaveData(
                fullMap
            );


        string saveString =
            JsonUtility.ToJson(
                saveData
            );


        StreamWriter writer =
            new StreamWriter(
                "./Assets/SavedMap/Map.txt"
            );


        writer.Write(
            saveString
        );


        writer.Close();
    }


    // ============================================================
    // INIT
    // ============================================================

    void Init()
    {
        targetBrushRadius =
            brushWorldRadius;


        int totalNumCellsX =
            numChunksX *
            (chunkResolution - 1) +
            1;


        int totalNumCellsY =
            numChunksY *
            (chunkResolution - 1) +
            1;


        fullMap =
            new float[
                totalNumCellsX,
                totalNumCellsY
            ];


        float chunkSize =
            mapWidth /
            numChunksX;


        float halfWidth =
            chunkSize *
            numChunksX /
            2f;


        float halfHeight =
            chunkSize *
            numChunksY /
            2f;


        chunks =
            new Chunk[
                numChunksX,
                numChunksY
            ];


        for (
            int y = 0;
            y < numChunksY;
            y++
        )
        {
            for (
                int x = 0;
                x < numChunksX;
                x++
            )
            {
                Vector2 chunkCentre =
                    new Vector2(
                        -halfWidth +
                        chunkSize *
                        (x + 0.5f),

                        -halfHeight +
                        chunkSize *
                        (y + 0.5f)
                    );


                chunks[x, y] =
                    new Chunk(
                        transform,
                        mapMaterial,
                        new Vector2Int(x, y),
                        chunkResolution,
                        chunkCentre,
                        chunkSize
                    );
            }
        }


        if (colliderAroundBorder)
        {
            EdgeCollider2D edgeCollider =
                gameObject.AddComponent<
                    EdgeCollider2D
                >();


            edgeCollider.points =
                new Vector2[]
                {
                    new Vector2(
                        -halfWidth,
                        -halfHeight
                    ),

                    new Vector2(
                        -halfWidth,
                        halfHeight
                    ),

                    new Vector2(
                        halfWidth,
                        halfHeight
                    ),

                    new Vector2(
                        halfWidth,
                        -halfHeight
                    ),

                    new Vector2(
                        -halfWidth,
                        -halfHeight
                    )
                };
        }
    }


    // ============================================================
    // UPDATE
    // ============================================================

    void Update()
    {
        HandleBrush();


        if (autoRefreshNoise)
        {
            Generate();


            DrawChunks();


            NotifyAStarGridTerrainChanged();
        }
    }


    // ============================================================
    // BRUSH INPUT
    // ============================================================

    void HandleBrush()
    {
        if (brush != null)
        {
            brush.gameObject.SetActive(
                useBrush
            );
        }


        if (!useBrush)
        {
            return;
        }


        if (brush == null)
        {
            Debug.LogWarning(
                "MapGenerator: Use Brush is enabled, " +
                "but no Brush Transform is assigned."
            );


            return;
        }


        targetBrushRadius +=
            Input.GetAxisRaw(
                "Mouse ScrollWheel"
            ) *
            brushRadiusInputSensitivity;


        targetBrushRadius =
            Mathf.Clamp(
                targetBrushRadius,
                brushMinMax.x,
                brushMinMax.y
            );


        brushWorldRadius =
            Mathf.Lerp(
                brushWorldRadius,
                targetBrushRadius,
                Time.deltaTime *
                6f
            );


        Vector2 mousePos =
            InputHelper.MouseWorldPos;


        brush.position =
            new Vector3(
                mousePos.x,
                mousePos.y,
                -1f
            );


        brush.localScale =
            Vector3.one *
            brushWorldRadius *
            2f;


        // ========================================================
        // NEW TERRAIN CONTROL
        // ========================================================
        //
        // SHIFT + LEFT MOUSE = create terrain
        //
        // ALT + SHIFT + LEFT MOUSE = subtract / erase terrain
        //
        // Plain left click does NOTHING.
        //
        // This also prevents the graph window from accidentally
        // triggering terrain edits while it is being manipulated.
        //

        bool shiftHeld =
            InputHelper.AnyOfTheseKeysHeld(
                KeyCode.LeftShift,
                KeyCode.RightShift
            );


        bool graphWindowActive =
            FoodPerformanceGraph.IsPointerOverGraphWindow;


        bool drawing =
            Input.GetMouseButton(0) &&
            shiftHeld &&
            !graphWindowActive;


        if (drawing)
        {
            DrawBrushStroke();
        }


        brushPosLastFrame =
            mousePos;
    }


    // ============================================================
    // DRAW BRUSH STROKE
    // ============================================================

    void DrawBrushStroke()
    {
        int totalNumCellsX =
            fullMap.GetLength(0);


        int totalNumCellsY =
            fullMap.GetLength(1);


        float chunkSize =
            mapWidth /
            numChunksX;


        float cellSize =
            chunkSize /
            (chunkResolution - 1f);


        float brushCellRadius =
            brushWorldRadius /
            cellSize;


        // ALT now means subtract/erase.
        //
        // SHIFT is reserved for the create-terrain action.
        bool subtractBrush =
            InputHelper.AnyOfTheseKeysHeld(
                KeyCode.LeftAlt,
                KeyCode.RightAlt
            );


        float brushValue =
            brushStrength *
            Time.deltaTime *
            (
                subtractBrush
                    ? 1f
                    : -1f
            );


        Vector2 strokeStartPos =
            brushPosLastFrame;


        Vector2 strokeEndPos =
            brush.transform.position;


        Vector2Int startCell =
            CellCoordFromPoint(
                strokeStartPos
            );


        Vector2Int endCell =
            CellCoordFromPoint(
                strokeEndPos
            );


        Vector2Int[] strokePath =
            BresenahmLine.GetPath(
                startCell.x,
                startCell.y,
                endCell.x,
                endCell.y
            );


        // Draw brush at all points along the stroke.
        for (
            int i = 0;
            i < strokePath.Length;
            i++
        )
        {
            int brushCentreX =
                strokePath[i].x;


            int brushCentreY =
                strokePath[i].y;


            for (
                int brushOffsetY =
                    -(int)brushCellRadius;

                brushOffsetY <=
                    (int)brushCellRadius;

                brushOffsetY++
            )
            {
                for (
                    int brushOffsetX =
                        -(int)brushCellRadius;

                    brushOffsetX <=
                        (int)brushCellRadius;

                    brushOffsetX++
                )
                {
                    int brushX =
                        brushCentreX +
                        brushOffsetX;


                    int brushY =
                        brushCentreY +
                        brushOffsetY;


                    if (
                        brushX >= 0 &&
                        brushX <
                        totalNumCellsX &&
                        brushY >= 0 &&
                        brushY <
                        totalNumCellsY
                    )
                    {
                        if (
                            brushOffsetX *
                            brushOffsetX +

                            brushOffsetY *
                            brushOffsetY

                            <=
                            brushCellRadius *
                            brushCellRadius
                        )
                        {
                            float dstFromCentre =
                                Mathf.Sqrt(
                                    brushOffsetX *
                                    brushOffsetX +

                                    brushOffsetY *
                                    brushOffsetY
                                );


                            float falloff =
                                1f -
                                Mathf.Clamp01(
                                    dstFromCentre /
                                    brushCellRadius
                                );


                            float newMapValue =
                                fullMap[
                                    brushX,
                                    brushY
                                ] +
                                brushValue *
                                falloff;


                            fullMap[
                                brushX,
                                brushY
                            ] =
                                Mathf.Clamp(
                                    newMapValue,
                                    iso - isoClamp,
                                    iso + isoClamp
                                );
                        }
                    }
                }
            }
        }


        UpdateChunksUnderStrokeParallel(
            strokeStartPos,
            strokeEndPos
        );
    }


    // ============================================================
    // POINT -> CELL
    // ============================================================

    Vector2Int CellCoordFromPoint(
        Vector2 point
    )
    {
        float halfWidth =
            mapWidth *
            0.5f;


        float halfHeight =
            halfWidth *
            numChunksY /
            (float)numChunksX;


        int cellX =
            Mathf.RoundToInt(
                Mathf.InverseLerp(
                    -halfWidth,
                    halfWidth,
                    point.x
                ) *
                (
                    fullMap.GetLength(0) -
                    1
                )
            );


        int cellY =
            Mathf.RoundToInt(
                Mathf.InverseLerp(
                    -halfHeight,
                    halfHeight,
                    point.y
                ) *
                (
                    fullMap.GetLength(1) -
                    1
                )
            );


        return new Vector2Int(
            cellX,
            cellY
        );
    }


    // ============================================================
    // UPDATE CHUNKS
    // ============================================================

    void UpdateChunksUnderStrokeParallel(
        Vector2 strokeStartPos,
        Vector2 strokeEndPos
    )
    {
        Vector2 strokeDir =
            (
                strokeEndPos -
                strokeStartPos
            ).normalized;


        if (
            strokeDir ==
            Vector2.zero
        )
        {
            Bounds2D brushBounds =
                new Bounds2D(
                    strokeStartPos,
                    Vector2.one *
                    brushWorldRadius *
                    2f
                );


            for (
                int y = 0;
                y < numChunksY;
                y++
            )
            {
                for (
                    int x = 0;
                    x < numChunksX;
                    x++
                )
                {
                    Bounds2D bounds =
                        chunks[x, y].bounds;


                    if (
                        brushBounds.OverlapBounds(
                            bounds
                        )
                    )
                    {
                        chunks[x, y]
                            .UpdateChunkMeshData(
                                fullMap,
                                iso,
                                smooth
                            );
                    }
                }
            }
        }
        else
        {
            strokeStartPos -=
                strokeDir *
                brushWorldRadius;


            strokeEndPos +=
                strokeDir *
                brushWorldRadius;


            Vector2 strokeNormal =
                new Vector2(
                    -strokeDir.y,
                    strokeDir.x
                );


            Vector2 a1 =
                strokeStartPos +
                strokeNormal *
                brushWorldRadius;


            Vector2 a2 =
                strokeStartPos -
                strokeNormal *
                brushWorldRadius;


            Vector2 b1 =
                strokeEndPos +
                strokeNormal *
                brushWorldRadius;


            Vector2 b2 =
                strokeEndPos -
                strokeNormal *
                brushWorldRadius;


            Bounds2D strokeBounds =
                new Bounds2D(
                    a1,
                    a2,
                    b1,
                    b2
                );


            Parallel.For(
                0,
                numChunksY *
                numChunksX,

                i =>
                    UpdateChunkIfInBrushStroke(
                        i,
                        a1,
                        a2,
                        b1,
                        b2,
                        strokeBounds
                    )
            );
        }


        for (
            int y = 0;
            y < numChunksY;
            y++
        )
        {
            for (
                int x = 0;
                x < numChunksX;
                x++
            )
            {
                chunks[x, y].UpdateMesh();
            }
        }


        // The colliders have now been updated.
        NotifyAStarGridTerrainChanged();
    }


    // ============================================================
    // FIND CHUNKS AFFECTED BY STROKE
    // ============================================================

    void UpdateChunkIfInBrushStroke(
        int i,
        Vector2 a1,
        Vector2 a2,
        Vector2 b1,
        Vector2 b2,
        Bounds2D strokeBounds
    )
    {
        int x =
            i %
            numChunksX;


        int y =
            i /
            numChunksX;


        Bounds2D bounds =
            chunks[x, y].bounds;


        if (
            strokeBounds.OverlapBounds(
                bounds
            )
        )
        {
            bool t1 =
                TriangleUtility
                    .TriangleOverlapsTriangle(
                        a1,
                        a2,
                        b1,
                        bounds.topLeft,
                        bounds.bottomLeft,
                        bounds.topRight
                    );


            bool t2 =
                TriangleUtility
                    .TriangleOverlapsTriangle(
                        a1,
                        a2,
                        b1,
                        bounds.bottomRight,
                        bounds.bottomLeft,
                        bounds.topRight
                    );


            bool t3 =
                TriangleUtility
                    .TriangleOverlapsTriangle(
                        b2,
                        a2,
                        b1,
                        bounds.topLeft,
                        bounds.bottomLeft,
                        bounds.topRight
                    );


            bool t4 =
                TriangleUtility
                    .TriangleOverlapsTriangle(
                        b2,
                        a2,
                        b1,
                        bounds.bottomRight,
                        bounds.bottomLeft,
                        bounds.topRight
                    );


            if (
                t1 ||
                t2 ||
                t3 ||
                t4
            )
            {
                chunks[x, y]
                    .UpdateChunkMeshData(
                        fullMap,
                        iso,
                        smooth
                    );
            }
        }
    }


    // ============================================================
    // NOTIFY A* GRID
    // ============================================================

    void NotifyAStarGridTerrainChanged()
    {
        if (
            aStarGrid == null
        )
        {
            aStarGrid =
                FindObjectOfType<
                    AStarGrid2D
                >();
        }


        if (
            aStarGrid != null &&
            aStarGrid.IsGridCreated()
        )
        {
            aStarGrid.MarkGridDirty();
        }
    }


    // ============================================================
    // GENERATE TERRAIN
    // ============================================================

    void Generate()
    {
        var prng =
            new System.Random(
                seed
            );


        Vector2 seedOffset =
            new Vector2(
                (
                    (float)prng.NextDouble() -
                    0.5f
                ) *
                5000f,

                (
                    (float)prng.NextDouble() -
                    0.5f
                ) *
                5000f
            );


        if (
            seed == 0
        )
        {
            seedOffset =
                Vector2.zero;
        }


        Vector2 offset =
            seedOffset +
            noiseOffset;


        int totalNumCellsX =
            fullMap.GetLength(0);


        int totalNumCellsY =
            fullMap.GetLength(1);


        for (
            int y = 0;
            y < totalNumCellsY;
            y++
        )
        {
            for (
                int x = 0;
                x < totalNumCellsX;
                x++
            )
            {
                bool isBorder =
                    (
                        x == 0 ||
                        x ==
                        totalNumCellsX -
                        1 ||
                        y == 0 ||
                        y ==
                        totalNumCellsY -
                        1
                    );


                if (
                    isBorder &&
                    border
                )
                {
                    fullMap[x, y] =
                        borderIso;
                }
                else
                {
                    float frequency =
                        noiseScale;


                    float amplitude =
                        1f;


                    float noise =
                        0f;


                    for (
                        int i = 0;
                        i < numLayers;
                        i++
                    )
                    {
                        float sampleX =
                            x /
                            (float)totalNumCellsX *
                            frequency +
                            offset.x;


                        float sampleY =
                            y /
                            (float)totalNumCellsY *
                            frequency +
                            offset.y;


                        noise +=
                            Mathf.PerlinNoise(
                                sampleX,
                                sampleY
                            ) *
                            amplitude;


                        frequency *=
                            lacunarity;


                        amplitude *=
                            persistence;
                    }


                    fullMap[x, y] =
                        Mathf.Clamp(
                            noise,
                            iso - isoClamp,
                            iso + isoClamp
                        );
                }
            }
        }
    }


    // ============================================================
    // DRAW CHUNKS
    // ============================================================

    void DrawChunks()
    {
        for (
            int y = 0;
            y < numChunksY;
            y++
        )
        {
            for (
                int x = 0;
                x < numChunksX;
                x++
            )
            {
                chunks[x, y]
                    .UpdateChunkMeshData(
                        fullMap,
                        iso,
                        smooth
                    );


                chunks[x, y]
                    .UpdateMesh();
            }
        }
    }


    // ============================================================
    // CHUNK
    // ============================================================

    public class Chunk
    {
        public Bounds2D bounds;


        Mesh mesh;

        GameObject meshHolder;

        Vector2 centre;

        float size;

        int resolution;

        float[,] chunkMap;

        bool needsMeshUpdate;

        MeshData meshData;

        Vector2Int chunkIndex;

        ColliderPath[] colliderPaths;

        MarchingSquares marchingSquares;

        EdgeColliderGenerator colliderGenerator;


        public Chunk(
            Transform holder,
            Material material,
            Vector2Int index,
            int resolution,
            Vector2 centre,
            float size
        )
        {
            this.chunkIndex =
                index;


            this.resolution =
                resolution;


            this.centre =
                centre;


            this.size =
                size;


            meshHolder =
                new GameObject(
                    "Chunk (" +
                    index.x +
                    ", " +
                    index.y +
                    ")"
                );


            meshHolder.layer =
                holder.gameObject.layer;


            meshHolder.transform.parent =
                holder;


            meshHolder.transform.position =
                centre;


            mesh =
                new Mesh();


            meshHolder
                .AddComponent<MeshFilter>()
                .mesh =
                mesh;


            meshHolder
                .AddComponent<MeshRenderer>()
                .material =
                material;


            chunkMap =
                new float[
                    resolution,
                    resolution
                ];


            marchingSquares =
                new MarchingSquares(
                    chunkMap.GetLength(0),
                    chunkMap.GetLength(1)
                );


            bounds =
                new Bounds2D(
                    centre,
                    Vector2.one *
                    size
                );


            colliderGenerator =
                new EdgeColliderGenerator();
        }


        public void UpdateChunkMeshData(
            float[,] fullMap,
            float iso,
            bool smooth
        )
        {
            int startX =
                chunkIndex.x *
                (resolution - 1);


            int startY =
                chunkIndex.y *
                (resolution - 1);


            for (
                int y = 0;
                y < resolution;
                y++
            )
            {
                for (
                    int x = 0;
                    x < resolution;
                    x++
                )
                {
                    chunkMap[x, y] =
                        fullMap[
                            startX + x,
                            startY + y
                        ];
                }
            }


            meshData =
                marchingSquares.GenerateMesh(
                    chunkMap,
                    iso,
                    size /
                    (resolution - 1),
                    smooth
                );


            colliderPaths =
                colliderGenerator
                    .Generate2DColliders(
                        meshData
                    );


            needsMeshUpdate =
                true;
        }


        public void UpdateMesh()
        {
            if (
                needsMeshUpdate
            )
            {
                needsMeshUpdate =
                    false;


                mesh.Clear();


                mesh.SetVertices(
                    meshData.vertices
                );


                mesh.SetTriangles(
                    meshData.triangles,
                    0,
                    true
                );


                EdgeColliderGenerator.SetColliders(
                    meshHolder,
                    colliderPaths
                );
            }
        }


        public void DrawBoundsGizmo()
        {
            Gizmos.color =
                Color.white;


            Gizmos.DrawWireCube(
                centre,
                Vector2.one *
                size
            );
        }
    }


    // ============================================================
    // GIZMOS
    // ============================================================

    void OnDrawGizmos()
    {
        Gizmos.color =
            Color.white;


        if (
            !Application.isPlaying
        )
        {
            float chunkSize =
                mapWidth /
                numChunksX;


            Gizmos.DrawWireCube(
                Vector2.zero,
                new Vector3(
                    chunkSize *
                    numChunksX,

                    chunkSize *
                    numChunksY
                )
            );
        }
    }


    // ============================================================
    // SAVE DATA
    // ============================================================

    [System.Serializable]
    public struct SaveData
    {
        public int width;

        public int height;

        public float[] map;


        public SaveData(
            float[,] map2D
        )
        {
            width =
                map2D.GetLength(0);


            height =
                map2D.GetLength(1);


            map =
                new float[
                    width *
                    height
                ];


            for (
                int y = 0;
                y < height;
                y++
            )
            {
                for (
                    int x = 0;
                    x < width;
                    x++
                )
                {
                    map[
                        y *
                        width +
                        x
                    ] =
                        map2D[x, y];
                }
            }
        }
    }
}