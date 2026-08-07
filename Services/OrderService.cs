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
    }

    /// <summary>
    /// An order line that couldn't be pulled as ordered -- requested exceeds what's
    /// actually on the shelf across this item's active locations. Carries the
    /// per-location breakdown so the picker can report the true count at whichever
    /// shelf(s) they're actually standing at, rather than one flat number.
    /// </summary>
    public class ShortPullLine
    {
        public int OrderItemId { get; set; }
        public string ItemId { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int Requested { get; set; }
        public System.Collections.Generic.List<LocationStock> Locations { get; set; } = new();
    }

    /// <summary>
    /// Outcome of a pickup attempt (PickUpOrder or ReportShortPull's immediate
    /// re-pickup). Lines that came up short are NOT fulfilled -- they're reported
    /// here instead, untouched, for correction via ReportShortPull. Other lines on
    /// the same order complete normally.
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

        // ==========================================
        // SESSION HELPERS (The "Locker" logic)
        // ==========================================

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

        // ==========================================
        // PUBLIC ACTIONS
        // ==========================================
        public LedgerDraft GetCurrentDraft()
        {
            return GetDraftFromSession();
        }
        public void StartOrder()
        {
            // Just wipe the locker for this specific user
            SaveDraftToSession(new LedgerDraft());
        }

        public void AddItem(string itemId, int qty, int? requestedVariantId = null, int thermocoupledCount = 0)
        {
            if (string.IsNullOrWhiteSpace(itemId) || qty <= 0) return;
            if (thermocoupledCount < 0) thermocoupledCount = 0;
            int tc = System.Math.Min(thermocoupledCount, qty);   // can't request more TC than units

            var draft = GetDraftFromSession();
            // Same item + same location request stack together; a different
            // requested location gets its own line so both requests survive.
            var existing = draft.Entries.FirstOrDefault(e => e.ItemId == itemId
                && e.RequestedVariantId == requestedVariantId);

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
                    ThermocoupledCount = tc
                });
            }

            SaveDraftToSession(draft);
        }

        public void RemoveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;

            var draft = GetDraftFromSession();
            draft.Entries.RemoveAll(e => e.ItemId == itemId);

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
                // 1. Create the Order header
                var order = new Order { CreatedAt = System.DateTime.UtcNow, Status = "Pending", RequestedBy = _currentUser.Name };
                _db.Orders.Add(order);
                _db.SaveChanges(); // This generates the new Order.Id
                newOrderId = order.Id;

                // 2. Attach the Items
                foreach (var entry in draft.Entries)
                {
                    // Verify Stock (Clean error message without quotes to protect Toast JS)
                    var avail = invService.GetAvailableQuantity(entry.ItemId);
                    if (avail < entry.Quantity)
                    {
                        throw new InvalidOperationException($"Failed: ID {entry.ItemId} only has {avail} available in stock.");
                    }

                    // TC request only sticks for motors, and never exceeds the line qty.
                    var invItem = invService.GetById(entry.ItemId);
                    int tcCount = InventoryService.IsMotorType(invItem?.Type)
                        ? System.Math.Min(entry.ThermocoupledCount, entry.Quantity)
                        : 0;

                    _db.OrderItems.Add(new OrderItem
                    {
                        OrderId = order.Id,
                        Order = order, // set FK + navigation so EF ties the line to the just-created order
                        ItemId = entry.ItemId,
                        Quantity = entry.Quantity,
                        RequestedVariantId = entry.RequestedVariantId,
                        ThermocoupledCount = tcCount
                    });
                }

                // 3. Save to DB and Commit
                _db.SaveChanges();
                tx.Commit();
                tx.Dispose(); // release the ambient tx so the post-commit ping can SaveChanges cleanly

                // 4. Wipe the user's Session Cart only upon success
                StartOrder();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // Reaching here means the order committed. Ping the L2 runners that a pickup
            // is waiting. Best-effort: a notification hiccup must never fail a real order.
            try
            {
                // Individual opt-in now (Settings -> Pickup Alerts checkbox), not tied to
                // access tier -- so promoting someone off Standard no longer silently
                // cuts them off from alerts they'd deliberately turned on.
                _notifications.CreateForSubscribers(
                "PickupRequested",
                $"New pickup requested: Order #{newOrderId} from {requester} ({itemCount} item{(itemCount == 1 ? "" : "s")}).",
                "/Home/PickupQueue",
                excludeUserName: requester);
            }
            catch { /* swallow: the order is already safely submitted */ }
        }

        // compressorUnits: orderItemId -> list of (LabNumber, SerialNumber) pairs,
        // one entry per physical unit being pulled on that line. Both soft --
        // null/blank in either slot just means that unit's field wasn't given.
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

                // Only lines still awaiting pickup -- a second attempt on the same
                // order (or one that already had a short line pulled aside) must
                // not re-touch lines that already resolved either way.
                foreach (var it in order.Items.Where(i => i.Status == "Pending"))
                {
                    var shortLine = FulfillOrderItem(it, orderId, variantChoices, compressorUnits);
                    if (shortLine != null) result.ShortLines.Add(shortLine);
                }

                // Order-level status follows the lines: nothing left Pending means
                // this order is done being worked on. If every line came up short,
                // call the order what it is -- Cancelled, not Completed -- since
                // nothing under this order id actually left the shelf. A mix of
                // Completed/Cancelled still counts as Completed: real fulfillment
                // happened here, the short line's real fulfillment lives on
                // whatever order ReportShortPull reissues.
                bool anyPending = order.Items.Any(i => i.Status == "Pending");
                if (anyPending)
                {
                    // Shouldn't happen -- FulfillOrderItem always leaves a line
                    // Completed or Cancelled -- but leave the order Pending rather
                    // than lie about it if it ever does.
                }
                else
                {
                    bool anyCompleted = order.Items.Any(i => i.Status == "Completed");
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

            // Order is picked up -- let the person who requested it know who grabbed it.
            // Best-effort, and never notify yourself if you picked up your own request.
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
        /// Pulls (or refuses) a single order line. Mutates `it.Status` to
        /// "Completed" or "Cancelled" and, on success, decrements stock / logs the
        /// pickup exactly as before. Returns the shortfall (stock left untouched)
        /// instead of pulling anything if the shelf can't cover what was ordered --
        /// a short pull is a data-integrity event, not a fulfillment variation, so
        /// nothing partial ever gets pulled here. Shared by PickUpOrder and by
        /// ReportShortPull's immediate re-pickup of the corrected quantity.
        /// </summary>
        private ShortPullLine? FulfillOrderItem(OrderItem it, int orderId,
            Dictionary<int, int>? variantChoices,
            Dictionary<int, List<(string? Lab, string? Serial)>>? compressorUnits)
        {
            var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
            if (inv == null)
            {
                // Item vanished after the order was placed. Nothing to pull or
                // report short on -- let the line close rather than block the
                // order forever.
                it.Status = "Completed";
                return null;
            }

            var activeVariants = inv.Variants.Where(v => !v.IsRetired).ToList();
            int totalAvailable = activeVariants.Sum(v => v.Quantity);

            if (it.Quantity > totalAvailable)
            {
                // Not enough on the shelf to honor this line as ordered. Refuse
                // it here rather than pulling whatever exists -- [decided] a
                // short pull is a data-integrity event, not a fulfillment
                // variation. Stock stays untouched; the picker reports the real
                // count via ReportShortPull, location by location.
                it.Status = "Cancelled";
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
                        RecordedQty = v.Quantity
                    }).ToList()
                };
            }

            // Which location to pull from first: the engineer's request
            // on the order line wins; else the pickup person's choice
            // (posted from the queue page); else no preference.
            int? preferId = it.RequestedVariantId;
            if (preferId == null && variantChoices != null
                && variantChoices.TryGetValue(it.Id, out int chosen))
            {
                preferId = chosen;
            }

            // Preferred variant first (if still active), then the rest
            // lowest-number-first as spill. The pre-check above guarantees
            // enough total stock exists, so this always fully satisfies the
            // line -- it can still span two locations, just never comes up short.
            var pullOrder = activeVariants
                .OrderBy(v => preferId.HasValue && v.Id == preferId.Value ? 0 : 1)
                .ThenBy(v => v.VariantNumber)
                .ToList();

            int remaining = it.Quantity;
            int remainingTc = it.ThermocoupledCount;   // TC motors still owed to this line
            var pulls = new List<string>();   // snapshot for the log
            foreach (var v in pullOrder)
            {
                if (remaining <= 0) break;
                int take = System.Math.Min(v.Quantity, remaining);
                if (take > 0)
                {
                    // How many of the pulled units are TC. Floor: if this
                    // stack lacks enough non-TC to cover `take`, the rest
                    // must be TC (keeps TC <= qty on what's left). Ceiling:
                    // the TC actually on the stack. Between those, honor
                    // what the order still asks for.
                    int nonTc = v.Quantity - v.ThermocoupledQty;
                    int forcedTc = System.Math.Max(0, take - nonTc);
                    int maxTc = System.Math.Min(take, v.ThermocoupledQty);
                    int tcTake = System.Math.Min(maxTc, System.Math.Max(forcedTc, remainingTc));

                    v.Quantity -= take;
                    v.ThermocoupledQty -= tcTake;
                    remaining -= take;
                    remainingTc = System.Math.Max(0, remainingTc - tcTake);
                    pulls.Add($"{take}{(tcTake > 0 ? $" [{tcTake} TC]" : "")} from V{v.VariantNumber} ({v.FdaString})");
                }
            }

            // LOAN STARTS HERE. Loanable units that ACTUALLY left the
            // shelf are now out and expected back (Controls: everything
            // pulled; Motors: the TC pulled). Based on what shipped, not
            // what was ordered, so a short pickup can't over-count a loan.
            int pulledQty = it.Quantity - remaining;
            int pulledTc = it.ThermocoupledCount - remainingTc;
            // Pass 6B: call the shared helper instead of repeating the
            // rule inline. This block WAS a duplicate of
            // InventoryService.LoanableQuantity -- which meant the helper
            // had zero callers, and adding compressors to it changed
            // nothing while this copy still returned 0 for them.
            //
            // Equivalent for the existing types: pulledTc can never exceed
            // pulledQty (TC is a subset of what was pulled), so the Min()
            // inside the helper resolves to pulledTc for motors exactly as
            // before. Controls are unchanged. Compressors now count.
            it.LoanOutstanding = InventoryService.LoanableQuantity(
                inv.Type, pulledQty, pulledTc);

            // COMPRESSOR MINI-VARIANTS: one CompressorUnit row per
            // physical unit actually pulled (pulledQty, not the ordered
            // qty -- a short pickup can't log units that never left the
            // shelf). Lab#/Serial# are both soft: whatever pairs were
            // posted for this line get used in order; anything past the
            // posted count (or left blank) just logs null/null. Missing
            // values are summarized onto the pickup log line below, not
            // blocked here.
            int missingLab = 0, missingSerial = 0, matchedUnits = 0;
            if (InventoryService.IsCompressorType(inv.Type) && pulledQty > 0)
            {
                List<(string? Lab, string? Serial)>? pairs = null;
                compressorUnits?.TryGetValue(it.Id, out pairs);
                var now = System.DateTime.UtcNow;

                // MATCH-OR-CREATE (Pass 6A). CompressorUnits is now a
                // roster, so a serial typed at pickup may already be on
                // it as On Hand stock. Blindly inserting would violate
                // IX_CompressorUnits_ItemId_SerialNumber and crash the
                // pickup for doing exactly the right thing -- reading
                // the number off the machine in your hands.
                //
                // Tracked (no AsNoTracking) so flips persist on SaveChanges.
                var onHand = _db.CompressorUnits
                    .Where(c => c.ItemId == it.ItemId && c.Status == UnitStatus.OnHand)
                    .OrderBy(c => c.RecordedAt)
                    .ToList();

                for (int u = 0; u < pulledQty; u++)
                {
                    string? lab = (pairs != null && u < pairs.Count) ? pairs[u].Lab : null;
                    string? serial = (pairs != null && u < pairs.Count) ? pairs[u].Serial : null;
                    lab = string.IsNullOrWhiteSpace(lab) ? null : lab.Trim();
                    serial = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
                    if (lab == null) missingLab++;
                    if (serial == null) missingSerial++;

                    CompressorUnit? known = null;
                    if (serial != null)
                    {
                        known = onHand.FirstOrDefault(c =>
                            !string.IsNullOrWhiteSpace(c.SerialNumber) &&
                            string.Equals(c.SerialNumber, serial, System.StringComparison.OrdinalIgnoreCase));
                    }

                    if (known != null)
                    {
                        // A unit we already knew about is leaving the shelf.
                        // Flip it -- its history (and lab number) carries forward.
                        onHand.Remove(known);
                        known.Status = UnitStatus.PickedUp;
                        known.OrderId = orderId;
                        known.OrderItemId = it.Id;
                        known.PickedUpAt = now;
                        known.PickedUpBy = _currentUser.Name;
                        known.ItemVariantId = null;   // off the shelf; 6B sets it again on return
                        if (lab != null) known.LabNumber = lab;
                        matchedUnits++;
                    }
                    else
                    {
                        // First time we've seen this one. Recorded and
                        // picked up in the same breath.
                        _db.CompressorUnits.Add(new CompressorUnit
                        {
                            ItemId = it.ItemId,
                            ItemVariantId = null,
                            SerialNumber = serial,
                            LabNumber = lab,
                            Status = UnitStatus.PickedUp,
                            RecordedAt = now,
                            RecordedBy = _currentUser.Name,
                            OrderId = orderId,
                            OrderItemId = it.Id,
                            PickedUpAt = now,
                            PickedUpBy = _currentUser.Name
                        });
                    }
                }
            }

            // TC MOTOR UNITS: mirrors the compressor block above, minus the
            // serial match step (motors have none to match on -- there's
            // nothing to key "is this the same physical unit" off of, so
            // every pull just draws the oldest On Hand rows first). Only the
            // TC subset of the pull gets a row; a non-TC motor never does.
            if (InventoryService.IsMotorType(inv.Type) && pulledTc > 0)
            {
                var now = System.DateTime.UtcNow;
                var onHandMotors = _db.MotorUnits
                    .Where(c => c.ItemId == it.ItemId && c.Status == UnitStatus.OnHand)
                    .OrderBy(c => c.RecordedAt)
                    .Take(pulledTc)
                    .ToList();

                foreach (var known in onHandMotors)
                {
                    known.Status = UnitStatus.PickedUp;
                    known.OrderId = orderId;
                    known.OrderItemId = it.Id;
                    known.PickedUpAt = now;
                    known.PickedUpBy = _currentUser.Name;
                    known.ItemVariantId = null;
                }

                // Untracked TC stock (no on-hand row existed) still needs to
                // be trackable once it's out -- created straight into Picked
                // Up, same as an unmatched compressor serial at pickup.
                for (int u = onHandMotors.Count; u < pulledTc; u++)
                {
                    _db.MotorUnits.Add(new MotorUnit
                    {
                        ItemId = it.ItemId,
                        ItemVariantId = null,
                        LabNumber = null,
                        Status = UnitStatus.PickedUp,
                        RecordedAt = now,
                        RecordedBy = _currentUser.Name,
                        OrderId = orderId,
                        OrderItemId = it.Id,
                        PickedUpAt = now,
                        PickedUpBy = _currentUser.Name
                    });
                }
            }

            var flagParts = new List<string>();
            if (missingLab > 0) flagParts.Add($"{missingLab} missing lab #");
            if (missingSerial > 0) flagParts.Add($"{missingSerial} missing serial #");
            if (matchedUnits > 0) flagParts.Add($"{matchedUnits} matched to known unit(s)");
            string compressorFlag = flagParts.Count > 0 ? $" ({string.Join(", ", flagParts)})" : "";

            // LOG THE PICKUP HERE! Details snapshot the actual pull
            // locations at this moment -- variant numbers can be
            // reused later, so the log must not rely on lookups.
            // QuantityChange logs what actually left (pulledQty), not what
            // was ordered -- the pre-check above means the two always match
            // for a line that reaches this point, but pulledQty is the
            // honest source regardless (see the short-pull writeup: ordered
            // != pulled is exactly the bug that let exports overstate).
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
        /// The other half of the short-pull flow (see FulfillOrderItem). The
        /// picker reports what's ACTUALLY on the shelf, location by location, for
        /// a line that came up short. This adjusts real stock to match what was
        /// seen -- an audit-logged correction, not a fulfillment -- then
        /// immediately issues and picks up a fresh order for the corrected
        /// quantity so the picker leaves with a clean pickup in one action. This
        /// consolidated action is what the widened Standard-level permission
        /// covers: it used to take a runner to find the shortage, an Engineer to
        /// cancel, and the requester to re-order.
        ///
        /// corrections: VariantId -> the count the picker actually saw there.
        /// Only include locations that need correcting; anything left out keeps
        /// its current recorded quantity.
        /// </summary>
        public PickupResult ReportShortPull(int orderId, int orderItemId, Dictionary<int, int> corrections)
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

                // Flip out of "Cancelled" immediately so a double-click or a
                // retried form post can't sail past the guard above and apply
                // this same correction (and reissue a duplicate order) twice.
                it.Status = "Corrected";

                var now = System.DateTime.UtcNow;
                foreach (var kv in corrections)
                {
                    var v = inv.Variants.FirstOrDefault(x => x.Id == kv.Key && !x.IsRetired);
                    if (v == null) continue;
                    int before = v.Quantity;
                    int after = System.Math.Max(0, kv.Value);
                    if (after == before) continue;

                    v.Quantity = after;
                    v.ThermocoupledQty = System.Math.Min(v.ThermocoupledQty, after);

                    _db.TransactionLogs.Add(new TransactionLog
                    {
                        Timestamp = now,
                        ActionType = "Stock Adjustment",
                        ItemId = it.ItemId,
                        ItemName = inv.ItemName,
                        QuantityChange = after - before,
                        Details = $"Short-pull correction on Order #{orderId}: V{v.VariantNumber} ({v.FdaString}) recorded {before}, actually {after}.",
                        User = _currentUser.Name
                    });
                }

                int available = inv.Variants.Where(v => !v.IsRetired).Sum(v => v.Quantity);
                int correctedQty = System.Math.Min(it.Quantity, available);

                var result = new PickupResult { OrderId = orderId };

                if (correctedQty <= 0)
                {
                    // Nothing to reissue -- the correction found zero real stock.
                    _db.SaveChanges();
                    tx.Commit();
                    return result;
                }

                var newOrder = new Order
                {
                    CreatedAt = now,
                    Status = "Pending",
                    RequestedBy = it.Order.RequestedBy
                };
                _db.Orders.Add(newOrder);
                _db.SaveChanges();   // generates newOrder.Id

                var newItem = new OrderItem
                {
                    OrderId = newOrder.Id,
                    Order = newOrder,
                    ItemId = it.ItemId,
                    Quantity = correctedQty,
                    Status = "Pending"
                };
                _db.OrderItems.Add(newItem);
                _db.SaveChanges();

                // Pick it up immediately -- the picker is already standing here
                // with the corrected count in hand. This IS the "pick up clean"
                // step; no second trip through the queue.
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
                    // Came up short again (e.g. two corrections raced on the same
                    // shelf) -- leave it Pending in the normal queue instead of
                    // silently failing.
                    result.ShortLines.Add(shortLine);
                }

                _db.SaveChanges();
                tx.Commit();
                return result;
            }
            catch { tx.Rollback(); throw; }
        }

        // Return loaned units to inventory: adds stock back at a chosen active
        // location OR a freshly minted one, and draws down LoanOutstanding. Motor
        // loans come back as TC stock (they were TC when they went out). Self-
        // scoped: you can only act on your own order's loans.
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

                bool asTc = InventoryService.IsMotorType(inv.Type);   // motor loans return as TC stock
                string Seg(string? v) => string.IsNullOrWhiteSpace(v) ? "0" : v.Trim().ToUpperInvariant();

                ItemVariant dest;
                if (targetVariantId.HasValue && targetVariantId.Value > 0)
                {
                    dest = inv.Variants.FirstOrDefault(v => v.Id == targetVariantId.Value && !v.IsRetired)
                        ?? throw new InvalidOperationException("The chosen location is no longer active.");
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
                        Parent = p, Major = m, Sub = s, Rack = rk, Row = rw,
                        FdaString = string.Join(".", new[] { p, m, s, rk, rw }),
                        RegisteredAt = System.DateTime.UtcNow,
                        IsRetired = false
                    };
                    inv.Variants.Add(dest);
                }

                // Pass 6B: act on the individual units the user ticked. Units are a
                // PARTIAL overlay -- only 184 of 825 compressors have a serial, so a
                // line of 3 may name 1 unit and leave 2 as pure quantity. This loop
                // is bounded by what was actually selected, never by the qty.
                string unitNote = "";
                var pickedUnits = new List<CompressorUnit>();
                if (unitIds != null && unitIds.Length > 0)
                {
                    pickedUnits = _db.CompressorUnits
                        .Where(c => unitIds.Contains(c.Id)
                                 && c.ItemId == it.ItemId
                                 && c.Status == UnitStatus.PickedUp)
                        .ToList();
                    var named = pickedUnits.Where(u => !string.IsNullOrWhiteSpace(u.SerialNumber))
                                           .Select(u => u.SerialNumber).ToList();
                    if (named.Count > 0) unitNote = $" [{string.Join(", ", named)}]";
                }
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

                // TC motor units have no serial for a human to tick, so unlike
                // compressors this isn't a selection -- the oldest `give` units
                // still out ON THIS ORDER LINE flip back automatically. Scoped
                // to OrderItemId so a return can't accidentally pull in a
                // different order's outstanding units for the same model.
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

        // Scrap loaned units: they aren't coming back, so NO stock is added --
        // this only closes out the loan counter and logs it. Self-scoped.
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

                // Pass 6B: act on the individual units the user ticked. Units are a
                // PARTIAL overlay -- only 184 of 825 compressors have a serial, so a
                // line of 3 may name 1 unit and leave 2 as pure quantity. This loop
                // is bounded by what was actually selected, never by the qty.
                string unitNote = "";
                var pickedUnits = new List<CompressorUnit>();
                if (unitIds != null && unitIds.Length > 0)
                {
                    pickedUnits = _db.CompressorUnits
                        .Where(c => unitIds.Contains(c.Id)
                                 && c.ItemId == it.ItemId
                                 && c.Status == UnitStatus.PickedUp)
                        .ToList();
                    var named = pickedUnits.Where(u => !string.IsNullOrWhiteSpace(u.SerialNumber))
                                           .Select(u => u.SerialNumber).ToList();
                    if (named.Count > 0) unitNote = $" [{string.Join(", ", named)}]";
                }
                string reasonNote = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason.Trim()}";

                // Scrapped units never come back to a shelf: ItemVariantId stays
                // null and the status is terminal. The roster row survives the
                // machine, so the serial's history is still queryable afterwards.
                foreach (var cu in pickedUnits)
                {
                    cu.Status = UnitStatus.Scrapped;
                    cu.ItemVariantId = null;
                }

                // Same auto-select reasoning as ReturnLoan: no serial to pick
                // from, so the oldest `drop` units still out on THIS order line
                // become the ones scrapped.
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

        public void CancelPersistedOrder(int orderId)
        {
            var order = _db.Orders.FirstOrDefault(o => o.Id == orderId);
            if (order != null)
            {
                order.Status = "Cancelled";
                _db.SaveChanges();
            }
        }

        public void CancelOrder()
        {
            StartOrder();
        }
    }
}
