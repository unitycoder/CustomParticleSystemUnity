using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CustomParticleSystem;
using System;

namespace CustomStringSystem
{
    public class StringSpawner : MonoBehaviour
    {
        [Tooltip("Simulation timestep (independent from the rendering timestep)")]
        public float SimulationTimestep = 1.0f / 60.0f;
        public bool EnableInput;
        public Solver StringSolver;
        [Range(0.95f, 1.0f)] public float KVerlet = 1.0f;
        public int NumberParticles;
        public float DistanceBetweenParticles;
        public Vector3 InitParticleSpawnDir;
        public bool[] FixedParticles;
        public float Gravity = 9.81f;
        public float Damping = 1.00f;
        public float Elasticity = 1.0f;
        [Range(0.0f, 1.0f)] public float ParticleBouncing = 0.2f;
        public float ParticleMass = 1.0f;
        public float ParticleRadius = 0.05f;
        public Mesh ParticleMesh;
        public Material ParticleMaterial;
        public bool Shadows;

        private Obstacle[] Obstacles;
        private float AccumulatedDeltaTime = 0.0f;
        private int LastNumberParticles;
        private BurstStringRenderer StringRenderer;
        private bool IsMovingParticle;
        private int MovingParticleIndex;
        private Camera MainCamera;

        private void Awake()
        {
            MainCamera = Camera.main;
        }

        private IEnumerator Start()
        {
            // Init variables
            LastNumberParticles = NumberParticles;
            // Wait one frame for the Destroy() calls to be done (objects bad inited)
            enabled = false;
            yield return null;
            enabled = true;
            // Find obstacles
            Obstacles = FindObjectsOfType<Obstacle>();
            Array.Sort(Obstacles, (a, b) => a.GetPriority() > b.GetPriority() ? -1 : 1);
            // Renderer
            StringRenderer = new BurstStringRenderer(this);
            UpdateMaximumParticles();
            // Application framerate
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
        }

        private void Update()
        {
            // Check Maximum Number of Particles
            if (NumberParticles != LastNumberParticles)
            {
                UpdateMaximumParticles();
                LastNumberParticles = NumberParticles;
            }

            // Update particles
            float deltaTime = Time.deltaTime + AccumulatedDeltaTime;
            while (deltaTime >= SimulationTimestep)
            {
                float deltaTimeStep = SimulationTimestep;
                deltaTime -= deltaTimeStep;

                StringRenderer.SolveForces(deltaTimeStep);
                StringRenderer.SolveMovement(StringSolver, deltaTimeStep, KVerlet);
                StringRenderer.SolveCollisions(Obstacles, deltaTimeStep);
            }

            // Input
            if (!EnableInput) return;
            if (Input.GetKeyDown(KeyCode.F))
            {
                FixParticle();
            }
            if (Input.GetMouseButtonDown(0))
            {
                StartMovingParticle();
            }
            else if (Input.GetMouseButtonUp(0))
            {
                IsMovingParticle = false;
            }
            if (IsMovingParticle)
            {
                MoveParticle();
            }

            // Render
            StringRenderer.UpdateInstances();
            StringRenderer.Render(Shadows);

            // Update Variables
            AccumulatedDeltaTime = deltaTime;
        }

        private void StartMovingParticle()
        {
            Ray line = MainCamera.ScreenPointToRay(Input.mousePosition);
            for (int i = 0; i < NumberParticles; i++)
            {
                if (TestSphereLineIntersection(i, line.origin, line.direction))
                {
                    MovingParticleIndex = i;
                    IsMovingParticle = true;
                    break;
                }
            }
        }

        private void MoveParticle()
        {
            Vector3 planePos = StringRenderer.GetParticlePosition(MovingParticleIndex);
            Ray line = MainCamera.ScreenPointToRay(Input.mousePosition);
            Vector3 planeNormal = -line.direction;
            Vector3 pos = Vector3.ProjectOnPlane(line.origin, planeNormal) + Vector3.Dot(planePos, planeNormal) * planeNormal;
            StringRenderer.SetParticlePosition(MovingParticleIndex, pos);
        }

        private void FixParticle()
        {
            Ray line = MainCamera.ScreenPointToRay(Input.mousePosition);
            for (int i = 0; i < NumberParticles; i++)
            {
                if (TestSphereLineIntersection(i, line.origin, line.direction))
                {
                    FixedParticles[i] = !FixedParticles[i];
                }
            }
        }

        private bool TestSphereLineIntersection(int indexParticle, Vector3 lineStart, Vector3 lineDir)
        {
            Vector3 particlePos = StringRenderer.GetParticlePosition(indexParticle);
            float a = Vector3.Dot(lineDir, (lineStart - particlePos));
            float l = Vector3.Magnitude(lineStart - particlePos);
            float d = a * a - (l * l - ParticleRadius * ParticleRadius);
            return d >= 0.0f;
        }

        private void UpdateMaximumParticles()
        {
            StringRenderer.SetMaximumParticles(NumberParticles, InitParticleSpawnDir);
        }

        private void OnDestroy()
        {
            if (StringRenderer != null) StringRenderer.Release();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
            {
                Vector3 dirNormalized = InitParticleSpawnDir.normalized;
                for (int i = 0; i < NumberParticles; i++)
                {
                    Gizmos.color = FixedParticles[i] ? Color.red : Color.blue;
                    Vector3 pos = transform.position + dirNormalized * DistanceBetweenParticles * i;
                    Gizmos.DrawWireSphere(pos, ParticleRadius);
                }
            }
        }
#endif

        public enum Solver
        {
            EulerOrig,
            EulerSemi,
            Verlet
        }
    }
}