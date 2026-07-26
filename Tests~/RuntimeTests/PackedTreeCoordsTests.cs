using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class PackedTreeCoordsTests : SpokeTestFixture {

        [Test]
        public void Compare_ParentBeforeDeeperChild() {
            var parent = default(PackedTreeCoords128).Extend(0);
            var child = parent.Extend(0);
            Assert.Less(parent.CompareTo(child), 0);
            Assert.Greater(child.CompareTo(parent), 0);
        }

        [Test]
        public void Compare_LeftSiblingBeforeRight() {
            var left = default(PackedTreeCoords128).Extend(0);
            var right = default(PackedTreeCoords128).Extend(1);
            Assert.Less(left.CompareTo(right), 0);
        }

        [Test]
        public void Compare_NestedOrderingMatchesImperativeWalk() {
            // A representative epoch tree — coords must sort in depth-first (imperative walk) order:
            //   []          (tree root)
            //   [0]         (Main)
            //   [0,0]       (epoch)
            //   [0,0,0]     (epoch *)
            //   [0,0,1]
            //   [0,1]       (epoch *)
            //   [0,1,0]     (epoch *)
            //   [0,1,1]
            var coords = new[] {
                default(PackedTreeCoords128),
                default(PackedTreeCoords128).Extend(0),
                default(PackedTreeCoords128).Extend(0).Extend(0),
                default(PackedTreeCoords128).Extend(0).Extend(0).Extend(0),
                default(PackedTreeCoords128).Extend(0).Extend(0).Extend(1),
                default(PackedTreeCoords128).Extend(0).Extend(1),
                default(PackedTreeCoords128).Extend(0).Extend(1).Extend(0),
                default(PackedTreeCoords128).Extend(0).Extend(1).Extend(1),
            };
            for (int i = 0; i < coords.Length - 1; i++) {
                Assert.Less(coords[i].CompareTo(coords[i + 1]), 0, $"coord[{i}] should be before coord[{i + 1}]");
            }
        }

        // The first layers pack into one word and the rest into a second, so ordering has to hold
        // within each word and across the seam between them.
        [Test]
        public void Compare_OrdersCorrectly_AcrossBothPackedWords() {
            // Diverging at a deep layer, well past the seam
            var a = Path(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0);
            var b = Path(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 1);
            Assert.Less(a.CompareTo(b), 0);

            // A divergence in an early layer outranks anything deeper
            var early = Path(0, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9);
            var late = Path(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.Less(early.CompareTo(late), 0);

            // A coord sorts before one that extends it, including right at the seam
            var atSeam = Path(1, 1, 1, 1, 1, 1, 1, 1);
            Assert.Less(atSeam.CompareTo(atSeam.Extend(0)), 0);
            Assert.Less(atSeam.Extend(0).CompareTo(atSeam.Extend(1)), 0);
        }

        [Test]
        public void Extend_UsesFullCapacity_ThenYieldsInvalid() {
            var deep = default(PackedTreeCoords128);
            for (int i = 0; i < 16; i++) deep = deep.Extend(255);
            Assert.IsTrue(deep.IsValid);
            Assert.IsFalse(deep.Extend(0).IsValid);
        }

        [Test]
        public void Extend_IndexOutOfRange_YieldsInvalid() {
            Assert.IsFalse(default(PackedTreeCoords128).Extend(256).IsValid);
            Assert.IsFalse(default(PackedTreeCoords128).Extend(-1).IsValid);
        }

        [Test]
        public void Extend_OfInvalid_StaysInvalid() {
            Assert.IsFalse(PackedTreeCoords128.Invalid.Extend(0).IsValid);
        }

        static PackedTreeCoords128 Path(params long[] indices) {
            var coord = default(PackedTreeCoords128);
            foreach (var i in indices) coord = coord.Extend(i);
            return coord;
        }
    }
}
