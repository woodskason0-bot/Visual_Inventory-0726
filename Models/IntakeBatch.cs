using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A bulk intake held because its location does not exist yet.
    /// Batches with a known location commit straight through and never land here.
    /// </summary>
    public class IntakeBatch
    {
        public int Id { get; set; }

        [MaxLength(100)] public string SubmittedBy { get; set; } = "";
        public DateTime SubmittedAt { get; set; }

        /// <summary>Ownership applied to every row. Branch is derived from Line, never stored.</summary>
        [MaxLength(50)] public string Line { get; set; } = "";
        [MaxLength(60)] public string Team { get; set; } = "";

        // Location as chosen. Codes, like ItemVariant.
        [MaxLength(20)] public string ParentCode { get; set; } = "";
        [MaxLength(20)] public string MajorCode { get; set; } = "";
        [MaxLength(20)] public string SubCode { get; set; } = "";
        [MaxLength(40)] public string Rack { get; set; } = "";
        [MaxLength(40)] public string Row { get; set; } = "";

        /// <summary>What they typed when the location was not in the list. Kept verbatim.</summary>
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

        /// <summary>Defaults to "N/A". Editable: plant orders come with real numbers.</summary>
        [MaxLength(60)] public string RheemPartNumber { get; set; } = "N/A";

        public int Quantity { get; set; } = 1;

        /// <summary>Compressors only. Comma-joined serials, one On Hand CompressorUnit each at commit.</summary>
        [MaxLength(500)] public string? Serials { get; set; }

        /// <summary>Motors only. A count: ThermocoupledQty plus that many blank MotorUnit rows at commit.</summary>
        public int ThermocoupledQty { get; set; }

        public IntakeBatch? Batch { get; set; }
    }

    public static class IntakeStatus
    {
        public const string Pending  = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }
}
