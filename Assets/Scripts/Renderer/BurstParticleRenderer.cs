using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using static Unity.Mathematics.math;
using Shape = CustomParticleSystem.ParticleSpawner.Shape;

namespace CustomParticleSystem
{
    using System.Runtime.CompilerServices;
    using Unity.Mathematics;

    public class BurstParticleRenderer : ParticleRenderer
    {
        private NativeArray<BurstParticle> Particles;
        private NativeArray<BurstPlane> Planes;

        private Bounds Bounds = new Bounds(Vector3.zero, 10000 * Vector3.one);
        private ComputeBuffer InstancesBuffer;
        private ComputeBuffer ArgsBuffer;
        private InstanceData[] InstancesData;
        private int MaximumParticles;
        private bool ObstaclesInit;

        public BurstParticleRenderer(Mesh mesh, Material material) : base(mesh, material) { }

        public override void SetMaximumParticles(int maxParticles)
        {
            MaximumParticles = maxParticles;
            if (Particles.IsCreated) Particles.Dispose();
            Particles = new NativeArray<BurstParticle>(MaximumParticles, Allocator.Persistent);
            InstancesData = new InstanceData[MaximumParticles];
            InitBuffers();
        }

        public override void SetProperties(float mass, float radius, float bouncing)
        {
            Particle.Mass = mass;
            Particle.Radius = radius;
            Particle.Bouncing = bouncing;
        }

        private void InitBuffers()
        {
            ReleaseBuffers();
            uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
            args[0] = (uint)Mesh.GetIndexCount(0);
            args[1] = (uint)MaximumParticles;
            args[2] = (uint)Mesh.GetIndexStart(0);
            args[3] = (uint)Mesh.GetBaseVertex(0);
            ArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            ArgsBuffer.SetData(args);

            InstancesBuffer = new ComputeBuffer(MaximumParticles, InstanceData.Size());
            InstancesBuffer.SetData(new InstanceData[MaximumParticles]);
            Material.SetBuffer("_PerInstanceData", InstancesBuffer);
        }

        public override void UpdateInstances()
        {
            for (int i = 0; i < MaximumParticles; i++)
            {
                if (Particles[i].LifeTime > 0)
                {
                    InstancesData[i] = new InstanceData(Particles[i].Position, Quaternion.identity, new Vector3(Particle.Radius * 2.0f, Particle.Radius * 2.0f, Particle.Radius * 2.0f));
                }
                else
                {
                    InstancesData[i] = new InstanceData(Vector3.zero, Quaternion.identity, Vector3.zero);
                }
            }
            InstancesBuffer.SetData(InstancesData);
        }

        public override void Render(bool shadows)
        {
            Graphics.DrawMeshInstancedIndirect(mesh: Mesh,
                                               submeshIndex: 0,
                                               material: Material,
                                               bounds: Bounds,
                                               bufferWithArgs: ArgsBuffer,
                                               castShadows: shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                                               receiveShadows: shadows);
        }

        public override void SolveMovement(Solver solver, float deltaTime, float kVerlet)
        {
            var job = new VerletSolver
            {
                Particles = Particles,
                KVerlet = kVerlet,
                DeltaTime = deltaTime,
                Mass = Particle.Mass
            };

            JobHandle handle = job.Schedule(Particles.Length, 64);
            handle.Complete(); // ensure job has finished
        }

        public override void SolveCollisions(Obstacle[] obstacles, float deltaTime)
        {
            UpdateObstaclesArray(obstacles);

            var job = new SolveCollisionsBurst
            { 
                Particles = Particles,
                Planes = Planes,
                DeltaTime = deltaTime,
                ParticleBouncing = Particle.Bouncing,
                ParticleRadius = Particle.Radius
            };

            JobHandle handle = job.Schedule(Particles.Length, 64);
            handle.Complete(); // ensure job has finished
        }

