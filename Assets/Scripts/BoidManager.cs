using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour
{

    const int threadGroupSize = 1024;

    public BoidSettings settings;
    public ComputeShader compute;
    Boid[] boids;
    ObstacleBoid[] obstacleBoids;

    void Start()
    {
        boids = FindObjectsOfType<Boid>();
        foreach (Boid b in boids)
        {
            b.Initialize(settings, null);
        }

        //Gather all the obstacles
        obstacleBoids = FindObjectsOfType<ObstacleBoid>();

    }

    void Update()
    {
        if (boids != null)
        {

            int numBoids = boids.Length;
            int numObstacleProbes = obstacleBoids.Length;

            // 1. array for the boids
            var boidData = new BoidData[numBoids];
            // 1.1 array for the boid-based obstacles, just positions;
            var obstacleData = new Vector3[numObstacleProbes];

            for (int i = 0; i < boids.Length; i++)
            {
                boidData[i].position = boids[i].position;
                boidData[i].direction = boids[i].forward;
            }

            for (int i = 0; i < obstacleBoids.Length; i++)
            {
                obstacleData[i] = obstacleBoids[i].position;
            }

            // 2. Buffer for the GPU
            var boidBuffer = new ComputeBuffer(numBoids, BoidData.Size);
            var obstacleProbesBuffer = new ComputeBuffer(numObstacleProbes, 3 * sizeof(float));

            boidBuffer.SetData(boidData);
            obstacleProbesBuffer.SetData(obstacleData);

            // 3. Set the buffer to the kernel i=0
            compute.SetBuffer(0, "boids", boidBuffer);
            compute.SetBuffer(0, "obstacles", obstacleProbesBuffer);

            compute.SetInt("numBoids", boids.Length);
            compute.SetInt("numObstacles", obstacleBoids.Length);
            compute.SetFloat("viewRadius", settings.perceptionRadius);
            compute.SetFloat("avoidRadius", settings.avoidanceRadius);

            // 4. Prepares Dispatch    
            // If we had 100 boids, ceil (100 / 1024) = 1, ciel(2000 / 1024) = 2
            // We have 1 threadGroup on the X axis
            int threadGroups = Mathf.CeilToInt(numBoids / (float)threadGroupSize);
            compute.Dispatch(0, threadGroups, 1, 1);

            // 5. Gets data back from the GPU. Recall this is a thread-blocking accion.
            // The main thread will be stopped until the data returns and is read
            boidBuffer.GetData(boidData);

            // 6. Updates the boid in the CPU
            for (int i = 0; i < boids.Length; i++)
            { // Skip 2nd
                boids[i].avgFlockHeading = boidData[i].flockHeading;
                boids[i].centreOfFlockmates = boidData[i].flockCentre;
                boids[i].avgAvoidanceHeading = boidData[i].avoidanceHeading;
                boids[i].numPerceivedFlockmates = boidData[i].numFlockmates;

                boids[i].numPerceivedObstacles = boidData[i].numObstacles;
                boids[i].avgObstacleAvoidanceHeading = boidData[i].obstacleAvoidanceHeading;

                //*NEW Boid-based obstacel avoidance
                //GatherObstacles(i);


                boids[i].UpdateBoid();
            }

            boidBuffer.Release();


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
                Debug.DrawLine(boids[boidIndex].position, boids[boidIndex].position + BoidToObstacle, Color.red);
                boids[boidIndex].numPerceivedObstacles += 1;

                //Normalized Boid-To-Obstacle vector
                Vector3 b2oN = BoidToObstacle / sqrDist;

                Vector3 normalDirectionAwayFromObstacle = b2oN;
                boids[boidIndex].avgObstacleAvoidanceHeading -= normalDirectionAwayFromObstacle;
                //boids[boidIndex].radialAttenuation += (1 / sqrDist) - (1 / (criticalDistance * criticalDistance));

            }

        }
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
        public int numObstacles; //* NEW

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