using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Draws the combined coverage of all buildings as one opaque area.
    // Each building contributes a filled circle (a triangle fan on the ground plane).
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RangeDisplay : SpokeBehaviour {

        [Header("References")]
        [SerializeField] MeshFilter meshFilter;
        [SerializeField] MeshRenderer meshRenderer;

        [Header("Attributes")]
        [SerializeField, Range(8, 128)] int segmentsPerCircle = 48;
        [SerializeField] float yOffset = 0.02f; // lift off the ground to avoid z-fighting

        [Header("Inputs")]
        [SerializeField] UState<List<Building>> buildings = new();
        [SerializeField] UState<Color> colour = new();

        public IState<List<Building>> Buildings => buildings;
        public IState<Color> Colour => colour;

        protected override void Init(EffectBuilder s) {

            var mesh = s.Effect(InitMesh);

            s.Effect(s => {
                var meshNow = s.D(mesh);
                if (meshNow == null) return;
                s.Effect(SyncMeshGeom(meshNow));
            });
        }

        EffectBlock<Mesh> InitMesh => s => {
            if (meshFilter == null || meshRenderer == null) return null;

            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var mesh = new Mesh { name = "RangeArea" };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;

            var material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.sharedMaterial = material;

            s.Effect(s => {
                var colourNow = s.D(colour);
                colourNow.a = 1f;
                material.color = colourNow;
            });

            s.OnCleanup(() => {
                SafeDestroy(mesh);
                SafeDestroy(material);
            });

            return State.Create(mesh);
        };

        EffectBlock SyncMeshGeom(Mesh mesh) => s => {
            var buildingsNow = s.D(buildings);
            var colourNow = s.D(colour);

            if (buildingsNow == null || buildingsNow.Count == 0) return;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            foreach (var building in buildingsNow) {
                if (building.Range <= 0f) return;

                var center = verts.Count;
                verts.Add(transform.InverseTransformPoint(building.transform.position));

                var ringStart = verts.Count;
                for (var i = 0; i < segmentsPerCircle; i++) {
                    var a = (i / (float)segmentsPerCircle) * Mathf.PI * 2f;
                    var world = building.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * building.Range;
                    verts.Add(transform.InverseTransformPoint(world));
                }

                for (var i = 0; i < segmentsPerCircle; i++) {
                    tris.Add(center);
                    tris.Add(ringStart + i);
                    tris.Add(ringStart + (i + 1) % segmentsPerCircle);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            s.OnCleanup(() => {
                mesh.Clear();
            });
        };

        static void SafeDestroy(Object o) {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
