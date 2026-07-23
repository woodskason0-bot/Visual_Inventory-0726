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

        // filterRheem replaced the old filterName slot: the Name filter box is
        // now "Rheem Part #" per leadership (PN is a primary identifier).
        public IActionResult Index(string? omniSearch, string? filterRheem, string? filterType, string? filterBrand, string? filterNotes, string? mode)
        {
            var allItems = _inventoryService.GetAll().ToList();

            // 1. MAP OVERLAY STATS (header cards above the map)
            ViewBag.TotalItems = allItems.Count;
            ViewBag.LowStockCount = allItems.Count(i => i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold && i.Quantity > 0);
            ViewBag.OutOfStockCount = allItems.Count(i => i.Quantity == 0);
            ViewBag.ActiveLocationsCount = allItems.Select(i => i.Parent).Where(p => !string.IsNullOrEmpty(p)).Distinct().Count();

            // 2. RECENT ACTIVITY FEED (Top 5 latest actions)
            ViewBag.RecentActivity = _db.TransactionLogs
                .AsNoTracking()
                .OrderByDescending(t => t.Timestamp)
                .Take(5)
                .ToList();

            // 2b. PENDING PICKUPS (powers the level-2 runner banner; oldest first)
            ViewBag.PendingPickups = _db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Where(o => o.Status == "Pending")
                .OrderBy(o => o.CreatedAt)
                .ToList();

            // 3. AUTOCOMPLETE & DRAFTS
            var autocompleteData = allItems.Select(i => new {
                id = i.ItemId,
                name = i.ItemName,
                // Identity fields the Modify Stock "Edit Details" pane prefills.
                rpn = i.RheemPartNumber,
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

            var currentDraft = _orderService.GetCurrentDraft();
            var draftEntries = currentDraft.Entries;
            ViewBag.DraftItemIds = draftEntries.Select(e => e.ItemId).ToHashSet();

            // LEDGER MODE
            if (mode == "Ledger")
            {
                ViewBag.SearchResult = new SearchResult { Mode = "Ledger", Items = new List<InventoryItem>() };
                ViewBag.Ledger = new LedgerViewModel { Entries = draftEntries };
                ViewBag.InventoryService = _inventoryService;
                return View();
            }

            // SEARCH MODE
            var searchResult = new SearchResult { Mode = mode ?? "None" };
            if (mode != "None")
            {
                var foundItems = _inventoryService.Search(omniSearch, filterRheem, filterType, filterBrand, filterNotes);
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

            return View();
        }

        // ============================
        // CART ACTIONS
        // ============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult AddToCart(string itemId, int quantity, int? requestedVariantId = null, int thermocoupledCount = 0)
        {
            _orderService.AddItem(itemId, quantity, requestedVariantId, thermocoupledCount);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = true, message = "Added to cart!" });
            TempData["Message"] = "Item added to cart.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult RemoveFromLedger(string itemId)
        {
            _orderService.RemoveItem(itemId);
            TempData["Message"] = "Item removed.";
            return RedirectToAction("Index", new { mode = "Ledger" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult SubmitLedger()
        {
            if (!_orderService.GetCurrentDraft().Entries.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index", new { mode = "Ledger" });
            }

            try
            {
                _orderService.Submit();
                TempData["Success"] = "Order submitted successfully.";
            }
            catch (Exception ex) { TempData["Error"] = "Submit failed: " + ex.GetBaseException().Message; }
            return RedirectToAction("Index");
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
        // the same team rule as the low-stock summary: a manager WITH a team sets
        // it for their team's items; a null-team manager (e.g. Kevin) sets it for
        // every item. Drives all alert displays + notifications via AlertThreshold.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Management)]
        public IActionResult SetDefaultThreshold(int threshold)
        {
            if (threshold < 0) return RedirectToAction("Index");

            string? myTeam = null;
            try
            {
                myTeam = _db.Users.AsNoTracking()
                    .Where(u => u.UserName == _currentUser.Name)
                    .Select(u => u.Team)
                    .FirstOrDefault();
            }
            catch { myTeam = null; }

            int count = _inventoryService.SetDefaultThreshold(myTeam, threshold);
            TempData["Success"] = string.IsNullOrWhiteSpace(myTeam)
                ? $"All item alert thresholds have been set to {threshold} ({count} items)."
                : $"All {myTeam} item alert thresholds have been set to {threshold} ({count} items).";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ModifyStock(string itemId, string actionType, int quantity, string? newGroup, string? newTeam,
            string? newParent, string? newMajor, string? newSub, string? newRack, string? newRow,
            string? targetVariant = null, int? transferQty = null, int thermocoupledQty = 0,
            string? newRheemPart = null, string? newDescription = null, string? newBrand = null)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return RedirectToAction("Index");

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
                return RedirectToAction("Index");
            }

            // Identity edits never touch quantities/variants -- separate path.
            if (string.Equals(actionType, "Edit Details", StringComparison.OrdinalIgnoreCase))
            {
                var (ok, message) = _inventoryService.UpdateItemDetails(itemId, newRheemPart, newDescription, newBrand);
                if (ok) TempData["Success"] = $"{itemId}: {message}";
                else TempData["Error"] = message;
                return RedirectToAction("Index");
            }

            try
            {
                var result = _inventoryService.ModifyStock(itemId, actionType, quantity, newGroup, newTeam,
                    newParent, newMajor, newSub, newRack, newRow, targetVariant, transferQty, thermocoupledQty);
                if (result != null)
                {
                    TempData["Success"] = $"Transaction '{actionType}' applied to {itemId}.";

                    // Edge-triggered low-stock email: fires only on the crossing.
                    _emailService.CheckAndSendStockAlert(result.Value.item, result.Value.oldQty, result.Value.newQty);
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Transaction failed: {ex.Message}";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult CreateItem(InventoryItem newItem)
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
                newItem.Group ??= "Commercial";
                newItem.Team ??= "Samurai";
                newItem.ProjectCode ??= "7166";

                // --- ITEM ID (server-authoritative) ---
                // The form's ID box is a read-only preview; the real ID is assigned
                // here from the chosen Group + Type so a stale preview or two
                // simultaneous registrations can never collide. Whatever the form
                // posted in ItemId is intentionally discarded.
                newItem.ItemId = _inventoryService.GenerateItemId(newItem.Group, newItem.Type);

                // --- LOCATION ENCODING (single source of truth) ---
                // The form posts friendly names ("RD Lab", "Trailer Area", ...).
                // We convert them to codes here, store the CODE in Parent for a
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

                _inventoryService.CreateItem(newItem);
                TempData["Success"] = $"Item '{newItem.ItemName}' registered as {newItem.ItemId}.";
            }
            catch (Exception ex) { TempData["Error"] = $"Failed to register item: {ex.Message}"; }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Admin)]
        public IActionResult SystemReset()
        {
            _orderService.StartOrder();
            TempData["Message"] = "Session reset.";
            return RedirectToAction("Index");
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
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult StartOrder()
        {
            _orderService.StartOrder();
            return RedirectToAction("Index", new { mode = "Ledger" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult CancelOrder()
        {
            _orderService.CancelOrder();
            TempData["Message"] = "Order draft canceled.";
            return RedirectToAction("Index");
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

            // Pass Transactions to the main Model
            var transactions = _db.TransactionLogs
                .AsNoTracking()
                .OrderByDescending(t => t.Timestamp)
                .ToList();

            return View(transactions);
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
                        LocationChoices = activeVars
                            .Select(v => new VariantChoiceViewModel { VariantId = v.Id, Label = VLabel(v) })
                            .ToList()
                    });
                }
            }

            return View(new MyOrdersViewModel { UserName = me, Orders = myOrders, Loans = loans });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ReturnLoan(int orderItemId, int qty, int? targetVariantId,
            string? newParent, string? newMajor, string? newSub, string? newRack, string? newRow)
        {
            try
            {
                _orderService.ReturnLoan(orderItemId, qty, targetVariantId,
                    newParent, newMajor, newSub, newRack, newRow);
                TempData["Success"] = "Loan return recorded.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("MyOrders");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult ScrapLoan(int orderItemId, int qty)
        {
            try
            {
                _orderService.ScrapLoan(orderItemId, qty);
                TempData["Success"] = "Loan scrap recorded.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("MyOrders");
        }

        public IActionResult PickupQueue()
        {
            var orders = _db.Orders.Where(o => o.Status == "Pending").Include(o => o.Items).AsNoTracking().OrderBy(o => o.CreatedAt).ToList();
            var result = orders.Select(order => {
                var vm = new PendingOrderViewModel { OrderId = order.Id, CreatedAt = order.CreatedAt, Status = order.Status, RequestedBy = order.RequestedBy };
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
                        string path = crumbs.Count > 0 ? string.Join(" › ", crumbs) : v.FdaString;
                        return $"V{v.VariantNumber} — {path} · Qty {v.Quantity}";
                    }

                    var activeVars = (itemEntity?.ActiveVariants ?? Enumerable.Empty<Visual_Inventory_System.Models.ItemVariant>())
                        .OrderBy(v => v.VariantNumber).ToList();

                    var itemVm = new PendingOrderItemViewModel
                    {
                        OrderItemId = it.Id,
                        ItemId = it.ItemId,
                        ItemName = itemEntity?.ItemName ?? "Unknown",
                        RheemPartNumber = itemEntity?.RheemPartNumber ?? "",
                        Quantity = it.Quantity,
                        AvailableForThisOrder = availForThis,
                        RequestedVariantId = it.RequestedVariantId,
                        RequestedLabel = it.RequestedVariantId.HasValue
                            ? activeVars.Where(v => v.Id == it.RequestedVariantId.Value).Select(VLabel).FirstOrDefault()
                            : null,
                        LocationChoices = activeVars
                            .Select(v => new VariantChoiceViewModel { VariantId = v.Id, Label = VLabel(v) })
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

                _orderService.PickUpOrder(orderId, choices.Count > 0 ? choices : null);
                TempData["Success"] = "Order completed.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }

            // SMART REDIRECT: Go back to the exact page you came from (Logs OR Orders)
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Orders") : Redirect(referer);
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
        // route (/Home/Index?omniSearch=ID) rather than duplicating that JS here.
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