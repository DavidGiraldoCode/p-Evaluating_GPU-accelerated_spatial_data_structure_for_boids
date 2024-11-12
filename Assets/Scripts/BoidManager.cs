using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour
{

    const int threadGroupSize = 1024;

    public BoidSettings settings;
    private Voxelizer _voxelizer;
    public ComputeShader compute;
    Boid[] boids;
    ObstacleBoid[] obstacleBoids;

    private ComputeBuffer obstacleProbesBuffer, pivotsTableBuffer, pivotsTableBufferREAD_ONLY;

    void Start()
    {
        boids = FindObjectsOfType<Boid>();
        foreach (Boid b in boids)
        {
            b.Initialize(settings, null);
        }

        
        _voxelizer = FindAnyObjectByType<Voxelizer>();

        obstacleProbesBuffer = _voxelizer.obstacleProbesPositions;
        Vector3[] obProbes = new Vector3[settings.totalObstacleCount];
        obstacleProbesBuffer.GetData(obProbes);

        for (int i = 0; i < settings.totalObstacleCount; i++)
        {
            Debug.Log($"obstacleProbesBuffer {i} world pos: {obProbes[i]}");
        }


        pivotsTableBuffer = _voxelizer.pivotsTableBuffer; // This is pass by references, both point to the same space in GPU memory
        int[,] pivotsTable = new int[_voxelizer.totalVoxels, 2];
        pivotsTableBuffer.GetData(pivotsTable);

        for (uint p = 0; p < _voxelizer.totalVoxels; p++)
        {
            Debug.Log($"In Boid Manager Cell {p}, Usage (u) = {pivotsTable[p, 0]}, Starting point = {pivotsTable[p, 1]}");
        }

        ComputeBuffer h = _voxelizer.hashedObstacleNodesBuffer; // This is pass by references, both point to the same space in GPU memory
        int[,] ha = new int[settings.totalObstacleCount, 2];
        h.GetData(ha);
        Debug.Log("Boid Manager, Unsorted List ====================================== ");
        for (uint i = 0; i < settings.totalObstacleCount; i++)
        {
            Debug.Log($"Hashed node {i}, Voxel id = {ha[i, 0]}, Obstacle id = {ha[i, 1]}");
        }

        // TODO ----------------------------
        // Convert the 2D array to a list of 1D arrays for sorting
        List<int[]> haList = new List<int[]>((int)settings.totalObstacleCount);

        for (int i = 0; i < settings.totalObstacleCount; i++)
        {
            haList.Add(new int[] { ha[i, 0], ha[i, 1] });
        }

        // Sort the list based on the first element of each array in descending order
        haList.Sort((a, b) => a[0].CompareTo(b[0]));

        // Copy the sorted list back to the 2D array
        int temp = 0;
        int startPoint = 0;
        for (int i = 0; i < settings.totalObstacleCount; i++)
        {
            ha[i, 0] = haList[i][0];
            ha[i, 1] = haList[i][1];

            if(temp != ha[i, 0]) // If the voxel id has change, update
            {
                temp = ha[i, 0];
                startPoint = ha[i, 1];
                pivotsTable[temp, 1] = i;
            }
                
             // The starting point index, inside the voxel, where the obstacles are

        }
        // TODO ----------------------------
        Debug.Log("Sorted List ====================================== ");
        for (uint i = 0; i < settings.totalObstacleCount; i++)
        {
            Debug.Log($"Hashed node {i}, Voxel id = {ha[i, 0]}, Obstacle id = {ha[i, 1]}");
        }

        Debug.Log("Updated pivotsTable ====================================== ");
        for (uint p = 0; p < _voxelizer.totalVoxels; p++)
        {
            Debug.Log($"In Boid Manager Cell {p}, Usage (u) = {pivotsTable[p, 0]}, Starting point = {pivotsTable[p, 1]}");
        }
        h.SetData(ha);
        pivotsTableBuffer.SetData(pivotsTable);

        pivotsTableBufferREAD_ONLY = new ComputeBuffer(_voxelizer.totalVoxels, sizeof(int) * 2, ComputeBufferType.Default);
        pivotsTableBufferREAD_ONLY.SetData(pivotsTable);

        compute.SetBuffer(1, "_PivotsTableBuffer", pivotsTableBufferREAD_ONLY);
        compute.SetBuffer(1, "_HashedObstacleNodes", h);
        compute.SetBuffer(1, "obstacles", obstacleProbesBuffer);
        compute.SetVector("_BoundsExtent", _voxelizer.boundsExtent);
        compute.SetVector("_VoxelResolution", new Vector3(_voxelizer.voxelsX, _voxelizer.voxelsY, _voxelizer.voxelsZ));

        
    }


    void Update()
    {
        if (boids != null)
        {

            int numBoids = boids.Length;
            //int numObstacleProbes = obstacleBoids.Length;

            // 1. array for the boids
            var boidData = new BoidData[numBoids];
            // 1.1 array for the boid-based obstacles, just positions;
            //var obstacleData = new Vector3[numObstacleProbes];

            for (int i = 0; i < boids.Length; i++)
            {
                boidData[i].position = boids[i].position;
                boidData[i].direction = boids[i].forward;
            }

            // for (int i = 0; i < obstacleBoids.Length; i++)
            // {
            //     obstacleData[i] = obstacleBoids[i].position;
            // }

            // 2. Buffer for the GPU
            var boidBuffer = new ComputeBuffer(numBoids, BoidData.Size);
            //var obstacleProbesBuffer = new ComputeBuffer(numObstacleProbes, 3 * sizeof(float));

            boidBuffer.SetData(boidData);
            //obstacleProbesBuffer.SetData(obstacleData);

            // 3. Set the buffer to the kernel i=0
            compute.SetBuffer(0, "boids", boidBuffer);
            //compute.SetBuffer(0, "obstacles", obstacleProbesBuffer);

            compute.SetInt("numBoids", boids.Length);
            //compute.SetInt("numObstacles", obstacleBoids.Length);
            compute.SetFloat("viewRadius", settings.perceptionRadius);
            compute.SetFloat("avoidRadius", settings.avoidanceRadius);

            // 4. Prepares Dispatch    
            // If we had 100 boids, ceil (100 / 1024) = 1, ciel(2000 / 1024) = 2
            // We have 1 threadGroup on the X axis

            //threadGroupSize = 1024;
            int threadGroups = Mathf.CeilToInt(numBoids / (float)threadGroupSize);
            compute.Dispatch(0, threadGroups, 1, 1);


            compute.SetBuffer(1, "boids", boidBuffer);
            compute.Dispatch(1, threadGroups, 1, 1); // CS_ObstacleAvoidance

            // 5. Gets data back from the GPU. Recall this is a thread-blocking accion.
            // The main thread will be stopped until the data returns and is read
            boidBuffer.GetData(boidData);


            //obstacleProbesBuffer.GetData(obstacleData); //* NEW

            // 6. Updates the boid in the CPU
            for (int i = 0; i < boids.Length; i++)
            { // Skip 2nd
                boids[i].avgFlockHeading = boidData[i].flockHeading;
                boids[i].centreOfFlockmates = boidData[i].flockCentre;
                boids[i].avgAvoidanceHeading = boidData[i].avoidanceHeading;
                boids[i].numPerceivedFlockmates = boidData[i].numFlockmates;

                boids[i].numPerceivedObstacles = boidData[i].numDetectedObstacles;
                //Debug.Log($" boids[i].numPerceivedObstacles: {boids[i].numPerceivedObstacles}");
                boids[i].avgObstacleAvoidanceHeading = boidData[i].obstacleAvoidanceHeading;

                //*NEW Boid-based obstacel avoidance
                //GatherObstacles(i);


                boids[i].UpdateBoid();
            }

            boidBuffer.Release();
            //obstacleProbesBuffer.Release(); //* NEW


        }
    }

    void GatherObstacles(int boidIndex)
    {
        for (int j = 0; j < obstacleBoids.Length; j++)
        {
            Vector3 BoidToObstacle = obstacleBoids[j].position - boids[boidIndex].position;
            float sqrDist = (BoidToObstacle.x * BoidToObstacle.x) + (BoidToObstacle.y * BoidToObstacle.y) + (BoidToObstacle.z * BoidToObstacle.z);
            float criticalDistance = 1.5f;//bstacleBoids[j].obstacleExtend.magnitude * 4.0f;

            if (sqrDist < (criticalDistance * criticalDistance))
            {
                //Debug.DrawLine(boids[boidIndex].position, boids[boidIndex].position + BoidToObstacle, Color.red);
                boids[boidIndex].numPerceivedObstacles += 1;

                //Normalized Boid-To-Obstacle vector
                Vector3 b2oN = BoidToObstacle / sqrDist;

                Vector3 normalDirectionAwayFromObstacle = b2oN;
                boids[boidIndex].avgObstacleAvoidanceHeading -= normalDirectionAwayFromObstacle;
                //boids[boidIndex].radialAttenuation += (1 / sqrDist) - (1 / (criticalDistance * criticalDistance));

            }

        }
    }

    private void OnDisable()
    {
        //obstacleProbesBuffer.Release();
        pivotsTableBufferREAD_ONLY.Dispose();
    }

    /// <summary>
    /// Create the simply version of the boid to sent to the GPU
    /// </summary>
    public struct BoidData
    {
        public Vector3 position;
        public Vector3 direction;

        public Vector3 flockHeading;
        public Vector3 flockCentre;
        public Vector3 avoidanceHeading;
        public Vector3 obstacleAvoidanceHeading; //* NEW

        public int numFlockmates;
        public int numDetectedObstacles; //* NEW

        // Computes the stride of this struct
        public static int Size
        {
            get
            {
                // 5 vectors, each holds 3 componentes {x,y,z}
                // an integer
                return sizeof(float) * 3 * 6 + sizeof(int) * 2;
            }
        }
    }

}