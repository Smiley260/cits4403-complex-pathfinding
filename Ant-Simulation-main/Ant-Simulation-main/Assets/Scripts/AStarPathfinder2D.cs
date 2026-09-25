using System;
using System.Collections.Generic;
using UnityEngine;
public class AStarPathfinder2D : MonoBehaviour
{
    [Header("References")]
    public AStarGrid2D grid;
    public Storm storm;

    [Header("Queued Requests")]
    [Tooltip(
        "Maximum queued requests processed per frame. " +
        "AStarAgent2D currently uses immediate searches instead."
    )]
    [Min(1)]
    public int maxPathRequestsPerFrame = 8;

    [Tooltip(
        "Maximum real time spent processing queued requests per frame. " +
        "This does not cap the work of one individual A* search."
    )]
    [Min(0.1f)]
    public float maxPathfindingMillisecondsPerFrame = 4f;

    // ============================================================
    // PATH REQUEST
    // ============================================================
    private struct PathRequest
    {
        public Vector2 startPosition;
        public Vector2 targetPosition;
        public Action<Vector2[]> callback;

        public PathRequest(
            Vector2 startPosition,
            Vector2 targetPosition,
            Action<Vector2[]> callback
        )
        {
            this.startPosition =
                startPosition;

            this.targetPosition =
                targetPosition;

            this.callback =
                callback;
        }
    }

    // ============================================================
    // SEARCH RECORD
    // ============================================================
    private class SearchRecord :
        IHeapItem<SearchRecord>
    {
        public Node2D node;
        public int gCost =
            int.MaxValue;
        public int hCost;
        public Node2D parent;
        private int heapIndex;

        public int fCost{
            get{
                if (gCost == int.MaxValue)
                {
                    return int.MaxValue;
                }
                return
                    gCost + hCost;
            }
        }

        public int HeapIndex
        {
            get{
                return heapIndex;
            }
            set{
                heapIndex = value;
            }
        }

        public SearchRecord( Node2D node )
        {
            this.node = node;
        }

        public int CompareTo(SearchRecord other)
        {
            int comparison = fCost.CompareTo(other.fCost);

            if (comparison == 0)
            {
                comparison = hCost.CompareTo(other.hCost);
            }

            // Heap expects larger comparison values to have
            // higher priority.
            //
            // Lower fCost therefore comes first.
            return -comparison;
        }
    }

    private readonly Queue<PathRequest> pathRequests = new Queue<PathRequest>();

    // ============================================================
    // READY
    // ============================================================
    public bool IsReady()
    {
        return
            grid != null &&
            grid.IsGridCreated();
    }

    // ============================================================
    // UPDATE
    // ============================================================
    private void Update()
    {
        if (!IsReady() || pathRequests.Count == 0){
            return;
        }

        ProcessQueuedPathRequests();
    }

    // ============================================================
    // QUEUED REQUEST API
    // ============================================================
    public void RequestPath(
        Vector2 startPosition,
        Vector2 targetPosition,
        Action<Vector2[]> callback
    ){
        pathRequests.Enqueue(
            new PathRequest(
                startPosition,
                targetPosition,
                callback
            )
        );
    }

    private void ProcessQueuedPathRequests(){
        float frameStart = Time.realtimeSinceStartup;

        int processed = 0;

        while (pathRequests.Count > 0){
            if (processed >= maxPathRequestsPerFrame){
                break;
            }

            if (processed > 0){
                float elapsedMilliseconds = (Time.realtimeSinceStartup - frameStart) *1000f;

                if (elapsedMilliseconds >=maxPathfindingMillisecondsPerFrame){
                    break;
                }
            }

            PathRequest request = pathRequests.Dequeue();

            Vector2[] path = FindPathInternal(request.startPosition,request.targetPosition);

            request.callback?.Invoke(path);

            processed++;
        }
    }

