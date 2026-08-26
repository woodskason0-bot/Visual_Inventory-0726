using Visual_Inventory_System.Models;
using Visual_Inventory_System.Services;
using Visual_Inventory_System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Linq;
using Visual_Inventory_System.Models.ViewModels;
using System;
using System.Collections.Generic;

namespace Visual_Inventory_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly OrderService _orderService;
        private readonly InventoryService _inventoryService;
        private readonly EmailService _emailService;
        private readonly AppDbContext _db;
        private readonly CurrentUserService _currentUser;
        private readonly NotificationService _notifications;

        public HomeController(OrderService orderService, InventoryService inventoryService, EmailService emailService, AppDbContext db, CurrentUserService currentUser, NotificationService notifications)
        {
            _orderService = orderService;
            _inventoryService = inventoryService;
            _emailService = emailService;
            _db = db;
            _currentUser = currentUser;
            _notifications = notifications;
        }

        // Pass 28 (2d) -- the swap. "/" now renders Command Center directly
        // (KPI strip, map + location list, Activity Feed, Need PN / Need Serial
        // donuts, Quick Actions, Stock Alerts/Pending/Incoming Shipments) instead
        // of the old combined dashboard+search page. Advanced Filters/Omni
        // Search/results grid/Export Wizard/Compressor+Motor Registry moved to
        // SearchCenter() in 2b and stay there -- Index() takes no query params
        // anymore, there's no holo-viewer here to feed. Picks up the sidebar's
        // light/shadow-card visual language rather than the old page's dark/
        // unstyled look -- Kason's call once he saw the sidebar next to the old
        // dashboard, ahead of Phase 4's originally-planned broader visual pass.
        public IActionResult Index()
        {
            var allItems = _inventoryService.GetAll().ToList();
            PopulateSearchViewBag(allItems);
            // The view recomputes its own zoneDataMap from a fresh GetAll() --
            // needs the service reference for that.
            ViewBag.InventoryService = _inventoryService;

            // KPI strip.
            ViewBag.TotalItems = allItems.Count;
            ViewBag.LowStockCount = allItems.Count(i => i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold && i.Quantity > 0);
            ViewBag.OutOfStockCount = allItems.Count(i => i.Quantity == 0);
            ViewBag.ActiveLocationsCount = allItems.Select(i => i.Parent).Where(p => !string.IsNullOrEmpty(p)).Distinct().Count();
            ViewBag.PnBacklogCount = GetPnBacklogItems(allItems).Count;

            // Activity Feed (top 5). Pass 15: same Line visibility as View Logs.
            ViewBag.RecentActivity = _inventoryService.ApplyLogVisibility(_db.TransactionLogs
                .AsNoTracking())
                .OrderByDescending(t => t.Timestamp)
                .Take(5)
                .ToList();

            // Map zones -- the view builds zoneDataMap client-side from this plus
            // LocationParents (already in PopulateSearchViewBag), and renders a
            // Location List from that same object.
            ViewBag.MapZones = _db.LocationZones.AsNoTracking()
                .Join(_db.Locations.Where(l => l.Level == LocationLevel.Parent && l.IsActive),
                      z => z.LocationId, l => l.Id,
                      (z, l) => new { l.Name, z.X, z.Y, z.W, z.H })
                .ToList()
                .Select(z => (Name: z.Name, Code: LocationCodec.Encode(z.Name),
                              X: z.X, Y: z.Y, W: z.W, H: z.H))
                .ToList();

            // Need Serial donut: real on-hand compressor STOCK (by quantity) that
            // has no serial recorded, same "physical gap" framing Need PN uses --
            // not blank-serial rows within the CompressorUnits table alone. Only
            // 184 of the 849 real on-hand compressor units have ever been logged
            // into CompressorUnits at all, and whoever creates a row tends to
            // fill the serial in at the same time, so "blank among rows that
            // exist" reads as a near-zero gap by construction, not the real one.
            // Found live (Kason: Need PN said 100%, Need Serial said 0% right
            // next to it, those should mean the same thing) -- verified against
            // the real db: true gap is 849 - 184 = 665 (78%), not 0.
            var compressorItems = allItems.Where(i => InventoryService.IsCompressorType(i.Type)).ToList();
            var compItemIds = compressorItems.Select(i => i.ItemId).ToHashSet();
            var onHandCompUnits = _db.CompressorUnits.AsNoTracking()
                .Where(u => u.Status == UnitStatus.OnHand).ToList();
            int compQtyTotal = compressorItems.Sum(i => i.Quantity);
            int compSerialsRecorded = onHandCompUnits.Count(u =>
                compItemIds.Contains(u.ItemId) && !string.IsNullOrWhiteSpace(u.SerialNumber));
            ViewBag.CompUnitsTotal = compQtyTotal;
            ViewBag.CompUnitsNeedSerialCount = Math.Max(0, compQtyTotal - compSerialsRecorded);

            // Bottom row: Pending combines an Order in flight with a held (not yet
            // approved/rejected) Intake batch -- two different tables, one
            // number. Incoming Shipments is anything not yet marked Done on the
            // Deliveries board.
            ViewBag.PendingCount = _db.Orders.AsNoTracking().Count(o => o.Status == "Pending")
                + _db.IntakeBatches.AsNoTracking().Count(b => b.Status == IntakeStatus.Pending);
            ViewBag.IncomingShipmentsCount = _db.Deliveries.AsNoTracking().Count(d => d.Status != "Done");

            return View("CommandCenter");
        }

        // Pass 28 (2b) -- Search Center: Advanced Filters, Omni Search, results
        // grid, Export Wizard, Compressor/Motor Registry, Modify Stock. Left
        // dark-themed/unstyled on purpose -- the visual match to the sidebar's
        // new look is Phase 4, not this pass (Command Center picked that up
        // early in 2c/2d; this page is still waiting on it).
        public IActionResult SearchCenter(string? omniSearch, string? filterRheem, string? filterType, string? filterBrand, string? filterNotes, string? filterBranch, string? mode, string? stockView)
        {
            var allItems = _inventoryService.GetAll().ToList();
            PopulateSearchViewBag(allItems);

            var currentDraft = _orderService.GetCurrentDraft();
            var draftEntries = currentDraft.Entries;
            ViewBag.DraftItemIds = draftEntries.Select(e => e.ItemId).ToHashSet();

            if (mode == "Ledger")
            {
                ViewBag.SearchResult = new SearchResult { Mode = "Ledger", Items = new List<InventoryItem>() };
                ViewBag.Ledger = new LedgerViewModel { Entries = draftEntries };
                ViewBag.InventoryService = _inventoryService;
                return View();
            }

            var searchResult = new SearchResult { Mode = mode ?? "None" };
            if (!string.IsNullOrEmpty(stockView))
            {
                // Pass 28 (2c): Command Center's KPI strip links here. None of these
                // three are expressible through the existing Rheem/Type/Brand/Notes
                // filters (no quantity-comparison dimension), so this is a fixed
                // named view over the item list instead of a Search() call -- same
                // predicates GetLowStockItems/GetOutOfStockItems/GetPnBacklogItems
                // use everywhere else, not a fourth redefinition.
                searchResult.Items = stockView switch
                {
                    "LowStock" => GetLowStockItems(allItems),
                    "OutOfStock" => GetOutOfStockItems(allItems),
                    "NeedsPN" => GetPnBacklogItems(allItems),
                    _ => new List<InventoryItem>()
                };
                searchResult.Mode = "Filter";
            }
            else if (mode != "None")
            {
                var foundItems = _inventoryService.Search(omniSearch, filterRheem, filterType, filterBrand, filterNotes, filterBranch);
                searchResult.Items = foundItems;

                if (!string.IsNullOrEmpty(omniSearch)) searchResult.Mode = "Omni";
                else if (foundItems.Any() || mode == "Filter") searchResult.Mode = "Filter";
            }

            ViewBag.SearchResult = searchResult;
            ViewBag.InventoryService = _inventoryService;

            ViewBag.OmniSearch = omniSearch ?? "";
            ViewBag.FilterRheem = filterRheem ?? "";
            ViewBag.FilterType = filterType ?? "";
            ViewBag.FilterBrand = filterBrand ?? "";
            ViewBag.FilterNotes = filterNotes ?? "";
            ViewBag.FilterBranch = filterBranch ?? "";

            return View();
        }

        // Shared "what counts as low stock / out of stock / needs a PN" --
        // reused by Search Center's stockView filter and Command Center's
        // Stock Alerts card / Need PN donut, so the definition can't drift into
        // a third hand-typed copy the way Rack/Row and Locations already did
        // before Pass 7C/23. GetLowStockItems deliberately does NOT exclude
        // zero-quantity items -- same "includes zero-stock-with-threshold items"
        // definition the dashboard's Stock Alerts widget has always used; the KPI
        // strip's separate "Low Stock" COUNT (which does exclude zero, so it
        // doesn't double-count with "Out of Stock") stays its own inline
        // expression in Index(), not routed through this.
        private static bool NeedsRheemPn(string? pn) =>
            string.IsNullOrWhiteSpace(pn) || pn.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase);

        private static List<InventoryItem> GetLowStockItems(List<InventoryItem> allItems) =>
            allItems.Where(i => i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold).OrderBy(i => i.Quantity).ToList();

        private static List<InventoryItem> GetOutOfStockItems(List<InventoryItem> allItems) =>
            allItems.Where(i => i.Quantity == 0).OrderBy(i => i.ItemName).ToList();

        private static List<InventoryItem> GetPnBacklogItems(List<InventoryItem> allItems) =>
            allItems.Where(i => NeedsRheemPn(i.RheemPartNumber)).OrderBy(i => i.ItemName).ToList();

        // Server data every Search Center-side view needs regardless of which
        // page renders it (Advanced Filters, Omni Search, results grid, Export
        // Wizard, Compressor/Motor Registry, the Modify Stock shared partial) --
        // shared by Index() and SearchCenter() so this JSON-shaping logic isn't
        // kept in sync by hand in two places, same reasoning as every other
        // "eight copies of the same vocabulary" trap this project has hit.
        private void PopulateSearchViewBag(List<InventoryItem> allItems)
        {
            // The Branch this session is scoped to, for graying out the other
            // Quick Filter branch buttons -- a specific Line wins (derive its
            // Branch), else a whole-Branch assignment, else blank (unscoped,
            // sees everything, so nothing gets grayed out for them either).
            ViewBag.MyBranch = !string.IsNullOrWhiteSpace(_currentUser.Line)
                ? (OrgStructure.BranchFor(_currentUser.Line) ?? "")
                : (_currentUser.Branch ?? "");

            var autocompleteData = allItems.Select(i => new {
                id = i.ItemId,
                name = i.ItemName,
                // Identity fields the Modify Stock "Edit Details" pane prefills.
                rpn = i.RheemPartNumber,
                line = i.Line,
                brand = i.Brand,
                desc = i.Description,
                threshold = i.AlertThreshold,
                quantity = i.Quantity,
                // Item TYPE drives the motors-only TC gate on the client. A type
                // that ends with "Motor" (Motor / ID Motor / OD Motor) is TC-eligible.
                type = i.Type,
                // Primary-variant location (back-compat seed for the modify-stock widget).
                parent = i.Parent,
                major = i.Major,
                sub = i.Sub,
                rack = i.Rack,
                row = i.Row,
                fda = i.FdaString,
                // Every active physical location; powers the variant selector,
                // transfer seeding, and client-side merge detection.
                variants = i.ActiveVariants.OrderBy(v => v.VariantNumber).Select(v => new {
                    vid = v.Id,
                    num = v.VariantNumber,
                    parent = v.Parent,
                    major = v.Major,
                    sub = v.Sub,
                    rack = v.Rack,
                    row = v.Row,
                    fda = v.FdaString,
                    qty = v.Quantity,
                    // How many of this variant's qty are thermocoupled (the tagged
                    // subset). Feeds the modal's TC cap so the human never guesses.
                    tcqty = v.ThermocoupledQty
                }).ToList()
            }).ToList();
            ViewBag.AutocompleteJson = System.Text.Json.JsonSerializer.Serialize(autocompleteData);
            ViewBag.OrgStructureJson = System.Text.Json.JsonSerializer.Serialize(OrgStructure.BranchLines);
            // Pass 7C: the location vocabulary, straight from the table. Replaces the
            // hardcoded <option value="RLB"> list AND the locMap {} object that used
            // to be maintained by hand in this view.
            ViewBag.LocationParents = BuildLocationParents();
            ViewBag.LocationTreeJson = System.Text.Json.JsonSerializer.Serialize(BuildLocationTree());
            ViewBag.LocationHierarchyCodedJson = System.Text.Json.JsonSerializer.Serialize(BuildLocationHierarchyCoded());
            ViewBag.RackRowJson = System.Text.Json.JsonSerializer.Serialize(BuildRackRowMap());
            // Pass 7A: the Team / Project picker is data-driven now. Only ACTIVE
            // teams are offered; hidden ones stay on the items already using them.
            ViewBag.ActiveTeams = _db.Teams.AsNoTracking()
                .Where(t => t.IsActive).OrderBy(t => t.Name).ToList();
            // Team -> home Line, for the registration/ownership auto-fill and the
            // compressor filter's Team suggestion. Metadata only -- picking a Team
            // suggests this Line, never enforces it. Teams with no Line assigned
            // are simply absent from the map (JS treats a missing key as "no suggestion").
            ViewBag.TeamLinesJson = System.Text.Json.JsonSerializer.Serialize(
                _db.Teams.AsNoTracking().Where(t => t.IsActive && t.Line != null)
                    .ToDictionary(t => t.Name, t => t.Line));
            // The export filter offers HIDDEN teams too -- you still need to pull a
            // report on a team that was retired last quarter.
            ViewBag.AllTeams = _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToList();
        }

        // Pass 28 (2b): now that a second real page (SearchCenter) can trigger
        // the same cart/stock actions Index always could, a hardcoded "back to
        // Index" would silently strand a SearchCenter user back on the old
        // dashboard after every action. Same fix already used by
        // LogCompressorUnits/LogMotorUnits/CancelPersisted/SetTheme -- go back to
        // the exact page the request came from, falling back to Index only when
        // there's no Referer at all (e.g. a bookmarked POST replay).
        private IActionResult SmartRedirect(string fallbackAction = "Index")
        {
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction(fallbackAction) : Redirect(referer);
        }

        // ============================
        // CART ACTIONS
        // ============================
        // Pass 9: registering and ordering dropped from Engineer to Standard.
        // Interns come in as Standard and could previously add stock to items that
        // already existed but could neither create one nor request anything -- a
        // half-capability that made no sense. Scrap, Ownership, Edit Details and
        // thresholds stay at Engineer: changing or destroying what exists is a
        // different kind of trust than adding to it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult AddToCart(string itemId, int quantity, int? requestedVariantId = null, int thermocoupledCount = 0)
        {
            _orderService.AddItem(itemId, quantity, requestedVariantId, thermocoupledCount);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = true, message = "Added to cart!" });
            TempData["Message"] = "Item added to cart.";
            return SmartRedirect();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult RemoveFromLedger(string itemId)
        {
            _orderService.RemoveItem(itemId);
            TempData["Message"] = "Item removed.";
            return SmartRedirect();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SubmitLedger()
        {
            if (!_orderService.GetCurrentDraft().Entries.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return SmartRedirect();
            }

            try
            {
                _orderService.Submit();
                TempData["Success"] = "Order submitted successfully.";
            }
            catch (Exception ex) { TempData["Error"] = "Submit failed: " + ex.GetBaseException().Message; }
            return SmartRedirect();
        }

        // ============================
        // TRANSACTIONS & ADMIN ACTIONS
        // ============================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult UpdateAlertThreshold(string itemId, int threshold)
        {
            if (string.IsNullOrWhiteSpace(itemId) || threshold < 0) return RedirectToAction("Index");

            _inventoryService.UpdateAlertThreshold(itemId, threshold);
            TempData["Success"] = $"Alert threshold for ID {itemId} set to {threshold}.";
            return RedirectToAction("Index");
        }

        // Bulk default: set the alert threshold for many items at once.
        // Management+ only (supervisors/management), NOT engineers. Scope follows
        // the same team rule as the low-stock summary: a manager with team(s) sets
        // it for those teams' items; a manager with no teams (e.g. Kevin) sets it
        // for every item. Drives all alert displays + notifications via AlertThreshold.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Management)]
        public IActionResult SetDefaultThreshold(int threshold)
        {
            if (threshold < 0) return RedirectToAction("Index");

            List<string> myTeams = new List<string>();
            try
            {
                myTeams = (from u in _db.Users.AsNoTracking()
                           join ut in _db.UserTeams.AsNoTracking() on u.Id equals ut.UserId
                           where u.UserName == _currentUser.Name
                           select ut.TeamName).ToList();
            }
            catch { myTeams = new List<string>(); }

            int count = _inventoryService.SetDefaultThreshold(myTeams, threshold);
            TempData["Success"] = myTeams.Count == 0
                ? $"All item alert thresholds have been set to {threshold} ({count} items)."
                : $"All {string.Join("/", myTeams)} item alert thresholds have been set to {threshold} ({count} items).";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ModifyStock(string itemId, string actionType, int quantity, string? newGroup, string? newTeam,
            string? newParent, string? newMajor, string? newSub, string? newRack, string? newRow,
            string? targetVariant = null, int? transferQty = null, int thermocoupledQty = 0,
            string? newRheemPart = null, string? newDescription = null, string? newBrand = null,
            string? newLine = null)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return SmartRedirect();

            // "Add" and "Adjustment" are allowed at Standard (runners restock and fix
            // counts on recount); Scrap, Ownership, and identity edits (Edit Details)
            // require Engineer or higher -- same tier that registers items.
            // Location Transfer is non-destructive (no quantity change) so it stays at Standard.
            var elevated = new[] { "Scrap", "Ownership", "Edit Details" };
            if (elevated.Contains(actionType, StringComparer.OrdinalIgnoreCase)
                && _currentUser.Level < AccessLevels.Engineer)
            {
                TempData["AuthError"] = $"Sorry, you're not authorized to perform '{actionType}' " +
                    "(requires Engineer access or higher). Please contact your supervisor or manager for assistance.";
                return SmartRedirect();
            }

            // Identity edits never touch quantities/variants -- separate path.
            if (string.Equals(actionType, "Edit Details", StringComparison.OrdinalIgnoreCase))
            {
                // Pass 7B: Edit Details no longer edits Line -- that pane's Branch/Line
                // fields moved to Internal - Transfer. newLine is passed as null so a
                // stale value posted from the (hidden) transfer pane can never silently
                // reassign an item's Line while someone edits its part number.
                var (ok, message) = _inventoryService.UpdateItemDetails(itemId, newRheemPart, newDescription, newBrand, null);
                if (ok) TempData["Success"] = $"{itemId}: {message}";
                else TempData["Error"] = message;
                return SmartRedirect();
            }

            // Compressor serials on an Add, same flat serial_N convention
            // CreateItem already reads (Pass 17). No-op for any other action --
            // InventoryService.ModifyStock only consults this on Add, and the
            // client only ever renders these boxes for Add + Type=Compressor.
            var serials = Request.Form.Keys
                .Where(k => k.StartsWith("serial_"))
                .Select(k => Request.Form[k].ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            try
            {
                var result = _inventoryService.ModifyStock(itemId, actionType, quantity, newGroup, newTeam,
                    newParent, newMajor, newSub, newRack, newRow, targetVariant, transferQty, thermocoupledQty,
                    newLine, serials);
                if (result != null)
                {
                    TempData["Success"] = $"Transaction '{actionType}' applied to {itemId}.";

                    // Edge-triggered low-stock email: fires only on the crossing.
                    _emailService.CheckAndSendStockAlert(result.Value.item, result.Value.oldQty, result.Value.newQty);
                }
                else
                {
                    // null now only means the item lookup failed -- used to fall
                    // through with no toast at all.
                    TempData["Error"] = $"Transaction '{actionType}' was not applied — item '{itemId}' not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Transaction failed: {ex.Message}";
            }
            return SmartRedirect();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult CreateItem(InventoryItem newItem, int thermocoupledQty = 0)
        {
            try
            {
                newItem.ItemName ??= "Unnamed Item";
                newItem.Type ??= "General";
                newItem.Brand ??= "Unknown";
                newItem.Description ??= "";

                // --- RHEEM PART NUMBER (required on NEW registrations) ---
                // Leadership: every item should carry one. Legacy rows may still
                // be blank, but nothing new enters without a PN, and a PN that
                // already belongs to another family is rejected so the same
                // physical part can't be registered twice under two ItemIds.
                newItem.RheemPartNumber = (newItem.RheemPartNumber ?? "").Trim();
                if (newItem.RheemPartNumber.Length == 0)
                {
                    TempData["Error"] = "Registration needs a Rheem part number (check the physical label).";
                    return RedirectToAction("Index");
                }
                var pnOwner = _inventoryService.FindByRheemPart(newItem.RheemPartNumber);
                if (pnOwner != null)
                {
                    TempData["Error"] = $"Rheem PN '{newItem.RheemPartNumber}' already exists as {pnOwner.ItemId} ({pnOwner.ItemName}). " +
                        "Add stock to that item instead of registering a duplicate.";
                    return RedirectToAction("Index");
                }
                newItem.Parent ??= "";
                newItem.Major ??= "";
                newItem.Sub ??= "";
                newItem.Rack ??= "";
                newItem.Row ??= "";

                // Team is now OPTIONAL and no longer defaults to Samurai. Blank is a
                // real choice; the project code follows whichever team was picked.
                newItem.Team = (newItem.Team ?? "").Trim();
                if (string.Equals(newItem.Team, "N/A", StringComparison.OrdinalIgnoreCase))
                    newItem.Team = "";
                newItem.ProjectCode = newItem.Team.Length == 0
                    ? ""
                    : (_db.Teams.FirstOrDefault(t => t.Name == newItem.Team)?.ProjectCode ?? "");
                newItem.Line ??= "";
                // Pass 15: Line is mandatory on new registrations going forward -- the
                // old "blank fails open" rollout default stays true for items already
                // in the system (nothing here retroactively touches them), but nobody
                // should be able to register something with no owner from here on.
                if (newItem.Line.Length == 0)
                {
                    TempData["Error"] = "A Line is required to register an item.";
                    return RedirectToAction("Index");
                }
                if (!OrgStructure.IsValidLine(newItem.Line))
                {
                    TempData["Error"] = $"'{newItem.Line}' isn't a recognized Line.";
                    return RedirectToAction("Index");
                }

                // Group is not a form field -- it's derived from the item's own
                // Line (the Branch/Line picked on THIS registration form), so the
                // ID prefix follows what was actually selected rather than the
                // registrant's own account Line. Set once here and never changed
                // after -- otherwise an item's id and its Group would disagree.
                newItem.Group = OrgStructure.GroupFor(newItem.Line);

                // --- ITEM ID (server-authoritative) ---
                // The form's ID box is a read-only preview; the real ID is assigned
                // here from the chosen Group + Type so a stale preview or two
                // simultaneous registrations can never collide. Whatever the form
                // posted in ItemId is intentionally discarded.
                newItem.ItemId = _inventoryService.GenerateItemId(newItem.Group, newItem.Type);

                // --- LOCATION ENCODING (single source of truth) ---
                // The form posts friendly names ("RD Lab", "Trailer Area", ...).
                // I convert them to codes here, store the CODE in Parent for a
                // consistent column, and rebuild the FDA string from codes so a
                // client-supplied value can never put bad data in the DB.
                string pCode = LocationCodec.Encode(newItem.Parent);
                string mCode = LocationCodec.Encode(newItem.Major);
                string sCode = LocationCodec.Encode(newItem.Sub);
                string rack = (newItem.Rack ?? "").Trim().ToUpperInvariant();
                string row = (newItem.Row ?? "").Trim();

                newItem.Parent = pCode;
                newItem.Major = mCode;
                newItem.Sub = sCode;
                newItem.FdaString = string.Join(".",
                    new[] { pCode, mCode, sCode, rack, row }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

                // Compressor serials, posted as serial_0, serial_1, ... (same flat
                // prefix-scan convention LogCompressorUnits already uses). No-op
                // for a non-compressor Type -- CreateItem gates on it internally.
                var serials = Request.Form.Keys
                    .Where(k => k.StartsWith("serial_"))
                    .Select(k => Request.Form[k].ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                _inventoryService.CreateItem(newItem, serials, thermocoupledQty);
                TempData["Success"] = $"Item '{newItem.ItemName}' registered as {newItem.ItemId}.";
            }
            catch (Exception ex) { TempData["Error"] = $"Failed to register item: {ex.Message}"; }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ClearCart()
        {
            _orderService.StartOrder();
            TempData["Message"] = "Cart cleared.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Admin)]
        public IActionResult DeleteItem(string itemId)
        {
            var (ok, message) = _inventoryService.DeleteItem(itemId);
            if (ok) TempData["Success"] = message;
            else TempData["Error"] = message;
            return SmartRedirect();
        }

        // Deletes ONE empty stack without touching the rest of the item --
        // Delete Item requires the item's TOTAL quantity to be 0, which never
        // fires for a variant that drained while the item still has stock
        // elsewhere. Same gate as Delete Item: Admin, since this removes a
        // row outright rather than mutating a quantity.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Admin)]
        public IActionResult DeleteVariant(string itemId, int variantId)
        {
            var (ok, message) = _inventoryService.DeleteVariant(itemId, variantId);
            if (ok) TempData["Success"] = message;
            else TempData["Error"] = message;
            return SmartRedirect();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult AddToStock(string itemId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            {
                TempData["Error"] = "Invalid input.";
                return RedirectToAction("Index");
            }
            _inventoryService.AddStock(itemId, quantity);
            TempData["Success"] = "Stock updated.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult ExportInventory(
            string? exportGroup, string? exportTeam, string? exportType, string? exportBrand, string? exportFda,
            bool expAvailable, bool expAlerts, bool expScrap, bool expOwnership, string expTimeFrame)
        {
            var fileBytes = _inventoryService.ExportToCsv(
                exportGroup, exportTeam, exportType, exportBrand, exportFda,
                expAvailable, expAlerts, expScrap, expOwnership, expTimeFrame);

            var fileName = $"Inventory_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(fileBytes, "text/csv", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult StartOrder()
        {
            _orderService.StartOrder();
            return RedirectToAction("Index", new { mode = "Ledger" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult CancelOrder()
        {
            _orderService.CancelOrder();
            TempData["Message"] = "Order draft canceled.";
            return SmartRedirect();
        }

        // ============================
        // LOGS & ORDER MANAGEMENT
        // ============================

        // This handles the Left Sidebar (Orders Only)
        [HttpGet]
        public IActionResult Orders()
        {
            var orders = _db.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            return View(orders);
        }

        // This handles the Top Right Menu (Master Logs & Orders)
        [HttpGet]
        public IActionResult Logs()
        {
            // Pass Orders via ViewBag
            ViewBag.Orders = _db.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            // Pass Transactions to the main Model. Pass 15: same Line visibility as
            // browsing -- a log about an item you can't see in Search shouldn't be
            // readable here either.
            var transactions = _inventoryService.ApplyLogVisibility(_db.TransactionLogs
                .AsNoTracking())
                .OrderByDescending(t => t.Timestamp)
                .ToList();

            return View(transactions);
        }


        // ----------------------------------------------------------------------
        // LOCATION VOCABULARY (Pass 7C)
        // Codes are DERIVED here, never read from the table -- LocationCodec.Encode
        // stays the only thing that turns a name into a code, so a stored code can
        // never disagree with the rule.
        // ----------------------------------------------------------------------
        private List<(string Code, string Name)> BuildLocationParents()
        {
            return _db.Locations.AsNoTracking()
                .Where(l => l.Level == LocationLevel.Parent && l.IsActive)
                .OrderBy(l => l.Name).ToList()
                .Select(l => (Code: LocationCodec.Encode(l.Name), Name: l.Name))
                .ToList();
        }

        // { "RD Lab": { "Metrology Mezzanine": ["Samurai", "Ninja", ...] }, ... }
        // Friendly names only -- the client derives codes with its own copy of the
        // same rule, which is why the shapes must stay identical.
        private Dictionary<string, Dictionary<string, List<string>>> BuildLocationTree()
        {
            var all = _db.Locations.AsNoTracking().Where(l => l.IsActive).ToList();
            var tree = new Dictionary<string, Dictionary<string, List<string>>>();
            foreach (var p in all.Where(l => l.Level == LocationLevel.Parent).OrderBy(l => l.Name))
            {
                var majors = new Dictionary<string, List<string>>();
                foreach (var m in all.Where(l => l.Level == LocationLevel.Major && l.ParentId == p.Id).OrderBy(l => l.Name))
                {
                    majors[m.Name] = all
                        .Where(l => l.Level == LocationLevel.Sub && l.ParentId == m.Id)
                        .OrderBy(l => l.Name).Select(l => l.Name).ToList();
                }
                tree[p.Name] = majors;
            }
            return tree;
        }

        // Same shape and source as BuildLocationTree() above, but CODE-keyed
        // (Parent code -> Major code -> [Sub codes]) instead of name-keyed --
        // what the Modify Stock / Location Transfer cascade actually needs,
        // since ItemVariant.Parent/Major/Sub store codes, not names. Used to be
        // built by walking allItems' already-stored codes instead of this
        // table, which meant a Sub added in Settings couldn't be picked as a
        // transfer destination until something was already stored there --
        // Registration/Intake never had that problem because BuildLocationTree()
        // already reads straight from this same table.
        private Dictionary<string, Dictionary<string, List<string>>> BuildLocationHierarchyCoded()
        {
            var all = _db.Locations.AsNoTracking().Where(l => l.IsActive).ToList();
            var tree = new Dictionary<string, Dictionary<string, List<string>>>();
            foreach (var p in all.Where(l => l.Level == LocationLevel.Parent).OrderBy(l => l.Name))
            {
                string pCode = LocationCodec.Encode(p.Name);
                var majors = new Dictionary<string, List<string>>();
                foreach (var m in all.Where(l => l.Level == LocationLevel.Major && l.ParentId == p.Id).OrderBy(l => l.Name))
                {
                    majors[LocationCodec.Encode(m.Name)] = all
                        .Where(l => l.Level == LocationLevel.Sub && l.ParentId == m.Id)
                        .OrderBy(l => l.Name).Select(l => LocationCodec.Encode(l.Name)).ToList();
                }
                tree[pCode] = majors;
            }
            return tree;
        }

        // Rack/Row suggestions (Pass 23) -- deliberately NOT a managed
        // vocabulary table like Parent/Major/Sub. Rack/Row stay free-form,
        // team-assigned values (see ItemVariant.cs); this just surfaces what's
        // already been typed into real stock at a given Parent/Major/Sub, so
        // picking one is a shortcut, never a gate -- a brand new value always
        // types in fine. Keyed "Parent|Major|Sub" (codes, blanks kept as
        // empty segments so the key always has exactly 3 parts).
        private Dictionary<string, object> BuildRackRowMap()
        {
            return _db.ItemVariants.AsNoTracking().Where(v => !v.IsRetired).ToList()
                .GroupBy(v => $"{v.Parent}|{v.Major}|{v.Sub}")
                .ToDictionary(
                    g => g.Key,
                    g => (object)new
                    {
                        racks = g.Select(v => v.Rack).Where(r => !string.IsNullOrWhiteSpace(r))
                                  .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToList(),
                        rows = g.Select(v => v.Row).Where(r => !string.IsNullOrWhiteSpace(r))
                                 .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToList()
                    });
        }


        // =====================================================================
        // BULK INTAKE (Pass 9) -- Standard+ can submit.
        // =====================================================================
        // Location pickers only offer what exists, so nobody can invent a shelf
        // by typing. The escape hatch is "not listed", which HOLDS the batch for
        // the superuser to map or create -- it does not block the person entering.

        [RequireLevel(AccessLevels.Standard)]
        public IActionResult Intake()
        {
            ViewBag.LocationTreeJson = System.Text.Json.JsonSerializer.Serialize(BuildLocationTree());
            ViewBag.LocationParents = BuildLocationParents();
            ViewBag.RackRowJson = System.Text.Json.JsonSerializer.Serialize(BuildRackRowMap());
            ViewBag.ActiveTeams = _db.Teams.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToList();
            ViewBag.OrgStructureJson = System.Text.Json.JsonSerializer.Serialize(OrgStructure.BranchLines);
            // Team -> home Line, same map Registration/Ownership already use, so
            // picking a Team here can suggest a Branch/Line the same way.
            ViewBag.TeamLinesJson = System.Text.Json.JsonSerializer.Serialize(
                _db.Teams.AsNoTracking().Where(t => t.IsActive && t.Line != null)
                    .ToDictionary(t => t.Name, t => t.Line));
            // Seed the Branch/Line selects from the signed-in user's own
            // assignment on page load -- a specific Line wins, else a
            // whole-Branch assignment (Branch preset, Line left for them to
            // pick), else neither. Same effective-Branch derivation Index()
            // uses for ViewBag.MyBranch. JSON-serialized (not a plain ViewBag
            // string) since these two feed the page's <script> block directly.
            ViewBag.MyLineJson = System.Text.Json.JsonSerializer.Serialize(_currentUser.Line ?? "");
            ViewBag.MyBranchJson = System.Text.Json.JsonSerializer.Serialize(
                !string.IsNullOrWhiteSpace(_currentUser.Line)
                    ? (OrgStructure.BranchFor(_currentUser.Line) ?? "")
                    : (_currentUser.Branch ?? ""));
            // Existing names + types, for the model autocomplete and the type
            // datalist -- same idea as the Modify Stock picker: suggest, don't force.
            // id/quantity feed the "already registered" split (Pass 21): a row
            // whose typed name exactly matches one of these gets pulled out of
            // Import into a batch Modify Stock instead. Quantity is a [NotMapped]
            // pass-through over Variants -- Include() first so it materializes
            // correctly, never inside the raw LINQ projection itself.
            ViewBag.KnownItemsJson = System.Text.Json.JsonSerializer.Serialize(
                _db.InventoryItems.AsNoTracking().Include(i => i.Variants).ToList()
                   .Select(i => new { id = i.ItemId, name = i.ItemName, type = i.Type, brand = i.Brand, rpn = i.RheemPartNumber, quantity = i.Quantity })
                   .ToList());
            ViewBag.KnownTypes = _db.InventoryItems.AsNoTracking()
                .Select(i => i.Type).Distinct().Where(t => t != "").OrderBy(t => t).ToList();
            ViewBag.MyPending = _db.IntakeBatches.AsNoTracking()
                .Where(b => b.SubmittedBy == _currentUser.Name && b.Status == IntakeStatus.Pending)
                .OrderByDescending(b => b.SubmittedAt).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SubmitIntake(
            string line, string? team,
            string? parentCode, string? majorCode, string? subCode, string? rack, string? row,
            string? requestedLocation,
            string[]? itemName, string[]? type, string[]? brand, string[]? rpn, int[]? qty,
            string[]? serial,
            bool preview = false)
        {
            // Extra serial boxes beyond each row's first (compressor rows with
            // Qty > 1) and each row's TC count (motor rows) are both posted with
            // a per-row index baked into the field name -- extraSerial_{row}_{slot}
            // and thermocoupledQty_{row} -- since a row can carry more than one
            // of the former and array-parameter binding can't key by row. Same
            // flat prefix-scan convention LogCompressorUnits already uses.
            var extraSerialsByRow = new Dictionary<int, List<string>>();
            foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("extraSerial_")))
            {
                var parts = key.Substring("extraSerial_".Length).Split('_');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int rowIdx)) continue;
                var val = Request.Form[key].ToString();
                if (string.IsNullOrWhiteSpace(val)) continue;
                if (!extraSerialsByRow.TryGetValue(rowIdx, out var list)) extraSerialsByRow[rowIdx] = list = new List<string>();
                list.Add(val.Trim());
            }
            var tcByRow = new Dictionary<int, int>();
            foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("thermocoupledQty_")))
            {
                if (!int.TryParse(key.Substring("thermocoupledQty_".Length), out int rowIdx)) continue;
                if (int.TryParse(Request.Form[key], out int tcVal)) tcByRow[rowIdx] = tcVal;
            }

            var lines = new List<InventoryService.IntakeLine>();
            for (int i = 0; itemName != null && i < itemName.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(itemName[i])) continue;

                var rowSerials = new List<string>();
                if (serial != null && i < serial.Length && !string.IsNullOrWhiteSpace(serial[i]))
                    rowSerials.Add(serial[i].Trim());
                if (extraSerialsByRow.TryGetValue(i, out var extras)) rowSerials.AddRange(extras);

                lines.Add(new InventoryService.IntakeLine
                {
                    ItemName = itemName[i],
                    Type = type != null && i < type.Length ? type[i] : "",
                    Brand = brand != null && i < brand.Length ? brand[i] : "",
                    RheemPartNumber = rpn != null && i < rpn.Length && !string.IsNullOrWhiteSpace(rpn[i]) ? rpn[i] : "N/A",
                    Quantity = qty != null && i < qty.Length ? qty[i] : 1,
                    Serials = rowSerials,
                    ThermocoupledQty = tcByRow.TryGetValue(i, out var tc) ? tc : 0
                });
            }
            if (lines.Count == 0)
            {
                TempData["Error"] = "Nothing to import — add at least one row.";
                return RedirectToAction("Intake");
            }

            team = (team ?? "").Trim();
            if (string.Equals(team, "N/A", StringComparison.OrdinalIgnoreCase)) team = "";

            // Pass 15: Line is mandatory here too, same as single-item registration --
            // checked before the pending-location hold path too, so a batch can never
            // sit in the approval queue with no owner waiting to be discovered later.
            line = (line ?? "").Trim();
            if (line.Length == 0)
            {
                TempData["Error"] = "A Line is required for intake.";
                return RedirectToAction("Intake");
            }
            if (!OrgStructure.IsValidLine(line))
            {
                TempData["Error"] = $"'{line}' isn't a recognized Line.";
                return RedirectToAction("Intake");
            }

            // ---- location not listed: HOLD the batch, don't lose the work ----
            // The Parent <select>'s "Not listed..." option posts the literal
            // sentinel "__NEW__", not blank -- checking only for blank meant
            // this branch could never actually trigger through the real UI;
            // "__NEW__" fell straight through to CommitIntake and got stored
            // as if it were a real location code (see CCR-0255).
            bool parentUnresolved = string.IsNullOrWhiteSpace(parentCode)
                || string.Equals(parentCode, "__NEW__", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(requestedLocation) && parentUnresolved)
            {
                var batch = new IntakeBatch
                {
                    SubmittedBy = _currentUser.Name, SubmittedAt = DateTime.UtcNow,
                    Line = line ?? "", Team = team,
                    ParentCode = "", MajorCode = "", SubCode = "",
                    Rack = (rack ?? "").Trim(), Row = (row ?? "").Trim(),
                    RequestedLocation = requestedLocation.Trim(),
                    Status = IntakeStatus.Pending
                };
                foreach (var l in lines)
                    batch.Rows.Add(new IntakeRow
                    {
                        ItemName = l.ItemName, Type = l.Type, Brand = l.Brand,
                        RheemPartNumber = l.RheemPartNumber, Quantity = l.Quantity,
                        Serials = l.Serials.Count > 0 ? string.Join(",", l.Serials) : null,
                        ThermocoupledQty = l.ThermocoupledQty
                    });
                _db.IntakeBatches.Add(batch);
                _db.SaveChanges();
                TempData["Success"] = $"Sent for approval — {lines.Count} row(s) held until '{requestedLocation.Trim()}' is sorted out. Nothing is lost.";
                return RedirectToAction("Intake");
            }

            // "Not listed..." picked but nothing typed for it -- same sentinel
            // problem as above, just with no requestedLocation to hold against.
            // Refuse rather than let "__NEW__" reach CommitIntake as if it were
            // a real location code.
            if (parentUnresolved)
            {
                TempData["Error"] = "Pick a location, or choose \"Not listed...\" and say what you call it.";
                return RedirectToAction("Intake");
            }

            var result = _inventoryService.CommitIntake(
                lines, line ?? "", team,
                parentCode ?? "", majorCode ?? "", subCode ?? "", rack ?? "", row ?? "",
                _currentUser.Name, _currentUser.Line ?? "", preview);

            if (preview || !result.Ok)
            {
                TempData["IntakePreview"] = System.Text.Json.JsonSerializer.Serialize(result);
                if (!result.Ok && !preview)
                {
                    // CommitIntake saves per row, so rows without errors DID land --
                    // only the erroring rows didn't. Resubmitting the whole batch is
                    // safe: rows already in are skipped, not duplicated.
                    int imported = result.NewItems.Count + result.AddedToExisting.Count;
                    TempData["Error"] = imported == 0
                        ? $"Nothing was imported — {result.Errors.Count} row(s) failed. Fix the problems listed and try again."
                        : $"Partial import: {imported} row(s) went in ({result.UnitsIn} unit(s)), {result.Errors.Count} failed. "
                          + "Fix the problems listed and resubmit — already-imported rows are skipped, not duplicated.";
                }
                return RedirectToAction("Intake");
            }

            TempData["Success"] = $"Imported {result.UnitsIn} unit(s): "
                + $"{result.NewItems.Count} new item(s), {result.AddedToExisting.Count} added to existing"
                + (result.SerialsLogged > 0 ? $", {result.SerialsLogged} serial(s) recorded" : "")
                + (result.Skipped.Count > 0 ? $", {result.Skipped.Count} skipped" : "") + ".";
            return RedirectToAction("Intake");
        }

        // Batch Modify Stock for rows Bulk Intake found already registered
        // (Pass 21) -- the review modal's "Apply All". Rows are posted with a
        // per-section index baked into the field name (stockItemId_N,
        // stockAction_N, stockQty_N, stockTc_N, stockSerial_N_slot), same flat
        // prefix-scan convention every other multi-row Intake/pickup post in
        // this app already uses.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SubmitIntakeStockUpdates(
            string? parentCode, string? majorCode, string? subCode, string? rack, string? row)
        {
            var serialsByIdx = new Dictionary<int, List<string>>();
            foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("stockSerial_")))
            {
                var parts = key.Substring("stockSerial_".Length).Split('_');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int idx)) continue;
                var val = Request.Form[key].ToString();
                if (string.IsNullOrWhiteSpace(val)) continue;
                if (!serialsByIdx.TryGetValue(idx, out var list)) serialsByIdx[idx] = list = new List<string>();
                list.Add(val.Trim());
            }

            var updates = new List<InventoryService.StockBatchUpdate>();
            var indices = Request.Form.Keys.Where(k => k.StartsWith("stockItemId_"))
                .Select(k => k.Substring("stockItemId_".Length)).Distinct();
            foreach (var idxStr in indices)
            {
                if (!int.TryParse(idxStr, out int idx)) continue;
                string itemId = Request.Form[$"stockItemId_{idx}"].ToString();
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                string action = Request.Form[$"stockAction_{idx}"].ToString();
                int.TryParse(Request.Form[$"stockQty_{idx}"], out int qty);
                int.TryParse(Request.Form[$"stockTc_{idx}"], out int tc);
                updates.Add(new InventoryService.StockBatchUpdate
                {
                    ItemId = itemId,
                    ActionType = string.Equals(action, "Adjustment", StringComparison.OrdinalIgnoreCase) ? "Adjustment" : "Add",
                    Quantity = qty,
                    ThermocoupledQty = tc,
                    Serials = serialsByIdx.TryGetValue(idx, out var s) ? s : new List<string>()
                });
            }

            if (updates.Count == 0)
            {
                TempData["Error"] = "Nothing to apply.";
                return RedirectToAction("Intake");
            }

            // Same sentinel guard as SubmitIntake -- these items already exist,
            // so "Not listed..." makes no sense here at all: refuse rather than
            // let "__NEW__" reach CommitIntakeStockBatch as if it were real.
            bool parentUnresolved = string.IsNullOrWhiteSpace(parentCode)
                || string.Equals(parentCode, "__NEW__", StringComparison.OrdinalIgnoreCase);
            if (parentUnresolved)
            {
                TempData["Error"] = "Pick a real location before applying stock changes -- a \"Not listed\" location needs approval first.";
                return RedirectToAction("Intake");
            }

            try
            {
                var results = _inventoryService.CommitIntakeStockBatch(
                    updates, parentCode ?? "", majorCode ?? "", subCode ?? "", rack ?? "", row ?? "");

                foreach (var r in results)
                    _emailService.CheckAndSendStockAlert(r.Item, r.OldQty, r.NewQty);

                TempData["Success"] = $"Updated stock for {results.Count} item(s).";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Nothing was applied — {ex.Message}";
            }
            return RedirectToAction("Intake");
        }

        // Self-scoped loan ledger: this user's orders + their items still out.
        public IActionResult MyOrders()
        {
            var me = _currentUser.Name;

            var myOrders = _db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.RequestedBy == me)
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            var invLookup = _db.InventoryItems.AsNoTracking().Include(i => i.Variants)
                .ToDictionary(i => i.ItemId);

            // Friendly per-location label for the return picker (decode codes,
            // drop "0"/blank levels), mirroring the pickup queue.
            string VLabel(Visual_Inventory_System.Models.ItemVariant v)
            {
                bool Real(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0";
                var crumbs = new List<string>();
                if (Real(v.Parent)) crumbs.Add(LocationCodec.Decode(v.Parent));
                if (Real(v.Major)) crumbs.Add(LocationCodec.Decode(v.Major));
                if (Real(v.Sub)) crumbs.Add(LocationCodec.Decode(v.Sub));
                // Pass 7C: Rack and Row were dropped from this label entirely, which
                // made every Lean-To variant read as plain "Plant Test Cells" -- and
                // two variants of the same item at different racks indistinguishable
                // in the picker. They are free text, so they are shown as stored.
                if (Real(v.Rack)) crumbs.Add(v.Rack.Trim());
                if (Real(v.Row)) crumbs.Add(v.Row.Trim());
                string path = crumbs.Count > 0 ? string.Join(" › ", crumbs) : v.FdaString;
                return $"V{v.VariantNumber} — {path} · Qty {v.Quantity}";
            }

            var loans = new List<LoanLineViewModel>();
            foreach (var o in myOrders)
            {
                foreach (var it in o.Items.Where(x => x.LoanOutstanding > 0))
                {
                    invLookup.TryGetValue(it.ItemId, out var inv);
                    var activeVars = (inv?.ActiveVariants ?? Enumerable.Empty<Visual_Inventory_System.Models.ItemVariant>())
                        .OrderBy(v => v.VariantNumber).ToList();

                    // Pass 6B: the named units still out on THIS line. Usually
                    // fewer than Outstanding -- serial coverage is partial, and the
                    // view renders the remainder as plain quantity.
                    var lineUnits = InventoryService.IsCompressorType(inv?.Type)
                        ? _db.CompressorUnits.AsNoTracking()
                              .Where(cu => cu.OrderItemId == it.Id
                                        && cu.Status == Visual_Inventory_System.Models.UnitStatus.PickedUp)
                              .OrderBy(cu => cu.SerialNumber)
                              .ToList()
                        : new List<Visual_Inventory_System.Models.CompressorUnit>();

                    loans.Add(new LoanLineViewModel
                    {
                        OrderItemId = it.Id,
                        OrderId = o.Id,
                        OrderedAt = o.CreatedAt,
                        ItemId = it.ItemId,
                        ItemName = inv?.ItemName ?? "Unknown",
                        RheemPartNumber = inv?.RheemPartNumber ?? "",
                        Description = inv?.Description ?? "",
                        ItemType = inv?.Type ?? "",
                        Outstanding = it.LoanOutstanding,
                        ReturnsAsTc = InventoryService.IsMotorType(inv?.Type),
                        IsCompressor = InventoryService.IsCompressorType(inv?.Type),
                        Units = lineUnits,
                        LocationChoices = activeVars
                            .Select(v => new VariantChoiceViewModel { VariantId = v.Id, Label = VLabel(v) })
                            .ToList()
                    });
                }
            }

            ViewBag.LocationParents = BuildLocationParents();
            return View(new MyOrdersViewModel { UserName = me, Orders = myOrders, Loans = loans });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ReturnLoan(int orderItemId, int qty, int? targetVariantId,
            string? newParent, string? newMajor, string? newSub, string? newRack, string? newRow,
            int[]? unitIds = null, string? reason = null)
        {
            try
            {
                _orderService.ReturnLoan(orderItemId, qty, targetVariantId,
                    newParent, newMajor, newSub, newRack, newRow, unitIds, reason);
                TempData["Success"] = "Loan return recorded.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("MyOrders");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ScrapLoan(int orderItemId, int qty, int[]? unitIds = null, string? reason = null)
        {
            try
            {
                _orderService.ScrapLoan(orderItemId, qty, unitIds, reason);
                TempData["Success"] = "Loan scrap recorded.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("MyOrders");
        }

        public IActionResult PickupQueue()
        {
            var orders = _db.Orders.Where(o => o.Status == "Pending").Include(o => o.Items).AsNoTracking().OrderBy(o => o.CreatedAt).ToList();
            var result = orders.Select(order => {
                var vm = new PendingOrderViewModel { OrderId = order.Id, CreatedAt = order.CreatedAt, Status = order.Status, RequestedBy = order.RequestedBy, SplitFromOrderId = order.SplitFromOrderId };
                var canFulfill = true;
                var blockedByPriority = false;

                foreach (var it in order.Items)
                {
                    var availForThis = _inventoryService.GetAvailableForOrder(it.ItemId, order.CreatedAt);
                    var totalAvailable = _inventoryService.GetAvailableQuantity(it.ItemId);
                    var itemEntity = _db.InventoryItems.AsNoTracking().Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);

                    // Friendly per-location labels for the pickup person. Decode the
                    // stored codes; "0"/blank levels are dropped from the breadcrumb.
                    string VLabel(Visual_Inventory_System.Models.ItemVariant v)
                    {
                        bool Real(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0";
                        var crumbs = new List<string>();
                        if (Real(v.Parent)) crumbs.Add(LocationCodec.Decode(v.Parent));
                        if (Real(v.Major)) crumbs.Add(LocationCodec.Decode(v.Major));
                        if (Real(v.Sub)) crumbs.Add(LocationCodec.Decode(v.Sub));
                        // Pass 7C: Rack and Row were dropped from this label entirely, which
                        // made every Lean-To variant read as plain "Plant Test Cells" -- and
                        // two variants of the same item at different racks indistinguishable
                        // in the picker. They are free text, so they are shown as stored.
                        if (Real(v.Rack)) crumbs.Add(v.Rack.Trim());
                        if (Real(v.Row)) crumbs.Add(v.Row.Trim());
                        string path = crumbs.Count > 0 ? string.Join(" › ", crumbs) : v.FdaString;
                        return $"V{v.VariantNumber} — {path} · Qty {v.Quantity}";
                    }

                    var activeVars = (itemEntity?.ActiveVariants ?? Enumerable.Empty<Visual_Inventory_System.Models.ItemVariant>())
                        .OrderBy(v => v.VariantNumber).ToList();

                    // Real on-hand units per location -- feeds the serial picker
                    // on Pickup Queue. Fetched once per line, grouped by variant,
                    // so choosing a location client-side can swap in exactly
                    // what's actually on that shelf instead of a blind text box.
                    var onHandByVariant = InventoryService.IsCompressorType(itemEntity?.Type)
                        ? _db.CompressorUnits.AsNoTracking()
                              .Where(c => c.ItemId == it.ItemId && c.Status == Visual_Inventory_System.Models.UnitStatus.OnHand && c.ItemVariantId != null)
                              .OrderBy(c => c.RecordedAt)
                              .GroupBy(c => c.ItemVariantId!.Value)
                              .ToDictionary(g => g.Key, g => g.Select(c => new OnHandUnitViewModel { SerialNumber = c.SerialNumber, LabNumber = c.LabNumber }).ToList())
                        : new Dictionary<int, List<OnHandUnitViewModel>>();

                    var itemVm = new PendingOrderItemViewModel
                    {
                        OrderItemId = it.Id,
                        ItemId = it.ItemId,
                        ItemName = itemEntity?.ItemName ?? "Unknown",
                        RheemPartNumber = itemEntity?.RheemPartNumber ?? "",
                        Type = itemEntity?.Type,
                        Quantity = it.Quantity,
                        AvailableForThisOrder = availForThis,
                        RequestedVariantId = it.RequestedVariantId,
                        RequestedLabel = it.RequestedVariantId.HasValue
                            ? activeVars.Where(v => v.Id == it.RequestedVariantId.Value).Select(VLabel).FirstOrDefault()
                            : null,
                        LocationChoices = activeVars
                            .Select(v => new VariantChoiceViewModel
                            {
                                VariantId = v.Id,
                                Label = VLabel(v),
                                Quantity = v.Quantity,
                                OnHandUnits = onHandByVariant.TryGetValue(v.Id, out var units) ? units : new List<OnHandUnitViewModel>()
                            })
                            .ToList()
                    };

                    vm.Items.Add(itemVm);
                    if (availForThis < it.Quantity) { canFulfill = false; if (totalAvailable >= it.Quantity) blockedByPriority = true; }
                }
                vm.CanFulfill = canFulfill;
                vm.IsBlockedByPriority = blockedByPriority;
                return vm;
            }).ToList();

            // "Go Store These": open + claimed sticky-note tasks (Done ones drop off).
            var storeTasks = _db.VisTasks.AsNoTracking()
                .Where(t => t.Status != "Done")
                .OrderBy(t => t.CreatedAt)
                .ToList();

            return View(new TasksAvailableViewModel { Orders = result, StoreTasks = storeTasks });
        }

        // Pin a lightweight "Go Store These" task. Engineer+ only. Deliberately
        // no structured item fields -- just a title + optional note.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult CreateStoreTask(string title, string? details)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "A task needs a title.";
                return RedirectToAction("PickupQueue");
            }

            _db.VisTasks.Add(new VisTask
            {
                TaskType = "Store",
                Title = title.Trim(),
                Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
                CreatedBy = _currentUser.Name,
                CreatedAt = DateTime.UtcNow,
                Status = "Open"
            });
            _db.SaveChanges();
            TempData["Success"] = "Task pinned.";
            return RedirectToAction("PickupQueue");
        }

        // Claim an open task. Standard+ (everyone but Viewer). First claim wins --
        // a task already claimed by someone else is left alone.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ClaimTask(int id)
        {
            var task = _db.VisTasks.FirstOrDefault(t => t.Id == id);
            if (task == null) { TempData["Error"] = "Task not found."; return RedirectToAction("PickupQueue"); }

            if (task.Status == "Open")
            {
                task.ClaimedBy = _currentUser.Name;
                task.ClaimedAt = DateTime.UtcNow;
                task.Status = "Claimed";
                _db.SaveChanges();
                TempData["Success"] = "Task claimed.";
            }
            else
            {
                TempData["Error"] = $"Already claimed by {task.ClaimedBy}.";
            }
            return RedirectToAction("PickupQueue");
        }

        // Mark a claimed task done -- only the person who claimed it. Done tasks
        // drop off the board.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult CompleteTask(int id)
        {
            var task = _db.VisTasks.FirstOrDefault(t => t.Id == id);
            if (task == null) { TempData["Error"] = "Task not found."; return RedirectToAction("PickupQueue"); }

            if (task.Status == "Claimed" &&
                string.Equals(task.ClaimedBy, _currentUser.Name, StringComparison.OrdinalIgnoreCase))
            {
                task.Status = "Done";
                task.CompletedAt = DateTime.UtcNow;
                _db.SaveChanges();
                TempData["Success"] = "Task completed.";
            }
            else
            {
                TempData["Error"] = "Only the person who claimed a task can finish it.";
            }
            return RedirectToAction("PickupQueue");
        }

        // Delivery intake board: the shared "Unknown Delivery" bucket (visible to
        // every Management+ user) plus anything routed straight to this person.
        // Done deliveries drop off, same as the VisTask board above.
        [RequireLevel(AccessLevels.Management)]
        public IActionResult Deliveries()
        {
            string mine = _currentUser.Name;

            var rows = _db.Deliveries.AsNoTracking()
                .Where(d => d.Status != "Done"
                    && (d.RecipientUserName == Delivery.UnknownRecipient || d.RecipientUserName == mine))
                .OrderBy(d => d.LoggedAt)
                .ToList();

            var displayNames = _db.Users.AsNoTracking()
                .ToDictionary(u => u.UserName, u => u.DisplayName, StringComparer.OrdinalIgnoreCase);

            var vms = rows.Select(d => new Visual_Inventory_System.Models.ViewModels.DeliveryViewModel
            {
                Id = d.Id,
                PhotoUrl = DeliveryPhotoStorage.UrlFor(d.PhotoPath),
                TrackingNumber = d.TrackingNumber,
                OrderNumber = d.OrderNumber,
                BrandOfShipping = d.BrandOfShipping,
                BrandOfItem = d.BrandOfItem,
                IsUnknownBucket = d.RecipientUserName == Delivery.UnknownRecipient,
                RecipientLabel = d.RecipientUserName == Delivery.UnknownRecipient
                    ? "Unknown Delivery"
                    : (displayNames.TryGetValue(d.RecipientUserName, out var dn) ? dn : d.RecipientUserName),
                LoggedBy = d.LoggedBy,
                LoggedAt = d.LoggedAt,
                Status = d.Status,
                ClaimedBy = d.ClaimedBy,
                ClaimedAt = d.ClaimedAt
            }).ToList();

            return View(vms);
        }

        // Delivery intake form. Standard+, matching CreateItem/StartOrder/Intake.
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult LogDelivery()
        {
            ViewBag.Recipients = _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.AccessLevel >= AccessLevels.Management)
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.UserName, u.DisplayName })
                .ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SubmitDelivery(IFormFile photo, string? trackingNumber, string? orderNumber,
            string? brandOfShipping, string? brandOfItem, string recipientUserName)
        {
            bool IsNa(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase);

            if (photo == null || photo.Length == 0)
            {
                TempData["Error"] = "A photo is required.";
                return RedirectToAction("LogDelivery");
            }

            if (IsNa(brandOfShipping) && IsNa(brandOfItem))
            {
                TempData["Error"] = "At least one brand (shipping or item) is required.";
                return RedirectToAction("LogDelivery");
            }

            if (string.IsNullOrWhiteSpace(recipientUserName))
            {
                TempData["Error"] = "Pick who this goes to, or Unknown Delivery.";
                return RedirectToAction("LogDelivery");
            }

            bool isUnknown = recipientUserName == Delivery.UnknownRecipient;
            if (!isUnknown && !_db.Users.Any(u => u.UserName == recipientUserName && u.IsActive && u.AccessLevel >= AccessLevels.Management))
            {
                TempData["Error"] = "That recipient isn't valid.";
                return RedirectToAction("LogDelivery");
            }

            string fileName = DeliveryPhotoStorage.Save(photo);

            var delivery = new Delivery
            {
                PhotoPath = fileName,
                TrackingNumber = trackingNumber?.Trim(),
                OrderNumber = orderNumber?.Trim(),
                BrandOfShipping = brandOfShipping?.Trim(),
                BrandOfItem = brandOfItem?.Trim(),
                RecipientUserName = recipientUserName,
                LoggedBy = _currentUser.Name,
                LoggedAt = DateTime.UtcNow,
                Status = "Open"
            };
            _db.Deliveries.Add(delivery);
            _db.SaveChanges();

            string message = $"Delivery logged by {_currentUser.Name}"
                + (IsNa(brandOfItem) ? "" : $" — {brandOfItem}")
                + (IsNa(trackingNumber) ? "" : $" (Tracking {trackingNumber})");

            if (isUnknown)
                _notifications.CreateForLevel(AccessLevels.Management, null, "DeliveryReceived", message, "/Home/Deliveries", _currentUser.Name);
            else
                _notifications.Create(recipientUserName, "DeliveryReceived", message, "/Home/Deliveries");

            TempData["Success"] = "Delivery logged.";
            return RedirectToAction("LogDelivery");
        }

        // Claim an open delivery. Management+ -- the same band it was routed to.
        // First claim wins, same as ClaimTask.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Management)]
        public IActionResult ClaimDelivery(int id)
        {
            var d = _db.Deliveries.FirstOrDefault(x => x.Id == id);
            if (d == null) { TempData["Error"] = "Delivery not found."; return RedirectToAction("Deliveries"); }

            if (d.Status == "Open")
            {
                d.ClaimedBy = _currentUser.Name;
                d.ClaimedAt = DateTime.UtcNow;
                d.Status = "Claimed";
                _db.SaveChanges();
                TempData["Success"] = "Delivery claimed.";
            }
            else
            {
                TempData["Error"] = $"Already claimed by {d.ClaimedBy}.";
            }
            return RedirectToAction("Deliveries");
        }

        // Mark a claimed delivery done -- only the person who claimed it, same as CompleteTask.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Management)]
        public IActionResult CompleteDelivery(int id)
        {
            var d = _db.Deliveries.FirstOrDefault(x => x.Id == id);
            if (d == null) { TempData["Error"] = "Delivery not found."; return RedirectToAction("Deliveries"); }

            if (d.Status == "Claimed" && string.Equals(d.ClaimedBy, _currentUser.Name, StringComparison.OrdinalIgnoreCase))
            {
                d.Status = "Done";
                d.CompletedAt = DateTime.UtcNow;
                _db.SaveChanges();
                TempData["Success"] = "Delivery marked done.";
            }
            else
            {
                TempData["Error"] = "Only the person who claimed it can finish it.";
            }
            return RedirectToAction("Deliveries");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult PickUpOrderConfirmed(int orderId)
        {
            try
            {
                // Optional per-item location choices posted from the queue page
                // as variantChoice_{orderItemId}=variantId. Absent/blank = auto.
                var choices = new Dictionary<int, int>();
                foreach (var key in Request.Form.Keys)
                {
                    if (key.StartsWith("variantChoice_")
                        && int.TryParse(key.Substring("variantChoice_".Length), out int oiId)
                        && int.TryParse(Request.Form[key], out int vId) && vId > 0)
                    {
                        choices[oiId] = vId;
                    }
                }

                // Compressor mini-variants: compLab_{orderItemId}_{n} / compSerial_{orderItemId}_{n},
                // n = 0-based unit index on that line. compSerial is the serial picker's
                // selected value -- a real on-hand serial, "__NONE__" (picker explicitly
                // said this unit has none), or "__ADD__" (picker is naming a serial the
                // roster doesn't know about yet, typed into compSerialManual_{oi}_{n}
                // instead). Both Lab# and the resolved serial are soft -- blank/"__NONE__"
                // posts through as null.
                var compressorUnits = new Dictionary<int, List<(string? Lab, string? Serial)>>();
                foreach (var key in Request.Form.Keys)
                {
                    if (!key.StartsWith("compLab_")) continue;
                    var rest = key.Substring("compLab_".Length);
                    var parts = rest.Split('_');
                    if (parts.Length != 2) continue;
                    if (!int.TryParse(parts[0], out int oiId) || !int.TryParse(parts[1], out int unitIdx)) continue;

                    string? lab = Request.Form[key].ToString();
                    string? serial = Request.Form[$"compSerial_{oiId}_{unitIdx}"].ToString();
                    if (serial == "__ADD__")
                        serial = Request.Form[$"compSerialManual_{oiId}_{unitIdx}"].ToString();
                    else if (serial == "__NONE__")
                        serial = null;

                    if (!compressorUnits.TryGetValue(oiId, out var list))
                    {
                        list = new List<(string?, string?)>();
                        compressorUnits[oiId] = list;
                    }
                    while (list.Count <= unitIdx) list.Add((null, null));
                    list[unitIdx] = (lab, serial);
                }

                var result = _orderService.PickUpOrder(orderId, choices.Count > 0 ? choices : null,
                    compressorUnits.Count > 0 ? compressorUnits : null);

                if (result.HasShortLines)
                {
                    // Not enough on the shelf for one or more lines -- stock was
                    // left untouched for those. Other lines on the order picked
                    // up normally. Stash the shortfall for PickupQueue to render
                    // a "what's actually here?" correction form.
                    TempData["ShortPullOrderId"] = orderId;
                    TempData["ShortPullJson"] = System.Text.Json.JsonSerializer.Serialize(result.ShortLines);
                    TempData["Error"] = result.ShortLines.Count == 1
                        ? $"{result.ShortLines[0].ItemName} came up short -- report the real shelf count below."
                        : $"{result.ShortLines.Count} items came up short -- report the real shelf counts below.";
                }
                else
                {
                    TempData["Success"] = "Order completed.";
                }
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            // SMART REDIRECT: Go back to the exact page you came from (Logs OR Orders)
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Orders") : Redirect(referer);
        }

        // Deliberate partial pickup (see OrderService.PickUpPartialAndSplit) --
        // the picker saw, before submitting, that the chosen location can't
        // cover the full line and chose to take what's there and defer the
        // rest. compLab_{u}/compSerial_{u}/compSerialManual_{u} are posted
        // per-unit for just the pickupQty units actually leaving (u = 0-based,
        // no orderItemId prefix needed since this action only ever resolves
        // one line at a time).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult PickUpPartialAndSplit(int orderId, int orderItemId, int variantId, int pickupQty)
        {
            try
            {
                var unitChoices = new Dictionary<int, (string? Lab, string? Serial)>();
                for (int u = 0; u < pickupQty; u++)
                {
                    string? lab = Request.Form[$"compLab_{u}"].ToString();
                    string? serial = Request.Form[$"compSerial_{u}"].ToString();
                    if (serial == "__ADD__")
                        serial = Request.Form[$"compSerialManual_{u}"].ToString();
                    else if (serial == "__NONE__")
                        serial = null;
                    unitChoices[u] = (lab, serial);
                }

                _orderService.PickUpPartialAndSplit(orderId, orderItemId, variantId, pickupQty, unitChoices);
                TempData["Success"] = $"Picked up {pickupQty}; the rest was split into a new order.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Orders") : Redirect(referer);
        }

        // Short-pull correction (see OrderService.ReportShortPull). Standard-level
        // on purpose -- this is the consolidated action that used to take a
        // runner, an Engineer to cancel, and the requester to re-order.
        // corrected_{variantId} = the count the picker actually saw at that
        // location; only fields the picker touched are posted.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ReportShortPull(int orderId, int orderItemId)
        {
            try
            {
                var corrections = new Dictionary<int, int>();
                foreach (var key in Request.Form.Keys)
                {
                    if (key.StartsWith("corrected_")
                        && int.TryParse(key.Substring("corrected_".Length), out int variantId)
                        && int.TryParse(Request.Form[key], out int actualQty))
                    {
                        corrections[variantId] = actualQty;
                    }
                }

                var result = _orderService.ReportShortPull(orderId, orderItemId, corrections);

                if (result.HasShortLines)
                {
                    TempData["ShortPullOrderId"] = result.OrderId;
                    TempData["ShortPullJson"] = System.Text.Json.JsonSerializer.Serialize(result.ShortLines);
                    TempData["Error"] = "Still short after that correction -- another check may have beaten you to it.";
                }
                else if (result.NewOrderId.HasValue)
                {
                    TempData["Success"] = $"Stock corrected and Order #{result.NewOrderId} picked up clean.";
                }
                else
                {
                    TempData["Success"] = "Stock corrected. Nothing was actually on the shelf, so no pickup was issued.";
                }
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("PickupQueue") : Redirect(referer);
        }

        // Logs/corrects On Hand compressor serials directly from the Compressors
        // modal roster -- the other entrance besides PickUpOrder's match-or-create.
        // Standard-level on purpose: anyone passing a shelf who notices an
        // unrecorded unit (or a typo'd one) should be able to fix it on the spot.
        // Rows are posted as serial_{n}/lab_{n}, each paired with unitId_{n}
        // (an existing On Hand unit's id, or 0 for a blank pad row the human
        // filled in) and variantId_{n} (only meaningful when unitId is 0).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult LogCompressorUnits(string itemId)
        {
            try
            {
                var rows = new List<(int UnitId, int? VariantId, string? Serial, string? Lab)>();
                var indices = Request.Form.Keys
                    .Where(k => k.StartsWith("serial_"))
                    .Select(k => k.Substring("serial_".Length))
                    .Distinct();

                foreach (var idx in indices)
                {
                    int.TryParse(Request.Form[$"unitId_{idx}"], out int unitId);
                    int? variantId = int.TryParse(Request.Form[$"variantId_{idx}"], out int vId) ? vId : (int?)null;
                    string? serial = Request.Form[$"serial_{idx}"];
                    string? lab = Request.Form[$"lab_{idx}"];
                    rows.Add((unitId, variantId, serial, lab));
                }

                var result = _inventoryService.LogCompressorUnits(itemId, rows, _currentUser.Name);
                if (result.Errors.Count > 0)
                    TempData["Error"] = string.Join(" ", result.Errors);
                else if (result.Changed)
                    TempData["Success"] = $"{itemId}: {result.Created.Count} logged, {result.Updated.Count} corrected.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            string referer2 = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer2) ? RedirectToAction("Index") : Redirect(referer2);
        }

        // TC motor equivalent of LogCompressorUnits. No serial_{n} field --
        // motors only ever carry a lab_{n}. Rows can target either an On Hand
        // OR a Picked Up (out on loan) unit; see InventoryService.LogMotorUnits.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult LogMotorUnits(string itemId)
        {
            try
            {
                var rows = new List<(int UnitId, int? VariantId, string? Lab)>();
                var indices = Request.Form.Keys
                    .Where(k => k.StartsWith("lab_"))
                    .Select(k => k.Substring("lab_".Length))
                    .Distinct();

                foreach (var idx in indices)
                {
                    int.TryParse(Request.Form[$"unitId_{idx}"], out int unitId);
                    int? variantId = int.TryParse(Request.Form[$"variantId_{idx}"], out int vId) ? vId : (int?)null;
                    string? lab = Request.Form[$"lab_{idx}"];
                    rows.Add((unitId, variantId, lab));
                }

                var result = _inventoryService.LogMotorUnits(itemId, rows, _currentUser.Name);
                if (result.Errors.Count > 0)
                    TempData["Error"] = string.Join(" ", result.Errors);
                else if (result.Changed)
                    TempData["Success"] = $"{itemId}: {result.Created.Count} logged, {result.Updated.Count} corrected.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            string referer3 = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer3) ? RedirectToAction("Index") : Redirect(referer3);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult CancelPersisted(int orderId)
        {
            try
            {
                _orderService.CancelPersistedOrder(orderId);
                TempData["Success"] = "Order cancelled.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            // SMART REDIRECT: Go back to the exact page you came from (Logs OR Orders)
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Orders") : Redirect(referer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DismissNotification(int id)
        {
            _notifications.Dismiss(id, _currentUser.Name);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkNotificationsRead()
        {
            _notifications.MarkAllRead(_currentUser.Name);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetTheme(string theme)
        {
            // Whitelist: anything that isn't "light" falls back to "dark".
            theme = (theme == "light") ? "light" : "dark";
            _currentUser.SetTheme(theme);

            var u = _db.Users.FirstOrDefault(x => x.UserName == _currentUser.Name);
            if (u != null)
            {
                u.Theme = theme;
                _db.SaveChanges();
            }

            // Return to the page the toggle was clicked on so it re-renders themed.
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Index") : Redirect(referer);
        }


        public IActionResult OrderDetails(int id)
        {
            var order = _db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefault(o => o.Id == id);
            if (order == null) return NotFound();
            ViewBag.InventoryLookup = _db.InventoryItems.AsNoTracking().Include(i => i.Variants).ToDictionary(i => i.ItemId);
            // Reverse lineage -- did anything split OFF of this order? (Forward
            // lineage, "this order split from #N," is just order.SplitFromOrderId
            // itself, no query needed.)
            ViewBag.SplitTo = _db.Orders.AsNoTracking()
                .Where(o => o.SplitFromOrderId == id)
                .Select(o => o.Id)
                .ToList();
            return View(order);
        }

        // ============================
        // NAME CAPTURE (lightweight, not authentication)
        // ============================
        [AllowWithoutName]
        [HttpGet]
        public IActionResult Identify(string? returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Users = _db.Users.Where(u => u.IsActive).OrderBy(u => u.DisplayName).ToList();
            return View();
        }

        [AllowWithoutName]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Identify(string name, string? returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Users = _db.Users.Where(u => u.IsActive).OrderBy(u => u.DisplayName).ToList();

            name = (name ?? "").Trim();
            // Require First.Last (letters, optional hyphen/apostrophe in each part).
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z'-]+\.[A-Za-z'-]+$"))
            {
                ViewBag.Error = "Please enter your name as First.Last (for example, Jane.Doe).";
                ViewBag.Entered = name;
                return View();
            }

            // Normalize capitalization to First.Last
            var parts = name.Split('.');
            string normalized = string.Join(".",
                parts.Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));

            _currentUser.Set(normalized);

            // Pull their access tier from the roster; unknown names get Viewer.
            var known = _db.Users.FirstOrDefault(u => u.UserName == normalized && u.IsActive);
            _currentUser.SetLevel(known?.AccessLevel ?? AccessLevels.Viewer);
            _currentUser.SetTheme(known?.Theme ?? "dark");
            _currentUser.SetLine(known?.Line ?? "");
            _currentUser.SetBranch(known?.Branch ?? "");

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index");
        }

        [AllowWithoutName]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SignOut()
        {
            _currentUser.Clear();
            return RedirectToAction("Identify");
        }

        // Full roster page for the "Total Items" dashboard stat. Deep-linking into
        // a specific item's Handle Stock panel reuses the existing Omni search
        // route (/Home/SearchCenter?omniSearch=ID) rather than duplicating that JS here.
        public IActionResult AllItems()
        {
            var items = _inventoryService.GetAll().OrderBy(i => i.ItemId).ToList();
            return View(items);
        }

        public IActionResult Privacy() => View();

        [AllowWithoutName]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}