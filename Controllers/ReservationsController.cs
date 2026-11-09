using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;
using Visual_Inventory_System.Services;

namespace Visual_Inventory_System.Controllers
{
    /// <summary>One row on Reserved Items.</summary>
    public class ReservedRowViewModel
    {
        public Reservation R { get; set; } = null!;
        public InventoryItem? Item { get; set; }
        public string ItemLine { get; set; } = "";
        public string ItemBranch { get; set; } = "";
        public string TypeKey { get; set; } = "other";
        public List<string> Held { get; set; } = new();
        public List<string> Named { get; set; } = new();
        public int HeldQty { get; set; }
        public bool Mine { get; set; }
        public bool CanManage { get; set; }
        public int? DaysLeft { get; set; }
    }

    public class ReservedGroupViewModel
    {
        public string Title { get; set; } = "";
        public bool IsPurpose { get; set; }
        public List<ReservedRowViewModel> Rows { get; set; } = new();
    }

    public class ReservedTierViewModel
    {
        public string Title { get; set; } = "";
        public string Sub { get; set; } = "";
        public bool Mine { get; set; }
        public bool Dim { get; set; }
        public List<ReservedGroupViewModel> Groups { get; set; } = new();
        public List<string> EmptyGroups { get; set; } = new();
        public int Count => Groups.Sum(g => g.Rows.Count);
    }

    /// <summary>
    /// Reserved Items (2026-11-08, docs/Unit_Reservations_Spec.md section 6) and the
    /// Reserve dialog's form/save/release. Every signed-in user can read the page
    /// (the global RequireNameFilter covers it); reserving, editing and releasing
    /// are Standard+ here and checked again, per item and per reservation, in
    /// ReservationService.
    /// </summary>
    public class ReservationsController : Controller
    {
        private readonly ReservationService _reservations;
        private readonly CurrentUserService _currentUser;
        private readonly AppDbContext _db;

        public ReservationsController(ReservationService reservations, CurrentUserService currentUser, AppDbContext db)
        {
            _reservations = reservations;
            _currentUser = currentUser;
            _db = db;
        }

