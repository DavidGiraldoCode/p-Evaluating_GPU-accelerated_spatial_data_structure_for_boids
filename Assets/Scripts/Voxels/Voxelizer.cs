// Original Work https://github.com/GarrettGunnell/CS2-Smoke-Grenades
/**
* My contributions:
* - Documentation
*/

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class Voxelizer : MonoBehaviour
{
    #region Grid setup variables
    [Header("Grid setup variables")]
    [Tooltip("This represents a vector from the center to the corner of the AABB")]
    public Vector3 boundsExtent = new Vector3(3, 3, 3);

    public float voxelSize = 0.25f;

    public GameObject objectsToVoxelize = null;

    [Range(0.0f, 2.0f)]
    public float intersectionBias = 1.0f;

    #endregion Grid setup variables

    #region Debug tools

    [Header("Debug tools")]
    [Tooltip("The mesh that help visualize the voxelized result, is can be spheres as well")]
    public Mesh debugMesh;
    public Transform debugPointTransformQuery;
    public Transform debugPointB;
    public bool debugStaticVoxels = false;
    public bool debugSmokeVoxels = false;
    public bool debugEdgeVoxels = false;

    #endregion Debug tools

    public Vector3 maxRadius = new Vector3(1, 1, 1);

    [Range(0.01f, 5.0f)]
    public float growthSpeed = 1.0f;

    [Range(0, 128)]
    public int maxFillSteps = 16;
    public bool iterateFill = false;
    public bool constantFill = false;


    #region GPGPU buffers
    private ComputeBuffer staticVoxelsBuffer, smokeVoxelsBuffer, smokePingVoxelsBuffer, argsBuffer;

    //* Define a buffer to store how many obstacle there are inside a voxel
    private ComputeBuffer obstacleCountAtVoxel, obstacleProbesPositions;
    [Tooltip("Passes the total obstacle count to the voxelizer")]
    public BoidSettings settings;
    public const uint MAX_OBSTACLE_PER_VOXEL = 20;
    public ComputeBuffer pivotsTableBuffer, hashedObstacleNodesBuffer;
    private int[,] pivotsTable, hashedObstacleNodes; // Pozzer
    // public struct HashedObstacle // Recall types cannot contain members of their own type
    // {
    //     public int[] real_indexes; // Is is arbitrary asigned.
    // };

    // *

    private ComputeShader voxelizeCompute;
    private Material debugVoxelMaterial;
    private Bounds debugBounds;

    #endregion GPGPU buffers
    public int voxelsX, voxelsY, voxelsZ, totalVoxels;
    private float radius;
    private Vector3 smokeOrigin;

    #region Getters
    public ComputeBuffer GetSmokeVoxelBuffer()
    {
        return smokeVoxelsBuffer;
    }

    public Vector3 GetVoxelResolution()
    {
        return new Vector3(voxelsX, voxelsY, voxelsZ);
    }

    public Vector3 GetBoundsExtent()
    {
        return boundsExtent;
    }

    public float GetVoxelSize()
    {
        return voxelSize;
    }

    public Vector3 GetSmokeOrigin()
    {
        return smokeOrigin;
    }

    // public Vector3 GetSmokeRadius()
    // {
    //     return Vector3.Lerp(Vector3.zero, maxRadius, Easing(radius));
    // }

    // public float GetEasing()
    // {
    //     return Easing(radius);
    // }
    #endregion Getters

    #region ray parameter
    private Vector3 rayOrigin, rayDirection;
    [Range(0.0f, 10.0f)]
    public float t = 1.0f;
    #endregion ray parameter

    private void OnEnable()
    {
        radius = 0.0f;
        // Loading resourses
        debugVoxelMaterial = new Material(Shader.Find("Hidden/VisualizeVoxels")); // The shader had to be translate to URP
        // Use of the class Resources, which allow to create a folder in unity and add any file that we want to get access to in code.  
        // more here https://docs.unity3d.com/ScriptReference/Resources.html
        voxelizeCompute = (ComputeShader)Resources.Load("Voxelize");

        // Taking the half diagonal vector of the boundsExtent time 2, we get 
        // the total size of the bounding volume.
        Vector3 boundsSize = boundsExtent * 2;
        // We use how much the half diagonal rises in the Y-axis to translate the bounding volume
        // and place it on the plane XZ.
        debugBounds = new Bounds(new Vector3(0, boundsExtent.y, 0), boundsSize);

        // Subdividing the bounds in each direction by the size of the voxel to get the total at each dimension
        voxelsX = Mathf.CeilToInt(boundsSize.x / voxelSize);
        voxelsY = Mathf.CeilToInt(boundsSize.y / voxelSize);
        voxelsZ = Mathf.CeilToInt(boundsSize.z / voxelSize);
        totalVoxels = voxelsX * voxelsY * voxelsZ;

        // TODO study stride (the space we need in memory)
        // Memory allocation to store all the potential voxels that are going to be an obstacle
        // Up to this moment the buffer is "empty", it only contains the capacity.
        // The voxels are represented as an array of ints { 0, 0, 0, 0, 0, 0, ... n}
        // `0` means is empty, `1` is full
        staticVoxelsBuffer = new ComputeBuffer(totalVoxels, 4);

        // Clear buffer, set the staticVoxelsBuffer to be the _Voxels inside the i=0 kernel
        // that just iterates and set all the voxels to `0`
        voxelizeCompute.SetBuffer(0, "_Voxels", staticVoxelsBuffer);

        // Sends the compute shader to the GPU, to run the kernel i=0
        // 128 is the number of thread per thread group, which detemine roughly how many voxels
        // are being process by one processor
        voxelizeCompute.Dispatch(0, Mathf.CeilToInt(totalVoxels / 128.0f), 1, 1);

        // Precompute voxelized representation of the scene
        ComputeBuffer verticesBuffer, trianglesBuffer;

        // For each mesh on the list of meshes to voxelize do
        foreach (Transform child in objectsToVoxelize.GetComponentsInChildren<Transform>())
        {
            MeshFilter meshFilter = child.gameObject.GetComponent<MeshFilter>();

            if (!meshFilter) continue; // Next child
            // Function only for reading mesh data and not for writing, id the mesh that all instances shared
            Mesh sharedMesh = meshFilter.sharedMesh;

            // Creates two buffers, one to store all the vertices and one to store all the striangles of the mesh
            // 3 * sizeof(float) becase a vertex is a vector (float3), contains data for x,y and z
            verticesBuffer = new ComputeBuffer(sharedMesh.vertexCount, 3 * sizeof(float));
            verticesBuffer.SetData(sharedMesh.vertices); // Attaching vectors in 3D to the buffer
            trianglesBuffer = new ComputeBuffer(sharedMesh.triangles.Length, sizeof(int));
            trianglesBuffer.SetData(sharedMesh.triangles);

            // Setting variables on the compute shader, recall the GPU knows nothing about WTF is going on on CPU world
            voxelizeCompute.SetBuffer(1, "_StaticVoxels", staticVoxelsBuffer);                          // Enough memory for how many voxels I need
            voxelizeCompute.SetBuffer(1, "_MeshVertices", verticesBuffer);
            voxelizeCompute.SetBuffer(1, "_MeshTriangleIndices", trianglesBuffer);

            voxelizeCompute.SetVector("_VoxelResolution", new Vector3(voxelsX, voxelsY, voxelsZ));

            // Half diagonal, how much offset in the Y-axis
            voxelizeCompute.SetVector("_BoundsExtent", boundsExtent);
            voxelizeCompute.SetMatrix("_MeshLocalToWorld", child.localToWorldMatrix); // Sends the matrix that accounts for transformations from Local to World space
            voxelizeCompute.SetInt("_VoxelCount", totalVoxels);
            voxelizeCompute.SetInt("_TriangleCount", sharedMesh.triangles.Length);
            voxelizeCompute.SetFloat("_VoxelSize", voxelSize);
            voxelizeCompute.SetFloat("_IntersectionBias", intersectionBias); //? Study

            // Sends the compute shader to the GPU
            // 128 is the number of thread per thread group, which detemine roughly how many voxels
            // are being process by one processor
            int threadGroupsX = Mathf.CeilToInt(totalVoxels / 128.0f);
            // kernel CS_VoxelizeMesh 
            voxelizeCompute.Dispatch(1, threadGroupsX, 1, 1);

            // Deallocates the memory, utimetly this is called in the ~Destructor.
            verticesBuffer.Release();
            trianglesBuffer.Release();
        }

        // Memory allocation again to store all position voxel that can become smoke
        smokeVoxelsBuffer = new ComputeBuffer(totalVoxels, sizeof(int));
        smokePingVoxelsBuffer = new ComputeBuffer(totalVoxels, sizeof(int));

        //pointVoxelsBuffer = new ComputeBuffer(totalVoxels, sizeof(int));

        // Clear buffers
        voxelizeCompute.SetBuffer(0, "_Voxels", smokeVoxelsBuffer);
        voxelizeCompute.Dispatch(0, Mathf.CeilToInt(totalVoxels / 128.0f), 1, 1);
        voxelizeCompute.SetBuffer(0, "_Voxels", smokePingVoxelsBuffer);
        voxelizeCompute.Dispatch(0, Mathf.CeilToInt(totalVoxels / 128.0f), 1, 1);

        // kernel CS_Seed
        voxelizeCompute.SetBuffer(2, "_SmokeVoxels", smokeVoxelsBuffer);

        // kernel CS_FillStep
        voxelizeCompute.SetBuffer(3, "_StaticVoxels", staticVoxelsBuffer);
        voxelizeCompute.SetBuffer(3, "_SmokeVoxels", smokeVoxelsBuffer);
        voxelizeCompute.SetBuffer(3, "_PingVoxels", smokePingVoxelsBuffer);

        // kernel CS_PingPong
        voxelizeCompute.SetBuffer(4, "_Voxels", smokeVoxelsBuffer);
        voxelizeCompute.SetBuffer(4, "_PingVoxels", smokePingVoxelsBuffer);
        voxelizeCompute.SetBuffer(4, "_StaticVoxels", staticVoxelsBuffer);

        // kernel CS_QueryPosition
        voxelizeCompute.SetBuffer(5, "_Voxels", smokeVoxelsBuffer);
        //kernel CS_RayAABBIntersection //6
        voxelizeCompute.SetBuffer(6, "_Voxels", smokeVoxelsBuffer);

        #region  Obstacle buffers
        //* Instantiating obstacles
        obstacleCountAtVoxel = new ComputeBuffer(totalVoxels, sizeof(int));
        obstacleProbesPositions = new ComputeBuffer((int)settings.totalObstacleCount, sizeof(float) * 3);

        pivotsTableBuffer = new ComputeBuffer(totalVoxels, sizeof(int) * 2);
        hashedObstacleNodesBuffer = new ComputeBuffer((int)settings.totalObstacleCount, sizeof(int)*2);

        //hashTable = new ComputeBuffer((int)settings.totalObstacleCount, sizeof(uint));

        obstacleProbesPositions.SetData(settings.obstaclePositions);

        pivotsTable         = new int[totalVoxels, 2];
        hashedObstacleNodes = new int[settings.totalObstacleCount, 2]; // Mistake, was passing the total of voxels.

        for (uint p = 0; p < totalVoxels; p++)
        {
            pivotsTable[p,0] = 0;  // U (usage), it starts in 0
            pivotsTable[p,1] = -1; // T (top), the obstacle on top of the stack
        }
        for (uint ob = 0; ob < settings.totalObstacleCount; ob++) // Mistake, was passing the total of voxels.
        {
            hashedObstacleNodes[ob, 0] = -1; // V (voxel) where the obstacle is hash
            hashedObstacleNodes[ob, 1] = -1; // N (next) the index of the obstacle behind on the stack, the first-in is -1
        }

        pivotsTableBuffer.SetData(pivotsTable);
        hashedObstacleNodesBuffer.SetData(hashedObstacleNodes);

        //obstacleHashTable.SetData(hashedObstacles);

        //* Counting, pass all the values needed for computing the location
        voxelizeCompute.SetVector("_VoxelResolution", new Vector3(voxelsX, voxelsY, voxelsZ));
        voxelizeCompute.SetVector("_BoundsExtent", boundsExtent);
        voxelizeCompute.SetInt("_ObstacleProbesCount", (int)settings.totalObstacleCount);
        voxelizeCompute.SetInt("_VoxelCount", totalVoxels); // The values we need at this stage

        voxelizeCompute.SetBuffer(7, "_ObstaclesCounterVoxels", obstacleCountAtVoxel);
        voxelizeCompute.SetBuffer(7, "_ObstacleProbesPositions", obstacleProbesPositions);
        voxelizeCompute.SetBuffer(7, "_PivotsTableBuffer", pivotsTableBuffer);
        voxelizeCompute.SetBuffer(7, "_HashedObstacleNodes", hashedObstacleNodesBuffer);
        //voxelizeCompute.SetBuffer(7, "_HashTable", hashTable);

        // I was dispatching the same number of threadgroups as if they were voxels, now I am calculating the distribution with the amount of obstacles
        voxelizeCompute.Dispatch(7, Mathf.CeilToInt((int)settings.totalObstacleCount / 128.0f), 1, 1);

        Debug.Log("Dispatch kernel ID=7 to hash the obstacle");

        //pivotsTableBuffer.GetData(pivotsTable);
        //hashedObstacleNodesBuffer.GetData(hashedObstacleNodes);


        //for (uint j = 0; j < (int)settings.totalObstacleCount; j++)
        //{
            //Debug.Log($"Cell {p}, Usage (u) = { pivotsTable[p,0] }, Top (top of the stack) = { pivotsTable[p,1] }");
           // Debug.Log($"Cell {j}, Voxel (v) = { hashedObstacleNodes[j,0] }, Next (down in the stack) = { hashedObstacleNodes[j,1] }");

        //}

        #endregion  Obstacle buffers

        // Debug instancing args
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)debugMesh.GetIndexCount(0);
        args[1] = (uint)totalVoxels;
        args[2] = (uint)debugMesh.GetIndexStart(0); //? starting index location
        args[3] = (uint)debugMesh.GetBaseVertex(0); //? base vertex index
        argsBuffer.SetData(args);

    }

    private void Update()
    {
        // Define the data in CPU we need to send to the GPU
        // Vector3 point = debugPointTransformQuery.position;
        // // Set that data into the compute shader
        // voxelizeCompute.SetVector("_Point", point);

        // // Clear the buffer
        // voxelizeCompute.SetBuffer(0, "_Voxels", smokeVoxelsBuffer);
        // voxelizeCompute.Dispatch(0, Mathf.CeilToInt(totalVoxels / 128.0f), 1, 1);

        // // kernel CS_QueryPosition          // 5
        // // I am currenty using the SmokeVoxels buffer
        // //voxelizeCompute.SetBuffer(5, "_Voxels", smokeVoxelsBuffer);
        // voxelizeCompute.Dispatch(5, 1, 1, 1);

        UpdateRay();
        DebugRay();

        if (debugStaticVoxels || debugSmokeVoxels || debugEdgeVoxels)
        {
            debugVoxelMaterial.SetBuffer("_StaticVoxels", staticVoxelsBuffer);
            debugVoxelMaterial.SetBuffer("_SmokeVoxels", smokeVoxelsBuffer);


            debugVoxelMaterial.SetBuffer("_ObstaclesCounterVoxels", obstacleCountAtVoxel);
            debugVoxelMaterial.SetBuffer("_PivotsTableBuffer", pivotsTableBuffer);


            debugVoxelMaterial.SetInt("_ObstacleProbesCount", (int)settings.totalObstacleCount);
            //debugVoxelMaterial.SetBuffer("_Voxels", smokeVoxelsBuffer);

            debugVoxelMaterial.SetVector("_VoxelResolution", new Vector3(voxelsX, voxelsY, voxelsZ));
            debugVoxelMaterial.SetVector("_BoundsExtent", boundsExtent);
            debugVoxelMaterial.SetFloat("_VoxelSize", voxelSize);
            debugVoxelMaterial.SetInt("_MaxFillSteps", maxFillSteps);
            debugVoxelMaterial.SetInt("_DebugSmokeVoxels", debugSmokeVoxels ? 1 : 0);
            debugVoxelMaterial.SetInt("_DebugStaticVoxels", debugStaticVoxels ? 1 : 0);

            // https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedIndirect.html
            //! This function is now obsolete. 
            Graphics.DrawMeshInstancedIndirect(debugMesh, 0, debugVoxelMaterial, debugBounds, argsBuffer);
            // TODO Use Graphics.RenderMeshIndirect instead. Draws the same mesh multiple times using GPU instancing.
        }

        //Debug.Log("(staticVoxelsBuffer.count: " + staticVoxelsBuffer.count);


    }

    void OnDisable()
    {
        staticVoxelsBuffer.Release();
        smokeVoxelsBuffer.Release();
        smokePingVoxelsBuffer.Release();
        argsBuffer.Release();

        //* Deallocating memory
        obstacleCountAtVoxel.Release();
        obstacleProbesPositions.Release();
        pivotsTableBuffer.Release();
        hashedObstacleNodesBuffer.Release();
        //hashTable.Release();
    }


    /// <summary>
    /// Helper function to visualize the bounding volume
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(debugBounds.center, debugBounds.extents * 2);
    }

    #region ray functions

    void UpdateRay()
    {
        rayOrigin = debugPointTransformQuery.position;
        rayDirection = (debugPointB.position - debugPointTransformQuery.position).normalized;

        voxelizeCompute.SetVector("_RayOrigin", rayOrigin);
        voxelizeCompute.SetVector("_RayDirection", rayDirection);

        voxelizeCompute.SetBuffer(0, "_Voxels", smokeVoxelsBuffer);
        voxelizeCompute.Dispatch(0, Mathf.CeilToInt(totalVoxels / 128.0f), 1, 1);

        voxelizeCompute.Dispatch(6, 1, 1, 1);
    }
    private void DebugRay()
    {
        Debug.DrawLine(rayOrigin, rayOrigin + t * rayDirection);
    }

    #endregion

}