        private void UpdateObstaclesArray(Obstacle[] obstacles)
        {
            if (!ObstaclesInit)
            {
                int planeCount = 0;
                int sphereCount = 0;
                int triangleCount = 0;
                foreach (Obstacle o in obstacles)
                {
                    if (o is Plane) planeCount += 1;
                    else if (o is Sphere) sphereCount += 1;
                    else if (o is Triangle) triangleCount += 1;
                    else if (o is Cube) o.GetComponent<MeshRenderer>().enabled = false;
                }
                Planes = new NativeArray<BurstPlane>(planeCount, Allocator.Persistent);
                ObstaclesInit = true;
            }
            int planeIndex = 0;
            int sphereIndex = 0;
            int triangleIndex = 0;
            foreach (Obstacle o in obstacles)
            {
                if (o is Plane)
                {
                    Plane p = o as Plane;
                    Planes[planeIndex] = new BurstPlane(p.Normal, p.D, p.Friction);
                    planeIndex += 1;
                }
            }
        }

        private int LastIndexSearchAvailable = 0;
        private int FindFirstAvailableParticle()
        {
            for (int i = LastIndexSearchAvailable; i < Particles.Length; i++)
            {
                LastIndexSearchAvailable = i + 1;
                if (Particles[i].LifeTime <= 0.0f) return i;
            }
            return -1;
        }

        public override void SpawnParticle(ParticleSpawner spawner)
        {
            int index = FindFirstAvailableParticle();
            if (index != -1)
            {
                // Emission Shape
                Vector3 spawnPos = new Vector3();
                Vector3 velocitySpawn = new Vector3();
                switch (spawner.EmissionShape)
                {
                    case Shape.Point:
                        spawnPos = spawner.transform.position;
                        break;
                    case Shape.Explosion:
                        float alpha = (360.0f * (UnityEngine.Random.value - 0.5f)) * Mathf.Deg2Rad;
                        float beta = (180.0f * (UnityEngine.Random.value - 0.5f)) * Mathf.Deg2Rad;
                        spawnPos = new Vector3(Mathf.Cos(alpha) * Mathf.Cos(beta), Mathf.Sin(beta), Mathf.Sin(alpha) * Mathf.Cos(beta));
                        velocitySpawn = spawner.EmissionExplosionSpeed * spawnPos;
                        spawnPos = spawnPos * 0.01f + spawner.transform.position;
                        break;
                    case Shape.Fountain:
                        spawnPos = spawner.transform.position;
                        velocitySpawn = new Vector3(UnityEngine.Random.value - 0.5f, spawner.EmissionFountainSpeed, UnityEngine.Random.value - 0.5f);
                        break;
                    case Shape.Waterfall:
                        spawnPos = spawner.transform.position + Vector3.up * spawner.EmissionWaterfallHeight;
                        velocitySpawn = new Vector3(UnityEngine.Random.value - 0.5f, -spawner.EmissionWaterfallSpeed, UnityEngine.Random.value - 0.5f);
                        break;
                    case Shape.SemiSphere:
                        alpha = (360.0f * (UnityEngine.Random.value - 0.5f)) * Mathf.Deg2Rad;
                        beta = (90.0f * UnityEngine.Random.value) * Mathf.Deg2Rad;
                        spawnPos = new Vector3(Mathf.Cos(alpha) * Mathf.Cos(beta), Mathf.Sin(beta), Mathf.Sin(alpha) * Mathf.Cos(beta));
                        velocitySpawn = spawner.EmissionSemiSphereSpeed * spawnPos;
                        spawnPos = spawnPos * spawner.EmissionSemiSphereRadius + spawner.transform.position;
                        break;
                }
                // Init Particle
                Particles[index] = new BurstParticle(spawnPos, velocitySpawn, spawner.ParticleMass * new Vector3(0.0f, -spawner.Gravity, 0.0f), spawner.ParticleLifeTime, spawner.SimulationTimestep);
            }
        }

