# Session notes

## Session 2024-11-9: Breakdown of the code

**Objectives:** Identify and be able to answer
- What the entry point?
- How is the communication between the CPU and the GPU being handle? (Back and forward)
- How are they handling obstacle avoidance?
- Whats the breaking point of the system in terms of number of boids?
- What/where is the potential bottle neck?

### Entry points:
- Spwner, then boid, then manager

### CPU - GPU
- BoidManager prepares a buffer with a copy of all boids and sends to the GPU
- BoidCompute computes neighbours and accumulators
- BoidManager reads back and passes accumulator to each boid
- Each boid updates the steering forces

### Obstacle avoidance
- Sphere Casting along a ray, physics engine. Is casts 300 rays in front. Potential complexity is $$O(r*m)$$ where $$r$$ are the ray and $$m$$ the colliders to check for
- This is a potential bottle neck as it might have a time complexity of $$O(n*r*m)$$ where $$n$$ are the boids.

### Evaluation
$$n$$ Boids count, with collision avoidance

| $$n$$ |   fps     |
|:===== |:=====     |
| 100   |   ~350    |
| 1000  |   ~90     |
| 10000 |   ~5      |

$$n$$ Boids count, without collision avoidance

| $$n$$ |   fps     |
|:===== |:=====     |
| 100   |   ~450    |
| 1000  |   ~150    |
| 10000 |   ~15     |

Obstacle avoidance is definitely a bottle neck