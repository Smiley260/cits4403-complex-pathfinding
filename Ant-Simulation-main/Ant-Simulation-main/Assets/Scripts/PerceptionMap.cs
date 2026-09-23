using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PerceptionMap : MonoBehaviour
{
    public Vector2 area;
    public AntSettings antSettings;

    public ParticleSystem particleDisplay;

    ParticleSystem.EmitParams particleEmitParams;

    float sqrPerceptionRadius;

    public Color pheremoneColor;
    public float pheremoneSize = 0.05f;
    public float initialAlpha = 1;

    int numCellsX;
    int numCellsY;

    Vector2 halfSize;

    float cellSizeReciprocal;

    Cell[,] cells;

    // Stores the perception radius currently
    // being used by the grid.
    float currentPerceptionRadius;


    // ==================================================
    // AWAKE
    // ==================================================

    void Awake()
    {
        Init();
    }


    // ==================================================
    // UPDATE
    // ==================================================

    void Update()
    {
        if (antSettings == null)
        {
            return;
        }


        // --------------------------------------------------
        // Update pheromone particle lifetime
        // --------------------------------------------------

        particleEmitParams.startLifetime =
            Mathf.Max(
                0.01f,
                antSettings.pheromoneEvaporateTime
            );


        // --------------------------------------------------
        // Check whether sensorSize changed
        // --------------------------------------------------

        float newPerceptionRadius =
            Mathf.Max(
                0.01f,
                antSettings.sensorSize
            );


        if (!Mathf.Approximately(
            newPerceptionRadius,
            currentPerceptionRadius))
        {
            RebuildGrid(newPerceptionRadius);
        }
    }


    // ==================================================
    // INITIALISE
    // ==================================================

    void Init()
    {
        if (antSettings == null)
        {
            Debug.LogError(
                "PerceptionMap requires an AntSettings asset."
            );

            return;
        }


        float perceptionRadius =
            Mathf.Max(
                0.01f,
                antSettings.sensorSize
            );


        currentPerceptionRadius =
            perceptionRadius;


        sqrPerceptionRadius =
            perceptionRadius *
            perceptionRadius;


        numCellsX =
            Mathf.CeilToInt(
                area.x /
                perceptionRadius
            );


        numCellsY =
            Mathf.CeilToInt(
                area.y /
                perceptionRadius
            );


        numCellsX =
            Mathf.Max(
                1,
                numCellsX
            );


        numCellsY =
            Mathf.Max(
                1,
                numCellsY
            );


        halfSize =
            new Vector2(
                numCellsX *
                perceptionRadius,

                numCellsY *
                perceptionRadius
            ) * 0.5f;


        cellSizeReciprocal =
            1f /
            perceptionRadius;


        cells =
            new Cell[
                numCellsX,
                numCellsY
            ];


        for (int y = 0; y < numCellsY; y++)
        {
            for (int x = 0; x < numCellsX; x++)
            {
                cells[x, y] =
                    new Cell();
            }
        }


        // --------------------------------------------------
        // Particle settings
        // --------------------------------------------------

        particleEmitParams.startLifetime =
            Mathf.Max(
                0.01f,
                antSettings.pheromoneEvaporateTime
            );

        particleEmitParams.startSize =
            pheremoneSize;


        if (particleDisplay != null)
        {
            var main =
                particleDisplay.main;

            main.maxParticles =
                1000 * 1000;


            var c =
                particleDisplay.colorOverLifetime;

            c.enabled = true;


            Gradient grad =
                new Gradient();


            grad.colorKeys =
                new GradientColorKey[]
                {
                    new GradientColorKey(
                        Color.white,
                        0
                    ),

                    new GradientColorKey(
                        Color.white,
                        1
                    )
                };


            grad.alphaKeys =
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(
                        initialAlpha,
                        0f
                    ),

                    new GradientAlphaKey(
                        0f,
                        1f
                    )
                };


            c.color =
                grad;
        }
    }


    // ==================================================
    // REBUILD GRID
    // ==================================================

    void RebuildGrid(float newPerceptionRadius)
    {
        if (cells == null)
        {
            Init();
            return;
        }


        // --------------------------------------------------
        // Save existing pheromones
        // --------------------------------------------------

        List<Entry> existingEntries =
            new List<Entry>();


        for (int y = 0; y < numCellsY; y++)
        {
            for (int x = 0; x < numCellsX; x++)
            {
                Cell cell =
                    cells[x, y];


                if (cell == null)
                {
                    continue;
                }


                LinkedListNode<Entry> node =
                    cell.entries.First;


                while (node != null)
                {
                    existingEntries.Add(
                        node.Value
                    );

                    node =
                        node.Next;
                }
            }
        }


        // --------------------------------------------------
        // Recalculate grid
        // --------------------------------------------------

        currentPerceptionRadius =
            newPerceptionRadius;


        sqrPerceptionRadius =
            newPerceptionRadius *
            newPerceptionRadius;


        numCellsX =
            Mathf.CeilToInt(
                area.x /
                newPerceptionRadius
            );


        numCellsY =
            Mathf.CeilToInt(
                area.y /
                newPerceptionRadius
            );


        numCellsX =
            Mathf.Max(
                1,
                numCellsX
            );


        numCellsY =
            Mathf.Max(
                1,
                numCellsY
            );


        halfSize =
            new Vector2(
                numCellsX *
                newPerceptionRadius,

                numCellsY *
                newPerceptionRadius
            ) * 0.5f;


        cellSizeReciprocal =
            1f /
            newPerceptionRadius;


        cells =
            new Cell[
                numCellsX,
                numCellsY
            ];


        for (int y = 0; y < numCellsY; y++)
        {
            for (int x = 0; x < numCellsX; x++)
            {
                cells[x, y] =
                    new Cell();
            }
        }


        // --------------------------------------------------
        // Put old pheromones back into the new grid
        // --------------------------------------------------

        foreach (Entry entry in existingEntries)
        {
            Vector2Int cellCoord =
                CellCoordFromPos(
                    entry.position
                );


            cells[
                cellCoord.x,
                cellCoord.y
            ].Add(entry);
        }
    }


    // ==================================================
    // ADD PHEROMONE
    // ==================================================

    public void Add(
        Vector2 point,
        float initialWeight
    )
    {
        if (cells == null)
        {
            return;
        }


        Vector2Int cellCoord =
            CellCoordFromPos(point);


        Cell cell =
            cells[
                cellCoord.x,
                cellCoord.y
            ];


        Entry entry =
            new Entry()
            {
                position = point,

                creationTime =
                    Time.time,

                initialWeight =
                    initialWeight
            };


        cell.Add(entry);


        // --------------------------------------------------
        // Visual particle
        // --------------------------------------------------

        if (particleDisplay != null)
        {
            particleEmitParams.startColor =
                new Color(
                    pheremoneColor.r,
                    pheremoneColor.g,
                    pheremoneColor.b,
                    initialWeight
                );


            particleEmitParams.position =
                point;


            particleDisplay.Emit(
                particleEmitParams,
                1
            );
        }
    }


    // ==================================================
    // GET PHEROMONES IN CIRCLE
    // ==================================================

    public int GetAllInCircle(
        Entry[] result,
        Vector2 centre
    )
    {
        if (cells == null)
        {
            return 0;
        }


        Vector2Int cellCoord =
            CellCoordFromPos(centre);


        int i = 0;

        float currentTime =
            Time.time;


        for (
            int offsetY = -1;
            offsetY <= 1;
            offsetY++
        )
        {
            for (
                int offsetX = -1;
                offsetX <= 1;
                offsetX++
            )
            {
                int cellX =
                    cellCoord.x +
                    offsetX;


                int cellY =
                    cellCoord.y +
                    offsetY;


                if (
                    cellX >= 0 &&
                    cellX < numCellsX &&
                    cellY >= 0 &&
                    cellY < numCellsY
                )
                {
                    Cell cell =
                        cells[
                            cellX,
                            cellY
                        ];


                    LinkedListNode<Entry>
                        currentEntryNode =
                            cell.entries.First;


                    while (
                        currentEntryNode != null
                    )
                    {
                        Entry currentEntry =
                            currentEntryNode.Value;


                        float currentLifetime =
                            currentTime -
                            currentEntry.creationTime;


                        // --------------------------------------------------
                        // Remove expired pheromone
                        // --------------------------------------------------

                        if (
                            currentLifetime >
                            antSettings.pheromoneEvaporateTime
                        )
                        {
                            LinkedListNode<Entry>
                                nextNode =
                                    currentEntryNode.Next;


                            cell.entries.Remove(
                                currentEntryNode
                            );


                            currentEntryNode =
                                nextNode;


                            continue;
                        }


                        // --------------------------------------------------
                        // Check whether pheromone is
                        // inside the sensing radius
                        // --------------------------------------------------

                        if (
                            (
                                currentEntry.position -
                                centre
                            ).sqrMagnitude <
                            sqrPerceptionRadius
                        )
                        {
                            if (
                                i >= result.Length
                            )
                            {
                                return result.Length;
                            }


                            result[i] =
                                currentEntry;


                            i++;
                        }


                        currentEntryNode =
                            currentEntryNode.Next;
                    }
                }
            }
        }


        return i;
    }


    // ==================================================
    // CONVERT POSITION TO CELL
    // ==================================================

    Vector2Int CellCoordFromPos(
        Vector2 point
    )
    {
        int x =
            (int)(
                (
                    point.x +
                    halfSize.x
                ) *
                cellSizeReciprocal
            );


        int y =
            (int)(
                (
                    point.y +
                    halfSize.y
                ) *
                cellSizeReciprocal
            );


        return new Vector2Int(
            Mathf.Clamp(
                x,
                0,
                numCellsX - 1
            ),

            Mathf.Clamp(
                y,
                0,
                numCellsY - 1
            )
        );
    }


    // ==================================================
    // CELL
    // ==================================================

    public class Cell
    {
        public LinkedList<Entry> entries;


        public Cell()
        {
            entries =
                new LinkedList<Entry>();
        }


        public void Add(
            Entry entry
        )
        {
            entries.AddLast(
                entry
            );
        }
    }


    // ==================================================
    // PHEROMONE ENTRY
    // ==================================================

    public struct Entry
    {
        public Vector2 position;

        public float initialWeight;

        public float creationTime;
    }
}