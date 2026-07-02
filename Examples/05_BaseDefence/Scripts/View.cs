using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Spoke.Examples.BaseDefence {

    public readonly struct View : IEquatable<View> {

        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Circle GroundArea;
        public readonly Vector3? MousePoint;

        public View(Camera camera, Plane groundPlane) {
            Position = camera.transform.position;
            Rotation = camera.transform.rotation;
            if (!TryViewCircle(groundPlane, camera, out GroundArea)) {
                Debug.LogError("Cannot find GroundArea, camera must be pointing at ground plane");
            }
            MousePoint = FindMousePoint(groundPlane, camera);
        }

        // The cursor's point on the ground plane — null while the cursor is over screen-space UI,
        // or its ray misses the plane.
        static Vector3? FindMousePoint(Plane plane, Camera cam) {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return null;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (plane.Raycast(ray, out var enter)) return ray.GetPoint(enter);
            return null;
        }

        // Bounding circle of the camera's ground footprint (the four viewport corners ray-cast to the
        // play plane). Returns false if a corner ray misses the plane.
        static bool TryViewCircle(Plane plane, Camera cam, out Circle circle) {
            circle = default;
            if (!GroundPoint(plane, cam, 0f, 0f, out var bl) || !GroundPoint(plane, cam, 1f, 0f, out var br) ||
                !GroundPoint(plane, cam, 0f, 1f, out var tl) || !GroundPoint(plane, cam, 1f, 1f, out var tr)) return false;
            var center = (bl + br + tl + tr) * 0.25f;
            var radius = Mathf.Max(Mathf.Max(Vector3.Distance(center, bl), Vector3.Distance(center, br)),
                                   Mathf.Max(Vector3.Distance(center, tl), Vector3.Distance(center, tr)));
            circle = new Circle(center, radius);
            return true;
        }

        static bool GroundPoint(Plane plane, Camera cam, float vx, float vy, out Vector3 point) {
            var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
            if (plane.Raycast(ray, out var enter)) { point = ray.GetPoint(enter); return true; }
            point = default;
            return false;
        }

        public bool Equals(View other) {
            return Position.Equals(other.Position) &&
                   Rotation.Equals(other.Rotation) &&
                   GroundArea.Equals(other.GroundArea) &&
                   EqualityComparer<Vector3?>.Default.Equals(MousePoint, other.MousePoint);
        }

        public override bool Equals(object obj) => obj is View view && Equals(view);
        public override int GetHashCode() => HashCode.Combine(Position, Rotation, GroundArea, MousePoint);
        public static bool operator ==(View left, View right) => left.Equals(right);
        public static bool operator !=(View left, View right) => !(left == right);
    }
}