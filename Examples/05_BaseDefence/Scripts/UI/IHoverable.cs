using System;

namespace Spoke.Examples.BaseDefence {

    public enum CoverageType { None, Power, Radar, Turret, Repair }

    // A unit the pointer can hover — what to say about it, what to show for it,
    // and the ground it occupies (for the highlight ring).
    public interface IHoverable {
        ISignal<HoverInfo> HoverInfo { get; }
    }

    public readonly struct HoverInfo : IEquatable<HoverInfo> {

        public readonly string Description;
        public readonly CoverageType Coverage;
        public readonly PowerNode PowerNode;

        public HoverInfo(string description, CoverageType coverage, PowerNode powerNode) {
            Description = description;
            Coverage = coverage;
            PowerNode = powerNode;
        }

        public bool Equals(HoverInfo other) {
            return Description == other.Description &&
                   Coverage == other.Coverage &&
                   PowerNode == other.PowerNode;
        }

        public override bool Equals(object obj) => obj is HoverInfo info && Equals(info);
        public override int GetHashCode() => HashCode.Combine(Description, Coverage, PowerNode);
        public static bool operator ==(HoverInfo left, HoverInfo right) => left.Equals(right);
        public static bool operator !=(HoverInfo left, HoverInfo right) => !(left == right);
    }
}
