using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Renders a set of line segments as one line mesh. Like CoverageDisplay it creates and owns its own
    // GameObject + mesh + material, all torn down on cleanup, and rebuilds whenever the segments change.
    public static class LinkDisplay {

        // Shows the node's parent chain up to the root — a line from each node to the provider
        // powering it. Re-walks whenever any parent along the chain changes.
        public static EffectBlock Draw(PowerNode start, ISignal<Color> colour) => s => {
            var segments = s.Memo(s => {
                var list = new List<(Vector3 from, Vector3 to)>();
                var node = start;
                while (node != null) {
                    var parent = s.D(node.Parent);
                    if (parent == null) break;
                    list.Add((node.transform.position, parent.transform.position));
                    node = parent;
                }
                return list;
            });
            s.Effect(DrawSegments(segments, colour));
        };

        static EffectBlock DrawSegments(ISignal<List<(Vector3 from, Vector3 to)>> segments, ISignal<Color> colour) => s => {

            var go = new GameObject("LinkDisplay");
            go.transform.position = Vector3.up * 0.01f;
            s.Effect("WithSafeDestroy", WithSafeDestroy(go));

            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var mesh = new Mesh { name = "PowerLinks" };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;
            s.Effect("WithSafeDestroy", WithSafeDestroy(mesh));

            var material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.sharedMaterial = material;
            s.Effect("WithSafeDestroy", WithSafeDestroy(material));

            s.Effect(s => material.color = s.D(colour));

            s.Effect("BuildLines", s => BuildLines(mesh, s.D(segments)));
        };

        static EffectBlock WithSafeDestroy(Object obj) => s => {
            s.OnCleanup(() => {
                if (obj == null) return;
                if (Application.isPlaying) Object.Destroy(obj);
                else Object.DestroyImmediate(obj);
            });
        };

        static void BuildLines(Mesh mesh, List<(Vector3 from, Vector3 to)> segments) {
            var verts = new List<Vector3>();
            var lines = new List<int>();
            for (var i = 0; segments != null && i < segments.Count; i++) {
                lines.Add(verts.Count); verts.Add(segments[i].from);
                lines.Add(verts.Count); verts.Add(segments[i].to);
            }
            mesh.SetVertices(verts);
            mesh.SetIndices(lines, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
        }
    }
}
