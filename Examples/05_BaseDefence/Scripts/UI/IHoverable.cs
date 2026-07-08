using System;

namespace Spoke.Examples.BaseDefence {

    /// <summary>The kind of coverage overlay a unit shows when hovered or placed.</summary>
    public enum CoverageType { None, Power, Radar, Turret, Repair }

    /// <summary>A unit the pointer can hover, exposing what to say about it and what to show for it.</summary>
    public interface IHoverable {
        /// <summary>What to display while this unit is hovered.</summary>
        ISignal<HoverInfo> HoverInfo { get; }
    }

    /// <summary>What to show while a unit is hovered or being placed.</summary>
    public readonly struct HoverInfo : IEquatable<HoverInfo> {

        /// <summary>Text shown in the hover panel.</summary>
        public readonly string Description;
        /// <summary>Which coverage overlay to draw, if any.</summary>
        public readonly CoverageType Coverage;
        /// <summary>The node whose power link to highlight, if any.</summary>
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
