namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A clickable rectangle on the facility map, tied to a Parent location.
    /// A Parent may have several. Coordinates are fractions 0-1, not pixels.
    /// </summary>
    public class LocationZone
    {
        public int Id { get; set; }

        /// <summary>The Parent-level Location this rectangle selects.</summary>
        public int LocationId { get; set; }

        /// <summary>Left edge, 0-1 across the image.</summary>
        public double X { get; set; }

        /// <summary>Top edge, 0-1 down the image.</summary>
        public double Y { get; set; }

        /// <summary>Width as a fraction of image width.</summary>
        public double W { get; set; }

        /// <summary>Height as a fraction of image height.</summary>
        public double H { get; set; }

        /// <summary>Optional note for when one Parent has several zones, e.g. "Lean-To corner".</summary>
        public string? Label { get; set; }
    }
}