    // ============================================================
    // IMMEDIATE PATH
    // ============================================================
    //
    // IMPORTANT:
    //
    // Each call invokes a completely independent A* search.
    //
    // The grid is shared.
    // The search state is NOT shared.
    //
    public Vector2[] FindPathImmediate(Vector2 startPosition, Vector2 targetPosition){

        if (!IsReady()){
            return null;
        }

        return FindPathInternal(startPosition,targetPosition);
    }

    // Compatibility method.
    public Vector2[] FindPath(Vector2 startPosition,Vector2 targetPosition){
        return FindPathImmediate(startPosition,targetPosition);
    }

    // ============================================================
    // A*
    // ============================================================
    private Vector2[] FindPathInternal(Vector2 startPosition, Vector2 targetPosition){
        if (!IsReady()){
            return null;
        }

        // --------------------------------------------------------
        // Start and target are converted independently.
        //
        // The actual current position only needs to be physically
        // reachable from the selected graph node.
        // --------------------------------------------------------
        Node2D startNode = grid.GetNearestReachableNode(startPosition);

        Node2D targetNode =grid.GetNearestReachableNode(targetPosition);

        if (startNode == null || targetNode == null){
            return null;
        }

        // ========================================================
        // SPECIAL CASE
        // ========================================================
        if (startNode == targetNode){
            if (grid.IsPhysicalSegmentClear(startPosition,targetPosition)){
                return new Vector2[]{targetPosition};
            }

            return null;
        }

        // ========================================================
        // COMPLETELY LOCAL SEARCH STATE
        // ========================================================
        //
        // This is what makes every agent's search independent.
        //
        Dictionary<Node2D, SearchRecord> records = new Dictionary<Node2D, SearchRecord>();

        HashSet<Node2D> closedSet = new HashSet<Node2D>();

        Heap<SearchRecord> openSet = new Heap<SearchRecord>(Mathf.Max(1,grid.MaxSize));

        SearchRecord startRecord = GetOrCreateRecord(startNode,records);

        startRecord.gCost = 0;

        startRecord.hCost =GetDistance(startNode,targetNode);

        startRecord.parent = null;

        openSet.Add(startRecord);

        // ========================================================
        // SEARCH
        // ========================================================
        while (openSet.Count > 0){
            SearchRecord currentRecord = openSet.RemoveFirst();

            Node2D currentNode = currentRecord.node;

            closedSet.Add(currentNode);

            if (currentNode == targetNode){
                return
                    BuildWorldPath(
                        startPosition,
                        targetPosition,
                        startNode,
                        targetNode,
                        records
                    );
            }

            List<Node2D> neighbours = grid.GetNeighbors(currentNode);

            if (neighbours == null){
                continue;
            }

            foreach (Node2D neighbour in neighbours){
                if (
                    neighbour == null ||
                    !neighbour.walkable ||
                    closedSet.Contains(neighbour)
                ){
                    continue;
                }

                SearchRecord neighbourRecord =
                    GetOrCreateRecord(neighbour,records);

                // ------------------------------------------------
                // BASE MOVEMENT COST
                // ------------------------------------------------
                int movementCost = GetDistance(currentNode,neighbour);

                // ------------------------------------------------
                // STORM COST
                // ------------------------------------------------
                if (storm != null){
                    float stormCost = Mathf.Max(1f,storm.GetTraversalCost(neighbour.worldPosition));

                    float additionalStormCost = (stormCost -1f) * 10f;

                    movementCost += Mathf.Max(0, Mathf.RoundToInt(additionalStormCost));
                }

                // ------------------------------------------------
                // NODE PENALTY
                // ------------------------------------------------
                movementCost +=
                    Mathf.Max(
                        0,
                        neighbour.movementPenalty
                    );

                int newCost =
                    currentRecord.gCost +
                    movementCost;

                bool wasNeverReached =
                    neighbourRecord.gCost ==
                    int.MaxValue;

                if (
                    wasNeverReached ||
                    newCost <
                    neighbourRecord.gCost
                )
                {
                    neighbourRecord.gCost =
                        newCost;

                    neighbourRecord.hCost =
                        GetDistance(
                            neighbour,
                            targetNode
                        );

                    neighbourRecord.parent =
                        currentNode;

                    if (
                        wasNeverReached
                    )
                    {
                        openSet.Add(
                            neighbourRecord
                        );
                    }
                    else
                    {
                        openSet.UpdateItem(
                            neighbourRecord
                        );
                    }
                }
            }
        }

        // No route exists.
        return null;
    }

