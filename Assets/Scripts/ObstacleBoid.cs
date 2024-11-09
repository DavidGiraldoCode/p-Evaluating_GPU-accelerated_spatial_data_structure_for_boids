using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObstacleBoid : MonoBehaviour
{
    public Vector3 position;

    [Tooltip("How large the Obstacle is")]
    public Vector3 obstacleExtend = new Vector3(1.0f, 1.0f, 1.0f);

    //[Range(0.1f, 5.0f)]
    //public float scalarExtend = 1.0f;

    private void Awake()
    {
    }

    private void Update()
    {
        //Debug.Log("transform.localScale: " + transform.localScale);
        obstacleExtend = transform.localScale * 0.25f;
        position = transform.position;
        Debug.DrawLine(position, position + obstacleExtend, Color.red);
    }
}
