using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Updates the simulation of the Boid, steering forces of cohession, separation and alignment.
/// Constanly checks for obstacles in front, and computes a random save route for scape
/// </summary>
public class Boid : MonoBehaviour
{

    BoidSettings settings;
    //* Testing
    public bool hasObstacleAvoidance = false;

    // State
    [HideInInspector]
    public Vector3 position;
    [HideInInspector]
    public Vector3 forward;
    Vector3 velocity;

    // To update:
    Vector3 acceleration;

    /// <summary>
    /// GPU driven variable, updated by the Compute shader
    /// </summary>
    [HideInInspector]
    public Vector3 avgFlockHeading;

    /// <summary>
    /// GPU driven variable, updated by the Compute shader
    /// </summary>
    [HideInInspector]
    public Vector3 avgAvoidanceHeading;

    /// <summary>
    /// GPU driven variable, updated by the Compute shader
    /// </summary>
    [HideInInspector]
    public Vector3 centreOfFlockmates;

    /// <summary>
    /// GPU driven variable, updated by the Compute shader
    /// </summary>
    [HideInInspector]
    public int numPerceivedFlockmates;

    //* NEW boid-base obstacle
    public Vector3 avgObstacleAvoidanceHeading;
    public int numPerceivedObstacles = 0;
    public float radialAttenuation = 0.0f;

    // Cached
    Material material;
    Transform cachedTransform;
    Transform target;

    void Awake()
    {
        material = transform.GetComponentInChildren<MeshRenderer>().material;
        cachedTransform = transform;
    }

    public void Initialize(BoidSettings settings, Transform target)
    {
        this.target = target;
        this.settings = settings;

        position = cachedTransform.position;
        forward = cachedTransform.forward;

        float startSpeed = (settings.minSpeed + settings.maxSpeed) / 2; // At half
        velocity = transform.forward * startSpeed;
    }

    public void SetColour(Color col)
    {
        if (material != null)
        {
            material.color = col;
        }
    }

    public void UpdateBoid()
    {
        Vector3 acceleration = Vector3.zero;

        if (target != null)
        {
            Vector3 offsetToTarget = (target.position - position);
            acceleration = SteerTowards(offsetToTarget) * settings.targetWeight;
        }


        if (numPerceivedFlockmates != 0)
        { // GPU driven variable
            centreOfFlockmates /= numPerceivedFlockmates;

            Vector3 offsetToFlockmatesCentre = (centreOfFlockmates - position); // GPU driven variable

            var alignmentForce = SteerTowards(avgFlockHeading) * settings.alignWeight; // GPU driven variable
            var cohesionForce = SteerTowards(offsetToFlockmatesCentre) * settings.cohesionWeight;
            var seperationForce = SteerTowards(avgAvoidanceHeading) * settings.seperateWeight; // GPU driven variable

            acceleration += alignmentForce;
            acceleration += cohesionForce;
            acceleration += seperationForce;
        }

        //* Testing hasObstacleAvoidance
        if (hasObstacleAvoidance && IsHeadingForCollision())
        {
            Vector3 collisionAvoidDir = ObstacleRays();
            Vector3 collisionAvoidForce = SteerTowards(collisionAvoidDir) * settings.avoidCollisionWeight;
            acceleration += collisionAvoidForce;
        }

        //* NEW boid-based obstacle
        //acceleration += ApplyObstacleAvoidanceForce();
        //Debug.Log($"numPerceivedObstacles: {numPerceivedObstacles}");
        if (numPerceivedObstacles > 0)
        {
            var obstacleSeperationForce = SteerTowards(avgObstacleAvoidanceHeading) * settings.amplitud/*settings.seperateWeight*/;
            acceleration += obstacleSeperationForce;
        }

        

        velocity += acceleration * Time.deltaTime;
        float speed = velocity.magnitude;
        Vector3 dir = velocity / speed;
        speed = Mathf.Clamp(speed, settings.minSpeed, settings.maxSpeed);
        velocity = dir * speed;

        cachedTransform.position += velocity * Time.deltaTime;
        cachedTransform.forward = dir;
        position = cachedTransform.position;
        forward = dir;

        //numPerceivedObstacles = 0; // This number is re-set on the GPU
    }

    bool IsHeadingForCollision()
    {
        RaycastHit hit;
        // Casts a sphere along a ray and returns detailed information on what was hit.
        // bool True when the sphere sweep intersects any collider, otherwise false.
        // Meaning for every boid, and for every collider in the scene, thus O(n*m) `n` boids and `m` colliders. Thinking that boids
        // Have no collider, other wize, it becomes O(n^2).

        // Recall: A Layer mask that is used to selectively ignore colliders when casting a capsule.
        if (Physics.SphereCast(position, settings.boundsRadius, forward, out hit, settings.collisionAvoidDst, settings.obstacleMask))
        {
            return true;
        }
        else { }
        return false;
    }

    /// <summary>
    /// Returns a direction where is save to fly
    /// Complexity O(n*m) n = number of rays (300), m = number of colliders in the scene
    /// </summary>
    /// <returns></returns>
    Vector3 ObstacleRays()
    {
        Vector3[] rayDirections = BoidHelper.directions;

        for (int i = 0; i < rayDirections.Length; i++)
        {
            Vector3 dir = cachedTransform.TransformDirection(rayDirections[i]);
            Ray ray = new Ray(position, dir);
            if (!Physics.SphereCast(ray, settings.boundsRadius, settings.collisionAvoidDst, settings.obstacleMask))
            {
                return dir;
            }
        }

        return forward;
    }

    Vector3 SteerTowards(Vector3 vector)
    {
        Vector3 v = vector.normalized * settings.maxSpeed - velocity;
        return Vector3.ClampMagnitude(v, settings.maxSteerForce);
    }

    //* NEW Boid-based obstacle avoidance
    Vector3 ApplyObstacleAvoidanceForce()
    {
        Vector3 obstacleSeperationForce = new Vector3(0, 0, 0);
        //Debug.Log("numPerceivedObstacles: " + numPerceivedObstacles);
        if (numPerceivedObstacles != 0)
        {

            obstacleSeperationForce = SteerTowards(avgObstacleAvoidanceHeading) * settings.amplitud/*settings.seperateWeight*/;
            //Debug.DrawLine(position, position + obstacleSeperationForce, Color.green);

            return obstacleSeperationForce;
            //numPerceivedObstacles = 0;
        }
        return obstacleSeperationForce;

    }

}