        private static bool Same(string? a, string? b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        public IActionResult Index()
        {
            var today = DateTime.Today;
            var live = _reservations.AllLive();
            var itemIds = live.Select(r => r.ItemId).Distinct().ToList();
            var items = _db.InventoryItems.AsNoTracking().Include(i => i.Variants)
                .Where(i => itemIds.Contains(i.ItemId)).ToList()
                .ToDictionary(i => i.ItemId);
            var pinIds = live.Select(r => r.Id).ToList();
            var comps = _db.CompressorUnits.AsNoTracking()
                .Where(c => c.ReservationId != null && pinIds.Contains(c.ReservationId.Value) && c.Status == UnitStatus.OnHand).ToList();
            var motors = _db.MotorUnits.AsNoTracking()
                .Where(m => m.ReservationId != null && pinIds.Contains(m.ReservationId.Value) && m.Status == UnitStatus.OnHand).ToList();

            ReservedRowViewModel Row(Reservation r)
            {
                items.TryGetValue(r.ItemId, out var item);
                string line = (item?.Line ?? "").Trim();
                var row = new ReservedRowViewModel
                {
                    R = r,
                    Item = item,
                    ItemLine = line,
                    ItemBranch = OrgStructure.BranchFor(line) ?? "",
                    TypeKey = InventoryService.IsCompressorType(item?.Type) ? "compressor" : InventoryService.IsMotorType(item?.Type) ? "motor" : "other",
                    Mine = _reservations.IsMine(r),
                    CanManage = _reservations.CanManage(r, item),
                    HeldQty = r.Lines.Sum(l => l.Quantity),
                    DaysLeft = r.EndDate.HasValue ? (r.EndDate.Value.Date - today).Days : null
                };
                foreach (var l in r.Lines.Where(l => l.Quantity > 0))
                {
                    var v = item?.Variants.FirstOrDefault(x => x.Id == l.ItemVariantId);
                    string where = v == null ? "a removed stack"
                        : $"V{v.VariantNumber} · {LocationCodec.FriendlyPath(v.Parent, v.Major, v.Sub, v.Rack, v.Row, v.FdaString)}";
                    row.Held.Add($"{l.Quantity}{(l.TcQuantity > 0 ? $" ({l.TcQuantity} TC)" : "")} on {where}");
                }
                row.Named.AddRange(comps.Where(c => c.ReservationId == r.Id)
                    .Select(c => !string.IsNullOrWhiteSpace(c.SerialNumber) ? "SN " + c.SerialNumber : "Lab " + c.LabNumber));
                row.Named.AddRange(motors.Where(m => m.ReservationId == r.Id).Select(m => "Lab " + m.LabNumber));
                return row;
            }

            // Soonest end first, until-notice last, then oldest first.
            IEnumerable<ReservedRowViewModel> Sorted(IEnumerable<ReservedRowViewModel> rows) =>
                rows.OrderBy(x => x.R.EndDate.HasValue ? 0 : 1).ThenBy(x => x.R.EndDate).ThenBy(x => x.R.CreatedAt);

            var rowsAll = live.Select(Row).ToList();
            var tiers = new List<ReservedTierViewModel>();

            // 1. Yours, one group per reason (Kason's purpose groups, rule 4).
            var mine = rowsAll.Where(x => x.Mine).ToList();
            var mineTier = new ReservedTierViewModel { Title = "Your reservations", Sub = "Edit or release from here", Mine = true };
            foreach (var g in mine.GroupBy(x => x.R.Reason.Trim(), StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(g => g.Min(x => x.R.EndDate ?? DateTime.MaxValue)))
                mineTier.Groups.Add(new ReservedGroupViewModel { Title = "Reserved for " + g.First().R.Reason.Trim(), IsPurpose = true, Rows = Sorted(g).ToList() });
            if (mineTier.Count == 0) mineTier.EmptyGroups.Add("You have nothing reserved.");
            tiers.Add(mineTier);

            var rest = rowsAll.Where(x => !x.Mine).ToList();
            string myLine = (_currentUser.Line ?? "").Trim();
            string myBranch = myLine.Length > 0 ? (OrgStructure.BranchFor(myLine) ?? "") : (_currentUser.Branch ?? "").Trim();

            void AddTier(string title, string sub, IEnumerable<ReservedRowViewModel> rows, bool dim = false)
            {
                var list = Sorted(rows).ToList();
                var t = new ReservedTierViewModel { Title = title, Sub = sub, Dim = dim };
                if (list.Count > 0) t.Groups.Add(new ReservedGroupViewModel { Rows = list });
                else t.EmptyGroups.Add("Nothing reserved.");
                tiers.Add(t);
            }

            if (myLine.Length == 0 && myBranch.Length == 0)
            {
                // No Line or Branch (fails open everywhere else too): nothing to grey.
                AddTier("Everything else", "Every other live hold", rest);
            }
            else
            {
                if (myLine.Length > 0)
                {
                    AddTier($"Your Line · {myLine}", "", rest.Where(x => Same(x.ItemLine, myLine)));
                    var branchLines = OrgStructure.BranchLines.TryGetValue(myBranch, out var bl) ? bl.Where(l => !Same(l, myLine)).ToList() : new List<string>();
                    if (myBranch.Length > 0)
                        AddTier($"Rest of {myBranch}", string.Join(" · ", branchLines),
                            rest.Where(x => !Same(x.ItemLine, myLine) && Same(x.ItemBranch, myBranch)));
                }
                else
                {
                    AddTier($"Your Branch · {myBranch}", string.Join(" · ", OrgStructure.BranchLines.TryGetValue(myBranch, out var bl2) ? bl2 : Array.Empty<string>()),
                        rest.Where(x => Same(x.ItemBranch, myBranch)));
                }

                var noLine = rest.Where(x => x.ItemLine.Length == 0).ToList();
                if (noLine.Count > 0) AddTier("Items with no Line", "Unassigned items show to everyone", noLine);

                // Other branches, greyed (Kason: less important, still readable).
                var others = rest.Where(x => x.ItemLine.Length > 0 && !Same(x.ItemBranch, myBranch)).ToList();
                var otherTier = new ReservedTierViewModel { Title = "Other branches", Sub = "Read only", Dim = true };
                foreach (var b in OrgStructure.BranchLines.Keys.Where(b => !Same(b, myBranch)))
                {
                    var inB = others.Where(x => Same(x.ItemBranch, b)).ToList();
                    if (inB.Count > 0) otherTier.Groups.Add(new ReservedGroupViewModel { Title = $"{b} · {inB.Count}", Rows = Sorted(inB).ToList() });
                    else otherTier.EmptyGroups.Add($"{b} · nothing reserved");
                }
                var unknown = others.Where(x => x.ItemBranch.Length == 0).ToList();
                if (unknown.Count > 0) otherTier.Groups.Add(new ReservedGroupViewModel { Title = $"Lines not in any branch · {unknown.Count}", Rows = Sorted(unknown).ToList() });
                tiers.Add(otherTier);
            }

            ViewBag.Tiers = tiers;
            ViewBag.TotalReservations = live.Count;
            ViewBag.TotalHeld = rowsAll.Sum(x => x.HeldQty);
            ViewBag.EndingThisWeek = rowsAll.Count(x => x.DaysLeft.HasValue && x.DaysLeft.Value <= 7);
            ViewBag.UntilNotice = rowsAll.Count(x => !x.R.EndDate.HasValue);
            return View();
        }

        /// <summary>The Reserve dialog's body, fetched into Search Center's #reserveModal.</summary>
        [HttpGet]
        public IActionResult Form(string itemId, int? id)
        {
            if (_currentUser.Level < AccessLevels.Standard)
                return StatusCode(403, "Reserving needs Standard access or higher.");
            var model = _reservations.BuildForm(itemId ?? "", id);
            if (model == null) return BadRequest("That item or reservation isn't available any more. Refresh and try again.");
            if (!_reservations.CanReserve(model.Item))
                return BadRequest("You can only reserve items on your own Line.");
            if (model.Existing != null && !_reservations.CanManage(model.Existing, model.Item))
                return BadRequest($"Only {ReservationService.Person(model.Existing.ReservedBy)}, or Engineer and up on this Line, can change this reservation.");
            return PartialView("_ReserveForm", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult Save()
        {
            var f = Request.Form;
            DateTime? D(string key) =>
                DateTime.TryParseExact(f[key].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            int I(string key) => int.TryParse(f[key].ToString(), out var n) ? n : 0;

            var input = new ReservationInput
            {
                Id = I("id"),
                ItemId = f["itemId"].ToString(),
                Reason = f["reason"].ToString(),
                Start = D("start"),
                End = D("end"),
                UntilNotice = f["untilMode"].ToString() == "notice"
            };
            var variantIds = f.Keys.Where(k => k.StartsWith("qty_"))
                .Select(k => int.TryParse(k.Substring(4), out var v) ? v : 0).Where(v => v > 0).Distinct();
            foreach (var vid in variantIds)
            {
                input.Lines.Add(new ReservationLineInput
                {
                    VariantId = vid,
                    Qty = I($"qty_{vid}"),
                    Tc = I($"tc_{vid}"),
                    PinIds = f[$"pin_{vid}"].Select(s => int.TryParse(s, out var u) ? u : 0).Where(u => u > 0).ToList(),
                    NewIdents = f[$"new_{vid}"].Select(s => s ?? "").ToList()
                });
            }

            try
            {
                var (ok, message) = _reservations.Save(input);
                TempData[ok ? "Success" : "Error"] = message;
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Reserve failed: " + ex.GetBaseException().Message;
            }
            return BackTo(input.ItemId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult Release(int id)
        {
            string? itemId = _db.Reservations.AsNoTracking().Where(r => r.Id == id).Select(r => r.ItemId).FirstOrDefault();
            try
            {
                var (ok, message) = _reservations.Release(id);
                TempData[ok ? "Success" : "Error"] = message;
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Release failed: " + ex.GetBaseException().Message;
            }
            return BackTo(itemId);
        }

        /// <summary>
        /// Back to the page the request came from, always as a local path. From
        /// Search Center the Item Card reopens on the item just changed, so the
        /// result is right there.
        /// </summary>
        private IActionResult BackTo(string? itemId)
        {
            string referer = Request.Headers["Referer"].ToString();
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return RedirectToAction("Index");
            string path = uri.AbsolutePath;
            if (path.Equals("/Home/SearchCenter", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(itemId))
            {
                var q = QueryHelpers.ParseQuery(uri.Query)
                    .Where(kv => kv.Key != "openCard" && kv.Key != "reserve")
                    .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
                q["openCard"] = itemId;
                return LocalRedirect(QueryHelpers.AddQueryString(path, q));
            }
            return LocalRedirect(path + uri.Query);
        }
    }
}