        private void ReleaseBuffers()
        {
            if (InstancesBuffer != null)
            {
                InstancesBuffer.Release();
                InstancesBuffer = null;
            }
            if (ArgsBuffer != null)
            {
                ArgsBuffer.Release();
                ArgsBuffer = null;
            }
        }

        public override void Release()
        {
            ReleaseBuffers();
            if (Particles.IsCreated) Particles.Dispose();
            if (Planes.IsCreated) Planes.Dispose();
        }

        private struct BurstParticle
        {
            public float3 Position;
            public float3 Velocity;
            public float3 PreviousPosition;
            public float3 PreviousVelocity;
            public float3 Force;
            public float LifeTime;

            public BurstParticle(float3 position, float3 velocity, float3 force, float lifeTime, float deltaTime)
            {
                Position = position;
                PreviousPosition = Position - velocity * deltaTime;
                Velocity = velocity;
                PreviousVelocity = velocity;
                Force = force;
                LifeTime = lifeTime;
            }
        }

        private struct BurstPlane
        {
            public float3 N;
            public float D;
            public float Friction;

            public BurstPlane(float3 n, float d, float friction)
            {
                N = n;
                D = d;
                Friction = friction;
            }
        }

        [BurstCompile]
        private struct VerletSolver : IJobParallelFor
        {
            public NativeArray<BurstParticle> Particles;
            public float KVerlet;
            public float DeltaTime;
            public float Mass;

            public void Execute(int index)
            {
                BurstParticle particle = Particles[index];
                if (particle.LifeTime > 0.0f)
                {
                    float3 previousPreviousPosition = particle.PreviousPosition;
                    particle.PreviousPosition = particle.Position;
                    particle.PreviousVelocity = particle.Velocity;
                    particle.Position = particle.PreviousPosition + KVerlet * (particle.PreviousPosition - previousPreviousPosition) + ((DeltaTime * DeltaTime) * particle.Force) / Mass;
                    particle.Velocity = (particle.Position - particle.PreviousPosition) / DeltaTime;
                    particle.LifeTime -= DeltaTime;
                    if (particle.LifeTime < 0.0f) particle.LifeTime = 0.0f;
                    Particles[index] = particle;
                }
            }
        }

        [BurstCompile]
        private struct SolveCollisionsBurst : IJobParallelFor
        {
            public NativeArray<BurstParticle> Particles;
            [ReadOnly] public NativeArray<BurstPlane> Planes;
            public float DeltaTime;
            public float ParticleRadius;
            public float ParticleBouncing;

            public void Execute(int index)
            {
                BurstParticle p = Particles[index];
                if (p.LifeTime > 0.0f)
                {
                    bool collision = false;
                    // Planes
                    for (int j = 0; j < Planes.Length; j++)
                    {
                        BurstPlane plane = Planes[j];
                        if (IsCrossingPlane(p, plane.N, plane.D))
                        {
                            p = CollisionPlaneParticle(p, plane.N, plane.D, plane.Friction);
                            collision = true;
                        }
                    }
                    // Spheres
                    // Triangles
                    if (collision) p.PreviousPosition = p.Position - p.Velocity * DeltaTime;
                    Particles[index] = p;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsCrossingPlane(BurstParticle p, float3 N, float d)
            {
                float sign = dot(p.Position - N * ParticleRadius, N) + d;
                sign *= dot(p.PreviousPosition - N * ParticleRadius, N) + d;
                return sign <= 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private BurstParticle CollisionPlaneParticle(BurstParticle p, float3 N, float d, float friction)
            {
                float dotVN = dot(p.Velocity, N);

                // Friction
                float3 velocityNormal = dotVN * N;
                float3 velocityTangent = p.Velocity - velocityNormal;

                // Elastic collision
                p.Velocity -= (1 + ParticleBouncing) * velocityNormal;

                // Apply friction
                p.Velocity -= friction * velocityTangent;

                // Update position
                p.Position -= (1 + ParticleBouncing) * (dot(p.Position - N * ParticleRadius, N) + d) * N;

                return p;
            }
        }
    }
}