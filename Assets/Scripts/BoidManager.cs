using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour {

    const int threadGroupSize = 1024;

    public BoidSettings settings;
    public ComputeShader compute;
    Boid[] boids;

    void Start () {
        boids = FindObjectsOfType<Boid> ();
        foreach (Boid b in boids) {
            b.Initialize (settings, null);
        }

    }

    void Update () {
        if (boids != null) {

            int numBoids = boids.Length;

            // 1. array for the boids
            var boidData = new BoidData[numBoids];

            for (int i = 0; i < boids.Length; i++) {
                boidData[i].position = boids[i].position;
                boidData[i].direction = boids[i].forward;
            }

            // 2. Buffer for the GPU
            var boidBuffer = new ComputeBuffer (numBoids, BoidData.Size);
            boidBuffer.SetData (boidData);

            // 3. Set the buffer to the kernel i=0
            compute.SetBuffer (0, "boids", boidBuffer);
            compute.SetInt ("numBoids", boids.Length);
            compute.SetFloat ("viewRadius", settings.perceptionRadius);
            compute.SetFloat ("avoidRadius", settings.avoidanceRadius);

            // 4. Prepares Dispatch    
            // If we had 100 boids, ceil (100 / 1024) = 1, ciel(2000 / 1024) = 2
            // We have 1 threadGroup on the X axis
            int threadGroups = Mathf.CeilToInt (numBoids / (float) threadGroupSize);
            compute.Dispatch (0, threadGroups, 1, 1);

            // 5. Gets data back from the GPU. Recall this is a thread-blocking accion.
            // The main thread will be stopped until the data returns and is read
            boidBuffer.GetData (boidData);

            // 6. Updates the boid in the CPU
            for (int i = 0; i < boids.Length; i++) {
                boids[i].avgFlockHeading = boidData[i].flockHeading;
                boids[i].centreOfFlockmates = boidData[i].flockCentre;
                boids[i].avgAvoidanceHeading = boidData[i].avoidanceHeading;
                boids[i].numPerceivedFlockmates = boidData[i].numFlockmates;

                boids[i].UpdateBoid ();
            }

            boidBuffer.Release ();
        }
    }

    /// <summary>
    /// Create the simply version of the boid to sent to the GPU
    /// </summary>
    public struct BoidData {
        public Vector3 position;
        public Vector3 direction;

        public Vector3 flockHeading;
        public Vector3 flockCentre;
        public Vector3 avoidanceHeading;
        public int numFlockmates;

        // Computes the stride of this struct
        public static int Size {
            get {
                // 5 vectors, each holds 3 componentes {x,y,z}
                // an integer
                return sizeof (float) * 3 * 5 + sizeof (int);
            }
        }
    }
}