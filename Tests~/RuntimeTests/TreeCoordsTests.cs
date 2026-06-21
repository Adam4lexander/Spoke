using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class TreeCoordsTests : SpokeTestFixture {

        [Test]
        public void Extend_BuildsDeeperCoord() {
            var root = default(TreeCoords);
            var child = root.Extend(0);
            var grandchild = child.Extend(3);

            Assert.AreEqual(0, child.Tail);
            Assert.AreEqual(3, grandchild.Tail);
        }

        [Test]
        public void Compare_ParentBeforeDeeperChild() {
            var parent = default(TreeCoords).Extend(0);
            var child = parent.Extend(0);
            Assert.Less(parent.CompareTo(child), 0);
            Assert.Greater(child.CompareTo(parent), 0);
        }

        [Test]
        public void Compare_LeftSiblingBeforeRight() {
            var left = default(TreeCoords).Extend(0);
            var right = default(TreeCoords).Extend(1);
            Assert.Less(left.CompareTo(right), 0);
        }

        [Test]
        public void Compare_NestedOrderingMatchesImperativeWalk() {
            // Mimics the tree in 00_SpokeRuntime.md §Ordering Epochs by Tree Coords:
            //   []          (tree root)
            //   [0]         (Main)
            //   [0,0]       (epoch)
            //   [0,0,0]     (epoch *)
            //   [0,0,1]
            //   [0,1]       (epoch *)
            //   [0,1,0]     (epoch *)
            //   [0,1,1]
            var coords = new[] {
                default(TreeCoords),
                default(TreeCoords).Extend(0),
                default(TreeCoords).Extend(0).Extend(0),
                default(TreeCoords).Extend(0).Extend(0).Extend(0),
                default(TreeCoords).Extend(0).Extend(0).Extend(1),
                default(TreeCoords).Extend(0).Extend(1),
                default(TreeCoords).Extend(0).Extend(1).Extend(0),
                default(TreeCoords).Extend(0).Extend(1).Extend(1),
            };
            for (int i = 0; i < coords.Length - 1; i++) {
                Assert.Less(coords[i].CompareTo(coords[i + 1]), 0, $"coord[{i}] should be before coord[{i + 1}]");
            }
        }

        [Test]
        public void Compare_SlowPath_ProducesSameOrderingAsPacked() {
            // Force slow path with val > 255
            var a = default(TreeCoords).Extend(300);
            var b = default(TreeCoords).Extend(400);
            Assert.Less(a.CompareTo(b), 0);
            Assert.Greater(b.CompareTo(a), 0);
        }
    }
}
