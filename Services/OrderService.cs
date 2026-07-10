using InventoryDevTwo.Models;
using InventoryDevTwo.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json; // Used for JSON shrinking/expanding
using System.Linq;

namespace InventoryDevTwo.Services
{
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
                _notifications.CreateForLevel(
                AccessLevels.Standard, AccessLevels.Standard,
                "PickupRequested",
                $"New pickup requested: Order #{newOrderId} from {requester} ({itemCount} item{(itemCount == 1 ? "" : "s")}).",
                "/Home/PickupQueue",
                excludeUserName: requester);
            }
            catch { /* swallow: the order is already safely submitted */ }
        }

        public void PickUpOrder(int orderId, Dictionary<int, int>? variantChoices = null)
        {
            // Captured inside the tx, used for the post-commit ping to the requester.
            string? requesterToNotify = null;
            string fulfiller = _currentUser.Name;

            using var tx = _db.Database.BeginTransaction();
            try
            {
                var order = _db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
                if (order == null) throw new InvalidOperationException("Order not found.");
                if (order.Status == "Completed") throw new InvalidOperationException("Already completed.");

                foreach (var it in order.Items)
                {
                    var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == it.ItemId);
                    if (inv != null)
                    {
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
                        // lowest-number-first as spill so a pickup never strands on
                        // a location that came up short.
                        var pullOrder = inv.Variants.Where(v => !v.IsRetired)
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
                        it.LoanOutstanding = InventoryService.IsControlType(inv.Type) ? pulledQty
                            : InventoryService.IsMotorType(inv.Type) ? pulledTc
                            : 0;

                        // LOG THE PICKUP HERE! Details snapshot the actual pull
                        // locations at this moment -- variant numbers can be
                        // reused later, so the log must not rely on lookups.
                        _db.TransactionLogs.Add(new TransactionLog
                        {
                            Timestamp = System.DateTime.UtcNow,
                            ActionType = "Order Picked Up",
                            ItemId = it.ItemId,
                            QuantityChange = -it.Quantity,
                            Details = pulls.Count > 0
                                ? $"Order #{orderId} — pulled {string.Join(", ", pulls)}"
                                : $"Order #{orderId}",
                            User = _currentUser.Name
                        });
                    }
                }

                order.Status = "Completed";
                order.FulfilledBy = _currentUser.Name;
                order.FulfilledAt = System.DateTime.UtcNow;
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
        }

        // Return loaned units to inventory: adds stock back at a chosen active
        // location OR a freshly minted one, and draws down LoanOutstanding. Motor
        // loans come back as TC stock (they were TC when they went out). Self-
        // scoped: you can only act on your own order's loans.
        public void ReturnLoan(int orderItemId, int qty, int? targetVariantId,
            string? newParent, string? newMajor, string? newSub, string? newRack, string? newRow)
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

                it.LoanOutstanding -= give;
                inv.LastUpdated = System.DateTime.UtcNow;
                inv.UpdatedBy = _currentUser.Name;

                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = System.DateTime.UtcNow,
                    ActionType = "Loan Return",
                    ItemId = it.ItemId,
                    QuantityChange = give,
                    Details = $"Returned {give}{(asTc ? " [TC]" : "")} from Order #{it.OrderId} into V{dest.VariantNumber} ({dest.FdaString}); {it.LoanOutstanding} still out.",
                    User = _currentUser.Name
                });

                _db.SaveChanges();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        // Scrap loaned units: they aren't coming back, so NO stock is added --
        // this only closes out the loan counter and logs it. Self-scoped.
        public void ScrapLoan(int orderItemId, int qty)
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

                it.LoanOutstanding -= drop;

                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = System.DateTime.UtcNow,
                    ActionType = "Loan Scrap",
                    ItemId = it.ItemId,
                    QuantityChange = 0,   // stock already left at pickup; scrap just means it never returns
                    Details = $"Scrapped {drop} out on loan from Order #{it.OrderId}; {it.LoanOutstanding} still out.",
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