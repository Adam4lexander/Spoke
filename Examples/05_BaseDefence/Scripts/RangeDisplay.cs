using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Renders a set of circles as one merged opaque coverage area.
    // It creates and owns its own GameObject + mesh + material, all torn down on cleanup,
    // and rebuilds the mesh whenever the circles signal changes.
    public static class RangeDisplay {

        const int segmentsPerCircle = 48;

        public static EffectBlock Draw(ISignal<List<Circle>> circles, ISignal<Color> colour) => s => {

            var go = new GameObject("RangeDisplay");
            go.transform.position = Vector3.up * 0.01f;
            s.Effect("WithSafeDestroy", WithSafeDestroy(go));

            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var mesh = new Mesh { name = "RangeArea" };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;
            s.Effect("WithSafeDestroy", WithSafeDestroy(mesh));

            var material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.sharedMaterial = material;
            s.Effect("WithSafeDestroy", WithSafeDestroy(material));

            s.Effect(s => {
                var colourNow = s.D(colour);
                colourNow.a = 1f;
                material.color = colourNow;
            });

            s.Effect("SyncMeshGeom", SyncMeshGeom(mesh, circles));
        };

        static EffectBlock WithSafeDestroy(Object obj) => s => {
            s.OnCleanup(() => {
                if (obj == null) return;
                if (Application.isPlaying) Object.Destroy(obj);
                else Object.DestroyImmediate(obj);
            });
        };

        static EffectBlock SyncMeshGeom(Mesh mesh, ISignal<List<Circle>> circles) => s => {
            var circlesNow = s.D(circles);
            if (circlesNow == null || circlesNow.Count == 0) return;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            foreach (var circle in circlesNow) {
                if (circle.Radius <= 0f) return;

                var center = verts.Count;
                verts.Add(circle.Center);

                var ringStart = verts.Count;
                for (var i = 0; i < segmentsPerCircle; i++) {
                    var a = (i / (float)segmentsPerCircle) * Mathf.PI * 2f;
                    var world = circle.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * circle.Radius;
                    verts.Add(world);
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

            s.OnCleanup(() => mesh.Clear());
        };
    }
}
