using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Draws the combined coverage of all buildings as one opaque area.
    // Each building contributes a filled circle (a triangle fan on the ground plane).
    [ExecuteAlways]
    public class RangeDisplay : SpokeBehaviour {

        [Header("References")]
        [SerializeField] UState<MeshFilter> meshFilter;
        [SerializeField] UState<MeshRenderer> meshRenderer;

        [Header("Attributes")]
        [SerializeField] UState<int> segmentsPerCircle = new(48);

        [Header("Inputs")]
        [SerializeField] UState<List<Building>> buildings = new();
        [SerializeField] UState<Color> colour = new();

        protected override void Init(EffectBuilder s) {

            var mesh = s.Effect(InitMesh);

            s.Phase(IsEnabled, s => {
                var meshNow = s.D(mesh);
                if (meshNow == null) return;
                s.Effect(SyncMeshGeom(meshNow));
            });
        }

        EffectBlock<Mesh> InitMesh => s => {
            var meshFilterNow = s.D(meshFilter);
            var meshRendererNow = s.D(meshRenderer);

            if (meshFilterNow == null || meshRendererNow == null) return null;

            meshRendererNow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRendererNow.receiveShadows = false;

            var mesh = new Mesh { name = "RangeArea" };
            mesh.MarkDynamic();
            meshFilterNow.sharedMesh = mesh;
            s.OnCleanup(() => {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            });

            var material = new Material(Shader.Find("Sprites/Default"));
            meshRendererNow.sharedMaterial = material;
            s.OnCleanup(() => {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            });

            s.Effect(s => {
                var colourNow = s.D(colour);
                colourNow.a = 1f;
                material.color = colourNow;
            });

            return State.Create(mesh);
        };

        EffectBlock SyncMeshGeom(Mesh mesh) => s => {
            if (s.D(buildings) == null || s.D(buildings).Count == 0) return;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            foreach (var building in s.D(buildings)) {
                if (s.D(building.Range) <= 0f) return;

                var center = verts.Count;
                verts.Add(transform.InverseTransformPoint(building.transform.position));

                var ringStart = verts.Count;
                for (var i = 0; i < s.D(segmentsPerCircle); i++) {
                    var a = (i / (float)s.D(segmentsPerCircle)) * Mathf.PI * 2f;
                    var world = building.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * s.D(building.Range);
                    verts.Add(transform.InverseTransformPoint(world));
                }

                for (var i = 0; i < s.D(segmentsPerCircle); i++) {
                    tris.Add(center);
                    tris.Add(ringStart + i);
                    tris.Add(ringStart + (i + 1) % s.D(segmentsPerCircle));
                }
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            s.OnCleanup(() => mesh.Clear());
        };
    }
}
