using InventoryDevTwo.Models;
using InventoryDevTwo.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace InventoryDevTwo.Services
{
    public class InventoryService
    {
        private readonly AppDbContext _db;
        private readonly CurrentUserService _currentUser;

        public InventoryService(AppDbContext db, CurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        // ============================
        // NEW ITEM REGISTRY LOGIC
        // ============================
        public string GetNextItemId()
        {
            var allIds = _db.InventoryItems.AsNoTracking().Select(i => i.ItemId).ToList();
            int maxId = 1000;

            foreach (var idStr in allIds)
            {
                if (int.TryParse(idStr, out int id))
                {
                    if (id > maxId) maxId = id;
                }
            }
            return (maxId + 1).ToString();
        }

        // ----------------------------------------------------------------------
        // ITEM ID GENERATOR  (format: <GroupInitial><Type first+last>-NNNN)
        //   e.g. Commercial + Valve -> "CVE-0001", Residential + Coil -> "RCL-0001"
        // The 4-digit number is sequenced PER PREFIX: the highest existing number
        // for that exact prefix + 1, zero-padded to 4 (spills to 5+ past 9999).
        // Legacy IDs that don't match the "<PREFIX>-<digits>" shape are ignored,
        // so old-style codes (e.g. CNEA) can't poison the count. The result is
        // re-checked for collisions before returning.
        // ----------------------------------------------------------------------
        public string GenerateItemId(string? group, string? type)
        {
            string prefix = BuildPrefix(group, type);   // includes trailing '-'

            int max = 0;
            var matches = _db.InventoryItems.AsNoTracking()
                .Where(i => i.ItemId.StartsWith(prefix))
                .Select(i => i.ItemId)
                .ToList();
            foreach (var id in matches)
            {
                var numPart = id.Substring(prefix.Length);
                if (numPart.Length > 0 && numPart.All(char.IsDigit)
                    && int.TryParse(numPart, out int n) && n > max)
                {
                    max = n;
                }
            }

            int next = max + 1;
            string candidate = prefix + next.ToString("D4");
            // Collision guard: never hand back an ID that already exists.
            while (_db.InventoryItems.Any(i => i.ItemId == candidate))
            {
                next++;
                candidate = prefix + next.ToString("D4");
            }
            return candidate;
        }

        // Group initial + type's first & last letter, uppercased, with trailing '-'.
        // Blank group/type fall back to the same defaults the controller applies.
        private static string BuildPrefix(string? group, string? type)
        {
            string g = string.IsNullOrWhiteSpace(group) ? "Commercial" : group.Trim();
            string t = string.IsNullOrWhiteSpace(type) ? "General" : type.Trim();

            char gi = char.ToUpperInvariant(g[0]);
            char tFirst = char.ToUpperInvariant(t[0]);
            char tLast = char.ToUpperInvariant(t[t.Length - 1]);
            return $"{gi}{tFirst}{tLast}-";
        }

        public void CreateItem(InventoryItem newItem)
        {
            newItem.LastUpdated = System.DateTime.UtcNow;
            newItem.UpdatedBy = _currentUser.Name;
            // First-registered timestamp. Set once here and never touched again
            // (unlike LastUpdated, which moves on every edit).
            if (newItem.RegisteredAt == null)
                newItem.RegisteredAt = System.DateTime.UtcNow;

            // Capture the STAGED values (posted by the form into the pass-through
            // setters) BEFORE adding the variant -- once a variant exists, the
            // getters read from it instead of the staging fields.
            int qty = newItem.Quantity;
            var variantOne = new ItemVariant
            {
                VariantNumber = 1,
                Quantity = qty,
                Parent = newItem.Parent,
                Major = newItem.Major,
                Sub = newItem.Sub,
                Rack = newItem.Rack,
                Row = newItem.Row,
                FdaString = newItem.FdaString,
                RegisteredAt = newItem.RegisteredAt,
                IsRetired = false
            };
            newItem.Variants.Add(variantOne);

            _db.InventoryItems.Add(newItem);

            // LOG THE CREATION
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = System.DateTime.UtcNow,
                ActionType = "New Registry",
                ItemId = newItem.ItemId,
                QuantityChange = qty,
                Details = $"Registered to {newItem.Group}/{newItem.Team}",
                User = newItem.UpdatedBy
            });

            _db.SaveChanges();
        }

        // ============================
        // SEARCH AND CORE LOGIC
        // ============================
        public List<InventoryItem> Search(string? omni, string? name, string? type, string? brand, string? notes)
        {
            // Include variants: results feed the holoviewer, which reads the
            // Quantity/location pass-throughs -- without variants loaded those
            // silently read 0/blank.
            var query = _db.InventoryItems.AsNoTracking()
                .Include(i => i.Variants)
                .AsQueryable();

            bool isFilterActive = false;

            if (!string.IsNullOrWhiteSpace(name))
            {
                query = query.Where(i => i.ItemName.ToLower().Contains(name.ToLower()));
                isFilterActive = true;
            }
            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(i => i.Type.ToLower().Contains(type.ToLower()));
                isFilterActive = true;
            }
            if (!string.IsNullOrWhiteSpace(brand))
            {
                query = query.Where(i => i.Brand.ToLower().Contains(brand.ToLower()));
                isFilterActive = true;
            }
            if (!string.IsNullOrWhiteSpace(notes))
            {
                // FdaString now lives on the variants (item.FdaString is NotMapped
                // and cannot be used in an EF query) -- match any active variant.
                query = query.Where(i => i.Description.ToLower().Contains(notes.ToLower()) ||
                                         i.Variants.Any(v => !v.IsRetired && v.FdaString.ToLower().Contains(notes.ToLower())));
                isFilterActive = true;
            }

            if (!isFilterActive && !string.IsNullOrWhiteSpace(omni))
            {
                var s = omni.ToLower();
                query = query.Where(i =>
                    i.ItemId.ToLower().Contains(s) ||
                    i.ItemName.ToLower().Contains(s) ||
                    i.Brand.ToLower().Contains(s) ||
                    i.Type.ToLower().Contains(s) ||
                    i.Variants.Any(v => !v.IsRetired && v.FdaString.ToLower().Contains(s)) ||
                    i.Team.ToLower().Contains(s) ||
                    i.Group.ToLower().Contains(s) ||
                    i.ProjectCode.ToLower().Contains(s)
                );
            }

            if (!isFilterActive && string.IsNullOrWhiteSpace(omni))
            {
                return new List<InventoryItem>();
            }

            return query.ToList();
        }

        public byte[] ExportToCsv(string? group, string? team, string? type, string? brand, string? fdaString,
                                  bool expAvailable, bool expAlerts, bool expScrap, bool expOwnership, string expTimeFrame)
        {
            var query = _db.InventoryItems.AsNoTracking()
                .Include(i => i.Variants)
                .AsQueryable();

            // 1. STRICT AND LOGIC (The Top Half of the Wizard)
            if (!string.IsNullOrWhiteSpace(group)) query = query.Where(i => i.Group.ToLower() == group.ToLower());
            if (!string.IsNullOrWhiteSpace(team)) query = query.Where(i => i.Team.ToLower() == team.ToLower());
            if (!string.IsNullOrWhiteSpace(type)) query = query.Where(i => i.Type.ToLower().Contains(type.ToLower()));
            if (!string.IsNullOrWhiteSpace(brand)) query = query.Where(i => i.Brand.ToLower().Contains(brand.ToLower()));
            if (!string.IsNullOrWhiteSpace(fdaString)) query = query.Where(i => i.Variants.Any(v => !v.IsRetired && v.FdaString.StartsWith(fdaString)));

            var items = query.ToList();
            var itemIds = items.Select(i => i.ItemId).ToList();

            // 2. FETCH TRANSACTION HISTORY (If a timeframe is requested)
            DateTime? since = null;
            if (expTimeFrame == "7") since = DateTime.UtcNow.AddDays(-7);
            else if (expTimeFrame == "30") since = DateTime.UtcNow.AddDays(-30);
            else if (expTimeFrame == "90") since = DateTime.UtcNow.AddDays(-90);

            var logsQuery = _db.TransactionLogs.AsNoTracking().Where(t => itemIds.Contains(t.ItemId));
            if (since.HasValue) logsQuery = logsQuery.Where(t => t.Timestamp >= since.Value);

            var allLogs = logsQuery.ToList();

            var builder = new StringBuilder();

            // CSV HEADER: Added Status, Scrapped, and Ownership columns
            builder.AppendLine("ItemID,ItemName,Type,Brand,Location,CurrentQuantity,Group,Team,ProjectCode,StatusTags,ScrappedQty,OwnershipChanges");

            foreach (var i in items)
            {
                var myLogs = allLogs.Where(t => t.ItemId == i.ItemId).ToList();

                // Calculate historical data based on the selected time frame
                int scrapQty = myLogs.Where(t => t.ActionType == "Scrap").Sum(t => System.Math.Abs(t.QuantityChange));
                int ownCount = myLogs.Count(t => t.ActionType == "Ownership");

                // Determine the true/false status for the 4 checkboxes
                bool isAvailable = i.Quantity > 0;
                bool isAlert = i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold;
                bool isScrap = scrapQty > 0;
                bool isOwn = ownCount > 0;

                // 3. THE "OR" LOGIC (Bottom Half of the Wizard)
                bool include = false;
                if (expAvailable && isAvailable) include = true;
                if (expAlerts && isAlert) include = true;
                if (expScrap && isScrap) include = true;
                if (expOwnership && isOwn) include = true;

                // If it didn't meet ANY of the checked boxes, skip this item entirely
                if (!include) continue;

                // Build a nice readable Status Tag string for the Excel file
                var tags = new List<string>();
                if (isAvailable) tags.Add("Available");
                if (isAlert) tags.Add("Low Stock Alert");
                if (isScrap) tags.Add("Scrapped");
                if (isOwn) tags.Add("Ownership Changed");
                string tagString = string.Join(" | ", tags);

                // Escape commas to protect the CSV structure
                string safeName = i.ItemName?.Replace(",", ";") ?? "";
                string safeBrand = i.Brand?.Replace(",", ";") ?? "";

                builder.AppendLine($"{i.ItemId},{safeName},{i.Type},{safeBrand},{i.FdaString},{i.Quantity},{i.Group},{i.Team},{i.ProjectCode},{tagString},{scrapQty},{ownCount}");
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        public int GetAvailableQuantity(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            var total = _db.ItemVariants.AsNoTracking()
                .Where(v => !v.IsRetired && v.InventoryItem.ItemId == itemId)
                .Sum(v => (int?)v.Quantity) ?? 0;
            var pendingAlloc = _db.OrderItems.AsNoTracking().Where(oi => oi.ItemId == itemId && oi.Order.Status == "Pending").Sum(oi => (int?)oi.Quantity) ?? 0;
            return System.Math.Max(0, total - pendingAlloc);
        }

        public int GetAvailableForOrder(string itemId, System.DateTime orderCreatedAt)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            var total = _db.ItemVariants.AsNoTracking()
                .Where(v => !v.IsRetired && v.InventoryItem.ItemId == itemId)
                .Sum(v => (int?)v.Quantity) ?? 0;
            var earlierAlloc = _db.OrderItems.AsNoTracking().Where(oi => oi.ItemId == itemId && oi.Order.Status == "Pending" && oi.Order.CreatedAt < orderCreatedAt).Sum(oi => (int?)oi.Quantity) ?? 0;
            return System.Math.Max(0, total - earlierAlloc);
        }

        public int GetTotalQuantity(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            return _db.ItemVariants.AsNoTracking()
                .Where(v => !v.IsRetired && v.InventoryItem.ItemId == itemId)
                .Sum(v => (int?)v.Quantity) ?? 0;
        }

        public int GetTotalDbCount() => _db.InventoryItems.Count();

        public IEnumerable<InventoryItem> GetAll() =>
            _db.InventoryItems.AsNoTracking().Include(i => i.Variants).ToList();

        public InventoryItem? GetById(string itemId) =>
            _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);

        public void UpdateAlertThreshold(string itemId, int threshold)
        {
            var item = _db.InventoryItems.FirstOrDefault(i => i.ItemId == itemId);
            if (item != null)
            {
                item.AlertThreshold = threshold;
                _db.SaveChanges();
            }
        }

        // Bulk-set the alert threshold across many items at once. When `team` is
        // a real team name, only that team's items are touched; when it's null/
        // blank (e.g. Kevin / Admin with no team), every item is set. Returns the
        // number of items affected. All downstream alert displays and the manager
        // low-stock summary read AlertThreshold, so they pick this up immediately.
        public int SetDefaultThreshold(string? team, int threshold)
        {
            var q = _db.InventoryItems.AsQueryable();
            if (!string.IsNullOrWhiteSpace(team))
                q = q.Where(i => i.Team == team);

            var items = q.ToList();
            foreach (var it in items)
                it.AlertThreshold = threshold;
            _db.SaveChanges();
            return items.Count;
        }

        // Smallest positive number not used by an ACTIVE variant. Retired rows
        // keep their number for history, but the number itself is reusable.
        private static int NextFreeVariantNumber(InventoryItem item)
        {
            var used = item.ActiveVariants.Select(v => v.VariantNumber).ToHashSet();
            int n = 1;
            while (used.Contains(n)) n++;
            return n;
        }

        // TC-eligible = the type ends with "Motor": Motor / ID Motor / OD Motor.
        // Every other type (Compressor, Coil, EEV, ...) stays TC-free (qty 0).
        // Keep this in lockstep with the client-side isMotorType() in Index.cshtml.
        public static bool IsMotorType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().ToLowerInvariant().EndsWith("motor");

        // Loanable "controls" = types that end with "Control" (e.g. "Control",
        // "Fan Control"). NOTE: Type is free-text and NO current item is typed
        // this way, so today this matches nothing -- control-ish stock (EEV, TXV,
        // VFD, Valve) is NOT counted until you either type items as "...Control"
        // or widen this one helper. This is the single place to change that rule.
        public static bool IsControlType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().ToLowerInvariant().EndsWith("control");

        // How many units of an order line are LOANABLE (library books, expected
        // back): a Control loans its whole quantity; a Motor loans only its TC
        // subset; everything else loans nothing. Drives LoanOutstanding at pickup.
        public static int LoanableQuantity(string? type, int quantity, int thermocoupledCount)
        {
            if (IsControlType(type)) return quantity;
            if (IsMotorType(type)) return System.Math.Min(thermocoupledCount, quantity);
            return 0;
        }

        public (InventoryItem item, int oldQty, int newQty)? ModifyStock(string itemId, string actionType, int quantity, string? newGroup, string? newTeam,
            string? newParent = null, string? newMajor = null, string? newSub = null, string? newRack = null, string? newRow = null,
            string? targetVariant = null, int? transferQty = null, int thermocoupledQty = 0)
        {
            var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return null;

            // TC only exists on motors; for anything else, force the incoming
            // count to 0 so a stray post can never tag a non-motor as TC.
            bool isMotor = IsMotorType(item.Type);
            if (!isMotor) thermocoupledQty = 0;
            if (thermocoupledQty < 0) thermocoupledQty = 0;

            // Blank segment -> "0" so every level is explicit (matches the
            // existing data convention, e.g. PATS.0.0.0.0).
            string Seg(string? v) => string.IsNullOrWhiteSpace(v) ? "0" : v.Trim().ToUpperInvariant();
            string FiveSeg(string? p, string? m, string? s, string? rk, string? rw)
                => string.Join(".", new[] { Seg(p), Seg(m), Seg(s), Seg(rk), Seg(rw) });

            // Resolve which variant this action targets. "" / null = primary
            // (single-location items never send one);
            // a number = that specific ItemVariant.Id; "NEW" (Add only) =
            // create a fresh variant at the posted location.
            bool wantsNewLocation = string.Equals(targetVariant, "NEW", System.StringComparison.OrdinalIgnoreCase);
            ItemVariant? pv = null;
            if (!wantsNewLocation && int.TryParse(targetVariant, out int tvId))
                pv = item.ActiveVariants.FirstOrDefault(v => v.Id == tvId);
            pv ??= item.PrimaryVariant;
            if (pv == null && !wantsNewLocation)
            {
                // Self-heal: an item somehow missing all variants gets a fresh
                // #1 at qty 0 (should never happen after backfill).
                pv = new ItemVariant { VariantNumber = 1, Quantity = 0, IsRetired = false };
                item.Variants.Add(pv);
            }

            int oldQty = item.Quantity;
            int qtyChange = 0;
            string details = "";

            if (actionType == "Add")
            {
                qtyChange = quantity;
                // Of the units being added, at most `quantity` can be TC.
                int tcAdd = System.Math.Min(thermocoupledQty, quantity);
                string tcNote = (isMotor && tcAdd > 0) ? $" ({tcAdd} thermocoupled)" : "";
                if (wantsNewLocation)
                {
                    // New physical spot for this family: mint the next free
                    // variant number at the posted (coded) location.
                    var nv = new ItemVariant
                    {
                        VariantNumber = NextFreeVariantNumber(item),
                        Quantity = quantity,
                        ThermocoupledQty = tcAdd,
                        Parent = Seg(newParent),
                        Major = Seg(newMajor),
                        Sub = Seg(newSub),
                        Rack = Seg(newRack),
                        Row = Seg(newRow),
                        RegisteredAt = System.DateTime.UtcNow,
                        IsRetired = false
                    };
                    nv.FdaString = FiveSeg(nv.Parent, nv.Major, nv.Sub, nv.Rack, nv.Row);
                    item.Variants.Add(nv);
                    details = $"Added {quantity} unit(s){tcNote} at NEW location {nv.FdaString} (Variant {nv.VariantNumber}).";
                }
                else
                {
                    pv!.Quantity += quantity;
                    // New TC rides on top of whatever TC the stack already held,
                    // never past the stack's new total.
                    pv.ThermocoupledQty = System.Math.Min(pv.ThermocoupledQty + tcAdd, pv.Quantity);
                    details = $"Added {quantity} unit(s){tcNote} to stock (Variant {pv.VariantNumber}, {pv.FdaString}).";
                }
            }
            else if (actionType == "Scrap")
            {
                qtyChange = -quantity;
                int actualScrap = System.Math.Min(quantity, pv!.Quantity);
                // How many scrapped units are TC. Floor: if there aren't enough
                // non-TC units to cover the scrap, the overflow MUST come out of
                // TC (you can't leave more TC than total). Ceiling: the TC on hand.
                int nonTc = pv.Quantity - pv.ThermocoupledQty;
                int forcedTc = System.Math.Max(0, actualScrap - nonTc);
                int tcScrap = System.Math.Min(pv.ThermocoupledQty,
                                System.Math.Max(forcedTc, System.Math.Min(thermocoupledQty, actualScrap)));
                pv.Quantity = System.Math.Max(0, pv.Quantity - quantity);
                pv.ThermocoupledQty = System.Math.Max(0, pv.ThermocoupledQty - tcScrap);
                string tcNote = (isMotor && tcScrap > 0) ? $" ({tcScrap} thermocoupled)" : "";
                details = $"Scrapped {quantity} unit(s){tcNote} (Variant {pv.VariantNumber}, {pv.FdaString}).";
            }
            else if (actionType == "Adjustment")
            {
                qtyChange = quantity;
                pv!.Quantity += quantity;
                // Adjustment never asks for TC, but a downward adjust can push
                // Qty below the existing TC count -- clamp so TC <= Qty holds.
                if (pv.ThermocoupledQty > pv.Quantity)
                    pv.ThermocoupledQty = System.Math.Max(0, pv.Quantity);
                details = $"Manual adjustment of {(quantity >= 0 ? "+" : "")}{quantity} unit(s) (Variant {pv.VariantNumber}).";
            }
            else if (actionType == "Ownership")
            {
                details = $"Moved from {item.Group}/{item.Team} to ";
                if (!string.IsNullOrWhiteSpace(newGroup)) item.Group = newGroup;
                if (!string.IsNullOrWhiteSpace(newTeam))
                {
                    item.Team = newTeam;
                    item.ProjectCode = (newTeam.ToLower() == "ninja") ? "7165" : "7166";
                }
                details += $"{item.Group}/{item.Team}";
            }
            else if (actionType == "Location Transfer")
            {
                // Move a quantity (default: all of it) from the source variant
                // to the destination. Three outcomes:
                //   destination matches ANOTHER active variant -> MERGE into it
                //     (source retires only if fully drained to 0);
                //   whole pile, no collision -> relocate the variant in place
                //     (keeps its number);
                //   partial, no collision -> SPLIT: new variant at destination.
                // Item total never changes (qtyChange stays 0).
                string dP = Seg(newParent), dM = Seg(newMajor), dS = Seg(newSub), dRk = Seg(newRack), dRw = Seg(newRow);
                string destFda = string.Join(".", new[] { dP, dM, dS, dRk, dRw });

                string oldFda = string.IsNullOrWhiteSpace(pv!.FdaString)
                    ? FiveSeg(pv.Parent, pv.Major, pv.Sub, pv.Rack, pv.Row)
                    : pv.FdaString;

                int moveQty = (transferQty.HasValue && transferQty.Value > 0)
                    ? System.Math.Min(transferQty.Value, pv.Quantity)
                    : pv.Quantity;

                // How many of the moved units are TC. Same floor/ceiling logic as
                // scrap: enough TC must move to keep the SOURCE's TC <= its leftover
                // qty, and no more TC than the source actually has.
                int srcNonTc = pv.Quantity - pv.ThermocoupledQty;
                int forcedTcMove = System.Math.Max(0, moveQty - srcNonTc);
                int tcMove = System.Math.Min(pv.ThermocoupledQty,
                                System.Math.Max(forcedTcMove, System.Math.Min(thermocoupledQty, moveQty)));
                // Whole-pile relocate carries ALL the source TC by definition.
                if (moveQty >= pv.Quantity) tcMove = pv.ThermocoupledQty;
                string tcMoveNote = (isMotor && tcMove > 0) ? $" [{tcMove} TC]" : "";

                var mergeTarget = item.ActiveVariants.FirstOrDefault(v => v.Id != pv.Id
                    && Seg(v.Parent) == dP && Seg(v.Major) == dM && Seg(v.Sub) == dS
                    && Seg(v.Rack) == dRk && Seg(v.Row) == dRw);

                if (mergeTarget != null)
                {
                    mergeTarget.Quantity += moveQty;
                    mergeTarget.ThermocoupledQty += tcMove;
                    pv.Quantity -= moveQty;
                    pv.ThermocoupledQty -= tcMove;
                    if (pv.Quantity <= 0)
                    {
                        pv.Quantity = 0;
                        pv.ThermocoupledQty = 0;
                        pv.IsRetired = true;   // fully drained -> retire (kept for history, number freed)
                        details = $"Moved {moveQty}{tcMoveNote} from {oldFda} into Variant {mergeTarget.VariantNumber} ({destFda}); Variant {pv.VariantNumber} fully merged and retired.";
                    }
                    else
                    {
                        details = $"Moved {moveQty}{tcMoveNote} from Variant {pv.VariantNumber} ({oldFda}) into Variant {mergeTarget.VariantNumber} ({destFda}); {pv.Quantity} remain at source.";
                    }
                }
                else if (moveQty >= pv.Quantity)
                {
                    // Whole pile, empty destination: relocate in place (TC rides along untouched).
                    pv.Parent = dP; pv.Major = dM; pv.Sub = dS; pv.Rack = dRk; pv.Row = dRw;
                    pv.FdaString = destFda;
                    details = $"Relocated from {oldFda} to {destFda}";
                }
                else
                {
                    // Partial move to an empty destination: split off a new variant.
                    var nv = new ItemVariant
                    {
                        VariantNumber = NextFreeVariantNumber(item),
                        Quantity = moveQty,
                        ThermocoupledQty = tcMove,
                        Parent = dP, Major = dM, Sub = dS, Rack = dRk, Row = dRw,
                        FdaString = destFda,
                        RegisteredAt = System.DateTime.UtcNow,
                        IsRetired = false
                    };
                    item.Variants.Add(nv);
                    pv.Quantity -= moveQty;
                    pv.ThermocoupledQty -= tcMove;
                    details = $"Split {moveQty}{tcMoveNote} from Variant {pv.VariantNumber} ({oldFda}) to NEW Variant {nv.VariantNumber} ({destFda}); {pv.Quantity} remain at source.";
                }
            }

            item.LastUpdated = System.DateTime.UtcNow;
            item.UpdatedBy = _currentUser.Name;

            // LOG THE TRANSACTION
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = System.DateTime.UtcNow,
                ActionType = actionType,
                ItemId = itemId,
                QuantityChange = qtyChange,
                Details = details,
                User = _currentUser.Name
            });

            _db.SaveChanges();

            return (item, oldQty, item.Quantity);
        }

        public void AddStock(string itemId, int qty)
        {
            if (string.IsNullOrWhiteSpace(itemId) || qty <= 0) return;
            var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (inv != null)
            {
                var pv = inv.PrimaryVariant;
                if (pv == null)
                {
                    // Self-heal (see ModifyStock): never expected post-backfill.
                    pv = new ItemVariant { VariantNumber = 1, Quantity = 0, IsRetired = false };
                    inv.Variants.Add(pv);
                }
                pv.Quantity += qty;
                inv.LastUpdated = System.DateTime.UtcNow;
                inv.UpdatedBy = _currentUser.Name;

                // LOG THE QUICK ADD
                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = System.DateTime.UtcNow,
                    ActionType = "Quick Add",
                    ItemId = itemId,
                    QuantityChange = qty,
                    Details = $"Quick add of {qty} unit(s).",
                    User = _currentUser.Name
                });

                _db.SaveChanges();
            }
        }
    }
}