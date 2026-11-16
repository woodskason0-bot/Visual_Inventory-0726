using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Services
{
    /// <summary>What the Reserve dialog posts, one stack per line.</summary>
    public class ReservationInput
    {
        public int Id { get; set; }
        public string ItemId { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public bool UntilNotice { get; set; }
        public List<ReservationLineInput> Lines { get; set; } = new();
    }

    public class ReservationLineInput
    {
        public int VariantId { get; set; }
        public int Qty { get; set; }
        public int Tc { get; set; }
        /// <summary>Recorded units ticked (compressor or motor rows, by the item's type).</summary>
        public List<int> PinIds { get; set; } = new();
        /// <summary>New compressor serials / motor lab #s typed in, recorded On Hand here and held.</summary>
        public List<string> NewIdents { get; set; } = new();
    }

    /// <summary>One stack as the Reserve dialog shows it.</summary>
    public class ReserveStackOption
    {
        public int VariantId { get; set; }
        public int Number { get; set; }
        public string Label { get; set; } = "";
        public string Team { get; set; } = "";
        public int OnShelf { get; set; }
        public int TcOnShelf { get; set; }
        public int MaxQty { get; set; }
        public int MaxTc { get; set; }
        public int Qty { get; set; }
        public int Tc { get; set; }
        /// <summary>Units recorded On Hand here that aren't room for more rows (rows already = shelf).</summary>
        public int RecordedRows { get; set; }
        public List<ReserveUnitOption> Units { get; set; } = new();
    }

    public class ReserveUnitOption
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public bool Checked { get; set; }
        /// <summary>Held by someone else's reservation: shown, not tickable.</summary>
        public string? TakenBy { get; set; }
    }

    public class ReserveFormModel
    {
        public InventoryItem Item { get; set; } = null!;
        public Reservation? Existing { get; set; }
        public bool IsCompressor { get; set; }
        public bool IsMotor { get; set; }
        public string Reason { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string ReservedByDisplay { get; set; } = "";
        public List<string> MyOpenReasons { get; set; } = new();
        public List<ReserveStackOption> Stacks { get; set; } = new();
    }

    /// <summary>
    /// Holds on stock. Instance methods serve the Reserve dialog and Reserved Items.
    /// Static ones run inside pickup, stock changes and the sweep, on the caller's context.
    /// </summary>
    public class ReservationService
    {
        private readonly AppDbContext _db;
        private readonly CurrentUserService _currentUser;
        private readonly InventoryService _inventory;

        public const string NotificationCategory = "Reservation";
        public const string SystemUser = "VIS";
        public const int ReasonMax = 200;

        public ReservationService(AppDbContext db, CurrentUserService currentUser, InventoryService inventory)
        {
            _db = db;
            _currentUser = currentUser;
            _inventory = inventory;
        }

        // Shared definitions

        /// <summary>Active, and not past its end date. The one definition of "still holding".</summary>
        public static IQueryable<Reservation> Live(IQueryable<Reservation> q, DateTime today) =>
            q.Where(r => r.Status == ReservationStatus.Active && (r.EndDate == null || r.EndDate >= today));

        public static bool IsLive(Reservation r, DateTime today) =>
            r.Status == ReservationStatus.Active && (r.EndDate == null || r.EndDate.Value.Date >= today);

        public static string Fmt(DateTime d) => d.ToString("MM-dd-yyyy");

        public static string Person(string? userName) => (userName ?? "").Replace('.', ' ');

        /// <summary>The hold in one sentence: reserved for X by Y from A to B, or until notice.</summary>
        public static string Sentence(Reservation r) =>
            $"Reserved for {r.Reason} by {Person(r.ReservedBy)} from {Fmt(r.StartDate)} "
            + (r.EndDate.HasValue ? $"to {Fmt(r.EndDate.Value)}" : "until notice");

        private static bool SameName(string? a, string? b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Live reservation id -> who holds it, for one item. Pin checks go through this.</summary>
        public static Dictionary<int, string> PinOwners(AppDbContext db, string itemId) =>
            Live(db.Reservations.AsNoTracking(), DateTime.Today)
                .Where(r => r.ItemId == itemId)
                .Select(r => new { r.Id, r.ReservedBy })
                .ToList()
                .ToDictionary(x => x.Id, x => x.ReservedBy);

        /// <summary>"Conner Walworth (2, G-cab sample #3 rebuild)" for every live hold on a stack. For refusal messages.</summary>
        public static string DescribeHoldsOn(AppDbContext db, string itemId, int variantId)
        {
            var rows = (from l in db.ReservationLines.AsNoTracking()
                        join r in Live(db.Reservations.AsNoTracking(), DateTime.Today) on l.ReservationId equals r.Id
                        where r.ItemId == itemId && l.ItemVariantId == variantId && l.Quantity > 0
                        select new { r.ReservedBy, r.Reason, l.Quantity }).ToList();
            return string.Join("; ", rows.Select(x => $"{Person(x.ReservedBy)} ({x.Quantity}, {x.Reason})"));
        }

        /// <summary>Notification row added to the caller's context, saved with the caller's own SaveChanges.</summary>
        private static void Notify(AppDbContext db, string recipient, string message)
        {
            if (string.IsNullOrWhiteSpace(recipient)) return;
            db.Notifications.Add(new Notification
            {
                RecipientUserName = recipient.Trim(),
                Category = NotificationCategory,
                Message = message,
                LinkUrl = "/Reservations",
                CreatedAt = DateTime.UtcNow
            });
        }

        private static void Log(AppDbContext db, string actionType, string itemId, string? itemName, string details, string user)
        {
            db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = actionType,
                ItemId = itemId,
                ItemName = itemName,
                QuantityChange = 0,
                Details = details,
                User = user
            });
        }

        private static string ItemName(AppDbContext db, string itemId) =>
            db.InventoryItems.AsNoTracking().Where(i => i.ItemId == itemId).Select(i => i.ItemName).FirstOrDefault() ?? itemId;

        /// <summary>
        /// Keeps named units in step with the counts: a unit off the shelf is no longer held, and a
        /// stack cannot name more units than its line holds. Clears every name once the hold ends.
        /// </summary>
        public static void TrimPins(AppDbContext db, Reservation r)
        {
            bool holding = r.Status == ReservationStatus.Active;
            var comps = db.CompressorUnits.Where(c => c.ReservationId == r.Id).ToList();
            var motors = db.MotorUnits.Where(m => m.ReservationId == r.Id).ToList();

            foreach (var c in comps.Where(c => !holding || c.Status != UnitStatus.OnHand || c.ReservationId != r.Id)) c.ReservationId = null;
            foreach (var m in motors.Where(m => !holding || m.Status != UnitStatus.OnHand || m.ReservationId != r.Id)) m.ReservationId = null;
            if (!holding) return;

            foreach (var g in comps.Where(c => c.ReservationId == r.Id).GroupBy(c => c.ItemVariantId))
            {
                int allowed = r.Lines.Where(l => l.ItemVariantId == g.Key).Sum(l => l.Quantity);
                foreach (var extra in g.OrderByDescending(c => c.RecordedAt).Take(Math.Max(0, g.Count() - allowed)))
                    extra.ReservationId = null;
            }
            foreach (var g in motors.Where(m => m.ReservationId == r.Id).GroupBy(m => m.ItemVariantId))
            {
                int allowed = r.Lines.Where(l => l.ItemVariantId == g.Key).Sum(l => l.TcQuantity);
                foreach (var extra in g.OrderByDescending(m => m.RecordedAt).Take(Math.Max(0, g.Count() - allowed)))
                    extra.ReservationId = null;
            }
        }

        // Called from pickup, stock changes and the sweep (static, caller's context)

        /// <summary>
        /// The requester's own holds go down by what their order took off each stack,
        /// whoever picked it up. At 0 the hold ends as Picked Up.
        /// </summary>
        public static void ConsumeForPickup(AppDbContext db, string itemId, string? requester,
            IEnumerable<(int VariantId, int Take, int TcTake)> pulls, string actor, string context)
        {
            if (string.IsNullOrWhiteSpace(requester)) return;
            string who = requester.Trim().ToLower();
            var mine = Live(db.Reservations, DateTime.Today)
                .Include(r => r.Lines)
                .Where(r => r.ItemId == itemId && r.ReservedBy.ToLower() == who)
                .OrderBy(r => r.CreatedAt)
                .ToList();
            if (mine.Count == 0) return;

            var used = new Dictionary<int, int>();
            foreach (var (variantId, take, tcTake) in pulls)
            {
                int q = take, t = tcTake;
                foreach (var r in mine)
                {
                    foreach (var line in r.Lines.Where(l => l.ItemVariantId == variantId))
                    {
                        if (q <= 0 && t <= 0) break;
                        int dq = Math.Min(line.Quantity, q);
                        int dt = Math.Min(line.TcQuantity, t);
                        line.Quantity -= dq;
                        line.TcQuantity = Math.Min(line.TcQuantity - dt, line.Quantity);
                        q -= dq;
                        t -= dt;
                        if (dq > 0) used[r.Id] = used.GetValueOrDefault(r.Id) + dq;
                    }
                }
            }

            string itemName = ItemName(db, itemId);
            foreach (var r in mine.Where(r => used.ContainsKey(r.Id)))
            {
                int left = r.Lines.Sum(l => l.Quantity);
                if (left <= 0)
                {
                    r.Status = ReservationStatus.PickedUp;
                    r.EndedAt = DateTime.UtcNow;
                    r.EndedBy = actor;
                    r.EndNote = context;
                }
                TrimPins(db, r);
                Log(db, left <= 0 ? "Reservation Ended" : "Reservation Changed", itemId, itemName,
                    $"{used[r.Id]} held for {Person(r.ReservedBy)} (\"{r.Reason}\") left on {context}; "
                    + (left <= 0 ? "the hold is used up." : $"{left} still held."),
                    actor);
            }
        }

        /// <summary>
        /// A short-pull correction is a recount, so it is never refused for a hold. Holds on the
        /// stack shrink to what is there, newest first, and their reservers are told.
        /// </summary>
        public static void ShrinkToFit(AppDbContext db, string itemId, ItemVariant v, string actor, string context)
        {
            var holds = Live(db.Reservations, DateTime.Today)
                .Include(r => r.Lines)
                .Where(r => r.ItemId == itemId && r.Lines.Any(l => l.ItemVariantId == v.Id && l.Quantity > 0))
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            int overQ = holds.SelectMany(r => r.Lines).Where(l => l.ItemVariantId == v.Id).Sum(l => l.Quantity) - v.Quantity;
            int overT = holds.SelectMany(r => r.Lines).Where(l => l.ItemVariantId == v.Id).Sum(l => l.TcQuantity) - v.ThermocoupledQty;
            if (overQ <= 0 && overT <= 0) return;

            string itemName = ItemName(db, itemId);
            foreach (var r in holds)
            {
                if (overQ <= 0 && overT <= 0) break;
                int cut = 0, tcCut = 0;
                foreach (var line in r.Lines.Where(l => l.ItemVariantId == v.Id))
                {
                    int dq = Math.Max(0, Math.Min(line.Quantity, overQ));
                    int tcBefore = line.TcQuantity;
                    line.Quantity -= dq;
                    overQ -= dq;
                    cut += dq;
                    // TC can't exceed what's held; then cut whatever TC is still over.
                    line.TcQuantity = Math.Min(line.TcQuantity, line.Quantity);
                    int dt = Math.Max(0, Math.Min(line.TcQuantity, overT - (tcBefore - line.TcQuantity)));
                    line.TcQuantity -= dt;
                    overT -= tcBefore - line.TcQuantity;
                    tcCut += tcBefore - line.TcQuantity;
                }
                if (cut <= 0 && tcCut <= 0) continue;
                int left = r.Lines.Sum(l => l.Quantity);
                if (left <= 0)
                {
                    r.Status = ReservationStatus.Released;
                    r.EndedAt = DateTime.UtcNow;
                    r.EndedBy = actor;
                    r.EndNote = context;
                }
                TrimPins(db, r);
                string cutText = cut > 0 ? $"{cut}" + (tcCut > 0 ? $" ({tcCut} TC)" : "") : $"{tcCut} TC";
                Log(db, left <= 0 ? "Reservation Ended" : "Reservation Changed", itemId, itemName,
                    $"Hold for {Person(r.ReservedBy)} (\"{r.Reason}\") cut by {cutText} on V{v.VariantNumber}: {context}; "
                    + (left <= 0 ? "nothing left to hold." : $"{left} still held."),
                    actor);
                Notify(db, r.ReservedBy,
                    $"Your hold on {itemId} ({r.Reason}) went down by {cutText}: {context}."
                    + (left <= 0 ? " Nothing is held any more." : $" {left} still held."));
            }
        }

        /// <summary>A whole-stack merge: the holds go with the stock to the stack it merged into.</summary>
        public static void MoveLines(AppDbContext db, string itemId, int fromVariantId, int toVariantId)
        {
            var holds = Live(db.Reservations, DateTime.Today)
                .Include(r => r.Lines)
                .Where(r => r.ItemId == itemId && r.Lines.Any(l => l.ItemVariantId == fromVariantId))
                .ToList();
            foreach (var r in holds)
            {
                foreach (var line in r.Lines.Where(l => l.ItemVariantId == fromVariantId).ToList())
                {
                    var into = r.Lines.FirstOrDefault(l => l.ItemVariantId == toVariantId);
                    if (into == null) { line.ItemVariantId = toVariantId; continue; }
                    into.Quantity += line.Quantity;
                    into.TcQuantity += line.TcQuantity;
                    line.Quantity = 0;
                    line.TcQuantity = 0;
                }
            }
        }

        /// <summary>Ends dated holds after their last day. Notifies once on the last day and once at the end.</summary>
        public static int Sweep(AppDbContext db, DateTime today)
        {
            int changed = 0;
            var due = db.Reservations.Include(r => r.Lines)
                .Where(r => r.Status == ReservationStatus.Active && r.EndDate != null && r.EndDate < today)
                .ToList();
            foreach (var r in due)
            {
                r.Status = ReservationStatus.Expired;
                r.EndedAt = DateTime.UtcNow;
                r.EndedBy = SystemUser;
                r.EndNote = $"Ended on its own after {Fmt(r.EndDate!.Value)}";
                TrimPins(db, r);
                int held = r.Lines.Sum(l => l.Quantity);
                Log(db, "Reservation Ended", r.ItemId, ItemName(db, r.ItemId),
                    $"Hold of {held} for {Person(r.ReservedBy)} (\"{r.Reason}\") ended on its own after {Fmt(r.EndDate.Value)}.",
                    SystemUser);
                Notify(db, r.ReservedBy,
                    $"Your hold on {r.ItemId} ({r.Reason}) ended on its own after {Fmt(r.EndDate.Value)}. The stock is available to everyone again.");
                changed++;
            }

            var lastDay = db.Reservations
                .Where(r => r.Status == ReservationStatus.Active && r.EndDate == today && !r.EndingNoticeSent)
                .ToList();
            foreach (var r in lastDay)
            {
                r.EndingNoticeSent = true;
                Notify(db, r.ReservedBy,
                    $"Your hold on {r.ItemId} ({r.Reason}) ends at the end of today ({Fmt(today)}). Extend it from Reserved Items if you still need it.");
                changed++;
            }

            if (changed > 0) db.SaveChanges();
            return changed;
        }

        // Who may do what

        /// <summary>Standard and up, on their own Line. Admin anywhere.</summary>
        public bool CanReserve(InventoryItem item) =>
            _currentUser.Level >= AccessLevels.Standard && _inventory.IsOwnLine(item.Line);

        /// <summary>The reserver, Engineer and up on the item's Line, or Admin.</summary>
        public bool CanManage(Reservation r, InventoryItem? item)
        {
            if (_currentUser.Level >= AccessLevels.Admin) return true;
            if (_currentUser.Level < AccessLevels.Standard) return false;
            if (SameName(r.ReservedBy, _currentUser.Name)) return true;
            return _currentUser.Level >= AccessLevels.Engineer && item != null && _inventory.IsOwnLine(item.Line);
        }

        public bool IsMine(Reservation r) => SameName(r.ReservedBy, _currentUser.Name);

        // Reads

        public List<Reservation> LiveForItem(string itemId) =>
            Live(_db.Reservations.AsNoTracking(), DateTime.Today)
                .Include(r => r.Lines)
                .Where(r => r.ItemId == itemId)
                .OrderBy(r => r.CreatedAt)
                .ToList();

        public List<Reservation> AllLive() =>
            Live(_db.Reservations.AsNoTracking(), DateTime.Today)
                .Include(r => r.Lines)
                .OrderBy(r => r.CreatedAt)
                .ToList();

        /// <summary>Held quantity per ItemId across every live hold, for Search Center's "N reserved" chip.</summary>
        public Dictionary<string, int> HeldPerItem() =>
            (from l in _db.ReservationLines.AsNoTracking()
             join r in Live(_db.Reservations.AsNoTracking(), DateTime.Today) on l.ReservationId equals r.Id
             group l by r.ItemId into g
             select new { g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToList()
            .Where(x => x.Qty > 0)
            .ToDictionary(x => x.Key, x => x.Qty);

        /// <summary>The signed-in person's open reasons, newest first, for the purpose-group buttons in Reserve.</summary>
        public List<string> MyOpenReasons()
        {
            string me = (_currentUser.Name ?? "").Trim().ToLower();
            return Live(_db.Reservations.AsNoTracking(), DateTime.Today)
                .Where(r => r.ReservedBy.ToLower() == me)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.Reason)
                .ToList()
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        public ReserveFormModel? BuildForm(string itemId, int? reservationId)
        {
            var item = _db.InventoryItems.AsNoTracking().Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return null;

            Reservation? existing = null;
            if (reservationId.HasValue && reservationId.Value > 0)
            {
                existing = _db.Reservations.AsNoTracking().Include(r => r.Lines).FirstOrDefault(r => r.Id == reservationId.Value && r.ItemId == itemId);
                if (existing == null || !IsLive(existing, DateTime.Today)) return null;
            }

            bool isComp = InventoryService.IsCompressorType(item.Type);
            bool isMotor = InventoryService.IsMotorType(item.Type);
            string by = existing?.ReservedBy ?? _currentUser.Name;
            var claims = StockClaims.FromVariants(_db, itemId, item.Variants);
            var owners = PinOwners(_db, itemId);

            var comps = isComp
                ? _db.CompressorUnits.AsNoTracking().Where(c => c.ItemId == itemId && c.Status == UnitStatus.OnHand && c.ItemVariantId != null).OrderBy(c => c.RecordedAt).ToList()
                : new List<CompressorUnit>();
            var motors = isMotor
                ? _db.MotorUnits.AsNoTracking().Where(m => m.ItemId == itemId && m.Status == UnitStatus.OnHand && m.ItemVariantId != null).OrderBy(m => m.RecordedAt).ToList()
                : new List<MotorUnit>();

            var model = new ReserveFormModel
            {
                Item = item,
                Existing = existing,
                IsCompressor = isComp,
                IsMotor = isMotor,
                Reason = existing?.Reason ?? "",
                Start = existing?.StartDate ?? DateTime.Today,
                End = existing?.EndDate,
                ReservedByDisplay = Person(by),
                MyOpenReasons = MyOpenReasons()
            };

            foreach (var v in item.ActiveVariants.OrderBy(v => v.VariantNumber))
            {
                var (maxQ, maxT) = claims.Holdable(by, v.Id, existing?.Id);
                var mine = existing?.Lines.Where(l => l.ItemVariantId == v.Id).ToList() ?? new List<ReservationLine>();
                var opt = new ReserveStackOption
                {
                    VariantId = v.Id,
                    Number = v.VariantNumber,
                    Label = LocationCodec.FriendlyPath(v.Parent, v.Major, v.Sub, v.Rack, v.Row, v.FdaString),
                    Team = v.Team ?? "",
                    OnShelf = v.Quantity,
                    TcOnShelf = v.ThermocoupledQty,
                    MaxQty = maxQ,
                    MaxTc = isMotor ? maxT : 0,
                    Qty = mine.Sum(l => l.Quantity),
                    Tc = mine.Sum(l => l.TcQuantity)
                };

                string? Taken(int? rid) =>
                    rid.HasValue && owners.TryGetValue(rid.Value, out var who) && (existing == null || rid.Value != existing.Id)
                        ? Person(who) : null;

                if (isComp)
                {
                    var here = comps.Where(c => c.ItemVariantId == v.Id).ToList();
                    opt.RecordedRows = here.Count;
                    foreach (var c in here.Where(c => !string.IsNullOrWhiteSpace(c.SerialNumber) || !string.IsNullOrWhiteSpace(c.LabNumber)))
                    {
                        string label = !string.IsNullOrWhiteSpace(c.SerialNumber)
                            ? "SN " + c.SerialNumber + (string.IsNullOrWhiteSpace(c.LabNumber) ? "" : " · Lab " + c.LabNumber)
                            : "Lab " + c.LabNumber;
                        opt.Units.Add(new ReserveUnitOption
                        {
                            Id = c.Id,
                            Label = label,
                            Checked = existing != null && c.ReservationId == existing.Id,
                            TakenBy = Taken(c.ReservationId)
                        });
                    }
                }
                else if (isMotor)
                {
                    var here = motors.Where(m => m.ItemVariantId == v.Id).ToList();
                    opt.RecordedRows = here.Count;
                    foreach (var m in here.Where(m => !string.IsNullOrWhiteSpace(m.LabNumber)))
                    {
                        opt.Units.Add(new ReserveUnitOption
                        {
                            Id = m.Id,
                            Label = "Lab " + m.LabNumber,
                            Checked = existing != null && m.ReservationId == existing.Id,
                            TakenBy = Taken(m.ReservationId)
                        });
                    }
                }
                model.Stacks.Add(opt);
            }
            return model;
        }

        // Writes

        public (bool Ok, string Message) Save(ReservationInput input)
        {
            var today = DateTime.Today;
            var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == input.ItemId);
            if (item == null) return (false, "That item no longer exists.");
            if (!CanReserve(item))
                return (false, "You can only reserve items on your own Line (Standard access and up).");

            Reservation? existing = null;
            if (input.Id > 0)
            {
                existing = _db.Reservations.Include(r => r.Lines).FirstOrDefault(r => r.Id == input.Id && r.ItemId == item.ItemId);
                if (existing == null || !IsLive(existing, today))
                    return (false, "That reservation has already ended, so it can't be changed. Reserve again if you still need it.");
                if (!CanManage(existing, item))
                    return (false, $"Only {Person(existing.ReservedBy)}, or Engineer and up on this item's Line, can change this reservation.");
            }

            string reason = (input.Reason ?? "").Trim();
            if (reason.Length == 0) return (false, "Say what it's reserved for.");
            if (reason.Length > ReasonMax) return (false, $"Keep the reason to {ReasonMax} characters.");

            DateTime start = (input.Start ?? today).Date;
            DateTime? end = input.UntilNotice ? null : input.End?.Date;
            if (!input.UntilNotice && end == null) return (false, "Pick an end date, or choose Until notice.");
            if (end.HasValue && end.Value < start) return (false, "The end date is before the start date.");
            if (end.HasValue && end.Value < today) return (false, "The end date has already passed.");

            bool isComp = InventoryService.IsCompressorType(item.Type);
            bool isMotor = InventoryService.IsMotorType(item.Type);
            var active = item.ActiveVariants.ToDictionary(v => v.Id);
            var owners = PinOwners(_db, item.ItemId);

            var comps = isComp ? _db.CompressorUnits.Where(c => c.ItemId == item.ItemId).ToList() : new List<CompressorUnit>();
            var motors = isMotor ? _db.MotorUnits.Where(m => m.ItemId == item.ItemId).ToList() : new List<MotorUnit>();

            var lines = new List<(ItemVariant V, int Qty, int Tc, List<int> Pins, List<string> New)>();
            var newSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // across every stack in this save
            foreach (var li in input.Lines)
            {
                var pins = (li.PinIds ?? new List<int>()).Distinct().ToList();
                var fresh = (li.NewIdents ?? new List<string>()).Select(s => (s ?? "").Trim()).Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (li.Qty <= 0 && pins.Count == 0 && fresh.Count == 0) continue;
                if (!active.TryGetValue(li.VariantId, out var v))
                    return (false, "One of those stacks is gone. Close this and open Reserve again.");
                int qty = Math.Max(0, li.Qty);
                int tc = isMotor ? Math.Min(Math.Max(0, li.Tc), qty) : 0;
                string where = $"V{v.VariantNumber}";

                if (!isComp && !isMotor && (pins.Count > 0 || fresh.Count > 0))
                    return (false, "Only compressors and TC motors can name specific units.");

                foreach (var id in pins)
                {
                    int? rid; int? vid; string status;
                    if (isComp)
                    {
                        var c = comps.FirstOrDefault(x => x.Id == id);
                        if (c == null) return (false, "One of the ticked units isn't on record any more. Open Reserve again.");
                        rid = c.ReservationId; vid = c.ItemVariantId; status = c.Status;
                    }
                    else
                    {
                        var m = motors.FirstOrDefault(x => x.Id == id);
                        if (m == null) return (false, "One of the ticked units isn't on record any more. Open Reserve again.");
                        rid = m.ReservationId; vid = m.ItemVariantId; status = m.Status;
                    }
                    if (status != UnitStatus.OnHand || vid != v.Id)
                        return (false, $"A ticked unit isn't on {where} any more. Open Reserve again.");
                    if (rid.HasValue && owners.TryGetValue(rid.Value, out var holder) && (existing == null || rid.Value != existing.Id))
                        return (false, $"A ticked unit is already held for {Person(holder)}.");
                }

                if (isComp)
                {
                    foreach (var s in fresh)
                    {
                        if (comps.Any(c => string.Equals(c.SerialNumber ?? "", s, StringComparison.OrdinalIgnoreCase)))
                            return (false, $"Serial {s} is already on record for {item.ItemId}. Tick it in the list instead.");
                        if (!newSerials.Add(s))
                            return (false, $"Serial {s} is typed on more than one stack. Name it on one only.");
                    }
                    int rows = comps.Count(c => c.Status == UnitStatus.OnHand && c.ItemVariantId == v.Id);
                    if (rows + fresh.Count > v.Quantity)
                        return (false, $"{where} has {v.Quantity} on the shelf and {rows} already recorded, so only {Math.Max(0, v.Quantity - rows)} new serial(s) can be added there.");
                    if (pins.Count + fresh.Count > qty)
                        return (false, $"{pins.Count + fresh.Count} units are named on {where} but only {qty} held there. Raise the quantity or untick some.");
                }
                if (isMotor)
                {
                    int rows = motors.Count(m => m.Status == UnitStatus.OnHand && m.ItemVariantId == v.Id);
                    if (rows + fresh.Count > v.ThermocoupledQty)
                        return (false, $"{where} has {v.ThermocoupledQty} TC motors and {rows} already recorded, so only {Math.Max(0, v.ThermocoupledQty - rows)} new lab # can be added there.");
                    if (pins.Count + fresh.Count > tc)
                        return (false, $"{pins.Count + fresh.Count} TC motors are named on {where} but only {tc} TC held there. Raise the TC count or untick some.");
                }
                lines.Add((v, qty, tc, pins, fresh));
            }

            if (lines.Sum(l => l.Qty) <= 0) return (false, "Hold at least 1 on one of the stacks.");

            string by = existing?.ReservedBy ?? _currentUser.Name;
            var claims = StockClaims.FromVariants(_db, item.ItemId, item.Variants);
            var replacement = lines.Select(l => StockClaims.NewHold(by, l.V.Id, l.Qty, l.Tc)).ToList();
            if (!claims.HoldsFit(existing?.Id ?? 0, replacement))
            {
                var parts = lines.Select(l =>
                {
                    var (mq, mt) = claims.Holdable(by, l.V.Id, existing?.Id);
                    return $"V{l.V.VariantNumber} up to {mq}" + (isMotor ? $" ({mt} TC)" : "");
                });
                return (false, "That's more than is free to hold right now (pending orders and other holds come first): "
                    + string.Join(", ", parts) + ".");
            }

            var now = DateTime.UtcNow;
            using var tx = _db.Database.BeginTransaction();
            try
            {
                bool isNew = existing == null;
                var r = existing ?? new Reservation
                {
                    ItemId = item.ItemId,
                    ReservedBy = _currentUser.Name,
                    CreatedAt = now,
                    Status = ReservationStatus.Active
                };
                string before = isNew ? "" : Describe(existing!, active);
                if (!isNew && r.EndDate != end) r.EndingNoticeSent = false;
                r.Reason = reason;
                r.StartDate = start;
                r.EndDate = end;
                if (isNew) { _db.Reservations.Add(r); _db.SaveChanges(); }
                else _db.ReservationLines.RemoveRange(r.Lines.ToList());

                r.Lines = lines.Select(l => new ReservationLine { ReservationId = r.Id, ItemVariantId = l.V.Id, Quantity = l.Qty, TcQuantity = l.Tc }).ToList();

                var keepPins = lines.SelectMany(l => l.Pins).ToHashSet();
                foreach (var c in comps.Where(c => c.ReservationId == r.Id && !keepPins.Contains(c.Id))) c.ReservationId = null;
                foreach (var m in motors.Where(m => m.ReservationId == r.Id && !keepPins.Contains(m.Id))) m.ReservationId = null;
                foreach (var c in comps.Where(c => keepPins.Contains(c.Id))) c.ReservationId = r.Id;
                foreach (var m in motors.Where(m => keepPins.Contains(m.Id))) m.ReservationId = r.Id;

                var recorded = new List<string>();
                foreach (var l in lines)
                {
                    foreach (var s in l.New)
                    {
                        if (isComp)
                            _db.CompressorUnits.Add(new CompressorUnit
                            {
                                ItemId = item.ItemId, ItemVariantId = l.V.Id, SerialNumber = s, Status = UnitStatus.OnHand,
                                RecordedAt = now, RecordedBy = _currentUser.Name, ReservationId = r.Id
                            });
                        else
                            _db.MotorUnits.Add(new MotorUnit
                            {
                                ItemId = item.ItemId, ItemVariantId = l.V.Id, LabNumber = s, Status = UnitStatus.OnHand,
                                RecordedAt = now, RecordedBy = _currentUser.Name, ReservationId = r.Id
                            });
                        recorded.Add($"{(isComp ? "SN" : "Lab")} {s} on V{l.V.VariantNumber}");
                    }
                }
                _db.SaveChanges();

                string after = Describe(r, active, comps, motors);
                string recNote = recorded.Count > 0 ? $" Recorded and held: {string.Join(", ", recorded)}." : "";
                if (isNew)
                    Log(_db, "Reserved", item.ItemId, item.ItemName, $"{Sentence(r)}: {after}.{recNote}", _currentUser.Name);
                else
                    Log(_db, "Reservation Changed", item.ItemId, item.ItemName,
                        $"{Sentence(r)}: {after} (was {before}).{recNote}", _currentUser.Name);

                if (!isNew && !SameName(r.ReservedBy, _currentUser.Name))
                    Notify(_db, r.ReservedBy, $"{Person(_currentUser.Name)} changed your hold on {item.ItemId} ({r.Reason}): now {after}.");

                _db.SaveChanges();
                tx.Commit();
                return (true, isNew
                    ? $"Reserved {r.Lines.Sum(l => l.Quantity)} of {item.ItemId} for {reason}."
                    : $"Reservation on {item.ItemId} updated.");
            }
            catch
            {
                tx.Rollback();
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        public (bool Ok, string Message) Release(int reservationId)
        {
            var r = _db.Reservations.Include(x => x.Lines).FirstOrDefault(x => x.Id == reservationId);
            if (r == null || !IsLive(r, DateTime.Today)) return (false, "That reservation has already ended.");
            var item = _db.InventoryItems.AsNoTracking().FirstOrDefault(i => i.ItemId == r.ItemId);
            if (!CanManage(r, item))
                return (false, $"Only {Person(r.ReservedBy)}, or Engineer and up on this item's Line, can release this hold.");

            int held = r.Lines.Sum(l => l.Quantity);
            r.Status = ReservationStatus.Released;
            r.EndedAt = DateTime.UtcNow;
            r.EndedBy = _currentUser.Name;
            r.EndNote = "Released";
            TrimPins(_db, r);
            Log(_db, "Reservation Released", r.ItemId, item?.ItemName,
                $"Hold of {held} for {Person(r.ReservedBy)} (\"{r.Reason}\") released.", _currentUser.Name);
            if (!SameName(r.ReservedBy, _currentUser.Name))
                Notify(_db, r.ReservedBy, $"{Person(_currentUser.Name)} released your hold on {r.ItemId} ({r.Reason}).");
            _db.SaveChanges();
            return (true, $"Released the hold on {r.ItemId}.");
        }

        /// <summary>"2 on V1 [SN a, SN b], 1 on V2", for log rows.</summary>
        private string Describe(Reservation r, Dictionary<int, ItemVariant> active,
            List<CompressorUnit>? comps = null, List<MotorUnit>? motors = null)
        {
            comps ??= _db.CompressorUnits.Where(c => c.ReservationId == r.Id).ToList();
            motors ??= _db.MotorUnits.Where(m => m.ReservationId == r.Id).ToList();
            return string.Join(", ", r.Lines.Where(l => l.Quantity > 0).Select(l =>
            {
                string v = active.TryGetValue(l.ItemVariantId, out var av) ? $"V{av.VariantNumber}" : "a removed stack";
                var names = comps.Where(c => c.ReservationId == r.Id && c.ItemVariantId == l.ItemVariantId)
                        .Select(c => !string.IsNullOrWhiteSpace(c.SerialNumber) ? "SN " + c.SerialNumber : "Lab " + c.LabNumber)
                    .Concat(motors.Where(m => m.ReservationId == r.Id && m.ItemVariantId == l.ItemVariantId).Select(m => "Lab " + m.LabNumber))
                    .ToList();
                return $"{l.Quantity}{(l.TcQuantity > 0 ? $" ({l.TcQuantity} TC)" : "")} on {v}"
                    + (names.Count > 0 ? $" [{string.Join(", ", names)}]" : "");
            }));
        }
    }
}
