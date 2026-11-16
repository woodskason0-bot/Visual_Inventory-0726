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
        private readonly ReservationService _reservations;

        public HomeController(OrderService orderService, InventoryService inventoryService, EmailService emailService, AppDbContext db, CurrentUserService currentUser, NotificationService notifications, ReservationService reservations)
        {
            _orderService = orderService;
            _inventoryService = inventoryService;
            _emailService = emailService;
            _db = db;
            _currentUser = currentUser;
            _notifications = notifications;
            _reservations = reservations;
        }

        // "/" is Command Center: KPIs, map, activity feed, backlog donuts, alerts. Search lives in SearchCenter().
        public IActionResult Index()
        {
            var allItems = _inventoryService.GetAll().ToList();
            PopulateSearchViewBag(allItems);
            // The view builds its own zone data from the service.
            ViewBag.InventoryService = _inventoryService;

            // KPI strip.
            ViewBag.TotalItems = allItems.Count;
            ViewBag.LowStockCount = allItems.Count(i => i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold && i.Quantity > 0);
            ViewBag.OutOfStockCount = allItems.Count(i => i.Quantity == 0);
            ViewBag.ActiveLocationsCount = allItems.Select(i => i.Parent).Where(p => !string.IsNullOrEmpty(p)).Distinct().Count();
            ViewBag.PnBacklogCount = GetPnBacklogItems(allItems).Count;

            // Activity feed, top 5, with the same Line visibility as View Logs.
            ViewBag.RecentActivity = _inventoryService.ApplyLogVisibility(_db.TransactionLogs
                .AsNoTracking())
                .OrderByDescending(t => t.Timestamp)
                .Take(5)
                .ToList();

            // Map zones. The view builds zoneDataMap and the Location List from this and LocationParents.
            ViewBag.MapZones = _db.LocationZones.AsNoTracking()
                .Join(_db.Locations.Where(l => l.Level == LocationLevel.Parent && l.IsActive),
                      z => z.LocationId, l => l.Id,
                      (z, l) => new { l.Name, z.X, z.Y, z.W, z.H })
                .ToList()
                .Select(z => (Name: z.Name, Code: LocationCodec.Encode(z.Name),
                              X: z.X, Y: z.Y, W: z.W, H: z.H))
                .ToList();

            // Need Serial: on-hand compressor stock by quantity with no serial recorded.
            // Counted against Quantity, not against CompressorUnit rows: most stock has no row at all.
            var compressorItems = allItems.Where(i => InventoryService.IsCompressorType(i.Type)).ToList();
            var compItemIds = compressorItems.Select(i => i.ItemId).ToHashSet();
            var onHandCompUnits = _db.CompressorUnits.AsNoTracking()
                .Where(u => u.Status == UnitStatus.OnHand).ToList();
            int compQtyTotal = compressorItems.Sum(i => i.Quantity);
            int compSerialsRecorded = onHandCompUnits.Count(u =>
                compItemIds.Contains(u.ItemId) && !string.IsNullOrWhiteSpace(u.SerialNumber));
            ViewBag.CompUnitsTotal = compQtyTotal;
            ViewBag.CompUnitsNeedSerialCount = Math.Max(0, compQtyTotal - compSerialsRecorded);

            // Pending = Pending orders plus held intake batches. Incoming = deliveries not yet Done.
            ViewBag.PendingCount = _db.Orders.AsNoTracking().Count(o => o.Status == "Pending")
                + _db.IntakeBatches.AsNoTracking().Count(b => b.Status == IntakeStatus.Pending);
            ViewBag.IncomingShipmentsCount = _db.Deliveries.AsNoTracking().Count(d => d.Status != "Done");

            return View("CommandCenter");
        }

        // Search Center: filters, omni search, results, Export wizard, Compressor/Motor registry, Modify Stock.
        public IActionResult SearchCenter(string? omniSearch, string? filterRheem, string? filterType, string? filterBrand, string? filterBranch, string? mode, string? stockView,
            string? filterLocParent, string? filterLocMajor, string? filterLocSub)
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
                ViewBag.TransferItemsJson = "[]";
                return View();
            }

            var searchResult = new SearchResult { Mode = mode ?? "None" };
            if (!string.IsNullOrEmpty(stockView))
            {
                // Command Center's KPI links: a fixed named view, using the same predicates as the cards.
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
                var foundItems = _inventoryService.Search(omniSearch, filterRheem, filterType, filterBrand, filterBranch,
                    filterLocParent, filterLocMajor, filterLocSub);
                searchResult.Items = foundItems;

                if (!string.IsNullOrEmpty(omniSearch)) searchResult.Mode = "Omni";
                else if (foundItems.Any() || mode == "Filter") searchResult.Mode = "Filter";
            }

            ViewBag.SearchResult = searchResult;
            ViewBag.InventoryService = _inventoryService;

            // Type and Brand options come from what this viewer can see. The server match is
            // still Contains, so "Motor" also returns ID Motor and OD Motor.
            ViewBag.FilterTypeOptions = allItems.Select(i => i.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
            ViewBag.FilterBrandOptions = allItems.Select(i => i.Brand)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(b => b).ToList();
            ViewBag.LocationFilterRows = BuildLocationFilterRows();

            // itemsList is Line-scoped and has to stay that way: AddItem has no Line check.
            // Transfers get their own list, scoped to exactly what this search returned.
            ViewBag.TransferItemsJson = System.Text.Json.JsonSerializer.Serialize(
                searchResult.Items.Select(i => new
                {
                    id = i.ItemId,
                    name = i.ItemName,
                    type = i.Type,
                    line = i.Line,
                    // Each stack with its team, so the requester sees whose slice they are picking.
                    variants = i.ActiveVariants.OrderBy(v => v.VariantNumber).Select(v => new
                    {
                        vid = v.Id,
                        num = v.VariantNumber,
                        fda = v.FdaString,
                        qty = v.Quantity,
                        tcqty = v.ThermocoupledQty,
                        team = v.Team
                    }).ToList()
                }).ToList());

            ViewBag.OmniSearch = omniSearch ?? "";
            ViewBag.FilterRheem = filterRheem ?? "";
            ViewBag.FilterType = filterType ?? "";
            ViewBag.FilterBrand = filterBrand ?? "";
            ViewBag.FilterLocParent = filterLocParent ?? "";
            ViewBag.FilterLocMajor = filterLocMajor ?? "";
            ViewBag.FilterLocSub = filterLocSub ?? "";
            ViewBag.FilterBranch = filterBranch ?? "";

            // "N reserved" on result rows.
            ViewBag.HeldPerItem = _reservations.HeldPerItem();

            return View();
        }

        // The Item Card: everything VIS stores about one item, returned as a partial so it is always fresh.
        // GetById is unfiltered by design. The activity list still goes through ApplyLogVisibility.
        [HttpGet]
        public IActionResult ItemCard(string itemId)
        {
            var item = _inventoryService.GetById(itemId ?? "");
            if (item == null) return NotFound();

            var draft = _orderService.GetCurrentDraft().Entries;
            var vm = new ItemCardViewModel
            {
                Item = item,
                Branch = OrgStructure.BranchFor(item.Line) ?? "",
                IsOwnLine = _inventoryService.IsOwnLine(item.Line),
                OnHand = item.Quantity,
                AvailableToViewer = _inventoryService.GetAvailableForViewer(item),
                // Line status too: a Split line's units have already left.
                CommittedToPending = _db.OrderItems.AsNoTracking()
                    .Where(oi => oi.ItemId == item.ItemId && oi.Order.Status == "Pending" && oi.Status == "Pending")
                    .Sum(oi => (int?)oi.Quantity) ?? 0,
                OutOnLoan = _db.OrderItems.AsNoTracking()
                    .Where(oi => oi.ItemId == item.ItemId && oi.LoanOutstanding > 0)
                    .Sum(oi => (int?)oi.LoanOutstanding) ?? 0,
                IsCompressor = InventoryService.IsCompressorType(item.Type),
                IsMotor = InventoryService.IsMotorType(item.Type),
                IsLoanable = InventoryService.IsControlType(item.Type) || InventoryService.IsMotorType(item.Type),
                InCartQty = draft.Where(e => e.ItemId == item.ItemId).Sum(e => e.Quantity),
                RecentLogs = _inventoryService.ApplyLogVisibility(_db.TransactionLogs.AsNoTracking())
                    .Where(t => t.ItemId == item.ItemId)
                    .OrderByDescending(t => t.Timestamp)
                    .Take(5)
                    .ToList()
            };
            if (vm.IsCompressor)
                vm.CompressorUnits = _db.CompressorUnits.AsNoTracking()
                    .Where(u => u.ItemId == item.ItemId).OrderByDescending(u => u.RecordedAt).ToList();
            if (vm.IsMotor)
                vm.MotorUnits = _db.MotorUnits.AsNoTracking()
                    .Where(u => u.ItemId == item.ItemId).OrderByDescending(u => u.RecordedAt).ToList();

            // The Reserved section, tile and columns.
            vm.Reservations = _reservations.LiveForItem(item.ItemId);
            vm.CanReserve = _reservations.CanReserve(item) && item.ActiveVariants.Any();
            vm.ManageableReservationIds = vm.Reservations.Where(r => _reservations.CanManage(r, item)).Select(r => r.Id).ToHashSet();
            vm.MyReservationIds = vm.Reservations.Where(r => _reservations.IsMine(r)).Select(r => r.Id).ToHashSet();

            return PartialView("_ItemCardPartial", vm);
        }

        // The shared definitions of low stock, out of stock and needs-a-PN.
        // GetLowStockItems includes zero-stock items; the KPI strip's Low Stock count does not.
        private static bool NeedsRheemPn(string? pn) =>
            string.IsNullOrWhiteSpace(pn) || pn.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase);

        private static List<InventoryItem> GetLowStockItems(List<InventoryItem> allItems) =>
            allItems.Where(i => i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold).OrderBy(i => i.Quantity).ToList();

        private static List<InventoryItem> GetOutOfStockItems(List<InventoryItem> allItems) =>
            allItems.Where(i => i.Quantity == 0).OrderBy(i => i.ItemName).ToList();

        private static List<InventoryItem> GetPnBacklogItems(List<InventoryItem> allItems) =>
            allItems.Where(i => NeedsRheemPn(i.RheemPartNumber)).OrderBy(i => i.ItemName).ToList();

        // Data every Search Center-side view needs, shared by Index() and SearchCenter().
        private void PopulateSearchViewBag(List<InventoryItem> allItems)
        {
            // The session's Branch, for greying out the other Quick Filter buttons: Line wins, else Branch, else blank.
            ViewBag.MyBranch = !string.IsNullOrWhiteSpace(_currentUser.Line)
                ? (OrgStructure.BranchFor(_currentUser.Line) ?? "")
                : (_currentUser.Branch ?? "");

            // Recorded On Hand units per stack, for Location Transfer's checklist. One query per table.
            var compressorsByVariant = _db.CompressorUnits.AsNoTracking()
                .Where(u => u.Status == UnitStatus.OnHand && u.ItemVariantId != null)
                .OrderBy(u => u.RecordedAt)
                .ToList()
                .GroupBy(u => u.ItemVariantId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(u => (object)new { id = u.Id, serial = u.SerialNumber, lab = u.LabNumber }).ToList());
            var motorsByVariant = _db.MotorUnits.AsNoTracking()
                .Where(u => u.Status == UnitStatus.OnHand && u.ItemVariantId != null)
                .OrderBy(u => u.RecordedAt)
                .ToList()
                .GroupBy(u => u.ItemVariantId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(u => (object)new { id = u.Id, lab = u.LabNumber }).ToList());

            var autocompleteData = allItems.Select(i => new {
                id = i.ItemId,
                name = i.ItemName,
                // Identity fields the Modify Stock "Edit Details" pane prefills.
                rpn = i.RheemPartNumber,
                line = i.Line,
                team = i.Team,
                brand = i.Brand,
                desc = i.Description,
                threshold = i.AlertThreshold,
                quantity = i.Quantity,
                // Type drives the motors-only TC gate on the client.
                type = i.Type,
                // Primary-variant location (back-compat seed for the modify-stock widget).
                parent = i.Parent,
                major = i.Major,
                sub = i.Sub,
                rack = i.Rack,
                row = i.Row,
                fda = i.FdaString,
                // Every active location: variant selector, transfer seeding, client-side merge detection.
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
                    // The TC subset, for the modal's TC cap.
                    tcqty = v.ThermocoupledQty,
                    // Which team's slice this stack is.
                    team = v.Team,
                    // Recorded On Hand units on this stack (see above).
                    cunits = compressorsByVariant.GetValueOrDefault(v.Id) ?? new List<object>(),
                    munits = motorsByVariant.GetValueOrDefault(v.Id) ?? new List<object>()
                }).ToList()
            }).ToList();
            ViewBag.AutocompleteJson = System.Text.Json.JsonSerializer.Serialize(autocompleteData);
            ViewBag.OrgStructureJson = System.Text.Json.JsonSerializer.Serialize(OrgStructure.BranchLines);
            // The location vocabulary, straight from the table.
            ViewBag.LocationParents = BuildLocationParents();
            ViewBag.LocationTreeJson = System.Text.Json.JsonSerializer.Serialize(BuildLocationTree());
            ViewBag.LocationHierarchyCodedJson = System.Text.Json.JsonSerializer.Serialize(BuildLocationHierarchyCoded());
            ViewBag.RackRowJson = System.Text.Json.JsonSerializer.Serialize(BuildRackRowMap());
            // Only active teams are offered. Hidden ones stay on items already using them.
            ViewBag.ActiveTeams = _db.Teams.AsNoTracking()
                .Where(t => t.IsActive).OrderBy(t => t.Name).ToList();
            // Team -> home Line, a suggestion only. Teams with no Line are absent from the map.
            ViewBag.TeamLinesJson = System.Text.Json.JsonSerializer.Serialize(
                _db.Teams.AsNoTracking().Where(t => t.IsActive && t.Line != null)
                    .ToDictionary(t => t.Name, t => t.Line));
            // The export filter offers hidden teams too: a retired team still needs a report.
            ViewBag.AllTeams = _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToList();
            // This user's teams, so Add to Cart's team picker only shows when it is ambiguous for them.
            ViewBag.MyTeamsJson = System.Text.Json.JsonSerializer.Serialize(_inventoryService.GetUserTeamNames(_currentUser.Name));
        }

        // Back to the page the request came from. Falls back to an action only when there is no Referer.
        private IActionResult SmartRedirect(string fallbackAction = "Index")
        {
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction(fallbackAction) : Redirect(referer);
        }

        // Standard: register, order. Engineer: change, scrap.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult AddToCart(string itemId, int quantity, int? requestedVariantId = null, int thermocoupledCount = 0, string? requestedTeam = null)
        {
            _orderService.AddItem(itemId, quantity, requestedVariantId, thermocoupledCount, requestedTeam);
            // cartCount updates the top-bar cart badge without a reload.
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, message = "Added to cart!", cartCount = _orderService.GetCurrentDraft().Entries.Count });
            TempData["Message"] = "Item added to cart.";
            return SmartRedirect();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult RemoveFromLedger(string itemId, int? requestedVariantId = null, string? requestedTeam = null)
        {
            // All three identify one cart line, the same key AddItem stacks on.
            _orderService.RemoveItem(itemId, requestedVariantId, requestedTeam);
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

        // Stock and admin actions

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

        // Bulk default threshold. Management and up, not Engineers.
        // A manager with teams sets it for those teams' items; with none, for every item.
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
            string? newLine = null, string? variantTeam = null,
            int[]? movingUnitIds = null, int[]? movingMotorUnitIds = null,
            int[]? leavingUnitIds = null, int[]? leavingMotorUnitIds = null)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return SmartRedirect();

            // Add, Adjustment and Location Transfer are Standard. Scrap, Ownership and
            // Edit Details need Engineer.
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
                // newLine is passed as null: a stale value from the hidden transfer pane must not reassign the Line.
                var (ok, message) = _inventoryService.UpdateItemDetails(itemId, newRheemPart, newDescription, newBrand, null);
                if (ok) TempData["Success"] = $"{itemId}: {message}";
                else TempData["Error"] = message;
                return SmartRedirect();
            }

            // Compressor serials on an Add, posted as serial_N. Ignored for every other action.
            var serials = Request.Form.Keys
                .Where(k => k.StartsWith("serial_"))
                .Select(k => Request.Form[k].ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            try
            {
                var result = _inventoryService.ModifyStock(itemId, actionType, quantity, newGroup, newTeam,
                    newParent, newMajor, newSub, newRack, newRow, targetVariant, transferQty, thermocoupledQty,
                    newLine, serials, variantTeam, movingUnitIds, movingMotorUnitIds,
                    leavingUnitIds, leavingMotorUnitIds);
                if (result != null)
                {
                    TempData["Success"] = $"Transaction '{actionType}' applied to {itemId}.";

                    // Edge-triggered low-stock email: fires only on the crossing.
                    _emailService.CheckAndSendStockAlert(result.Value.item, result.Value.oldQty, result.Value.newQty);
                }
                else
                {
                    // null means the item lookup failed.
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
                // Same reset as InventoryService.CreateItem: the getters below would read a posted variant first.
                newItem.Id = 0;
                newItem.Variants = new List<ItemVariant>();
                newItem.RegisteredAt = null;
                newItem.AlertThreshold = 0;

                newItem.ItemName ??= "Unnamed Item";
                newItem.Type ??= "General";
                newItem.Brand ??= "Unknown";
                newItem.Description ??= "";

                // Rheem part number: required on new registrations, and rejected if another item owns it.
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

                // Team is optional. Blank is a real choice; the project code follows the team.
                newItem.Team = (newItem.Team ?? "").Trim();
                if (string.Equals(newItem.Team, "N/A", StringComparison.OrdinalIgnoreCase))
                    newItem.Team = "";
                newItem.ProjectCode = newItem.Team.Length == 0
                    ? ""
                    : (_db.Teams.FirstOrDefault(t => t.Name == newItem.Team)?.ProjectCode ?? "");
                newItem.Line ??= "";
                // Line is required on new registrations. Older items with a blank Line are not touched.
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

                // Group is derived from the Line picked on this form, set once, never changed.
                newItem.Group = OrgStructure.GroupFor(newItem.Line);

                // Item id: assigned here. The form's id box is a preview and whatever it posted is discarded.
                newItem.ItemId = _inventoryService.GenerateItemId(newItem.Group, newItem.Type);

                // Location: the form posts friendly names. Codes are derived here and the FDA string rebuilt from them.
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

                // Compressor serials, posted as serial_0, serial_1, ... Ignored for other types.
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

        // Deletes one empty stack and leaves the rest of the item. Admin, like Delete Item: it removes a row.
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
            string? exportGroup, string? exportTeam, string? exportType, string? exportBrand,
            string? exportLocParent, string? exportLocMajor, string? exportLocSub,
            string? exportRack, string? exportRow,
            bool expAvailable, bool expAlerts, bool expScrap, bool expOwnership, string expTimeFrame)
        {
            var fileBytes = _inventoryService.ExportToCsv(
                exportGroup, exportTeam, exportType, exportBrand,
                exportLocParent, exportLocMajor, exportLocSub, exportRack, exportRow,
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

        // Logs and orders

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

            // Transactions are the model, with the same Line visibility as browsing.
            var transactions = _inventoryService.ApplyLogVisibility(_db.TransactionLogs
                .AsNoTracking())
                .OrderByDescending(t => t.Timestamp)
                .ToList();

            return View(transactions);
        }


        // Location vocabulary. Codes are derived here with LocationCodec.Encode, never read from the table.
        private List<(string Code, string Name)> BuildLocationParents()
        {
            return _db.Locations.AsNoTracking()
                .Where(l => l.Level == LocationLevel.Parent && l.IsActive)
                .OrderBy(l => l.Name).ToList()
                .Select(l => (Code: LocationCodec.Encode(l.Name), Name: l.Name))
                .ToList();
        }

        /// <summary>
        /// Flat (Parent, Major, Sub) rows with both code and name at each level, for Search Center's
        /// location filter. The code is what is stored and posted; the name is what a person reads.
        /// </summary>
        private List<(string PCode, string PName, string MCode, string MName, string SCode, string SName)> BuildLocationFilterRows()
        {
            var all = _db.Locations.AsNoTracking().Where(l => l.IsActive).ToList();
            var rows = new List<(string, string, string, string, string, string)>();
            foreach (var p in all.Where(l => l.Level == LocationLevel.Parent).OrderBy(l => l.Name))
            {
                string pc = LocationCodec.Encode(p.Name);
                var majors = all.Where(l => l.Level == LocationLevel.Major && l.ParentId == p.Id).OrderBy(l => l.Name).ToList();
                if (majors.Count == 0) { rows.Add((pc, p.Name, "", "", "", "")); continue; }
                foreach (var m in majors)
                {
                    string mc = LocationCodec.Encode(m.Name);
                    var subs = all.Where(l => l.Level == LocationLevel.Sub && l.ParentId == m.Id).OrderBy(l => l.Name).ToList();
                    if (subs.Count == 0) { rows.Add((pc, p.Name, mc, m.Name, "", "")); continue; }
                    foreach (var s in subs)
                        rows.Add((pc, p.Name, mc, m.Name, LocationCodec.Encode(s.Name), s.Name));
                }
            }
            return rows;
        }

        // { "RD Lab": { "Metrology Mezzanine": ["Samurai", "Ninja"] } }. Names only:
        // the client derives codes with its own copy of the rule.
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

        // The same tree keyed by code (Parent -> Major -> [Sub]), for the Modify Stock and
        // Location Transfer cascade. Built from the Locations table, not from stored stock.
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

        // Rack/Row suggestions: what is already typed on real stock at each Parent|Major|Sub.
        // A shortcut, never a gate. The key always has exactly three parts.
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


        // Bulk intake. Standard and up. Location pickers only offer what exists;
        // "not listed" holds the batch for the superuser instead of blocking the person.

        [RequireLevel(AccessLevels.Standard)]
        public IActionResult Intake()
        {
            ViewBag.LocationTreeJson = System.Text.Json.JsonSerializer.Serialize(BuildLocationTree());
            ViewBag.LocationParents = BuildLocationParents();
            ViewBag.RackRowJson = System.Text.Json.JsonSerializer.Serialize(BuildRackRowMap());
            ViewBag.ActiveTeams = _db.Teams.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToList();
            ViewBag.OrgStructureJson = System.Text.Json.JsonSerializer.Serialize(OrgStructure.BranchLines);
            // Team -> home Line, so picking a Team can suggest a Branch and Line.
            ViewBag.TeamLinesJson = System.Text.Json.JsonSerializer.Serialize(
                _db.Teams.AsNoTracking().Where(t => t.IsActive && t.Line != null)
                    .ToDictionary(t => t.Name, t => t.Line));
            // Seeds the Branch and Line selects from the user's own assignment. JSON, since the page script reads them.
            ViewBag.MyLineJson = System.Text.Json.JsonSerializer.Serialize(_currentUser.Line ?? "");
            ViewBag.MyBranchJson = System.Text.Json.JsonSerializer.Serialize(
                !string.IsNullOrWhiteSpace(_currentUser.Line)
                    ? (OrgStructure.BranchFor(_currentUser.Line) ?? "")
                    : (_currentUser.Branch ?? ""));
            // Known names and types, for the autocomplete and the "already registered" split.
            // Quantity is a [NotMapped] pass-through: Include() first, never inside the LINQ projection.
            ViewBag.KnownItemsJson = System.Text.Json.JsonSerializer.Serialize(
                _db.InventoryItems.AsNoTracking().Include(i => i.Variants).ToList()
                   .Select(i => new { id = i.ItemId, name = i.ItemName, type = i.Type, brand = i.Brand, rpn = i.RheemPartNumber, quantity = i.Quantity })
                   .ToList());
            ViewBag.KnownTypes = KnownItemTypes();
            ViewBag.MyPending = _db.IntakeBatches.AsNoTracking()
                .Where(b => b.SubmittedBy == _currentUser.Name && b.Status == IntakeStatus.Pending)
                .OrderByDescending(b => b.SubmittedAt).ToList();
            return View();
        }

        // The Type vocabulary is whatever real items carry. One definition for the dropdown and SubmitIntake's gate.
        private List<string> KnownItemTypes() =>
            _db.InventoryItems.AsNoTracking()
               .Select(i => i.Type).Distinct().Where(t => t != "").OrderBy(t => t).ToList();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SubmitIntake(
            string line, string? team,
            string? parentCode, string? majorCode, string? subCode, string? rack, string? row,
            string? requestedLocation,
            string[]? itemName, string[]? type, string[]? brand, string[]? rpn, int[]? qty,
            string[]? serial, string[]? typeNew,
            bool preview = false)
        {
            // Extra serials and TC counts carry a row index in the field name
            // (extraSerial_{row}_{slot}, thermocoupledQty_{row}): array binding cannot key by row.
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

            // Type gate: an existing type (stored in its existing spelling), or one the row
            // flagged as new. Anything else refuses the whole batch.
            var knownTypes = KnownItemTypes();
            var typeErrors = new List<string>();

            var lines = new List<InventoryService.IntakeLine>();
            for (int i = 0; itemName != null && i < itemName.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(itemName[i])) continue;

                string rowType = (type != null && i < type.Length ? type[i] : "")?.Trim() ?? "";
                string? canonType = knownTypes.FirstOrDefault(t => string.Equals(t, rowType, StringComparison.OrdinalIgnoreCase));
                bool flaggedNew = typeNew != null && i < typeNew.Length && typeNew[i] == "1";
                if (rowType.Length == 0)
                    typeErrors.Add($"Row {i + 1} ({itemName[i].Trim()}): Type is required.");
                else if (canonType != null)
                    rowType = canonType;
                else if (!flaggedNew)
                    typeErrors.Add($"Row {i + 1} ({itemName[i].Trim()}): \"{rowType}\" isn't an existing type — pick one, or add it as a new type.");

                var rowSerials = new List<string>();
                if (serial != null && i < serial.Length && !string.IsNullOrWhiteSpace(serial[i]))
                    rowSerials.Add(serial[i].Trim());
                if (extraSerialsByRow.TryGetValue(i, out var extras)) rowSerials.AddRange(extras);

                lines.Add(new InventoryService.IntakeLine
                {
                    ItemName = itemName[i],
                    Type = rowType,
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
            if (typeErrors.Count > 0)
            {
                TempData["Error"] = "Nothing was imported. " + string.Join(" ", typeErrors);
                return RedirectToAction("Intake");
            }

            team = (team ?? "").Trim();
            if (string.Equals(team, "N/A", StringComparison.OrdinalIgnoreCase)) team = "";

            // Line is required, and checked before the hold path so no batch waits in the queue with no owner.
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

            // Location not listed: hold the batch. The Parent select's "Not listed..." posts
            // the sentinel "__NEW__", not blank.
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

            // "Not listed..." with nothing typed: refuse, so "__NEW__" never reaches CommitIntake.
            if (parentUnresolved)
            {
                TempData["Error"] = "Pick a location, or choose \"Not listed...\" and say what you call it.";
                return RedirectToAction("Intake");
            }

            var result = _inventoryService.CommitIntake(
                lines, line ?? "", team,
                parentCode ?? "", majorCode ?? "", subCode ?? "", rack ?? "", row ?? "",
                _currentUser.Name, preview);

            if (preview || !result.Ok)
            {
                TempData["IntakePreview"] = System.Text.Json.JsonSerializer.Serialize(result);
                if (!result.Ok && !preview)
                {
                    // CommitIntake saves per row, so good rows did land. Resubmitting skips them.
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

        // Batch Modify Stock for intake rows that were already registered: the review modal's Apply All.
        // Fields carry a section index: stockItemId_N, stockAction_N, stockQty_N, stockTc_N, stockSerial_N_slot.
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

            // These items already exist, so "Not listed" makes no sense here. Refuse the "__NEW__" sentinel.
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

        // My Activity: this user's orders, loans and internal transfers.
        public IActionResult MyActivity()
        {
            var me = _currentUser.Name;

            var myOrders = _db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.RequestedBy == me)
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            var invLookup = _db.InventoryItems.AsNoTracking().Include(i => i.Variants)
                .ToDictionary(i => i.ItemId);

            // Friendly location label for the return picker: codes decoded, "0" and blank levels dropped.
            string VLabel(Visual_Inventory_System.Models.ItemVariant v)
            {
                bool Real(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0";
                var crumbs = new List<string>();
                if (Real(v.Parent)) crumbs.Add(LocationCodec.Decode(v.Parent));
                if (Real(v.Major)) crumbs.Add(LocationCodec.Decode(v.Major));
                if (Real(v.Sub)) crumbs.Add(LocationCodec.Decode(v.Sub));
                // Rack and Row are shown as stored, so two stacks at different racks can be told apart.
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
                    // Only the shelves this line's own team owns. "" still sees every location.
                    var activeVars = (inv?.ActiveVariants ?? Enumerable.Empty<Visual_Inventory_System.Models.ItemVariant>())
                        .Where(v => string.IsNullOrEmpty(it.Team) || v.Team == it.Team)
                        .OrderBy(v => v.VariantNumber).ToList();

                    // The named units still out on this line. Usually fewer than Outstanding.
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

            // Internal transfers: my own requests with full history, and Requested rows awaiting my approval.
            TransferLineViewModel ToTransferLine(TransferRequest b)
            {
                invLookup.TryGetValue(b.ItemId, out var bInv);
                var bActiveVars = (bInv?.ActiveVariants ?? Enumerable.Empty<ItemVariant>()).OrderBy(v => v.VariantNumber).ToList();
                bool isComp = InventoryService.IsCompressorType(bInv?.Type);
                bool isMotor = InventoryService.IsMotorType(bInv?.Type);

                // Only units with a serial are named options. Anonymous stock is what "No serial" is for.
                // Units in a live reservation are left out: the approval would refuse them.
                var heldIds = isComp ? ReservationService.PinOwners(_db, b.ItemId) : new Dictionary<int, string>();
                var onHandUnits = isComp
                    ? _db.CompressorUnits.AsNoTracking()
                        .Where(cu => cu.ItemId == b.ItemId && cu.Status == UnitStatus.OnHand
                                  && cu.SerialNumber != null && cu.SerialNumber != "")
                        .OrderBy(cu => cu.RecordedAt)
                        .ToList()
                        .Where(cu => !(cu.ReservationId.HasValue && heldIds.ContainsKey(cu.ReservationId.Value)))
                        .ToList()
                    : new List<CompressorUnit>();

                string? resolvedLabel = null;
                if (b.RequestedVariantId.HasValue)
                {
                    var rv = bActiveVars.FirstOrDefault(x => x.Id == b.RequestedVariantId.Value);
                    resolvedLabel = rv != null ? VLabel(rv) : "requested location no longer active -- will auto-pull";
                }

                // The pull plan from the service that will execute it, one slot per physical unit.
                var unitSlots = new List<TransferUnitSlotViewModel>();
                if (isComp && bInv != null && b.Status == TransferStatus.Requested)
                {
                    foreach (var (variantId, take) in _orderService.PlanTransferPull(bInv, b.RequestedVariantId, b.Quantity, b.RequesterUserName))
                    {
                        var pv = bActiveVars.FirstOrDefault(v => v.Id == variantId);
                        var here = onHandUnits.Where(x => x.ItemVariantId == variantId)
                            .Select(x => new OnHandUnitViewModel { SerialNumber = x.SerialNumber, LabNumber = x.LabNumber })
                            .ToList();
                        for (int u = 0; u < take; u++)
                            unitSlots.Add(new TransferUnitSlotViewModel
                            {
                                VariantId = variantId,
                                LocationLabel = pv != null ? VLabel(pv) : $"Variant #{variantId}",
                                OnHandUnits = here,
                                // Staggered, so two units off one shelf do not both open on unit #1.
                                DefaultSerial = u < here.Count ? here[u].SerialNumber : null
                            });
                    }
                }

                return new TransferLineViewModel
                {
                    UnitSlots = unitSlots,
                    Id = b.Id,
                    ItemId = b.ItemId,
                    ItemName = bInv?.ItemName ?? "Unknown",
                    RheemPartNumber = bInv?.RheemPartNumber ?? "",
                    ItemType = bInv?.Type ?? "",
                    Quantity = b.Quantity,
                    ThermocoupledCount = b.ThermocoupledCount,
                    RequesterUserName = b.RequesterUserName,
                    Status = b.Status,
                    RequestedAt = b.RequestedAt,
                    Note = b.Note,
                    DecidedBy = b.DecidedBy,
                    DecidedAt = b.DecidedAt,
                    IsCompressor = isComp,
                    IsMotor = isMotor,
                    ResolvedLocationLabel = resolvedLabel,
                    LocationChoices = bActiveVars.Select(v => new VariantChoiceViewModel
                    {
                        VariantId = v.Id,
                        Label = VLabel(v),
                        Quantity = v.Quantity,
                        OnHandUnits = onHandUnits.Where(u => u.ItemVariantId == v.Id)
                            .Select(u => new OnHandUnitViewModel { SerialNumber = u.SerialNumber, LabNumber = u.LabNumber }).ToList()
                    }).ToList()
                };
            }

            var myTransferRequests = _db.TransferRequests.AsNoTracking()
                .Where(b => b.RequesterUserName == me)
                .OrderByDescending(b => b.RequestedAt)
                .ToList()
                .Select(ToTransferLine).ToList();

            var awaitingApproval = _db.TransferRequests.AsNoTracking()
                .Where(b => b.Status == TransferStatus.Requested)
                .OrderByDescending(b => b.RequestedAt)
                .ToList()
                // Line, and once an item is split, Team: only requests this user can actually approve.
                .Where(b => invLookup.TryGetValue(b.ItemId, out var lentInv) && _inventoryService.CanApproveTransfer(lentInv, b.RequestedVariantId))
                .Select(ToTransferLine).ToList();

            ViewBag.LocationParents = BuildLocationParents();
            return View(new MyActivityViewModel
            {
                UserName = me,
                Orders = myOrders,
                Loans = loans,
                Transfer = new TransferSectionViewModel { MyRequests = myTransferRequests, AwaitingApproval = awaitingApproval }
            });
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
            return RedirectToAction("MyActivity");
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
            return RedirectToAction("MyActivity");
        }

        // Internal transfers

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult RequestTransfer(string itemId, int quantity,
            int? requestedVariantId = null, int thermocoupledCount = 0, string? note = null)
        {
            try
            {
                _orderService.RequestTransfer(itemId, quantity, requestedVariantId, thermocoupledCount, note);
                TempData["Success"] = "Transfer request sent.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return SmartRedirect();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult ApproveTransfer(int transferRequestId)
        {
            try
            {
                // compLab_{n} / compSerial_{n}, n = 0-based unit index. One transfer request is one line.
                var pairs = new List<(string? Lab, string? Serial)>();
                var indices = Request.Form.Keys
                    .Where(k => k.StartsWith("compLab_"))
                    .Select(k => int.TryParse(k.Substring("compLab_".Length), out int n) ? n : -1)
                    .Where(n => n >= 0)
                    .OrderBy(n => n);
                foreach (var n in indices)
                {
                    string? lab = Request.Form[$"compLab_{n}"].ToString();
                    string? serial = Request.Form[$"compSerial_{n}"].ToString();
                    if (serial == "__ADD__") serial = Request.Form[$"compSerialManual_{n}"].ToString();
                    else if (serial == "__NONE__") serial = null;
                    pairs.Add((string.IsNullOrWhiteSpace(lab) ? null : lab, string.IsNullOrWhiteSpace(serial) ? null : serial));
                }

                _orderService.ApproveTransfer(transferRequestId, pairs.Count > 0 ? pairs : null);
                TempData["Success"] = "Transfer approved.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("MyActivity");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult DenyTransfer(int transferRequestId)
        {
            try
            {
                _orderService.DenyTransfer(transferRequestId);
                TempData["Success"] = "Transfer denied.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("MyActivity");
        }

        public IActionResult PickupQueue()
        {
            var orders = _db.Orders.Where(o => o.Status == "Pending").Include(o => o.Items).AsNoTracking().OrderBy(o => o.CreatedAt).ToList();
            var result = orders.Select(order => {
                var vm = new PendingOrderViewModel { OrderId = order.Id, CreatedAt = order.CreatedAt, Status = order.Status, RequestedBy = order.RequestedBy, SplitFromOrderId = order.SplitFromOrderId };
                var canFulfill = true;
                var blockedByPriority = false;

                // Only lines still Pending, the same set PickUpOrder acts on. A Split line's units have left.
                foreach (var it in order.Items.Where(i => i.Status == "Pending"))
                {
                    // Measured for whoever placed the order, against earlier claims only.
                    var availForThis = _inventoryService.GetAvailableForOrder(it.ItemId, order.CreatedAt, it.Team, order.RequestedBy, it.Id);
                    var totalAvailable = _inventoryService.GetAvailableIgnoringPriority(it.ItemId, it.Team, order.RequestedBy, it.Id);
                    var itemEntity = _db.InventoryItems.AsNoTracking().Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);

                    // Friendly location labels: codes decoded, "0" and blank levels dropped.
                    string VLabel(Visual_Inventory_System.Models.ItemVariant v)
                    {
                        bool Real(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0";
                        var crumbs = new List<string>();
                        if (Real(v.Parent)) crumbs.Add(LocationCodec.Decode(v.Parent));
                        if (Real(v.Major)) crumbs.Add(LocationCodec.Decode(v.Major));
                        if (Real(v.Sub)) crumbs.Add(LocationCodec.Decode(v.Sub));
                        // Rack and Row are shown as stored, so two stacks at different racks can be told apart.
                        if (Real(v.Rack)) crumbs.Add(v.Rack.Trim());
                        if (Real(v.Row)) crumbs.Add(v.Row.Trim());
                        string path = crumbs.Count > 0 ? string.Join(" › ", crumbs) : v.FdaString;
                        return $"V{v.VariantNumber} — {path} · Qty {v.Quantity}";
                    }

                    // The same team scoping as the real pull, so every choice offered can be honoured.
                    var activeVars = (itemEntity?.ActiveVariants ?? Enumerable.Empty<Visual_Inventory_System.Models.ItemVariant>())
                        .Where(v => string.IsNullOrEmpty(it.Team) || v.Team == it.Team)
                        .OrderBy(v => v.VariantNumber).ToList();

                    // On-hand units per location, for the serial picker. Only rows with a serial are named options.
                    // A serial held for someone else is not offered. The orderer's own held units come first.
                    var pinOwners = ReservationService.PinOwners(_db, it.ItemId);
                    bool HeldByOther(int? rid) => rid.HasValue && pinOwners.TryGetValue(rid.Value, out var who)
                        && !string.Equals(who, order.RequestedBy ?? "", StringComparison.OrdinalIgnoreCase);
                    bool HeldByMe(int? rid) => rid.HasValue && pinOwners.TryGetValue(rid.Value, out var who)
                        && string.Equals(who, order.RequestedBy ?? "", StringComparison.OrdinalIgnoreCase);
                    var onHandByVariant = InventoryService.IsCompressorType(itemEntity?.Type)
                        ? _db.CompressorUnits.AsNoTracking()
                              .Where(c => c.ItemId == it.ItemId && c.Status == Visual_Inventory_System.Models.UnitStatus.OnHand && c.ItemVariantId != null
                                       && c.SerialNumber != null && c.SerialNumber != "")
                              .OrderBy(c => c.RecordedAt)
                              .ToList()
                              .Where(c => !HeldByOther(c.ReservationId))
                              .OrderBy(c => HeldByMe(c.ReservationId) ? 0 : 1)
                              .GroupBy(c => c.ItemVariantId!.Value)
                              .ToDictionary(g => g.Key, g => g.Select(c => new OnHandUnitViewModel { SerialNumber = c.SerialNumber, LabNumber = c.LabNumber }).ToList())
                        : new Dictionary<int, List<OnHandUnitViewModel>>();

                    // The TC on this line's shelves, minus other people's holds: the count Pick Up checks before it pulls.
                    int tcAsked = InventoryService.IsMotorType(itemEntity?.Type) ? it.ThermocoupledCount : 0;
                    int tcOnShelves = 0;
                    if (tcAsked > 0 && itemEntity != null)
                    {
                        var holdView = StockClaims.FromVariants(_db, it.ItemId, itemEntity.Variants);
                        tcOnShelves = activeVars.Sum(v => Math.Max(0, v.ThermocoupledQty - holdView.HeldByOthers(v.Id, order.RequestedBy ?? "").Tc));
                    }

                    var itemVm = new PendingOrderItemViewModel
                    {
                        OrderItemId = it.Id,
                        ItemId = it.ItemId,
                        ItemName = itemEntity?.ItemName ?? "Unknown",
                        RheemPartNumber = itemEntity?.RheemPartNumber ?? "",
                        Type = itemEntity?.Type,
                        Quantity = it.Quantity,
                        AvailableForThisOrder = availForThis,
                        TcAsked = tcAsked,
                        TcOnShelves = tcOnShelves,
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

                // Lines held for the Non-TC Motor answer. Shown on their own, never pickable.
                foreach (var it in order.Items.Where(i => i.Status == "Waiting"))
                {
                    var waitItem = _db.InventoryItems.AsNoTracking().FirstOrDefault(i => i.ItemId == it.ItemId);
                    vm.WaitingLines.Add(new WaitingLineViewModel
                    {
                        OrderItemId = it.Id,
                        ItemId = it.ItemId,
                        ItemName = waitItem?.ItemName ?? "Unknown",
                        Quantity = it.Quantity,
                        TcAsked = it.ThermocoupledCount,
                        Note = it.Note ?? "",
                        CanAnswer = _orderService.CanAnswerNonTc(order, waitItem)
                    });
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

        // Pins a "Go Store These" task. Engineer and up. A title and an optional note, nothing structured.
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

        // Claims an open task. Standard and up. First claim wins.
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

        // Marks a claimed task done. Only the person who claimed it.
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

        // Delivery routing. A named recipient's delivery also reaches the Engineers on their Line,
        // else on their Branch's Lines. AudienceLinesFor and RecipientsReaching are inverses:
        // change one, change the other.

        /// <summary>Lines whose L3s a delivery for this recipient should reach. Empty = nobody but the recipient.</summary>
        private static string[] AudienceLinesFor(string? line, string? branch)
        {
            var l = (line ?? "").Trim();
            if (l.Length > 0) return new[] { l };

            // Branch fallback: only for someone with a Branch and no Line.
            var b = (branch ?? "").Trim();
            if (b.Length > 0 && OrgStructure.BranchLines.TryGetValue(b, out var lines)) return lines;

            return Array.Empty<string>();
        }

        /// <summary>UserNames whose deliveries this viewer should see: the inverse of AudienceLinesFor.</summary>
        private List<string> RecipientsReaching(string? myLine)
        {
            var mine = (myLine ?? "").Trim();
            if (mine.Length == 0) return new List<string>();
            string mineLower = mine.ToLower();
            string myBranch = (OrgStructure.BranchFor(mine) ?? "").Trim();

            return _db.Users.AsNoTracking()
                .Where(u => u.IsActive
                    && ((u.Line != null && u.Line.ToLower() == mineLower)
                        || ((u.Line == null || u.Line == "")
                            && myBranch != "" && u.Branch != null && u.Branch == myBranch)))
                .Select(u => u.UserName)
                .ToList();
        }

        // The Deliveries board: the Unknown bucket plus anything routed to this person. Engineer and up.
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult Deliveries()
        {
            string mine = _currentUser.Name;

            // An Engineer also sees deliveries for anyone whose audience includes their Line. Engineers only.
            var reaching = _currentUser.Level == AccessLevels.Engineer
                ? RecipientsReaching(_currentUser.Line)
                : new List<string>();

            var rows = _db.Deliveries.AsNoTracking()
                .Where(d => d.Status != "Done"
                    && (d.RecipientUserName == Delivery.UnknownRecipient
                        || d.RecipientUserName == mine
                        || reaching.Contains(d.RecipientUserName)))
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
            // Engineer and up: everyone who can open the board. AccessLevel rides along so the view can group the list.
            ViewBag.Recipients = _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.AccessLevel >= AccessLevels.Engineer)
                .OrderByDescending(u => u.AccessLevel)
                .ThenBy(u => u.DisplayName)
                .Select(u => new { u.UserName, u.DisplayName, u.AccessLevel, u.Line, u.Branch })
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
            // Matches the dropdown. A direct POST naming someone who cannot open the board is refused.
            if (!isUnknown && !_db.Users.Any(u => u.UserName == recipientUserName && u.IsActive && u.AccessLevel >= AccessLevels.Engineer))
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

            string extra = "";
            if (isUnknown)
            {
                // Exactly Engineer, not Engineer and up. Not line-aware: an unlabelled box has no Line.
                _notifications.CreateForLevel(AccessLevels.Engineer, AccessLevels.Engineer, "DeliveryReceived", message, "/Home/Deliveries", _currentUser.Name);
            }
            else
            {
                // The recipient always hears about it, in the second person.
                _notifications.Create(recipientUserName, "DeliveryReceived",
                    $"A delivery was logged for you by {_currentUser.Name}"
                    + (IsNa(brandOfItem) ? "" : $" — {brandOfItem}")
                    + (IsNa(trackingNumber) ? "" : $" (Tracking {trackingNumber})"),
                    "/Home/Deliveries");

                // The Engineers on their Line hear too, in the third person so nobody thinks it is theirs.
                var recip = _db.Users.AsNoTracking().FirstOrDefault(u => u.UserName == recipientUserName);
                if (recip != null)
                {
                    string who = string.IsNullOrWhiteSpace(recip.DisplayName) ? recipientUserName : recip.DisplayName;
                    int reached = _notifications.CreateForLine(
                        AudienceLinesFor(recip.Line, recip.Branch),
                        AccessLevels.Engineer,
                        "DeliveryReceived",
                        $"Delivery for {who}, logged by {_currentUser.Name}"
                        + (IsNa(brandOfItem) ? "" : $" — {brandOfItem}")
                        + (IsNa(trackingNumber) ? "" : $" (Tracking {trackingNumber})"),
                        "/Home/Deliveries",
                        // Excludes the logger and the recipient, who already has their own message.
                        _currentUser.Name, recipientUserName);
                    if (reached > 0)
                        extra = $" {who} and {reached} engineer{(reached == 1 ? "" : "s")} on their line were notified.";
                }
            }

            TempData["Success"] = "Delivery logged." + extra;
            return RedirectToAction("LogDelivery");
        }

        // Claims an open delivery. Engineer and up. First claim wins.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
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

        // Marks a claimed delivery done. Only the person who claimed it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
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
                // variantChoice_{orderItemId} = variantId, optional. Absent or blank = auto.
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

                // compLab_{orderItemId}_{n} / compSerial_{orderItemId}_{n}, n = 0-based unit index.
                // compSerial is a real serial, "__NONE__", or "__ADD__" (typed into compSerialManual_...).
                var compressorUnits = new Dictionary<int, List<(string? Lab, string? Serial)>>();
                foreach (var key in Request.Form.Keys)
                {
                    if (!key.StartsWith("compLab_")) continue;
                    var rest = key.Substring("compLab_".Length);
                    var parts = rest.Split('_');
                    if (parts.Length != 2) continue;
                    if (!int.TryParse(parts[0], out int oiId) || !int.TryParse(parts[1], out int unitIdx)) continue;
                    // The list is padded out to unitIdx, so cap it.
                    if (unitIdx < 0 || unitIdx >= MaxUnitsPerLine)
                        throw new InvalidOperationException("That pickup form isn't valid -- reload the queue and try again.");

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

                // TC lines held for the orderer's answer, said after whatever else happened.
                string held = result.WaitingLines.Count > 0 ? " Held: " + string.Join(" ", result.WaitingLines) : "";
                if (result.HasShortLines)
                {
                    // Short lines left stock untouched. Stash them for the queue page's correction form.
                    TempData["ShortPullOrderId"] = orderId;
                    TempData["ShortPullJson"] = System.Text.Json.JsonSerializer.Serialize(result.ShortLines);
                    TempData["Error"] = (result.ShortLines.Count == 1
                        ? $"{result.ShortLines[0].ItemName} came up short -- report the real shelf count below."
                        : $"{result.ShortLines.Count} items came up short -- report the real shelf counts below.") + held;
                }
                else if (result.WaitingLines.Count > 0)
                {
                    TempData["Error"] = $"Order #{orderId}: "
                        + (result.CompletedLines > 0 ? $"{result.CompletedLines} line{(result.CompletedLines == 1 ? "" : "s")} picked up." : "nothing picked up.")
                        + held;
                }
                else
                {
                    TempData["Success"] = "Order completed.";
                }
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            // Back to the page the request came from.
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Orders") : Redirect(referer);
        }

        // Ceiling on the per-unit form loops below, which run before the service validates. Far above any real line.
        private const int MaxUnitsPerLine = 5000;

        // Deliberate partial pickup. compLab_{u} / compSerial_{u} / compSerialManual_{u} are posted
        // for just the units leaving, u = 0-based.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult PickUpPartialAndSplit(int orderId, int orderItemId, int variantId, int pickupQty)
        {
            try
            {
                if (pickupQty > MaxUnitsPerLine)
                    throw new InvalidOperationException("That pickup quantity isn't valid -- reload the queue and try again.");
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

        // Short-pull correction. Standard on purpose.
        // corrected_{variantId} = the count the picker saw there. Only touched fields are posted.
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

                // leavingUnit_{variantId} / leavingMotor_{variantId}: recorded units ticked as gone, one field per unit.
                var leavingUnits = new Dictionary<int, IReadOnlyCollection<int>>();
                var leavingMotors = new Dictionary<int, IReadOnlyCollection<int>>();
                foreach (var key in Request.Form.Keys)
                {
                    bool compressor = key.StartsWith("leavingUnit_");
                    bool motor = key.StartsWith("leavingMotor_");
                    if (!compressor && !motor) continue;
                    if (!int.TryParse(key.Substring(compressor ? "leavingUnit_".Length : "leavingMotor_".Length), out int variantId)) continue;
                    var ids = Request.Form[key].Select(s => int.TryParse(s, out int id) ? id : -1).Where(id => id > 0).ToList();
                    (compressor ? leavingUnits : leavingMotors)[variantId] = ids;
                }

                var result = _orderService.ReportShortPull(orderId, orderItemId, corrections, leavingUnits, leavingMotors);

                if (result.HasShortLines)
                {
                    TempData["ShortPullOrderId"] = result.OrderId;
                    TempData["ShortPullJson"] = System.Text.Json.JsonSerializer.Serialize(result.ShortLines);
                    TempData["Error"] = "Still short after that correction -- another check may have beaten you to it.";
                }
                else if (result.WaitingLines.Count > 0)
                {
                    TempData["Error"] = $"Stock corrected and reissued as Order #{result.NewOrderId}, but nothing was picked up. Held: " + string.Join(" ", result.WaitingLines);
                }
                else if (result.NewOrderId.HasValue)
                {
                    TempData["Success"] = $"Stock corrected and Order #{result.NewOrderId} picked up clean.";
                }
                else
                {
                    // "Nothing to issue" can also mean what is left is reserved, so say both.
                    TempData["Success"] = "Stock corrected. Nothing on the shelf was free for this order (none left, or what's left is reserved), so no pickup was issued.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                // The refusal rolled everything back and the line is still short. Put the form back.
                try
                {
                    var again = _orderService.GetShortPullLine(orderId, orderItemId);
                    if (again != null)
                    {
                        TempData["ShortPullOrderId"] = orderId;
                        TempData["ShortPullJson"] = System.Text.Json.JsonSerializer.Serialize(new List<ShortPullLine> { again });
                    }
                }
                catch { /* the refusal toast already says what happened */ }
            }

            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("PickupQueue") : Redirect(referer);
        }

        // Logs or corrects On Hand compressor serials from the roster. Standard on purpose.
        // Rows: serial_{n} / lab_{n} with unitId_{n} (0 = a new row) and variantId_{n}.
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

        // The TC motor version: lab_{n} only. Rows may target On Hand or Picked Up units.
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

        // "Is a Non-TC Motor okay?" for a TC line held at pickup. The orderer, Engineer and up on the Line, or Admin.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult AnswerNonTc(int orderItemId, string? answer)
        {
            if (answer != "yes" && answer != "no")
            {
                TempData["Error"] = "Answer Yes or No.";
                return SmartRedirect();
            }
            try
            {
                var (ok, message, _) = _orderService.AnswerNonTc(orderItemId, answer == "yes");
                TempData[ok ? "Success" : "Error"] = message;
            }
            catch (Exception ex) { TempData["Error"] = "That answer wasn't saved: " + ex.GetBaseException().Message; }
            return SmartRedirect();
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

            // Back to the page the request came from.
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
            // Reverse lineage: what split off this order. Forward lineage is order.SplitFromOrderId.
            ViewBag.SplitTo = _db.Orders.AsNoTracking()
                .Where(o => o.SplitFromOrderId == id)
                .Select(o => o.Id)
                .ToList();
            // The units this order pulled, by order line, so a multi-line order does not mix serials.
            ViewBag.UnitsByOrderItem = _db.CompressorUnits.AsNoTracking()
                .Where(c => c.OrderId == id)
                .ToList()
                .GroupBy(c => c.OrderItemId)
                .ToDictionary(g => g.Key, g => g.ToList());
            // Waiting lines this person may answer, and whether this order is a Non-TC Motor resubmission.
            var lookup = (Dictionary<string, InventoryItem>)ViewBag.InventoryLookup;
            ViewBag.AnswerableLineIds = order.Status != "Pending" ? new HashSet<int>() : order.Items
                .Where(i => i.Status == "Waiting" && _orderService.CanAnswerNonTc(order, lookup.GetValueOrDefault(i.ItemId)))
                .Select(i => i.Id).ToHashSet();
            ViewBag.FromResubmission = order.SplitFromOrderId.HasValue
                && _db.OrderItems.AsNoTracking().Any(i => i.OrderId == order.SplitFromOrderId && i.Status == "Resubmitted");
            return View(order);
        }

        // Name capture (not authentication)
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

            // First.Last capitalisation. Only a fallback for a name not on the roster: it flattens interior capitals.
            var parts = name.Split('.');
            string normalized = string.Join(".",
                parts.Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));

            // Case-insensitive, because SQLite compares TEXT as binary by default.
            // ToLower(), not a StringComparison overload: EF cannot translate that.
            string lookup = normalized.ToLower();
            var known = _db.Users.FirstOrDefault(u => u.UserName.ToLower() == lookup && u.IsActive);

            // The roster's spelling wins: notifications, deliveries and team membership all match on this string.
            _currentUser.Set(known?.UserName ?? normalized);
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

        // The full item list behind the Total Items stat. Deep links reuse /Home/SearchCenter?omniSearch=ID.
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