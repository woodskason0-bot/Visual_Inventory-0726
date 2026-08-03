namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A clickable rectangle on the facility map, tied to a PARENT location.
    ///
    /// Before Pass 8 these were four hardcoded &lt;area&gt; tags in Index.cshtml --
    /// the FIFTH copy of the location vocabulary, and the one Pass 7C missed
    /// because it was an image map rather than a dropdown. A Parent added in
    /// Settings appeared in every picker and was invisible on the map.
    ///
    /// ONE PARENT MAY HAVE SEVERAL ZONES. The same area can occupy more than one
    /// part of the building, so this is its own table rather than four columns on
    /// Location.
    ///
    /// COORDINATES ARE NORMALISED 0-1, not pixels. The old markup stored pixels
    /// measured against a 1146x643 image, which welded the data to that exact
    /// file -- swap the floorplan for a different resolution and every zone
    /// silently shifts. A fraction means "44% across, 57% down" and survives it.
    ///
    /// Only Parents get zones. Majors and Subs are reached through the post-click
    /// menu, which is already driven by live stock data and needs nothing here.
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
