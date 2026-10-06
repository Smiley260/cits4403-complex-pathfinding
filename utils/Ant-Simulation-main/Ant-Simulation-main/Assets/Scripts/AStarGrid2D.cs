using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AStarGrid2D : MonoBehaviour
{
    [Header("Grid")]

    public Vector2 gridWorldSize =
        new Vector2(60f, 33.75f);

    [Min(0.25f)]
    public float nodeRadius = 0.35f;


    [Header("Agent Collision")]

    [Tooltip(
        "Physical radius of an A* agent."
    )]
    [Min(0.05f)]
    public float agentRadius = 0.2f;


    [Tooltip(
        "Extra distance kept between the planned route and walls."
    )]
    [Min(0f)]
    public float wallClearance = 0.05f;


    [Header("Obstacles")]

    public LayerMask unwalkableMask;


    [Tooltip(
        "Kept for compatibility with the existing project."
    )]
    public int obstacleProximityPenalty = 10;


    [Header("Grid Generation")]

    [Min(1)]
    [Tooltip(
        "Kept for compatibility with the existing project."
    )]
    public int rowsPerFrame = 4;


    [Header("Dynamic Terrain")]

    [Min(0f)]
    [Tooltip(
        "Delay after terrain changes before rebuilding the A* graph. " +
        "This prevents a rebuild for every brush frame."
    )]
    public float terrainRebuildDelay = 0.15f;


    [Header("Gizmos")]

    public bool showGizmos = false;


    private Node2D[,] nodes;

    private int gridSizeX;
    private int gridSizeY;

    private float nodeDiameter;

    private bool gridCreated;

    private bool gridDirty;

    private float rebuildNotBefore;

    private bool rebuildingGrid;


    // ============================================================
    // PROPERTIES
    // ============================================================

    public int MaxSize
    {
        get
        {
            return gridSizeX * gridSizeY;
        }
    }


    // Clearance used while BUILDING the A* graph.
    //
    // Physical agent radius + additional safety margin.
    public float PathClearanceRadius
    {
        get
        {
            return
                agentRadius +
                wallClearance;
        }
    }


    // Actual physical radius of the agent.
    public float PhysicalClearanceRadius
    {
        get
        {
            return agentRadius;
        }
    }


    // ============================================================
    // DYNAMIC TERRAIN
    // ============================================================

    public void MarkGridDirty()
    {
        gridDirty =
            true;


        rebuildNotBefore =
            Time.unscaledTime +
            Mathf.Max(
                0f,
                terrainRebuildDelay
            );
    }


    private void LateUpdate()
    {
        if (
            !gridDirty ||
            rebuildingGrid
        )
        {
            return;
        }


        if (
            Time.unscaledTime <
            rebuildNotBefore
        )
        {
            return;
        }


        gridDirty =
            false;


        CreateGrid();
    }


    // ============================================================
    // GRID STATUS
    // ============================================================

    public bool IsGridCreated()
    {
        return
            gridCreated &&
            nodes != null;
    }


    // ============================================================
    // GRID CREATION ROUTINE
    // ============================================================

    public IEnumerator CreateGridRoutine()
    {
        CreateGrid();

        yield break;
    }


    // ============================================================
    // CREATE GRID
    // ============================================================

    public void CreateGrid()
    {
        if (
            rebuildingGrid
        )
        {
            return;
        }


        rebuildingGrid =
            true;


        gridDirty =
            false;


        gridCreated =
            false;


        nodeDiameter =
            nodeRadius * 2f;


        gridSizeX =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    gridWorldSize.x /
                    nodeDiameter
                )
            );


        gridSizeY =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    gridWorldSize.y /
                    nodeDiameter
                )
            );


        nodes =
            new Node2D[
                gridSizeX,
                gridSizeY
            ];


        // Make sure Marching Squares colliders are synchronized.
        Physics2D.SyncTransforms();


        Vector2 bottomLeft =
            (Vector2)transform.position
            - Vector2.right *
              gridWorldSize.x / 2f
            - Vector2.up *
              gridWorldSize.y / 2f;


        // ========================================================
        // CREATE NODES
        // ========================================================

        for (
            int y = 0;
            y < gridSizeY;
            y++
        )
        {
            for (
                int x = 0;
                x < gridSizeX;
                x++
            )
            {
                Vector2 worldPosition =
                    bottomLeft
                    + Vector2.right *
                      (
                          x *
                          nodeDiameter +
                          nodeRadius
                      )
                    + Vector2.up *
                      (
                          y *
                          nodeDiameter +
                          nodeRadius
                      );


                bool walkable =
                    IsPositionWalkable(
                        worldPosition
                    );


                nodes[x, y] =
                    new Node2D(
                        walkable,
                        worldPosition,
                        x,
                        y,
                        0
                    );
            }
        }


        // ========================================================
        // BUILD NEIGHBOURS
        // ========================================================

        for (
            int y = 0;
            y < gridSizeY;
            y++
        )
        {
            for (
                int x = 0;
                x < gridSizeX;
                x++
            )
            {
                BuildNeighboursForNode(
                    nodes[x, y]
                );
            }
        }


        gridCreated =
            true;


        rebuildingGrid =
            false;


        Debug.Log(
            "A* Grid created: " +
            gridSizeX +
            " x " +
            gridSizeY +
            " (" +
            MaxSize +
            " nodes)"
        );
    }


    // ============================================================
    // BUILD NEIGHBOURS
    // ============================================================

    private void BuildNeighboursForNode(
        Node2D node
    )
    {
        node.neighbours =
            new List<Node2D>(8);


        if (
            !node.walkable
        )
        {
            return;
        }


        for (
            int offsetX = -1;
            offsetX <= 1;
            offsetX++
        )
        {
            for (
                int offsetY = -1;
                offsetY <= 1;
                offsetY++
            )
            {
                if (
                    offsetX == 0 &&
                    offsetY == 0
                )
                {
                    continue;
                }


                int checkX =
                    node.gridX +
                    offsetX;


                int checkY =
                    node.gridY +
                    offsetY;


                if (
                    checkX < 0 ||
                    checkX >= gridSizeX ||
                    checkY < 0 ||
                    checkY >= gridSizeY
                )
                {
                    continue;
                }


                Node2D neighbour =
                    nodes[
                        checkX,
                        checkY
                    ];


                if (
                    !neighbour.walkable
                )
                {
                    continue;
                }


                // ------------------------------------------------
                // Prevent diagonal corner cutting.
                // ------------------------------------------------

                if (
                    offsetX != 0 &&
                    offsetY != 0
                )
                {
                    Node2D horizontal =
                        nodes[
                            node.gridX +
                            offsetX,
                            node.gridY
                        ];


                    Node2D vertical =
                        nodes[
                            node.gridX,
                            node.gridY +
                            offsetY
                        ];


                    if (
                        !horizontal.walkable ||
                        !vertical.walkable
                    )
                    {
                        continue;
                    }
                }


                // ------------------------------------------------
                // Check the actual edge between nodes.
                // ------------------------------------------------

                if (
                    !IsSegmentClear(
                        node.worldPosition,
                        neighbour.worldPosition
                    )
                )
                {
                    continue;
                }


                node.neighbours.Add(
                    neighbour
                );
            }
        }
    }


    // ============================================================
    // WORLD -> NODE
    // ============================================================

    public Node2D NodeFromWorldPoint(
        Vector2 worldPosition
    )
    {
        if (
            !IsGridCreated()
        )
        {
            return null;
        }


        Vector2 localPosition =
            worldPosition -
            (Vector2)transform.position;


        float percentX =
            (
                localPosition.x +
                gridWorldSize.x / 2f
            ) /
            gridWorldSize.x;


        float percentY =
            (
                localPosition.y +
                gridWorldSize.y / 2f
            ) /
            gridWorldSize.y;


        percentX =
            Mathf.Clamp01(
                percentX
            );


        percentY =
            Mathf.Clamp01(
                percentY
            );


        int x =
            Mathf.RoundToInt(
                (
                    gridSizeX -
                    1
                ) *
                percentX
            );


        int y =
            Mathf.RoundToInt(
                (
                    gridSizeY -
                    1
                ) *
                percentY
            );


        return nodes[x, y];
    }


    // ============================================================
    // PLANNING WALKABILITY
    // ============================================================

    public bool IsPositionWalkable(
        Vector2 worldPosition
    )
    {
        Collider2D hit =
            Physics2D.OverlapCircle(
                worldPosition,
                PathClearanceRadius,
                unwalkableMask
            );


        return
            hit == null;
    }


    // ============================================================
    // PHYSICAL WALKABILITY
    // ============================================================

    public bool IsPhysicalPositionWalkable(
        Vector2 worldPosition
    )
    {
        Collider2D hit =
            Physics2D.OverlapCircle(
                worldPosition,
                PhysicalClearanceRadius,
                unwalkableMask
            );


        return
            hit == null;
    }


    // ============================================================
    // PLANNING SEGMENT
    // ============================================================

    public bool IsSegmentClear(
        Vector2 start,
        Vector2 end
    )
    {
        Vector2 delta =
            end - start;


        float distance =
            delta.magnitude;


        if (
            !IsPositionWalkable(
                start
            ) ||
            !IsPositionWalkable(
                end
            )
        )
        {
            return false;
        }


        if (
            distance <=
            0.0001f
        )
        {
            return true;
        }


        RaycastHit2D hit =
            Physics2D.CircleCast(
                start,
                PathClearanceRadius,
                delta.normalized,
                distance + 0.001f,
                unwalkableMask
            );


        return
            hit.collider == null;
    }


    // ============================================================
    // PHYSICAL SEGMENT
    // ============================================================

    public bool IsPhysicalSegmentClear(
        Vector2 start,
        Vector2 end
    )
    {
        Vector2 delta =
            end - start;


        float distance =
            delta.magnitude;


        if (
            !IsPhysicalPositionWalkable(
                start
            ) ||
            !IsPhysicalPositionWalkable(
                end
            )
        )
        {
            return false;
        }


        if (
            distance <=
            0.0001f
        )
        {
            return true;
        }


        RaycastHit2D hit =
            Physics2D.CircleCast(
                start,
                PhysicalClearanceRadius,
                delta.normalized,
                distance + 0.001f,
                unwalkableMask
            );


        return
            hit.collider == null;
    }


    // ============================================================
    // GET NEIGHBOURS
    // ============================================================

    public List<Node2D> GetNeighbors(
        Node2D node
    )
    {
        if (
            node == null
        )
        {
            return null;
        }


        return node.neighbours;
    }


    // ============================================================
    // FIND NEAREST REACHABLE NODE
    // ============================================================

    public Node2D GetNearestReachableNode(
        Vector2 worldPosition
    )
    {
        if (
            !IsGridCreated()
        )
        {
            return null;
        }


        Node2D nearestNode =
            NodeFromWorldPoint(
                worldPosition
            );


        // Normal case.
        if (
            nearestNode != null &&
            nearestNode.walkable &&
            IsPhysicalSegmentClear(
                worldPosition,
                nearestNode.worldPosition
            )
        )
        {
            return nearestNode;
        }


        // Fallback search.
        Node2D bestNode =
            null;


        float bestDistance =
            float.MaxValue;


        for (
            int x = 0;
            x < gridSizeX;
            x++
        )
        {
            for (
                int y = 0;
                y < gridSizeY;
                y++
            )
            {
                Node2D candidate =
                    nodes[x, y];


                if (
                    !candidate.walkable
                )
                {
                    continue;
                }


                float distance =
                    (
                        candidate.worldPosition -
                        worldPosition
                    ).sqrMagnitude;


                if (
                    distance >=
                    bestDistance
                )
                {
                    continue;
                }


                if (
                    !IsPhysicalSegmentClear(
                        worldPosition,
                        candidate.worldPosition
                    )
                )
                {
                    continue;
                }


                bestDistance =
                    distance;


                bestNode =
                    candidate;
            }
        }


        return bestNode;
    }


    // ============================================================
    // GIZMOS
    // ============================================================

    private void OnDrawGizmos()
    {
        Gizmos.color =
            Color.yellow;


        Gizmos.DrawWireCube(
            transform.position,
            gridWorldSize
        );


        if (
            !showGizmos ||
            !IsGridCreated()
        )
        {
            return;
        }


        foreach (
            Node2D node
            in nodes
        )
        {
            Gizmos.color =
                node.walkable
                    ? new Color(
                        1f,
                        1f,
                        1f,
                        0.15f
                    )
                    : new Color(
                        1f,
                        0f,
                        0f,
                        0.35f
                    );


            Gizmos.DrawCube(
                node.worldPosition,
                Vector3.one *
                nodeDiameter *
                0.8f
            );
        }
    }
}


// =================================================================
// NODE
// =================================================================

public class Node2D
{
    public bool walkable;

    public Vector2 worldPosition;

    public int gridX;
    public int gridY;

    public int movementPenalty;

    public List<Node2D> neighbours;


    public Node2D(
        bool walkable,
        Vector2 worldPosition,
        int gridX,
        int gridY,
        int movementPenalty
    )
    {
        this.walkable =
            walkable;


        this.worldPosition =
            worldPosition;


        this.gridX =
            gridX;


        this.gridY =
            gridY;


        this.movementPenalty =
            movementPenalty;


        this.neighbours =
            new List<Node2D>(8);
    }
}