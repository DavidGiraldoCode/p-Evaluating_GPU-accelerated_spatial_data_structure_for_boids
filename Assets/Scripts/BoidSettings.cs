using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

[CreateAssetMenu]
public class BoidSettings : ScriptableObject
{
    // Settings
    public float minSpeed = 2;
    public float maxSpeed = 5;
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1;
    public float maxSteerForce = 3;

    public float alignWeight = 1;
    public float cohesionWeight = 1;
    public float seperateWeight = 1;

    public float targetWeight = 1;

    [Header("Collisions")]
    public LayerMask obstacleMask;
    public float boundsRadius = .27f;
    public float avoidCollisionWeight = 10;
    public float collisionAvoidDst = 5;

    [Header("New Obstacle Avoidance")]
    public float amplitud = 0.0f;
    public uint totalObstacleCount = 0;
    public List<Vector3> obstaclePositions = new List<Vector3>();

}