    // ============================================================
    // SEARCH RECORD
    // ============================================================
    private SearchRecord GetOrCreateRecord(
        Node2D node,
        Dictionary<Node2D, SearchRecord> records
    )
    {
        SearchRecord record;

        if (
            !records.TryGetValue(
                node,
                out record
            )
        )
        {
            record =
                new SearchRecord(
                    node
                );

            records.Add(
                node,
                record
            );
        }

        return record;
    }

    // ============================================================
    // BUILD WORLD PATH
    // ============================================================
    private Vector2[] BuildWorldPath(
        Vector2 startPosition,
        Vector2 targetPosition,
        Node2D startNode,
        Node2D targetNode,
        Dictionary<Node2D, SearchRecord> records
    )
    {
        List<Vector2> waypoints =
            new List<Vector2>();

        Node2D currentNode =
            targetNode;

        while (
            currentNode !=
            startNode
        )
        {
            waypoints.Add(
                currentNode.worldPosition
            );

            SearchRecord currentRecord;

            if (
                !records.TryGetValue(
                    currentNode,
                    out currentRecord
                )
            )
            {
                return null;
            }

            currentNode =
                currentRecord.parent;

            if (
                currentNode == null
            )
            {
                return null;
            }
        }

        waypoints.Reverse();

        // Always finish at the actual requested world position.
        if (
            waypoints.Count == 0
        )
        {
            waypoints.Add(
                targetPosition
            );
        }
        else
        {
            Vector2 finalGridPoint =
                waypoints[
                    waypoints.Count - 1
                ];

            if (
                Vector2.Distance(
                    finalGridPoint,
                    targetPosition
                ) >
                0.01f
            )
            {
                waypoints.Add(
                    targetPosition
                );
            }
        }

        // ========================================================
        // VALIDATE THE WORLD ROUTE
        // ========================================================
        //
        // First segment:
        // actual agent position -> graph
        //
        // Use PHYSICAL clearance because the actual agent may be
        // sitting inside the additional planning margin.
        //
        // Middle segments:
        // node -> node
        //
        // These were created with planning clearance.
        //
        // Final segment:
        // graph -> exact target
        //
        // Use physical clearance because that is what the actual
        // agent needs to traverse.
        //
        Vector2 previous =
            startPosition;

        for (
            int i = 0;
            i < waypoints.Count;
            i++
        )
        {
            Vector2 current =
                waypoints[i];

            bool firstSegment =
                i == 0;

            bool finalSegment =
                i ==
                waypoints.Count - 1;

            bool clear;

            if (
                firstSegment ||
                finalSegment
            )
            {
                clear =
                    grid.IsPhysicalSegmentClear(
                        previous,
                        current
                    );
            }
            else
            {
                clear =
                    grid.IsSegmentClear(
                        previous,
                        current
                    );
            }

            if (
                !clear
            )
            {
                return null;
            }

            previous =
                current;
        }

        return
            waypoints.ToArray();
    }

    // ============================================================
    // GRID DISTANCE
    // ============================================================
    private int GetDistance(
        Node2D nodeA,
        Node2D nodeB
    )
    {
        int dstX =
            Mathf.Abs(
                nodeA.gridX -
                nodeB.gridX
            );

        int dstY =
            Mathf.Abs(
                nodeA.gridY -
                nodeB.gridY
            );

        if (
            dstX >
            dstY
        )
        {
            return
                14 *
                dstY +
                10 *
                (
                    dstX -
                    dstY
                );
        }

        return
            14 *
            dstX +
            10 *
            (
                dstY -
                dstX
            );
    }
}