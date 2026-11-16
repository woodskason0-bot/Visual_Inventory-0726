using Visual_Inventory_System.Models;
using Visual_Inventory_System.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json; // Used for JSON shrinking/expanding
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>One location's recorded quantity for a line that came up short at pickup.</summary>
    public class LocationStock
    {
        public int VariantId { get; set; }
        public string Fda { get; set; } = "";
        public int RecordedQty { get; set; }
        /// <summary>TC count on this stack (motors); a correction that lowers it can leave TC unit rows over.</summary>
        public int RecordedTc { get; set; }
        /// <summary>Units recorded On Hand here, so a correction below them can ask which are gone.</summary>
        public System.Collections.Generic.List<RecordedUnit> CompressorUnits { get; set; } = new();
        public System.Collections.Generic.List<RecordedUnit> MotorUnits { get; set; } = new();
    }

    /// <summary>One recorded On Hand unit on a stack: its row id and whatever identifies it.</summary>
    public class RecordedUnit
    {
        public int Id { get; set; }
        public string? Serial { get; set; }
        public string? Lab { get; set; }
    }

    /// <summary>An order line the shelf cannot cover, with the per-location breakdown for the picker to correct.</summary>
    public class ShortPullLine
    {
        public int OrderItemId { get; set; }
        public string ItemId { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int Requested { get; set; }
        public System.Collections.Generic.List<LocationStock> Locations { get; set; } = new();
    }

    /// <summary>
    /// Outcome of a pickup attempt. Short lines are not fulfilled: they are reported here,
    /// untouched, for ReportShortPull. Other lines complete normally.
    /// </summary>
    public class PickupResult
    {
        public int OrderId { get; set; }
        public int? NewOrderId { get; set; }   // set by ReportShortPull when a correction reissues clean
        public System.Collections.Generic.List<ShortPullLine> ShortLines { get; set; } = new();
        public bool HasShortLines => ShortLines.Count > 0;
    }

    public class OrderService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly CurrentUserService _currentUser;
        private readonly NotificationService _notifications;
        private const string SessionKey = "_OrderDraft";

        public OrderService(AppDbContext db, IHttpContextAccessor httpContextAccessor, CurrentUserService currentUser, NotificationService notifications)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
            _notifications = notifications;
        }

        // Session helpers (the "locker")

        private LedgerDraft GetDraftFromSession()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null) return new LedgerDraft();

            string? json = session.GetString(SessionKey);

            // If the locker is empty, return a fresh empty draft
            if (string.IsNullOrEmpty(json)) return new LedgerDraft();

            // Otherwise, "expand" the JSON string back into the LedgerDraft object
            return JsonSerializer.Deserialize<LedgerDraft>(json) ?? new LedgerDraft();
        }

        private void SaveDraftToSession(LedgerDraft draft)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null) return;

            // "Shrink" the draft object into a JSON string and put it in the locker
            string json = JsonSerializer.Serialize(draft);
            System.Diagnostics.Debug.WriteLine($"SESSION STORAGE: {json}");
            session.SetString(SessionKey, json);
        }

        // Public actions
        public LedgerDraft GetCurrentDraft()
        {
            return GetDraftFromSession();
        }
        public void StartOrder()
        {
            // Just wipe the locker for this specific user
            SaveDraftToSession(new LedgerDraft());
        }

        public void AddItem(string itemId, int qty, int? requestedVariantId = null, int thermocoupledCount = 0, string? requestedTeam = null)
        {
            if (string.IsNullOrWhiteSpace(itemId) || qty <= 0) return;
            if (thermocoupledCount < 0) thermocoupledCount = 0;
            int tc = System.Math.Min(thermocoupledCount, qty);   // can't request more TC than units
            string? team = string.IsNullOrWhiteSpace(requestedTeam) ? null : requestedTeam.Trim();

            var draft = GetDraftFromSession();
            // Same item, location and team stack on one line. Any difference gets its own line.
            var existing = draft.Entries.FirstOrDefault(e => e.ItemId == itemId
                && e.RequestedVariantId == requestedVariantId
                && e.RequestedTeam == team);

            if (existing != null)
            {
                existing.Quantity += qty;
                // TC requests stack too, never past the line's new total.
                existing.ThermocoupledCount = System.Math.Min(existing.ThermocoupledCount + tc, existing.Quantity);
            }
            else
            {
                draft.Entries.Add(new LedgerDraftEntry
                {
                    ItemId = itemId,
                    Quantity = qty,
                    Action = "ADD_TO_CART",
                    RequestedVariantId = requestedVariantId,
                    ThermocoupledCount = tc,
                    RequestedTeam = team
                });
            }

            SaveDraftToSession(draft);
        }

        /// <summary>The team this line orders against. "" for unsplit items and the fails-open cases. Throws when ambiguous.</summary>
        private string ResolveOrderingTeam(InventoryItem item, string? requestedTeam)
        {
            // The rule lives in InventoryService so Search Center's "Available" resolves the same way.
            var inv = new InventoryService(_db, _currentUser);
            var (team, ambiguous, applicable) = inv.ResolveOrderingTeam(item, requestedTeam);

            if (ambiguous)
                throw new System.InvalidOperationException(
                    $"'{item.ItemId}' is split across your teams ({string.Join(", ", applicable)}) -- pick which team you're ordering for.");

            return team;
        }

        /// <summary>Removes one cart line, matched on item, location and team, the same key AddItem uses.</summary>
        public void RemoveItem(string itemId, int? requestedVariantId = null, string? requestedTeam = null)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;
            string? team = string.IsNullOrWhiteSpace(requestedTeam) ? null : requestedTeam.Trim();

            var draft = GetDraftFromSession();
            draft.Entries.RemoveAll(e => e.ItemId == itemId
                && e.RequestedVariantId == requestedVariantId
                && e.RequestedTeam == team);

            SaveDraftToSession(draft);
        }

        public void Submit()
        {
            var draft = GetDraftFromSession();

            if (!draft.Entries.Any())
                throw new InvalidOperationException("Cannot submit an empty order.");

            var invService = new InventoryService(_db, _currentUser);

            // Captured for the post-commit runner ping (fired only after a clean submit).
            string requester = _currentUser.Name;
            int itemCount = draft.Entries.Count;
            int newOrderId = 0;

            using var tx = _db.Database.BeginTransaction();
            try
            {
                var order = new Order { CreatedAt = System.DateTime.UtcNow, Status = "Pending", RequestedBy = _currentUser.Name };
                _db.Orders.Add(order);
                _db.SaveChanges(); // This generates the new Order.Id
                newOrderId = order.Id;

                // What this cart has claimed so far. Saved lines come from the database, but the cart's
                // own lines are not saved yet, so each is checked together with the ones before it.
                var claimsByItem = new Dictionary<string, StockClaims>();
                var cartLinesByItem = new Dictionary<string, List<StockClaims.Line>>();

                foreach (var entry in draft.Entries)
                {
                    var invItem = invService.GetById(entry.ItemId);
                    if (invItem == null)
                        throw new InvalidOperationException($"Failed: ID {entry.ItemId} no longer exists.");

                    // "" for an unsplit item. Throws only when the requester's own teams collide.
                    string team = ResolveOrderingTeam(invItem, entry.RequestedTeam);

                    // TC request only sticks for motors, and never exceeds the line qty.
                    int tcCount = InventoryService.IsMotorType(invItem.Type)
                        ? System.Math.Min(entry.ThermocoupledCount, entry.Quantity)
                        : 0;

                    // Stock check. No quotes in the message: it lands in toast JS.
                    if (!claimsByItem.TryGetValue(entry.ItemId, out var claims))
                    {
                        claims = StockClaims.Load(_db, entry.ItemId);
                        claimsByItem[entry.ItemId] = claims;
                        cartLinesByItem[entry.ItemId] = new List<StockClaims.Line>();
                    }
                    var earlierInCart = cartLinesByItem[entry.ItemId];
                    var thisLine = StockClaims.NewLine(requester, team, entry.Quantity, tcCount, earlierInCart.Count);
                    if (!claims.Fits(earlierInCart.Append(thisLine)))
                    {
                        // Say how many really are free for this line, after the cart's own earlier lines.
                        var withCart = new StockClaims(entry.ItemId, claims.Stacks, claims.Holds, claims.Lines.Concat(earlierInCart).ToList());
                        int avail = withCart.AvailableQty(requester, team);
                        int availTc = withCart.AvailableTc(requester, team);
                        string after = earlierInCart.Count > 0 ? " after the other cart lines for it" : "";
                        if (avail >= entry.Quantity && tcCount > availTc)
                            throw new InvalidOperationException(
                                $"Failed: ID {entry.ItemId} only has {availTc} thermocoupled available{after} -- {tcCount} asked for.");
                        throw new InvalidOperationException(
                            $"Failed: ID {entry.ItemId} only has {avail} available in stock{after}"
                            + (claims.Holds.Count > 0 ? " (reserved units don't count)." : "."));
                    }
                    earlierInCart.Add(thisLine);

                    _db.OrderItems.Add(new OrderItem
                    {
                        OrderId = order.Id,
                        Order = order, // set FK + navigation so EF ties the line to the just-created order
                        ItemId = entry.ItemId,
                        Quantity = entry.Quantity,
                        RequestedVariantId = entry.RequestedVariantId,
                        ThermocoupledCount = tcCount,
                        Team = team
                    });
                }

                _db.SaveChanges();
                tx.Commit();
                tx.Dispose(); // release the ambient tx so the post-commit ping can SaveChanges cleanly

                // Clear the cart only on success.
                StartOrder();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // Committed. Tell the runners a pickup is waiting. Best-effort: never fail a real order.
            try
            {
                // Individual opt-in, not tied to access tier.
                _notifications.CreateForSubscribers(
                "PickupRequested",
                $"New pickup requested: Order #{newOrderId} from {requester} ({itemCount} item{(itemCount == 1 ? "" : "s")}).",
                "/Home/PickupQueue",
                excludeUserName: requester);
            }
            catch { /* swallow: the order is already safely submitted */ }
        }

        // compressorUnits: orderItemId -> (LabNumber, SerialNumber) per physical unit. Either may be blank.
        public PickupResult PickUpOrder(int orderId, Dictionary<int, int>? variantChoices = null,
            Dictionary<int, List<(string? Lab, string? Serial)>>? compressorUnits = null)
        {
            // Captured inside the tx, used for the post-commit ping to the requester.
            string? requesterToNotify = null;
            string fulfiller = _currentUser.Name;
            var result = new PickupResult { OrderId = orderId };

            using var tx = _db.Database.BeginTransaction();
            try
            {
                var order = _db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
                if (order == null) throw new InvalidOperationException("Order not found.");
                if (order.Status == "Completed") throw new InvalidOperationException("Already completed.");
                // Any non-Pending order is final. An engineer-cancelled order keeps its lines "Pending"
                // (line-level "Cancelled" means short at pickup), so this guard is all that stops a pull.
                if (order.Status != "Pending") throw new InvalidOperationException($"Order #{orderId} was cancelled and can no longer be picked up.");

                // Only lines still Pending. A second attempt must not re-touch resolved lines.
                foreach (var it in order.Items.Where(i => i.Status == "Pending"))
                {
                    var shortLine = FulfillOrderItem(it, orderId, variantChoices, compressorUnits);
                    if (shortLine != null) result.ShortLines.Add(shortLine);
                }

                // The order follows its lines. Every line short = Cancelled: nothing left the shelf.
                // Any Completed or Split line = Completed.
                bool anyPending = order.Items.Any(i => i.Status == "Pending");
                if (anyPending)
                {
                    // Should not happen. Leave the order Pending rather than lie about it.
                }
                else
                {
                    // Split counts: stock did leave under this order id.
                    bool anyCompleted = order.Items.Any(i => i.Status == "Completed" || i.Status == "Split");
                    order.Status = anyCompleted ? "Completed" : "Cancelled";
                    order.FulfilledBy = _currentUser.Name;
                    order.FulfilledAt = System.DateTime.UtcNow;
                }

                _db.SaveChanges();
                tx.Commit();
                tx.Dispose(); // release the ambient tx so the post-commit ping can SaveChanges cleanly

                requesterToNotify = order.RequestedBy;
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // Tell the requester who picked it up. Best-effort, and never notify yourself.
            try
            {
                if (!string.IsNullOrWhiteSpace(requesterToNotify)
                && !string.Equals(requesterToNotify, fulfiller, System.StringComparison.OrdinalIgnoreCase))
                {
                    _notifications.Create(
                    requesterToNotify,
                    "Fulfilled",
                    $"Your order #{orderId} was picked up by {fulfiller}.",
                    $"/Home/OrderDetails/{orderId}");
                }
            }
            catch { /* swallow: pickup already committed */ }

            return result;
        }

        /// <summary>
        /// A deliberate partial pickup of a compressor line: pulls exactly `pickupQty` from one
        /// stack, no spill, and spins the remainder off as a new Pending order.
        /// </summary>
        public PickupResult PickUpPartialAndSplit(int orderId, int orderItemId, int variantId, int pickupQty,
            Dictionary<int, (string? Lab, string? Serial)>? unitChoices)
        {
            using var tx = _db.Database.BeginTransaction();
            try
            {
                var order = _db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
                if (order == null) throw new InvalidOperationException("Order not found.");
                if (order.Status != "Pending") throw new InvalidOperationException($"Order #{orderId} was cancelled and can no longer be picked up.");

                var it = order.Items.FirstOrDefault(x => x.Id == orderItemId && x.Status == "Pending");
                if (it == null) throw new InvalidOperationException("Order line not found, or already resolved.");

                var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
                if (inv == null) throw new InvalidOperationException("That item no longer exists in inventory.");
                if (!InventoryService.IsCompressorType(inv.Type))
                    throw new InvalidOperationException("Partial pickup with split is only wired up for compressors right now.");
                if (pickupQty <= 0 || pickupQty >= it.Quantity)
                    throw new InvalidOperationException("Partial pickup must be for fewer units than the line originally asked for.");

                var variant = inv.Variants.FirstOrDefault(v => v.Id == variantId && !v.IsRetired);
                if (variant == null) throw new InvalidOperationException("That location is no longer active.");
                // The queue only offers this team's locations. This holds the boundary against a direct POST.
                if (!string.IsNullOrEmpty(it.Team) && variant.Team != it.Team)
                    throw new InvalidOperationException(
                        $"That location belongs to {(string.IsNullOrWhiteSpace(variant.Team) ? "no team" : variant.Team)}, not {it.Team} -- this line can only be pulled from its own team's stock.");
                // Live recheck: the queue page may be stale.
                if (variant.Quantity < pickupQty)
                    throw new InvalidOperationException($"Only {variant.Quantity} actually on hand there now -- someone beat you to it. Refresh and try again.");

                var now = System.DateTime.UtcNow;

                // What other people hold on this stack stays, whoever is picking up.
                string requester = order.RequestedBy ?? "";
                var holdView = StockClaims.FromVariants(_db, it.ItemId, inv.Variants);
                var heldByOthers = holdView.HeldByOthers(variant.Id, requester);
                int freeHere = System.Math.Max(0, variant.Quantity - heldByOthers.Qty);
                if (pickupQty > freeHere)
                    throw new InvalidOperationException(HeldRefusal(it.ItemId, new List<ItemVariant> { variant }, requester, holdView, freeHere, pickupQty));

                // One stack, so one pass of FulfillOrderItem's TC math, computed before the take.
                var (_, tcTake) = PlanTake(variant, pickupQty, it.ThermocoupledCount, heldByOthers);
                var pins = ReservationService.PinOwners(_db, it.ItemId);

                variant.Quantity -= pickupQty;
                variant.ThermocoupledQty -= tcTake;

                int remainderQty = it.Quantity - pickupQty;
                int remainderTc = System.Math.Max(0, it.ThermocoupledCount - tcTake);

                var (newOrder, _) = CreateSplitOrder(order, it.ItemId, remainderQty, it.RequestedVariantId, remainderTc, it.Team);

                it.Quantity = pickupQty;
                it.ThermocoupledCount = tcTake;
                it.LoanOutstanding = InventoryService.LoanableQuantity(inv.Type, pickupQty, tcTake);
                it.Status = "Split";

                int missingLab = 0, missingSerial = 0, matchedUnits = 0;
                var onHandHere = _db.CompressorUnits
                    .Where(c => c.ItemId == it.ItemId && c.ItemVariantId == variantId && c.Status == UnitStatus.OnHand)
                    .OrderBy(c => c.RecordedAt)
                    .ToList();
                var claimedThisCall = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int u = 0; u < pickupQty; u++)
                {
                    (string? Lab, string? Serial) pair = (null, null);
                    unitChoices?.TryGetValue(u, out pair);
                    // variant.Quantity is already net of this pull: add back the slots still to assign.
                    AssignOneCompressorUnit(it.ItemId, onHandHere, pair.Lab, pair.Serial, orderId, it.Id, null, now,
                        claimedThisCall, variant.Quantity + (pickupQty - u), unitChoices != null && unitChoices.ContainsKey(u),
                        ref missingLab, ref missingSerial, ref matchedUnits, pins, requester);
                }
                ReservationService.ConsumeForPickup(_db, it.ItemId, requester,
                    new List<(int, int, int)> { (variant.Id, pickupQty, tcTake) }, _currentUser.Name, $"Order #{orderId}");

                var flagParts = new List<string>();
                if (missingLab > 0) flagParts.Add($"{missingLab} missing lab #");
                if (missingSerial > 0) flagParts.Add($"{missingSerial} missing serial #");
                if (matchedUnits > 0) flagParts.Add($"{matchedUnits} matched to known unit(s)");
                string compressorFlag = flagParts.Count > 0 ? $" ({string.Join(", ", flagParts)})" : "";

                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = now,
                    ActionType = "Order Picked Up",
                    ItemId = it.ItemId,
                    ItemName = inv.ItemName,
                    QuantityChange = -pickupQty,
                    Details = $"Order #{orderId} — pulled {pickupQty}{(tcTake > 0 ? $" [{tcTake} TC]" : "")} from V{variant.VariantNumber} ({variant.FdaString}), remaining {remainderQty} split to Order #{newOrder.Id}." + compressorFlag,
                    User = _currentUser.Name
                });

                bool anyPending = order.Items.Any(i => i.Status == "Pending");
                if (!anyPending)
                {
                    bool anyCompleted = order.Items.Any(i => i.Status == "Completed" || i.Status == "Split");
                    order.Status = anyCompleted ? "Completed" : "Cancelled";
                    order.FulfilledBy = _currentUser.Name;
                    order.FulfilledAt = now;
                }

                _db.SaveChanges();
                tx.Commit();

                return new PickupResult { OrderId = orderId };
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        /// <summary>The short-pull correction form's data for one line: each location's count and its recorded units.</summary>
        private ShortPullLine BuildShortPullLine(OrderItem it, InventoryItem inv, List<ItemVariant> activeVariants)
        {
            var variantIds = activeVariants.Select(v => v.Id).ToList();
            // Tracked, then rechecked in memory: an earlier line of this pickup may have changed an unsaved row.
            var compressorsHere = InventoryService.IsCompressorType(inv.Type)
                ? _db.CompressorUnits
                    .Where(c => c.ItemId == it.ItemId && c.Status == UnitStatus.OnHand && c.ItemVariantId != null && variantIds.Contains(c.ItemVariantId.Value))
                    .OrderBy(c => c.RecordedAt).ToList()
                    .Where(c => c.Status == UnitStatus.OnHand && c.ItemVariantId != null && variantIds.Contains(c.ItemVariantId.Value)).ToList()
                : new List<CompressorUnit>();
            var motorsHere = InventoryService.IsMotorType(inv.Type)
                ? _db.MotorUnits
                    .Where(m => m.ItemId == it.ItemId && m.Status == UnitStatus.OnHand && m.ItemVariantId != null && variantIds.Contains(m.ItemVariantId.Value))
                    .OrderBy(m => m.RecordedAt).ToList()
                    .Where(m => m.Status == UnitStatus.OnHand && m.ItemVariantId != null && variantIds.Contains(m.ItemVariantId.Value)).ToList()
                : new List<MotorUnit>();

            return new ShortPullLine
            {
                OrderItemId = it.Id,
                ItemId = it.ItemId,
                ItemName = inv.ItemName,
                Requested = it.Quantity,
                Locations = activeVariants.Select(v => new LocationStock
                {
                    VariantId = v.Id,
                    Fda = v.FdaString,
                    RecordedQty = v.Quantity,
                    RecordedTc = v.ThermocoupledQty,
                    CompressorUnits = compressorsHere.Where(c => c.ItemVariantId == v.Id)
                        .Select(c => new RecordedUnit { Id = c.Id, Serial = c.SerialNumber, Lab = c.LabNumber }).ToList(),
                    MotorUnits = motorsHere.Where(m => m.ItemVariantId == v.Id)
                        .Select(m => new RecordedUnit { Id = m.Id, Lab = m.LabNumber }).ToList()
                }).ToList()
            };
        }

        /// <summary>
        /// The correction form's data for a line still flagged short, or null.
        /// Lets the controller bring the form back after a refused correction.
        /// </summary>
        public ShortPullLine? GetShortPullLine(int orderId, int orderItemId)
        {
            var it = _db.OrderItems.FirstOrDefault(o => o.Id == orderItemId && o.OrderId == orderId);
            if (it == null || it.Status != "Cancelled") return null;
            var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
            if (inv == null) return null;
            var active = inv.Variants
                .Where(v => !v.IsRetired && (string.IsNullOrEmpty(it.Team) || v.Team == it.Team))
                .ToList();
            return BuildShortPullLine(it, inv, active);
        }

        private static bool SameName(string? a, string? b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Who placed the order this line is on: the person whose holds a pickup may use.</summary>
        private string RequesterOf(OrderItem it) =>
            it.Order?.RequestedBy
            ?? _db.Orders.Where(o => o.Id == it.OrderId).Select(o => o.RequestedBy).FirstOrDefault()
            ?? "";

        /// <summary>How much of one stack a pull may take, and how much of that is TC, leaving what others hold.</summary>
        private static (int Take, int TcTake) PlanTake(ItemVariant v, int remaining, int remainingTc, (int Qty, int Tc) heldByOthers)
        {
            int capQ = System.Math.Max(0, v.Quantity - heldByOthers.Qty);
            int capT = System.Math.Max(0, v.ThermocoupledQty - heldByOthers.Tc);
            int nonTcFree = System.Math.Max(0, (v.Quantity - v.ThermocoupledQty) - System.Math.Max(0, heldByOthers.Qty - heldByOthers.Tc));
            int take = System.Math.Min(remaining, System.Math.Min(capQ, nonTcFree + capT));
            if (take <= 0) return (0, 0);
            int forcedTc = System.Math.Max(0, take - nonTcFree);
            int maxTc = System.Math.Min(take, capT);
            int tcTake = System.Math.Min(maxTc, System.Math.Max(forcedTc, remainingTc));
            return (take, tcTake);
        }

        /// <summary>The refusal when other people's holds are what make a pull come up short.</summary>
        private string HeldRefusal(string itemId, List<ItemVariant> stacks, string requester, StockClaims view, int free, int wanted)
        {
            var names = stacks
                .Select(v => ReservationService.DescribeHoldsOn(_db, itemId, v.Id))
                .Where(s => s.Length > 0)
                .ToList();
            return $"{itemId}: only {free} of what's on the shelf can go to {ReservationService.Person(requester)} -- the rest is reserved"
                + (names.Count > 0 ? $" ({string.Join("; ", names)})" : "")
                + $". {wanted} needed. Ask them to release it, or change the order. Nothing was picked up.";
        }

        /// <summary>
        /// Pulls one order line, or refuses it. A line the shelf cannot cover is set Cancelled and
        /// returned as a short pull with stock untouched. Nothing partial is ever pulled here.
        /// </summary>
        private ShortPullLine? FulfillOrderItem(OrderItem it, int orderId,
            Dictionary<int, int>? variantChoices,
            Dictionary<int, List<(string? Lab, string? Serial)>>? compressorUnits)
        {
            var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
            if (inv == null)
            {
                // The item was deleted after the order was placed. Close the line rather than block the order.
                it.Status = "Completed";
                return null;
            }

            // A resolved team limits the pull to that team's variants. "" sees every active variant.
            var activeVariants = inv.Variants
                .Where(v => !v.IsRetired && (string.IsNullOrEmpty(it.Team) || v.Team == it.Team))
                .ToList();
            int totalAvailable = activeVariants.Sum(v => v.Quantity);

            if (it.Quantity > totalAvailable)
            {
                // Not enough on the shelf. Refuse the line and leave stock untouched: a short pull is a data problem.
                it.Status = "Cancelled";
                return BuildShortPullLine(it, inv, activeVariants);
            }

            // Stock held for anyone but the requester stays on the shelf, whoever picks up.
            // Short because of holds is a refusal with names, never the short-pull path.
            string requester = RequesterOf(it);
            var holdView = StockClaims.FromVariants(_db, it.ItemId, inv.Variants);
            int freeForRequester = activeVariants.Sum(v => System.Math.Max(0, v.Quantity - holdView.HeldByOthers(v.Id, requester).Qty));
            if (it.Quantity > freeForRequester)
                throw new InvalidOperationException(HeldRefusal(it.ItemId, activeVariants, requester, holdView, freeForRequester, it.Quantity));

            // Pull from: the line's requested location, else the picker's choice, else no preference.
            int? preferId = it.RequestedVariantId;
            if (preferId == null && variantChoices != null
                && variantChoices.TryGetValue(it.Id, out int chosen))
            {
                preferId = chosen;
            }

            // Preferred stack first, then stacks the requester holds, then the rest by number. May span locations.
            var myHeld = holdView.Holds.Where(h => SameName(h.By, requester)).Select(h => h.VariantId).ToHashSet();
            var pullOrder = activeVariants
                .OrderBy(v => preferId.HasValue && v.Id == preferId.Value ? 0 : myHeld.Contains(v.Id) ? 1 : 2)
                .ThenBy(v => v.VariantNumber)
                .ToList();

            int remaining = it.Quantity;
            int remainingTc = it.ThermocoupledCount;   // TC motors still owed to this line
            int pulledTc = 0;   // TC motors that actually left, forced ones included
            var pulls = new List<string>();   // snapshot for the log
            // Which variant each pulled unit came from, so unit rows are matched on that shelf, not pooled.
            var variantPulls = new List<(int VariantId, int Take, int TcTake)>();
            foreach (var v in pullOrder)
            {
                if (remaining <= 0) break;
                var (take, tcTake) = PlanTake(v, remaining, remainingTc, holdView.HeldByOthers(v.Id, requester));
                if (take > 0)
                {
                    v.Quantity -= take;
                    v.ThermocoupledQty -= tcTake;
                    remaining -= take;
                    remainingTc = System.Math.Max(0, remainingTc - tcTake);
                    pulledTc += tcTake;
                    pulls.Add($"{take}{(tcTake > 0 ? $" [{tcTake} TC]" : "")} from V{v.VariantNumber} ({v.FdaString})");
                    variantPulls.Add((v.Id, take, tcTake));
                }
            }
            var pins = ReservationService.PinOwners(_db, it.ItemId);

            // The loan starts here, based on what left the shelf, not what was ordered.
            // pulledTc is summed in the loop: the floor can force more TC out than the line asked for.
            int pulledQty = it.Quantity - remaining;
            it.LoanOutstanding = InventoryService.LoanableQuantity(
                inv.Type, pulledQty, pulledTc);

            // One CompressorUnit row per unit pulled. Lab# and Serial# are both optional:
            // missing values are counted onto the log line, never blocked.
            int missingLab = 0, missingSerial = 0, matchedUnits = 0;
            if (InventoryService.IsCompressorType(inv.Type) && pulledQty > 0)
            {
                List<(string? Lab, string? Serial)>? pairs = null;
                compressorUnits?.TryGetValue(it.Id, out pairs);
                var now = System.DateTime.UtcNow;

                // One entry per physical unit in pull order, so slot u matches its posted pair and its shelf.
                var unitVariant = new List<int>();
                foreach (var (variantId, take, _) in variantPulls)
                    for (int i = 0; i < take; i++) unitVariant.Add(variantId);

                // Fetched once per variant and edited in memory: a fresh query would miss this call's unsaved changes.
                var onHandByVariant = new Dictionary<int, List<CompressorUnit>>();
                List<CompressorUnit> OnHandAt(int variantId)
                {
                    if (!onHandByVariant.TryGetValue(variantId, out var list))
                    {
                        list = _db.CompressorUnits
                            .Where(c => c.ItemId == it.ItemId && c.ItemVariantId == variantId && c.Status == UnitStatus.OnHand)
                            .OrderBy(c => c.RecordedAt)
                            .ToList()
                            // Rechecked in memory: an earlier line of this pickup may have changed a tracked row.
                            .Where(c => c.Status == UnitStatus.OnHand && c.ItemVariantId == variantId)
                            .ToList();
                        onHandByVariant[variantId] = list;
                    }
                    return list;
                }

                var claimedThisCall = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                // Slots left per shelf. Quantity is already net of this pull, so quantity + slots = units still there.
                var slotsLeft = variantPulls.ToDictionary(p => p.VariantId, p => p.Take);
                for (int u = 0; u < pulledQty; u++)
                {
                    string? lab = (pairs != null && u < pairs.Count) ? pairs[u].Lab : null;
                    string? serial = (pairs != null && u < pairs.Count) ? pairs[u].Serial : null;
                    int vid = unitVariant[u];
                    AssignOneCompressorUnit(it.ItemId, OnHandAt(vid), lab, serial, orderId, it.Id, null, now,
                        claimedThisCall, inv.Variants.First(x => x.Id == vid).Quantity + slotsLeft[vid],
                        pairs != null && u < pairs.Count,
                        ref missingLab, ref missingSerial, ref matchedUnits, pins, requester);
                    slotsLeft[vid]--;
                }
            }

            // TC motor units: the same, without the serial match. Only the TC part of the pull gets a row.
            if (InventoryService.IsMotorType(inv.Type) && pulledTc > 0)
                TakeMotorUnits(it.ItemId, variantPulls, orderId, it.Id, null, System.DateTime.UtcNow, pins, requester);

            // The orderer's own holds go down by what just left (and end at 0).
            ReservationService.ConsumeForPickup(_db, it.ItemId, requester, variantPulls, _currentUser.Name, $"Order #{orderId}");

            var flagParts = new List<string>();
            if (missingLab > 0) flagParts.Add($"{missingLab} missing lab #");
            if (missingSerial > 0) flagParts.Add($"{missingSerial} missing serial #");
            if (matchedUnits > 0) flagParts.Add($"{matchedUnits} matched to known unit(s)");
            string compressorFlag = flagParts.Count > 0 ? $" ({string.Join(", ", flagParts)})" : "";

            // The log snapshots the pull locations: variant numbers can be reused later.
            // QuantityChange is what left, not what was ordered.
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = System.DateTime.UtcNow,
                ActionType = "Order Picked Up",
                ItemId = it.ItemId,
                ItemName = inv.ItemName,
                QuantityChange = -pulledQty,
                Details = (pulls.Count > 0
                    ? $"Order #{orderId} — pulled {string.Join(", ", pulls)}"
                    : $"Order #{orderId}") + compressorFlag,
                User = _currentUser.Name
            });

            it.Status = "Completed";
            return null;
        }

        /// <summary>
        /// Match-or-create for one compressor unit leaving one specific shelf. Shared by pickup, partial
        /// pickup and transfer approval. Pass orderId/orderItemId or transferRequestId, never both.
        /// </summary>
        private void AssignOneCompressorUnit(
            string itemId, List<CompressorUnit> onHandHere, string? lab, string? serial,
            int? orderId, int? orderItemId, int? transferRequestId, System.DateTime now,
            HashSet<string> claimedThisCall, int shelfUnitsLeft, bool slotChosenByPicker,
            ref int missingLab, ref int missingSerial, ref int matchedUnits,
            Dictionary<int, string> pins, string requester)
        {
            // A unit named in someone else's hold never leaves for this order. The requester's own go first.
            bool OthersPin(CompressorUnit c) => c.ReservationId.HasValue
                && pins.TryGetValue(c.ReservationId.Value, out var who) && !SameName(who, requester);
            bool MinePin(CompressorUnit c) => c.ReservationId.HasValue
                && pins.TryGetValue(c.ReservationId.Value, out var who) && SameName(who, requester);

            lab = string.IsNullOrWhiteSpace(lab) ? null : lab.Trim();
            serial = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
            if (lab == null) missingLab++;
            if (serial == null) missingSerial++;

            // The same serial picked for two slots is a same-submission mistake.
            // Checked before the on-hand match so it reports the right cause.
            if (serial != null && !claimedThisCall.Add(serial))
            {
                throw new System.InvalidOperationException(
                    $"Serial \"{serial}\" was picked for more than one unit on this line -- each physical unit needs its own.");
            }

            CompressorUnit? known = null;
            if (serial != null)
            {
                known = onHandHere.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.SerialNumber) &&
                    string.Equals(c.SerialNumber, serial, System.StringComparison.OrdinalIgnoreCase));
                if (known != null && OthersPin(known))
                    throw new System.InvalidOperationException(
                        $"Serial \"{serial}\" is reserved for {ReservationService.Person(pins[known.ReservationId!.Value])} -- pick a different unit for this order. Nothing was picked up.");
            }
            else
            {
                // No serial given: take the oldest unnamed on-hand unit on this shelf.
                // With no per-unit choice at all, the requester's own held unit goes first.
                known = (!slotChosenByPicker ? onHandHere.FirstOrDefault(MinePin) : null)
                    ?? onHandHere.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.SerialNumber) && !OthersPin(c));

                // No unnamed row. If every unit still on the shelf has a recorded serial, the one leaving
                // must be one of them: the picker says which, or with no choice made, the oldest goes.
                if (known == null && onHandHere.Count >= shelfUnitsLeft)
                {
                    if (slotChosenByPicker)
                        throw new System.InvalidOperationException(
                            "Every unit left on that shelf already has a recorded serial, so \"No serial\" can't be the one leaving -- "
                            + "pick its serial from the list (or \"Serial not in list\" if it truly isn't recorded). Nothing was picked up.");
                    known = onHandHere.Where(c => !OthersPin(c)).OrderBy(c => MinePin(c) ? 0 : 1).FirstOrDefault();
                }
            }

            if (known != null)
            {
                // A known unit is leaving: flip it, keep its history. Nobody holds it any more.
                onHandHere.Remove(known);
                known.ReservationId = null;
                known.Status = UnitStatus.PickedUp;
                known.OrderId = orderId;
                known.OrderItemId = orderItemId;
                known.TransferRequestId = transferRequestId;
                known.PickedUpAt = now;
                known.PickedUpBy = _currentUser.Name;
                known.ItemVariantId = null;   // off the shelf; set again on return
                if (lab != null) known.LabNumber = lab;
                matchedUnits++;
            }
            else if (serial != null && _db.CompressorUnits.Any(c => c.ItemId == itemId && c.SerialNumber == serial))
            {
                // The serial is on the roster for this item, but not on this shelf (or it already left).
                // Case-sensitive, like the unique index. Refuse rather than take it from wherever it is.
                throw new System.InvalidOperationException(
                    $"Serial \"{serial}\" is already on record for {itemId} at a different location (or already picked up) -- double check you're pulling from the right shelf.");
            }
            else
            {
                // First time this unit is seen: recorded and picked up at once.
                _db.CompressorUnits.Add(new CompressorUnit
                {
                    ItemId = itemId,
                    ItemVariantId = null,
                    SerialNumber = serial,
                    LabNumber = lab,
                    Status = UnitStatus.PickedUp,
                    RecordedAt = now,
                    RecordedBy = _currentUser.Name,
                    OrderId = orderId,
                    OrderItemId = orderItemId,
                    TransferRequestId = transferRequestId,
                    PickedUpAt = now,
                    PickedUpBy = _currentUser.Name
                });
            }
        }

        /// <summary>
        /// The TC motor counterpart: flips the oldest On Hand MotorUnit rows on each shelf the pull
        /// drew TC from. TC with no row is created straight into Picked Up.
        /// </summary>
        private void TakeMotorUnits(string itemId, List<(int VariantId, int Take, int TcTake)> variantPulls,
            int? orderId, int? orderItemId, int? transferRequestId, System.DateTime now,
            Dictionary<int, string> pins, string requester)
        {
            // Same hold rule: someone else's named TC motor stays, the requester's own go first.
            bool OthersPin(MotorUnit m) => m.ReservationId.HasValue
                && pins.TryGetValue(m.ReservationId.Value, out var who) && !SameName(who, requester);
            bool MinePin(MotorUnit m) => m.ReservationId.HasValue
                && pins.TryGetValue(m.ReservationId.Value, out var who) && SameName(who, requester);

            foreach (var (variantId, _, tcTake) in variantPulls)
            {
                if (tcTake <= 0) continue;

                var onHandHere = _db.MotorUnits
                    .Where(m => m.ItemId == itemId && m.ItemVariantId == variantId && m.Status == UnitStatus.OnHand)
                    .OrderBy(m => m.RecordedAt)
                    .ToList()
                    // Rechecked in memory, as in FulfillOrderItem.
                    .Where(m => m.Status == UnitStatus.OnHand && m.ItemVariantId == variantId && !OthersPin(m))
                    .OrderBy(m => MinePin(m) ? 0 : 1)
                    .Take(tcTake)
                    .ToList();

                foreach (var known in onHandHere)
                {
                    known.ReservationId = null;
                    known.Status = UnitStatus.PickedUp;
                    known.OrderId = orderId;
                    known.OrderItemId = orderItemId;
                    known.TransferRequestId = transferRequestId;
                    known.PickedUpAt = now;
                    known.PickedUpBy = _currentUser.Name;
                    known.ItemVariantId = null;
                }

                for (int u = onHandHere.Count; u < tcTake; u++)
                {
                    _db.MotorUnits.Add(new MotorUnit
                    {
                        ItemId = itemId,
                        ItemVariantId = null,
                        LabNumber = null,
                        Status = UnitStatus.PickedUp,
                        RecordedAt = now,
                        RecordedBy = _currentUser.Name,
                        OrderId = orderId,
                        OrderItemId = orderItemId,
                        TransferRequestId = transferRequestId,
                        PickedUpAt = now,
                        PickedUpBy = _currentUser.Name
                    });
                }
            }
        }

        /// <summary>Spins a quantity off as a new Pending order, with SplitFromOrderId recording the lineage.</summary>
        /// <param name="team">The original line's team. Must be carried: without it the remainder pulls from every team's stock.</param>
        private (Order Order, OrderItem Item) CreateSplitOrder(Order original, string itemId, int quantity, int? requestedVariantId, int thermocoupledCount, string team)
        {
            var newOrder = new Order
            {
                CreatedAt = System.DateTime.UtcNow,
                Status = "Pending",
                RequestedBy = original.RequestedBy,
                SplitFromOrderId = original.Id
            };
            _db.Orders.Add(newOrder);
            _db.SaveChanges();   // generates newOrder.Id

            var newItem = new OrderItem
            {
                OrderId = newOrder.Id,
                ItemId = itemId,
                Quantity = quantity,
                RequestedVariantId = requestedVariantId,
                ThermocoupledCount = thermocoupledCount,
                Team = team,
                Status = "Pending"
            };
            _db.OrderItems.Add(newItem);
            _db.SaveChanges();

            return (newOrder, newItem);
        }

        /// <summary>
        /// The picker reports the real count for a short line (corrections: VariantId -> count seen).
        /// Stock is corrected, then a fresh order for the corrected quantity is issued and picked up at once.
        /// </summary>
        public PickupResult ReportShortPull(int orderId, int orderItemId, Dictionary<int, int> corrections,
            Dictionary<int, IReadOnlyCollection<int>>? leavingCompressorUnits = null,
            Dictionary<int, IReadOnlyCollection<int>>? leavingMotorUnits = null)
        {
            using var tx = _db.Database.BeginTransaction();
            try
            {
                var it = _db.OrderItems.Include(o => o.Order).FirstOrDefault(o => o.Id == orderItemId && o.OrderId == orderId);
                if (it == null) throw new InvalidOperationException("Order line not found.");
                if (it.Status != "Cancelled")
                    throw new InvalidOperationException("This line isn't flagged short -- nothing to correct.");

                var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
                if (inv == null) throw new InvalidOperationException("That item no longer exists in inventory.");

                // Leave "Cancelled" at once so a double submit cannot apply the correction twice.
                it.Status = "Corrected";

                var now = System.DateTime.UtcNow;
                foreach (var kv in corrections)
                {
                    var v = inv.Variants.FirstOrDefault(x => x.Id == kv.Key && !x.IsRetired);
                    if (v == null) continue;
                    int before = v.Quantity;
                    int tcBefore = v.ThermocoupledQty;
                    int after = System.Math.Max(0, kv.Value);
                    if (after == before) continue;

                    v.Quantity = after;
                    v.ThermocoupledQty = System.Math.Min(v.ThermocoupledQty, after);

                    // A count below the units recorded here must say which are gone. A correction upward never asks.
                    string unitNote = "";
                    if (v.Quantity < before || v.ThermocoupledQty < tcBefore)
                    {
                        IReadOnlyCollection<int>? gone = null, goneMotors = null;
                        leavingCompressorUnits?.TryGetValue(v.Id, out gone);
                        leavingMotorUnits?.TryGetValue(v.Id, out goneMotors);
                        // A recount is the truth: a held unit may go, and the holds shrink to fit right after.
                        unitNote = InventoryService.RetireSurplusUnits(_db, inv, v, gone, goneMotors, heldMayLeave: true);
                        ReservationService.ShrinkToFit(_db, it.ItemId, v, _currentUser.Name,
                            $"short-pull correction on Order #{orderId} found {after} on V{v.VariantNumber}");
                    }

                    _db.TransactionLogs.Add(new TransactionLog
                    {
                        Timestamp = now,
                        ActionType = "Stock Adjustment",
                        ItemId = it.ItemId,
                        ItemName = inv.ItemName,
                        QuantityChange = after - before,
                        Details = $"Short-pull correction on Order #{orderId}: V{v.VariantNumber} ({v.FdaString}) recorded {before}, actually {after}." + unitNote,
                        User = _currentUser.Name
                    });
                }
                // The re-pickup reads holds from the database, so the shrink is saved first.
                _db.SaveChanges();

                // Only the line's own team's stock, minus what other people hold, or the re-pickup would refuse.
                string lineRequester = it.Order.RequestedBy ?? "";   // Order is Included above
                var holdView = StockClaims.FromVariants(_db, it.ItemId, inv.Variants);
                int available = inv.Variants
                    .Where(v => !v.IsRetired && (string.IsNullOrEmpty(it.Team) || v.Team == it.Team))
                    .Sum(v => System.Math.Max(0, v.Quantity - holdView.HeldByOthers(v.Id, lineRequester).Qty));
                int correctedQty = System.Math.Min(it.Quantity, available);

                var result = new PickupResult { OrderId = orderId };

                if (correctedQty <= 0)
                {
                    // Nothing to reissue -- the correction found zero real stock.
                    _db.SaveChanges();
                    tx.Commit();
                    return result;
                }

                // The reissue keeps the requested location and the TC count, clamped to the corrected quantity.
                var (newOrder, newItem) = CreateSplitOrder(it.Order, it.ItemId, correctedQty, it.RequestedVariantId,
                    System.Math.Min(it.ThermocoupledCount, correctedQty), it.Team);

                // Picked up at once: the picker is standing there with the corrected count.
                var shortLine = FulfillOrderItem(newItem, newOrder.Id, null, null);
                if (shortLine == null)
                {
                    newOrder.Status = "Completed";
                    newOrder.FulfilledBy = _currentUser.Name;
                    newOrder.FulfilledAt = now;
                    result.NewOrderId = newOrder.Id;
                }
                else
                {
                    // Short again (two corrections raced). It stays Pending in the queue.
                    result.ShortLines.Add(shortLine);
                }

                _db.SaveChanges();
                tx.Commit();
                return result;
            }
            catch
            {
                tx.Rollback();
                // Rolled-back changes are still tracked. Clear them so the controller reads what the database holds.
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        /// <summary>
        /// The compressor unit rows a Return or Scrap acts on, checked before anything changes.
        /// The ticks and the quantity must describe the same units on this loan line.
        /// </summary>
        private List<CompressorUnit> LoanUnitsToSettle(OrderItem it, int settling, int[]? unitIds, string verbPast)
        {
            var lineUnits = _db.CompressorUnits
                .Where(c => c.OrderItemId == it.Id && c.ItemId == it.ItemId && c.Status == UnitStatus.PickedUp)
                .ToList();
            var ids = (unitIds ?? System.Array.Empty<int>()).Distinct().ToList();
            var picked = lineUnits.Where(c => ids.Contains(c.Id)).ToList();

            if (picked.Count != ids.Count)
                throw new InvalidOperationException(
                    "One or more ticked units aren't out on this loan line any more -- refresh the page and try again.");
            if (picked.Count > settling)
                throw new InvalidOperationException(
                    $"{picked.Count} units are ticked but only {settling} {(settling == 1 ? "is" : "are")} being {verbPast} -- untick the ones that aren't, or raise the quantity. Nothing was changed.");

            int untracked = System.Math.Max(0, it.LoanOutstanding - lineUnits.Count);
            int unticked = settling - picked.Count;
            if (unticked > untracked)
            {
                int more = unticked - untracked;
                throw new InvalidOperationException(
                    $"{settling} {(settling == 1 ? "unit is" : "units are")} being {verbPast} but only {picked.Count} {(picked.Count == 1 ? "is" : "are")} ticked -- tick which {more} more recorded {(more == 1 ? "unit is" : "units are")} included. Nothing was changed.");
            }

            return picked;
        }

        // Returns loaned units to a chosen active location or a new one, and draws down LoanOutstanding.
        // Motor loans come back as TC stock. Own orders only.
        public void ReturnLoan(int orderItemId, int qty, int? targetVariantId,
            string? newParent, string? newMajor, string? newSub, string? newRack, string? newRow,
            int[]? unitIds = null, string? reason = null)
        {
            if (qty <= 0) return;

            using var tx = _db.Database.BeginTransaction();
            try
            {
                var it = _db.OrderItems.Include(o => o.Order).FirstOrDefault(o => o.Id == orderItemId);
                if (it == null) throw new InvalidOperationException("Loan line not found.");
                if (!string.Equals(it.Order.RequestedBy, _currentUser.Name, System.StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("You can only return your own order's loans.");

                int give = System.Math.Min(qty, it.LoanOutstanding);
                if (give <= 0) throw new InvalidOperationException("Nothing outstanding to return on this line.");

                var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
                if (inv == null) throw new InvalidOperationException("That item no longer exists in inventory.");

                // Checked before anything moves, including the new-location save below.
                var pickedUnits = LoanUnitsToSettle(it, give, unitIds, "returned");

                bool asTc = InventoryService.IsMotorType(inv.Type);   // motor loans return as TC stock
                string Seg(string? v) => string.IsNullOrWhiteSpace(v) ? "0" : v.Trim().ToUpperInvariant();

                // Units come back to the team they were pulled for: the line's team, else the item's.
                string returnTeam = string.IsNullOrWhiteSpace(it.Team) ? (inv.Team ?? "") : it.Team;

                ItemVariant dest;
                if (targetVariantId.HasValue && targetVariantId.Value > 0)
                {
                    dest = inv.Variants.FirstOrDefault(v => v.Id == targetVariantId.Value && !v.IsRetired)
                        ?? throw new InvalidOperationException("The chosen location is no longer active.");
                    // Only this line's own team's shelves. Holds the boundary against a direct POST.
                    if (!string.IsNullOrEmpty(it.Team) && dest.Team != it.Team)
                        throw new InvalidOperationException(
                            $"That location belongs to {(string.IsNullOrWhiteSpace(dest.Team) ? "no team" : dest.Team)}, not {it.Team} -- this loan can only be returned to its own team's stock.");
                    dest.Quantity += give;
                    if (asTc) dest.ThermocoupledQty = System.Math.Min(dest.ThermocoupledQty + give, dest.Quantity);
                }
                else
                {
                    // Mint a new variant at the posted location (next free number).
                    var used = inv.Variants.Where(v => !v.IsRetired).Select(v => v.VariantNumber).ToHashSet();
                    int nextNum = 1; while (used.Contains(nextNum)) nextNum++;
                    string p = Seg(newParent), m = Seg(newMajor), s = Seg(newSub), rk = Seg(newRack), rw = Seg(newRow);
                    dest = new ItemVariant
                    {
                        VariantNumber = nextNum,
                        Quantity = give,
                        ThermocoupledQty = asTc ? give : 0,
                        Team = returnTeam,
                        Parent = p, Major = m, Sub = s, Rack = rk, Row = rw,
                        FdaString = string.Join(".", new[] { p, m, s, rk, rw }),
                        RegisteredAt = System.DateTime.UtcNow,
                        IsRetired = false
                    };
                    inv.Variants.Add(dest);
                    _db.SaveChanges();   // dest.Id is needed: ItemVariantId has no navigation, so EF would store 0
                }

                // The units the user ticked, already validated by LoanUnitsToSettle.
                string unitNote = "";
                var named = pickedUnits.Where(u => !string.IsNullOrWhiteSpace(u.SerialNumber))
                                       .Select(u => u.SerialNumber).ToList();
                if (named.Count > 0) unitNote = $" [{string.Join(", ", named)}]";
                string reasonNote = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason.Trim()}";

                // Returned units land ON the shelf they were returned to.
                foreach (var cu in pickedUnits)
                {
                    cu.Status = UnitStatus.OnHand;
                    cu.ItemVariantId = dest.Id;
                    cu.OrderId = null;
                    cu.OrderItemId = null;
                    cu.PickedUpAt = null;
                    cu.PickedUpBy = null;
                }

                // TC motors have no serial to tick: the oldest units still out on this order line flip back.
                if (asTc)
                {
                    var returningMotors = _db.MotorUnits
                        .Where(c => c.OrderItemId == it.Id && c.Status == UnitStatus.PickedUp)
                        .OrderBy(c => c.PickedUpAt)
                        .Take(give)
                        .ToList();
                    foreach (var mu in returningMotors)
                    {
                        mu.Status = UnitStatus.OnHand;
                        mu.ItemVariantId = dest.Id;
                        mu.OrderId = null;
                        mu.OrderItemId = null;
                        mu.PickedUpAt = null;
                        mu.PickedUpBy = null;
                    }
                    var labs = returningMotors.Where(m => !string.IsNullOrWhiteSpace(m.LabNumber))
                        .Select(m => m.LabNumber).ToList();
                    if (labs.Count > 0) unitNote = $" [{string.Join(", ", labs)}]";
                }

                it.LoanOutstanding -= give;
                inv.LastUpdated = System.DateTime.UtcNow;
                inv.UpdatedBy = _currentUser.Name;

                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = System.DateTime.UtcNow,
                    ActionType = "Loan Return",
                    ItemId = it.ItemId,
                    ItemName = inv.ItemName,
                    QuantityChange = give,
                    Details = $"Returned {give}{(asTc ? " [TC]" : "")}{unitNote} from Order #{it.OrderId} into V{dest.VariantNumber} ({dest.FdaString}); {it.LoanOutstanding} still out.{reasonNote}",
                    User = _currentUser.Name
                });

                _db.SaveChanges();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        // Scraps loaned units: no stock is added, the loan counter closes and it is logged. Own orders only.
        public void ScrapLoan(int orderItemId, int qty, int[]? unitIds = null, string? reason = null)
        {
            if (qty <= 0) return;

            using var tx = _db.Database.BeginTransaction();
            try
            {
                var it = _db.OrderItems.Include(o => o.Order).FirstOrDefault(o => o.Id == orderItemId);
                if (it == null) throw new InvalidOperationException("Loan line not found.");
                if (!string.Equals(it.Order.RequestedBy, _currentUser.Name, System.StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("You can only scrap your own order's loans.");

                int drop = System.Math.Min(qty, it.LoanOutstanding);
                if (drop <= 0) throw new InvalidOperationException("Nothing outstanding to scrap on this line.");

                bool isMotor = InventoryService.IsMotorType(
                    _db.InventoryItems.AsNoTracking().Where(i => i.ItemId == it.ItemId).Select(i => i.Type).FirstOrDefault());

                // Same unit checks as ReturnLoan.
                var pickedUnits = LoanUnitsToSettle(it, drop, unitIds, "scrapped");
                string unitNote = "";
                var named = pickedUnits.Where(u => !string.IsNullOrWhiteSpace(u.SerialNumber))
                                       .Select(u => u.SerialNumber).ToList();
                if (named.Count > 0) unitNote = $" [{string.Join(", ", named)}]";
                string reasonNote = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason.Trim()}";

                // Scrapped is final. The row stays so the serial's history can still be read.
                foreach (var cu in pickedUnits)
                {
                    cu.Status = UnitStatus.Scrapped;
                    cu.ItemVariantId = null;
                }

                // Motors: the oldest units still out on this order line.
                if (isMotor)
                {
                    var scrappingMotors = _db.MotorUnits
                        .Where(c => c.OrderItemId == it.Id && c.Status == UnitStatus.PickedUp)
                        .OrderBy(c => c.PickedUpAt)
                        .Take(drop)
                        .ToList();
                    foreach (var mu in scrappingMotors)
                    {
                        mu.Status = UnitStatus.Scrapped;
                        mu.ItemVariantId = null;
                    }
                    var labs = scrappingMotors.Where(m => !string.IsNullOrWhiteSpace(m.LabNumber))
                        .Select(m => m.LabNumber).ToList();
                    if (labs.Count > 0) unitNote = $" [{string.Join(", ", labs)}]";
                }

                it.LoanOutstanding -= drop;

                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = System.DateTime.UtcNow,
                    ActionType = "Loan Scrap",
                    ItemId = it.ItemId,
                    ItemName = _db.InventoryItems.AsNoTracking().Where(i => i.ItemId == it.ItemId).Select(i => i.ItemName).FirstOrDefault(),
                    QuantityChange = 0,   // stock already left at pickup; scrap just means it never returns
                    Details = $"Scrapped {drop}{unitNote} out on loan from Order #{it.OrderId}; {it.LoanOutstanding} still out.{reasonNote}",
                    User = _currentUser.Name
                });

                _db.SaveChanges();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        // Only a Pending order can be cancelled. Lines are left alone, on purpose.
        // ItemId is blank on the log: an order can span many items.
        public void CancelPersistedOrder(int orderId)
        {
            using var tx = _db.Database.BeginTransaction();
            try
            {
                var order = _db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
                if (order == null) throw new InvalidOperationException("Order not found.");
                if (order.Status != "Pending")
                    throw new InvalidOperationException(order.Status == "Cancelled"
                        ? $"Order #{orderId} was already cancelled."
                        : $"Order #{orderId} is already {order.Status.ToLowerInvariant()} and can't be cancelled.");

                order.Status = "Cancelled";
                int released = order.Items.Count(i => i.Status == "Pending");
                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = System.DateTime.UtcNow,
                    ActionType = "Order Cancelled",
                    ItemId = "",
                    QuantityChange = 0,
                    Details = $"Order #{orderId} (requested by {order.RequestedBy}) cancelled; {released} pending line(s) released.",
                    User = _currentUser.Name
                });
                _db.SaveChanges();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public void CancelOrder()
        {
            StartOrder();
        }

        // Internal transfer. One-way, one approval. Parallel to the cart and order pipeline:
        // a transfer never creates an Order row. Approval needs Engineer+ on the item's Line, enforced here.

        public void RequestTransfer(string itemId, int qty,
            int? requestedVariantId = null, int thermocoupledCount = 0, string? note = null)
        {
            if (string.IsNullOrWhiteSpace(itemId) || qty <= 0) return;

            var inv = new InventoryService(_db, _currentUser);
            var item = inv.GetById(itemId);
            if (item == null) throw new System.InvalidOperationException("Item not found.");

            // Transfers are for other Lines' items. Your own Line's stock goes through the cart.
            if (inv.IsOwnLine(item.Line))
                throw new System.InvalidOperationException("This item belongs to your own Line -- use Add to Cart instead of Request Transfer.");

            // Capped at what is available to give, net of Pending orders and holds.
            string targetTeam = inv.ResolveTransferTeam(item, requestedVariantId);
            int available = inv.GetAvailableQuantity(itemId, targetTeam);
            if (qty > available)
                throw new System.InvalidOperationException(
                    $"Only {available} of {itemId} available to transfer"
                    + (targetTeam.Length > 0 ? $" from {targetTeam}'s stock" : "")
                    + " -- units already on a pending order or reserved don't count.");

            int tc = InventoryService.IsMotorType(item.Type) ? System.Math.Min(System.Math.Max(0, thermocoupledCount), qty) : 0;
            // TC is counted across claims too.
            if (tc > 0)
            {
                int availTc = inv.GetAvailableTc(itemId, targetTeam);
                if (tc > availTc)
                    throw new System.InvalidOperationException(
                        $"Only {availTc} thermocoupled {itemId} available to transfer -- {tc} asked for.");
            }

            _db.TransferRequests.Add(new TransferRequest
            {
                ItemId = itemId,
                Quantity = qty,
                ThermocoupledCount = tc,
                RequestedVariantId = requestedVariantId,
                RequesterUserName = _currentUser.Name,
                Status = TransferStatus.Requested,
                RequestedAt = System.DateTime.UtcNow,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });
            _db.SaveChanges();
        }

        /// <summary>
        /// The variants an approval draws from, in order: the preferred location, then by number, all
        /// inside the team that owns the requested slice. One definition for ApproveTransfer and the UI.
        /// </summary>
        private List<ItemVariant> TransferPullOrder(InventoryItem item, int? requestedVariantId, out string targetTeam)
        {
            targetTeam = new InventoryService(_db, _currentUser).ResolveTransferTeam(item, requestedVariantId);
            string team = targetTeam;
            return item.Variants
                .Where(v => !v.IsRetired && (team.Length == 0 || v.Team == team))
                .OrderBy(v => requestedVariantId.HasValue && v.Id == requestedVariantId.Value ? 0 : 1)
                .ThenBy(v => v.VariantNumber)
                .ToList();
        }

        /// <summary>How many units an approval would take from each location, in pull order. Reads only.</summary>
        public List<(int VariantId, int Take)> PlanTransferPull(InventoryItem item, int? requestedVariantId, int quantity, string? requester = null)
        {
            // Held stock stays put for a transfer exactly as it does for a pickup.
            var holdView = StockClaims.FromVariants(_db, item.ItemId, item.Variants);
            var plan = new List<(int VariantId, int Take)>();
            int remaining = quantity;
            foreach (var v in TransferPullOrder(item, requestedVariantId, out _))
            {
                if (remaining <= 0) break;
                var (take, _) = PlanTake(v, remaining, 0, holdView.HeldByOthers(v.Id, requester ?? ""));
                if (take <= 0) continue;
                plan.Add((v.Id, take));
                remaining -= take;
            }
            return plan;
        }

        // compressorUnits: Lab#/Serial# pairs per physical unit, both optional, the same shape as a pickup.
        public void ApproveTransfer(int transferRequestId, List<(string? Lab, string? Serial)>? compressorUnits = null)
        {
            using var tx = _db.Database.BeginTransaction();
            try
            {
                var req = _db.TransferRequests.FirstOrDefault(b => b.Id == transferRequestId);
                if (req == null) throw new System.InvalidOperationException("Transfer request not found.");
                if (req.Status != TransferStatus.Requested) throw new System.InvalidOperationException("This request has already been decided.");

                var inv = new InventoryService(_db, _currentUser);
                var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == req.ItemId);
                if (item == null) throw new System.InvalidOperationException("That item no longer exists in inventory.");
                if (!inv.CanApproveTransfer(item, req.RequestedVariantId))
                    throw new System.InvalidOperationException("You can only approve transfers against your own Line's items -- and, once an item is split across teams, only the owning team's own Engineers can approve that slice.");

                // The pull stays inside the slice CanApproveTransfer gated on. "" sees every active variant.
                var pullOrder = TransferPullOrder(item, req.RequestedVariantId, out string targetTeam);
                // Ceiling = available, not raw shelf quantity, so a transfer cannot outbid an earlier order.
                // Measured for the requester: the approver's own holds must not become transferable.
                string requester = req.RequesterUserName ?? "";
                int totalAvailable = inv.GetAvailableQuantity(req.ItemId, targetTeam, requester);
                if (req.Quantity > totalAvailable)
                    throw new System.InvalidOperationException(
                        $"Not enough available -- {req.Quantity} requested, {totalAvailable} free"
                        + (targetTeam.Length > 0 ? $" across {targetTeam}'s locations" : " across active locations")
                        + " (units already committed to a pending order or reserved are excluded).");
                if (req.ThermocoupledCount > 0)
                {
                    int availTc = inv.GetAvailableTc(req.ItemId, targetTeam, requester);
                    if (req.ThermocoupledCount > availTc)
                        throw new System.InvalidOperationException(
                            $"Not enough thermocoupled available -- {req.ThermocoupledCount} requested, {availTc} free.");
                }
                var holdView = StockClaims.FromVariants(_db, req.ItemId, item.Variants);
                var pins = ReservationService.PinOwners(_db, req.ItemId);

                int remaining = req.Quantity;
                int remainingTc = req.ThermocoupledCount;
                int pulledTc = 0;   // summed as taken -- see FulfillOrderItem for why
                var pulls = new List<string>();
                var variantPulls = new List<(int VariantId, int Take, int TcTake)>();
                foreach (var v in pullOrder)
                {
                    if (remaining <= 0) break;
                    var (take, tcTake) = PlanTake(v, remaining, remainingTc, holdView.HeldByOthers(v.Id, requester));
                    if (take <= 0) continue;

                    v.Quantity -= take;
                    v.ThermocoupledQty -= tcTake;
                    remaining -= take;
                    remainingTc = System.Math.Max(0, remainingTc - tcTake);
                    pulledTc += tcTake;
                    pulls.Add($"{take}{(tcTake > 0 ? $" [{tcTake} TC]" : "")} from V{v.VariantNumber} ({v.FdaString})");
                    variantPulls.Add((v.Id, take, tcTake));
                }

                var now = System.DateTime.UtcNow;
                int pulledQty = req.Quantity - remaining;

                int missingLab = 0, missingSerial = 0, matchedUnits = 0;
                if (InventoryService.IsCompressorType(item.Type) && pulledQty > 0)
                {
                    var unitVariant = new List<int>();
                    foreach (var (variantId, take, _) in variantPulls)
                        for (int i = 0; i < take; i++) unitVariant.Add(variantId);

                    var onHandByVariant = new Dictionary<int, List<CompressorUnit>>();
                    List<CompressorUnit> OnHandAt(int variantId)
                    {
                        if (!onHandByVariant.TryGetValue(variantId, out var list))
                        {
                            list = _db.CompressorUnits
                                .Where(c => c.ItemId == req.ItemId && c.ItemVariantId == variantId && c.Status == UnitStatus.OnHand)
                                .OrderBy(c => c.RecordedAt)
                                .ToList();
                            onHandByVariant[variantId] = list;
                        }
                        return list;
                    }

                    var claimedThisCall = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    var slotsLeft = variantPulls.ToDictionary(p => p.VariantId, p => p.Take);   // see FulfillOrderItem
                    for (int u = 0; u < pulledQty; u++)
                    {
                        string? lab = (compressorUnits != null && u < compressorUnits.Count) ? compressorUnits[u].Lab : null;
                        string? serial = (compressorUnits != null && u < compressorUnits.Count) ? compressorUnits[u].Serial : null;
                        int vid = unitVariant[u];
                        AssignOneCompressorUnit(req.ItemId, OnHandAt(vid), lab, serial, null, null, req.Id, now,
                            claimedThisCall, item.Variants.First(x => x.Id == vid).Quantity + slotsLeft[vid],
                            compressorUnits != null && u < compressorUnits.Count,
                            ref missingLab, ref missingSerial, ref matchedUnits, pins, requester);
                        slotsLeft[vid]--;
                    }
                }

                if (InventoryService.IsMotorType(item.Type) && pulledTc > 0)
                    TakeMotorUnits(req.ItemId, variantPulls, null, null, req.Id, now, pins, requester);

                item.LastUpdated = now;
                item.UpdatedBy = _currentUser.Name;

                req.Status = TransferStatus.Approved;
                req.DecidedBy = _currentUser.Name;
                req.DecidedAt = now;

                var flagParts = new List<string>();
                if (missingLab > 0) flagParts.Add($"{missingLab} missing lab #");
                if (missingSerial > 0) flagParts.Add($"{missingSerial} missing serial #");
                if (matchedUnits > 0) flagParts.Add($"{matchedUnits} matched to known unit(s)");
                string compressorFlag = flagParts.Count > 0 ? $" ({string.Join(", ", flagParts)})" : "";

                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = now,
                    ActionType = "Transfer Approved",
                    ItemId = req.ItemId,
                    ItemName = item.ItemName,
                    QuantityChange = -pulledQty,
                    Details = (pulls.Count > 0
                        ? $"Transfer #{req.Id} approved for {req.RequesterUserName} -- pulled {string.Join(", ", pulls)}"
                        : $"Transfer #{req.Id} approved for {req.RequesterUserName}") + compressorFlag,
                    User = _currentUser.Name
                });

                _db.SaveChanges();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public void DenyTransfer(int transferRequestId)
        {
            var req = _db.TransferRequests.FirstOrDefault(b => b.Id == transferRequestId);
            if (req == null) throw new System.InvalidOperationException("Transfer request not found.");
            if (req.Status != TransferStatus.Requested) throw new System.InvalidOperationException("This request has already been decided.");

            var inv = new InventoryService(_db, _currentUser);
            var item = inv.GetById(req.ItemId);
            if (item != null && !inv.CanApproveTransfer(item, req.RequestedVariantId))
                throw new System.InvalidOperationException("You can only decide transfers against your own Line's items -- and, once an item is split across teams, only the owning team's own Engineers can decide that slice.");

            req.Status = TransferStatus.Denied;
            req.DecidedBy = _currentUser.Name;
            req.DecidedAt = System.DateTime.UtcNow;
            _db.SaveChanges();
        }
    }
}
