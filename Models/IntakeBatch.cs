using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A bulk intake submitted from the Intake screen and HELD because the
    /// location it names doesn't exist yet.
    ///
    /// Batches whose location is already known never land here -- they commit
    /// straight through. This table only holds the ones waiting on a decision,
    /// which keeps the queue meaningful instead of a log of everything.
    ///
    /// The point is that a team entering stock can't invent a location, but also
    /// isn't stopped by one. They type what they call it, the rows are kept
    /// intact, and the superuser either MAPS it to somewhere that already exists
    /// under a different name, or creates it. Either way the same commit path
    /// runs afterwards -- one pipeline, two entrances.
    /// </summary>
    public class IntakeBatch
    {
        public int Id { get; set; }

        [MaxLength(100)] public string SubmittedBy { get; set; } = "";
        public DateTime SubmittedAt { get; set; }

        /// <summary>Ownership applied to every row. Branch is derived from Line, never stored.</summary>
        [MaxLength(50)] public string Line { get; set; } = "";
        [MaxLength(60)] public string Team { get; set; } = "";

        // ---- Location, as chosen. Codes, matching how ItemVariant stores them. ----
        [MaxLength(20)] public string ParentCode { get; set; } = "";
        [MaxLength(20)] public string MajorCode { get; set; } = "";
        [MaxLength(20)] public string SubCode { get; set; } = "";
        [MaxLength(40)] public string Rack { get; set; } = "";
        [MaxLength(40)] public string Row { get; set; } = "";

        /// <summary>
        /// What they typed when their location wasn't in the list. Kept verbatim --
        /// if three teams all write "North Bay" for the same place, that's worth
        /// seeing before deciding what the real name should be.
        /// </summary>
        [MaxLength(120)] public string? RequestedLocation { get; set; }

        [MaxLength(20)] public string Status { get; set; } = IntakeStatus.Pending;

        [MaxLength(100)] public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
        [MaxLength(300)] public string? Note { get; set; }

        public List<IntakeRow> Rows { get; set; } = new();
    }

    /// <summary>One line of a held batch: a model and how many of it.</summary>
    public class IntakeRow
    {
        public int Id { get; set; }
        public int IntakeBatchId { get; set; }

        [Required, MaxLength(120)] public string ItemName { get; set; } = "";
        [MaxLength(60)] public string Type { get; set; } = "";
        [MaxLength(60)] public string Brand { get; set; } = "";

        /// <summary>Defaults to "N/A" but is editable -- plant orders do come with real numbers.</summary>
        [MaxLength(60)] public string RheemPartNumber { get; set; } = "N/A";

        public int Quantity { get; set; } = 1;

        /// <summary>Compressors only. Becomes an On Hand CompressorUnit at commit.</summary>
        [MaxLength(50)] public string? SerialNumber { get; set; }

        public IntakeBatch? Batch { get; set; }
    }

    public static class IntakeStatus
    {
        public const string Pending  = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }
}
