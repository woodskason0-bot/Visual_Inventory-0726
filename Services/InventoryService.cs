using Visual_Inventory_System.Models;
using Visual_Inventory_System.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace Visual_Inventory_System.Services
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
        // LINE-BASED VISIBILITY
        // ============================
        // Applied to browsing/search surfaces only (GetAll, Search, ExportToCsv)
        // -- NOT to GetById, FindByRheemPart, or the quantity-math helpers,
        // which need to see the true, whole-org picture so lookups, duplicate
        // checks, and Engineer+ cross-Line reassignment keep working.
        private IQueryable<InventoryItem> ApplyLineVisibility(IQueryable<InventoryItem> query)
        {
            // Level 5 sees everything, regardless of Line.
            if (_currentUser.Level >= AccessLevels.Admin) return query;

            var myLine = (_currentUser.Line ?? "").Trim();
            if (myLine.Length > 0)
            {
                var myLineLower = myLine.ToLower();
                // Blank item Line -- also fails open (not yet assigned to any Line).
                return query.Where(i => i.Line == "" || i.Line.ToLower() == myLineLower);
            }

            // No specific Line -- a whole-Branch assignment sees every Line under
            // that Branch (plus still-unassigned items), without going all the
            // way to Admin's full-org bypass. Line above always wins if somehow
            // both are set; the Settings UI keeps them mutually exclusive.
            var myBranch = (_currentUser.Branch ?? "").Trim();
            if (myBranch.Length > 0 && OrgStructure.BranchLines.TryGetValue(myBranch, out var branchLines))
            {
                var branchLinesLower = branchLines.Select(l => l.ToLower()).ToList();
                return query.Where(i => i.Line == "" || branchLinesLower.Contains(i.Line.ToLower()));
            }

            // Not yet assigned a Line or a Branch -- fail OPEN, not closed, so
            // nobody's dashboard looks broken while rollout is still in progress.
            return query;
        }

        /// <summary>
        /// Same visibility rule as browsing (Pass 15), applied to TransactionLogs --
        /// View Logs and the Activity Feed used to show every action for every item
        /// regardless of the viewer's Line, which is a wider audience than can even
        /// see those items in Search. Built on top of ApplyLineVisibility rather than
        /// duplicating the Line/Branch logic: a log row is visible if it isn't about
        /// an item at all (blank ItemId -- Settings/admin actions), if the item it
        /// names has since been deleted (nothing left to check a Line against, same
        /// "keeps reading sensibly" treatment Delete Item already gives these), or if
        /// the item it names is one this viewer can currently see.
        /// </summary>
        public IQueryable<TransactionLog> ApplyLogVisibility(IQueryable<TransactionLog> query)
        {
            if (_currentUser.Level >= AccessLevels.Admin) return query;

            var allItemIds = _db.InventoryItems.AsNoTracking().Select(i => i.ItemId);
            var visibleItemIds = ApplyLineVisibility(_db.InventoryItems.AsNoTracking()).Select(i => i.ItemId);

            return query.Where(log =>
                log.ItemId == "" ||
                !allItemIds.Contains(log.ItemId) ||
                visibleItemIds.Contains(log.ItemId));
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

        // serials: compressors only, one CompressorUnit per non-blank entry.
        // thermocoupledQty: motors only, clamped to Quantity -- sets
        // ItemVariant.ThermocoupledQty and mints that many blank (no lab yet)
        // MotorUnit rows, same shared entrance Intake uses (LogIntakeSerials/
        // LogIntakeThermocoupled) so both forms feed the roster identically.
        public void CreateItem(InventoryItem newItem, List<string>? serials = null, int thermocoupledQty = 0)
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
            int tcQty = System.Math.Max(0, System.Math.Min(thermocoupledQty, qty));
            var variantOne = new ItemVariant
            {
                VariantNumber = 1,
                Quantity = qty,
                ThermocoupledQty = tcQty,
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
                ItemName = newItem.ItemName,
                QuantityChange = qty,
                Details = $"Registered to {newItem.Group}/{newItem.Team} (Line: {(string.IsNullOrEmpty(newItem.Line) ? "unassigned" : newItem.Line)})",
                User = newItem.UpdatedBy
            });

            _db.SaveChanges();   // need variantOne.Id before unit rows can reference it

            var cleanSerials = (serials ?? new List<string>())
                .Select(s => (s ?? "").Trim()).Where(s => s.Length > 0)
                .Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
            if (cleanSerials.Count > 0)
                LogIntakeSerials(newItem.ItemId, variantOne.Id, cleanSerials, newItem.UpdatedBy,
                    System.DateTime.UtcNow, new IntakeResult());
            if (tcQty > 0)
                LogIntakeThermocoupled(newItem.ItemId, variantOne.Id, tcQty, newItem.UpdatedBy,
                    System.DateTime.UtcNow, new IntakeResult());
        }

        // ============================
        // SEARCH AND CORE LOGIC
        // ============================
        // NOTE: the old "name" filter slot is now the Rheem Part # filter
        // (leadership: PN is a primary identifier). Item NAME is still
        // reachable through omni-search.
        public List<InventoryItem> Search(string? omni, string? rheemPart, string? type, string? brand, string? notes)
        {
            // Include variants: results feed the holoviewer, which reads the
            // Quantity/location pass-throughs -- without variants loaded those
            // silently read 0/blank.
            var query = ApplyLineVisibility(_db.InventoryItems.AsNoTracking()
                .Include(i => i.Variants)
                .AsQueryable());

            bool isFilterActive = false;

            if (!string.IsNullOrWhiteSpace(rheemPart))
            {
                query = query.Where(i => i.RheemPartNumber.ToLower().Contains(rheemPart.ToLower()));
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
                    i.RheemPartNumber.ToLower().Contains(s) ||
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
            var query = ApplyLineVisibility(_db.InventoryItems.AsNoTracking()
                .Include(i => i.Variants)
                .AsQueryable());

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
            builder.AppendLine("ItemID,RheemPartNumber,ItemName,Type,Brand,Location,CurrentQuantity,Group,Team,ProjectCode,StatusTags,ScrappedQty,OwnershipChanges");

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
                string safeRpn = i.RheemPartNumber?.Replace(",", ";") ?? "";

                builder.AppendLine($"{i.ItemId},{safeRpn},{safeName},{i.Type},{safeBrand},{i.FdaString},{i.Quantity},{i.Group},{i.Team},{i.ProjectCode},{tagString},{scrapQty},{ownCount}");
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

        /// <summary>
        /// Hard-deletes an item's catalog row once nothing about it is still live.
        /// TransactionLogs (and any already-Returned/Scrapped CompressorUnit/MotorUnit
        /// history rows) are deliberately NOT touched -- same pattern as the soft-hide
        /// on Team/User, they keep reading sensibly under the now-defunct ItemId
        /// string. Re-registering the same model later mints a fresh ItemId; numbers
        /// are never reused.
        /// </summary>
        public (bool ok, string message) DeleteItem(string itemId)
        {
            var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return (false, "Item not found.");

            int totalQty = item.Variants.Where(v => !v.IsRetired).Sum(v => v.Quantity);
            if (totalQty != 0)
                return (false, $"Can't delete -- {itemId} still has {totalQty} unit(s) on hand.");

            bool outOnLoan = _db.CompressorUnits.Any(c => c.ItemId == itemId && c.Status == UnitStatus.PickedUp)
                || _db.MotorUnits.Any(m => m.ItemId == itemId && m.Status == UnitStatus.PickedUp);
            if (outOnLoan)
                return (false, $"Can't delete -- {itemId} has unit(s) still out on loan. Return or scrap them first.");

            bool onPendingOrder = _db.OrderItems.Any(oi => oi.ItemId == itemId && oi.Order.Status == "Pending");
            if (onPendingOrder)
                return (false, $"Can't delete -- {itemId} is on a pending order.");

            string itemName = item.ItemName;

            _db.ItemVariants.RemoveRange(item.Variants);
            _db.InventoryItems.Remove(item);

            // Logged AFTER the item's gone, same as every other action -- this is
            // itself the last entry the audit log will ever see for this ItemId,
            // so the ItemName snapshot above is what makes it (and everything
            // before it) still readable in the feed.
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = System.DateTime.UtcNow,
                ActionType = "Item Deleted",
                ItemId = itemId,
                ItemName = itemName,
                QuantityChange = 0,
                Details = $"{itemName} ({itemId}) deleted -- quantity was 0, nothing outstanding.",
                User = _currentUser.Name
            });

            _db.SaveChanges();

            return (true, $"{itemId} deleted. Its transaction history remains in the audit log.");
        }

        public IEnumerable<InventoryItem> GetAll() =>
            ApplyLineVisibility(_db.InventoryItems.AsNoTracking().Include(i => i.Variants)).ToList();

        public InventoryItem? GetById(string itemId) =>
            _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);

        // Case-insensitive lookup by Rheem PN. Used for the registration
        // duplicate check ("this part already exists as CVE-0042") and the
        // Edit Details uniqueness gate.
        public InventoryItem? FindByRheemPart(string? rheemPartNumber)
        {
            if (string.IsNullOrWhiteSpace(rheemPartNumber)) return null;
            var pn = rheemPartNumber.Trim().ToLower();
            if (pn == "n/a") return null; // shared "no PN yet" sentinel -- never a real owner
            return _db.InventoryItems.AsNoTracking()
                .FirstOrDefault(i => i.RheemPartNumber.ToLower() == pn);
        }

        // ============================
        // IDENTITY EDITS (Edit Details)
        // ============================
        // Family-level identity fields, editable from the Modify Stock modal:
        //   Rheem PN  -- set or change, REJECTED if another item already owns it
        //                ("taken"): PN is a business identifier like ItemId.
        //   Description -- free edit.
        //   Brand     -- fill-if-blank ONLY: an existing brand is never
        //                overwritten from here (matches "add a brand if there
        //                is not one already").
        // Variants are untouched -- identity lives on the family record.
        // Returns (ok, message); logs an "Edit Details" transaction on success.
        public (bool ok, string message) UpdateItemDetails(string itemId, string? rheemPartNumber, string? description, string? brand, string? line = null)
        {
            // Deliberately NOT Line-filtered: an Engineer+ reassigning ownership
            // has to be able to find and act on an item outside their own Line --
            // that's the entire point of reassignment.
            var item = _db.InventoryItems.FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return (false, $"Item {itemId} not found.");

            var changes = new List<string>();

            if (rheemPartNumber != null)
            {
                string pn = rheemPartNumber.Trim();
                if (pn != item.RheemPartNumber)
                {
                    if (pn.Length > 0)
                    {
                        var pnLower = pn.ToLower();
                        if (pnLower != "n/a") // shared sentinel -- never checked for a duplicate owner
                        {
                            var owner = _db.InventoryItems.AsNoTracking()
                                .FirstOrDefault(i => i.ItemId != itemId && i.RheemPartNumber.ToLower() == pnLower);
                            if (owner != null)
                                return (false, $"Rheem part number '{pn}' is already taken by {owner.ItemId} ({owner.ItemName}).");
                        }
                    }
                    changes.Add(string.IsNullOrEmpty(item.RheemPartNumber)
                        ? $"Rheem PN set to '{pn}'"
                        : $"Rheem PN '{item.RheemPartNumber}' -> '{pn}'");
                    item.RheemPartNumber = pn;
                }
            }

            if (description != null && description.Trim() != item.Description)
            {
                changes.Add("Description updated");
                item.Description = description.Trim();
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                if (string.IsNullOrWhiteSpace(item.Brand) || item.Brand.Trim().Equals("Unknown", System.StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add($"Brand set to '{brand.Trim()}'");
                    item.Brand = brand.Trim();
                }
                else if (!brand.Trim().Equals(item.Brand.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    // Existing brand stays; tell the caller instead of silently dropping.
                    return (false, $"{itemId} already has brand '{item.Brand}' -- brand can only be added when blank.");
                }
            }

            if (line != null)
            {
                string newLine = line.Trim();
                if (newLine != item.Line)
                {
                    if (newLine.Length > 0 && !OrgStructure.IsValidLine(newLine))
                        return (false, $"'{newLine}' isn't a recognized Line.");

                    changes.Add(string.IsNullOrEmpty(item.Line)
                        ? $"Line set to '{newLine}'"
                        : $"Line '{item.Line}' -> '{newLine}'");
                    item.Line = newLine;
                }
            }

            if (changes.Count == 0) return (true, "No changes to apply.");

            item.LastUpdated = System.DateTime.UtcNow;
            item.UpdatedBy = _currentUser.Name;

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = System.DateTime.UtcNow,
                ActionType = "Edit Details",
                ItemId = itemId,
                ItemName = item.ItemName,
                QuantityChange = 0,
                Details = string.Join("; ", changes) + ".",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            return (true, string.Join("; ", changes) + ".");
        }

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
        public int SetDefaultThreshold(List<string>? teams, int threshold)
        {
            var q = _db.InventoryItems.AsQueryable();
            if (teams != null && teams.Count > 0)
                q = q.Where(i => teams.Contains(i.Team));

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
        // "Fan Control").
        //
        // CORRECTED Pass 7B: this comment used to claim no item was typed this way
        // and that the rule therefore matched nothing. That is FALSE -- 9 items are
        // Type = "Control" as of the 7/28 audit, so the Control loan path is live
        // and those items loan their full quantity.
        //
        // Still true: other control-ish stock (EEV, VFD, Valve, TXV) is NOT matched,
        // because Type is free text and those aren't spelled "...Control". Widen it
        // here if that changes -- this is the single place the rule lives.
        public static bool IsControlType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().ToLowerInvariant().EndsWith("control");

        // Compressor mini-variant tracking is exact-match, not suffix-match --
        // "Compressor" is the one literal Type value the existing sidebar quick
        // filter already uses (filterType=Compressor), so this stays in lockstep
        // with that rather than inventing a second convention.
        public static bool IsCompressorType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().Equals("Compressor", System.StringComparison.OrdinalIgnoreCase);

        // All CompressorUnit rows, grouped by ItemId, newest first.
        // As of Pass 6A this is a ROSTER, not a pickup log: it holds On Hand
        // stock as well as units that have left. Ordered by RecordedAt because
        // PickedUpAt is now NULL for anything still on a shelf -- ordering by
        // that would sort every on-hand unit into one indistinguishable clump.
        public Dictionary<string, List<CompressorUnit>> GetCompressorUnitsGrouped()
        {
            return _db.CompressorUnits
                .AsNoTracking()
                .OrderByDescending(c => c.RecordedAt)
                .ToList()
                .GroupBy(c => c.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // On-hand units for one family, oldest first -- the pool a pickup draws
        // from and what 6B's Done Using / Return list will render. Serial-less
        // stock has no row here, which is expected: units are a partial overlay
        // over ItemVariant.Quantity, never a replacement for it.
        public List<CompressorUnit> GetOnHandUnits(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return new List<CompressorUnit>();
            return _db.CompressorUnits
                .AsNoTracking()
                .Where(c => c.ItemId == itemId && c.Status == UnitStatus.OnHand)
                .OrderBy(c => c.RecordedAt)
                .ToList();
        }

        public class UnitLogResult
        {
            public List<string> Created { get; } = new();
            public List<string> Updated { get; } = new();
            public List<string> Errors { get; } = new();
            public bool Changed => Created.Count > 0 || Updated.Count > 0;
        }

        /// <summary>
        /// Logs/corrects On Hand compressor units directly from the roster view --
        /// the other entrance to CompressorUnits besides PickUpOrder's match-or-
        /// create. Lets anyone passing a shelf record a serial that was never
        /// picked up (825 units, ~184 have a serial as of the 7/28 intake -- this
        /// is how that gap actually closes over time instead of only at pickup).
        ///
        /// Each row is either an edit to an EXISTING On Hand unit (unitId > 0) or
        /// a NEW one at a given location (unitId == 0, variantId required). A row
        /// with both fields blank is a no-op -- the caller pads the roster with
        /// blank rows up to each variant's Quantity, and most stay blank.
        /// </summary>
        public UnitLogResult LogCompressorUnits(string itemId,
            List<(int UnitId, int? VariantId, string? Serial, string? Lab)> rows, string recordedBy)
        {
            var result = new UnitLogResult();
            var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (inv == null) { result.Errors.Add("Item not found."); return result; }

            var now = System.DateTime.UtcNow;
            foreach (var row in rows)
            {
                string? serial = string.IsNullOrWhiteSpace(row.Serial) ? null : row.Serial.Trim();
                string? lab = string.IsNullOrWhiteSpace(row.Lab) ? null : row.Lab.Trim();

                if (row.UnitId > 0)
                {
                    // Editing an existing On Hand unit's serial/lab.
                    var unit = _db.CompressorUnits.FirstOrDefault(c =>
                        c.Id == row.UnitId && c.ItemId == itemId && c.Status == UnitStatus.OnHand);
                    if (unit == null) continue; // picked up / scrapped between page load and save -- skip quietly
                    if (unit.SerialNumber == serial && unit.LabNumber == lab) continue; // unchanged

                    if (serial != null && _db.CompressorUnits.Any(c =>
                        c.Id != unit.Id && c.ItemId == itemId && c.SerialNumber == serial))
                    {
                        result.Errors.Add($"Serial '{serial}' is already recorded on this model.");
                        continue;
                    }
                    unit.SerialNumber = serial;
                    unit.LabNumber = lab;
                    result.Updated.Add(serial ?? lab ?? $"unit #{unit.Id}");
                }
                else
                {
                    // A blank pad row the human actually filled in.
                    if (serial == null && lab == null) continue;
                    if (!row.VariantId.HasValue)
                    {
                        result.Errors.Add("Missing location for a new unit.");
                        continue;
                    }
                    var variant = inv.Variants.FirstOrDefault(v => v.Id == row.VariantId.Value && !v.IsRetired);
                    if (variant == null)
                    {
                        result.Errors.Add("That location is no longer active.");
                        continue;
                    }
                    if (serial != null && _db.CompressorUnits.Any(c => c.ItemId == itemId && c.SerialNumber == serial))
                    {
                        result.Errors.Add($"Serial '{serial}' is already recorded on this model.");
                        continue;
                    }
                    _db.CompressorUnits.Add(new CompressorUnit
                    {
                        ItemId = itemId,
                        ItemVariantId = variant.Id,
                        SerialNumber = serial,
                        LabNumber = lab,
                        Status = UnitStatus.OnHand,
                        RecordedAt = now,
                        RecordedBy = recordedBy
                    });
                    result.Created.Add(serial ?? lab ?? "(no serial)");
                }
            }

            if (result.Changed)
            {
                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = now,
                    ActionType = "Unit Logged",
                    ItemId = itemId,
                    ItemName = inv.ItemName,
                    QuantityChange = 0,
                    Details = $"{result.Created.Count} new unit(s) logged, {result.Updated.Count} corrected"
                        + (result.Created.Count + result.Updated.Count > 0
                            ? $": {string.Join(", ", result.Created.Concat(result.Updated))}" : "") + ".",
                    User = recordedBy
                });
                _db.SaveChanges();
            }

            return result;
        }

        // All MotorUnit rows, grouped by ItemId. CompressorUnit's sibling --
        // see MotorUnit.cs for why it's a separate table instead of one
        // generalized over both.
        public Dictionary<string, List<MotorUnit>> GetMotorUnitsGrouped()
        {
            return _db.MotorUnits
                .AsNoTracking()
                .OrderByDescending(c => c.RecordedAt)
                .ToList()
                .GroupBy(c => c.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Logs/corrects TC motor units. Unlike LogCompressorUnits, edits reach
        /// BOTH On Hand AND Picked Up (out on loan) rows -- [decided] a motor's
        /// lab number is worth capturing whenever someone notices it, not just
        /// while it happens to be on a shelf, since there's no serial to fall
        /// back on for identity. Scrapped rows are terminal, same as compressors.
        /// New rows (blank pad filled in) are still On-Hand-only -- you can't
        /// invent a unit that's out on loan, only correct one that already
        /// exists because PickUpOrder created it.
        /// </summary>
        public UnitLogResult LogMotorUnits(string itemId,
            List<(int UnitId, int? VariantId, string? Lab)> rows, string recordedBy)
        {
            var result = new UnitLogResult();
            var inv = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (inv == null) { result.Errors.Add("Item not found."); return result; }

            var now = System.DateTime.UtcNow;
            foreach (var row in rows)
            {
                string? lab = string.IsNullOrWhiteSpace(row.Lab) ? null : row.Lab.Trim();

                if (row.UnitId > 0)
                {
                    var unit = _db.MotorUnits.FirstOrDefault(c =>
                        c.Id == row.UnitId && c.ItemId == itemId && c.Status != UnitStatus.Scrapped);
                    if (unit == null) continue; // scrapped between page load and save -- skip quietly
                    if (unit.LabNumber == lab) continue; // unchanged

                    unit.LabNumber = lab;
                    result.Updated.Add(lab ?? $"unit #{unit.Id}");
                }
                else
                {
                    if (lab == null) continue; // blank pad row left blank
                    if (!row.VariantId.HasValue)
                    {
                        result.Errors.Add("Missing location for a new unit.");
                        continue;
                    }
                    var variant = inv.Variants.FirstOrDefault(v => v.Id == row.VariantId.Value && !v.IsRetired);
                    if (variant == null)
                    {
                        result.Errors.Add("That location is no longer active.");
                        continue;
                    }
                    _db.MotorUnits.Add(new MotorUnit
                    {
                        ItemId = itemId,
                        ItemVariantId = variant.Id,
                        LabNumber = lab,
                        Status = UnitStatus.OnHand,
                        RecordedAt = now,
                        RecordedBy = recordedBy
                    });
                    result.Created.Add(lab);
                }
            }

            if (result.Changed)
            {
                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = now,
                    ActionType = "Unit Logged",
                    ItemId = itemId,
                    ItemName = inv.ItemName,
                    QuantityChange = 0,
                    Details = $"{result.Created.Count} new TC motor unit(s) logged, {result.Updated.Count} corrected"
                        + (result.Created.Count + result.Updated.Count > 0
                            ? $": {string.Join(", ", result.Created.Concat(result.Updated))}" : "") + ".",
                    User = recordedBy
                });
                _db.SaveChanges();
            }

            return result;
        }


        // =====================================================================
        // BULK INTAKE (Pass 9)
        // =====================================================================
        // ONE commit path, two entrances: a batch whose location already exists
        // runs this straight from the Intake screen, and a batch that was held
        // pending runs the SAME method once the superuser resolves the location.
        // Anything that diverged between those two would drift apart within a
        // month, which is the whole reason it lives here and not in a controller.

        public class IntakeLine
        {
            public string ItemName { get; set; } = "";
            public string Type { get; set; } = "";
            public string Brand { get; set; } = "";
            public string RheemPartNumber { get; set; } = "N/A";
            public int Quantity { get; set; } = 1;
            // Compressors only. One row, up to Quantity slots -- unlike the old
            // single-serial field, a row can now name every unit it's bringing in.
            public List<string> Serials { get; set; } = new();
            // Motors only. "How many of these are thermocoupled" -- a count, not
            // per-unit identity (motors have no serial to key on). Clamped to
            // Quantity server-side regardless of what the client posts.
            public int ThermocoupledQty { get; set; }
        }

        public class IntakeResult
        {
            public List<string> NewItems { get; set; } = new();       // ItemIds minted
            public List<string> AddedToExisting { get; set; } = new();
            public List<string> Skipped { get; set; } = new();
            public List<string> Errors { get; set; } = new();
            public int UnitsIn { get; set; }
            public int SerialsLogged { get; set; }
            public int TcUnitsLogged { get; set; }
            public bool Ok => Errors.Count == 0;
        }

        /// <summary>
        /// Commit a batch of rows at one location, under one Line/Team.
        ///
        /// preview=true runs every check and reports what WOULD happen without
        /// writing -- the same thing the hand-written 02_verify SQL files did, but
        /// before the fact instead of after.
        ///
        /// Group is derived from the SUBMITTER's Line, not the batch's, so the
        /// ItemId prefix keeps recording who created the record (Pass 7A).
        /// </summary>
        public IntakeResult CommitIntake(
            IEnumerable<IntakeLine> lines, string line, string team,
            string parentCode, string majorCode, string subCode, string rack, string row,
            string submittedBy, string submitterLine, bool preview)
        {
            var res = new IntakeResult();
            string Seg(string? v) => string.IsNullOrWhiteSpace(v) ? "" : v.Trim();
            parentCode = Seg(parentCode); majorCode = Seg(majorCode); subCode = Seg(subCode);
            rack = Seg(rack); row = Seg(row);

            if (parentCode.Length == 0) { res.Errors.Add("No location chosen."); return res; }

            string fda = string.Join(".", new[] { parentCode, majorCode, subCode, rack, row }
                                          .Where(x => x.Length > 0));
            string grp = OrgStructure.GroupFor(submitterLine);
            var now = System.DateTime.UtcNow;
            string ts = now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (var l in lines)
            {
                string name = Seg(l.ItemName);
                if (name.Length == 0) continue;               // blank grid rows are normal
                int qty = l.Quantity < 1 ? 1 : l.Quantity;
                string pn = Seg(l.RheemPartNumber);
                if (pn.Length == 0) pn = "N/A";
                string type = Seg(l.Type);
                var serials = (l.Serials ?? new List<string>())
                    .Select(Seg).Where(s => s.Length > 0).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
                // Clamped here, not just client-side -- never trust the posted count
                // past what's actually coming in.
                int tcQty = System.Math.Max(0, System.Math.Min(l.ThermocoupledQty, qty));

                // A real PN already owned by another family is the one thing that
                // must block -- it means the same physical part is about to exist
                // twice under two ItemIds. "N/A" is the shared sentinel, exempt.
                if (!string.Equals(pn, "N/A", System.StringComparison.OrdinalIgnoreCase))
                {
                    var owner = _db.InventoryItems.AsNoTracking()
                        .FirstOrDefault(i => i.RheemPartNumber.ToLower() == pn.ToLower()
                                          && i.ItemName.ToLower() != name.ToLower());
                    if (owner != null)
                    {
                        res.Errors.Add($"{name}: Rheem PN '{pn}' already belongs to {owner.ItemId} ({owner.ItemName}).");
                        continue;
                    }
                }

                var existing = _db.InventoryItems.Include(i => i.Variants)
                    .FirstOrDefault(i => i.ItemName.ToLower() == name.ToLower());

                if (existing != null)
                {
                    // Known model. Stock lands as a variant at this location --
                    // never a second item, which is the rule the compressor loads
                    // established and the reason 18 models legitimately sit in two
                    // places.
                    var here = existing.Variants.FirstOrDefault(v => !v.IsRetired && v.FdaString == fda);
                    if (here != null)
                    {
                        res.Skipped.Add($"{name}: already has stock at {fda} (V{here.VariantNumber}, qty {here.Quantity}) — use Add Stock to top it up.");
                        continue;
                    }
                    res.AddedToExisting.Add($"{existing.ItemId} {name} +{qty} at {fda}");
                    res.UnitsIn += qty;
                    if (preview)
                    {
                        res.SerialsLogged += serials.Count;
                        res.TcUnitsLogged += tcQty;
                        continue;
                    }

                    var nv = new ItemVariant
                    {
                        VariantNumber = NextFreeVariantNumber(existing),
                        Quantity = qty, ThermocoupledQty = tcQty,
                        Parent = parentCode, Major = majorCode, Sub = subCode, Rack = rack, Row = row,
                        FdaString = fda, RegisteredAt = now, IsRetired = false
                    };
                    existing.Variants.Add(nv);
                    existing.LastUpdated = now; existing.UpdatedBy = submittedBy;
                    _db.TransactionLogs.Add(new TransactionLog
                    {
                        Timestamp = now, ActionType = "Quick Add", ItemId = existing.ItemId,
                        ItemName = existing.ItemName,
                        QuantityChange = qty,
                        Details = $"Intake: {qty} unit(s) at {fda} (Variant {nv.VariantNumber}).",
                        User = submittedBy
                    });
                    _db.SaveChanges();
                    if (serials.Count > 0) LogIntakeSerials(existing.ItemId, nv.Id, serials, submittedBy, now, res);
                    if (tcQty > 0) LogIntakeThermocoupled(existing.ItemId, nv.Id, tcQty, submittedBy, now, res);
                }
                else
                {
                    string itemId = GenerateItemId(grp, type);
                    res.NewItems.Add($"{itemId} {name} x{qty}");
                    res.UnitsIn += qty;
                    if (preview)
                    {
                        res.SerialsLogged += serials.Count;
                        res.TcUnitsLogged += tcQty;
                        continue;
                    }

                    var item = new InventoryItem
                    {
                        ItemId = itemId, ItemName = name, Type = type, Brand = Seg(l.Brand),
                        Description = "", RheemPartNumber = pn, Line = line, Team = team,
                        Group = grp, ProjectCode = team.Length == 0 ? ""
                            : (_db.Teams.FirstOrDefault(t => t.Name == team)?.ProjectCode ?? ""),
                        AlertThreshold = 0, RegisteredAt = now, LastUpdated = now, UpdatedBy = submittedBy
                    };
                    item.Variants.Add(new ItemVariant
                    {
                        VariantNumber = 1, Quantity = qty, ThermocoupledQty = tcQty,
                        Parent = parentCode, Major = majorCode, Sub = subCode, Rack = rack, Row = row,
                        FdaString = fda, RegisteredAt = now, IsRetired = false
                    });
                    _db.InventoryItems.Add(item);
                    _db.TransactionLogs.Add(new TransactionLog
                    {
                        Timestamp = now, ActionType = "New Registry", ItemId = itemId,
                        ItemName = item.ItemName,
                        QuantityChange = qty,
                        Details = $"Registered to {grp}/{(team.Length == 0 ? "no team" : team)} (Line: {(line.Length == 0 ? "unassigned" : line)}) via Intake.",
                        User = submittedBy
                    });
                    _db.SaveChanges();
                    var newVariant = item.Variants.First();
                    if (serials.Count > 0) LogIntakeSerials(itemId, newVariant.Id, serials, submittedBy, now, res);
                    if (tcQty > 0) LogIntakeThermocoupled(itemId, newVariant.Id, tcQty, submittedBy, now, res);
                }
            }
            return res;
        }

        // Serials given at intake are stock ON THE SHELF, so each unit is recorded
        // On Hand -- not Picked Up. A duplicate (item, serial) is skipped rather
        // than thrown: the unique index would reject it anyway and a whole batch
        // failing over one repeated serial helps nobody. Shared by both Intake
        // branches AND New Item Registry (CreateItem) -- one entrance for "a
        // serial was captured at intake/registration time" regardless of which
        // form it came through.
        private void LogIntakeSerials(string itemId, int variantId, List<string> serials, string by, System.DateTime now, IntakeResult res)
        {
            if (!IsCompressorType(_db.InventoryItems.AsNoTracking()
                    .FirstOrDefault(i => i.ItemId == itemId)?.Type)) return;

            foreach (var serial in serials)
            {
                bool dupe = _db.CompressorUnits.Any(c => c.ItemId == itemId
                    && c.SerialNumber != null && c.SerialNumber.ToLower() == serial.ToLower());
                if (dupe) { res.Skipped.Add($"{itemId}: serial '{serial}' already on record."); continue; }

                _db.CompressorUnits.Add(new CompressorUnit
                {
                    ItemId = itemId, ItemVariantId = variantId, SerialNumber = serial,
                    Status = UnitStatus.OnHand, RecordedAt = now, RecordedBy = by
                });
                res.SerialsLogged++;
            }
            _db.SaveChanges();
        }

        // "How many of these are thermocoupled" at intake/registration time --
        // motors have no serial to key on, so this is a COUNT, not per-unit
        // identity (unlike compressors above). ItemVariant.ThermocoupledQty is
        // already set by the caller before this runs; this only mints the
        // matching blank (no lab yet) MotorUnit roster rows, same shape
        // PickUpOrder already creates for untracked TC stock leaving the shelf --
        // so the lab number has real rows to attach to later, via the existing
        // Log Units entrance, rather than being asked for here.
        private void LogIntakeThermocoupled(string itemId, int variantId, int count, string by, System.DateTime now, IntakeResult res)
        {
            if (!IsMotorType(_db.InventoryItems.AsNoTracking()
                    .FirstOrDefault(i => i.ItemId == itemId)?.Type) || count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                _db.MotorUnits.Add(new MotorUnit
                {
                    ItemId = itemId, ItemVariantId = variantId, LabNumber = null,
                    Status = UnitStatus.OnHand, RecordedAt = now, RecordedBy = by
                });
            }
            _db.SaveChanges();
            res.TcUnitsLogged += count;
        }

        // How many units of an order line are LOANABLE (library books, expected
        // back): a Control loans its whole quantity; a Motor loans only its TC
        // subset; everything else loans nothing. Drives LoanOutstanding at pickup.
        public static int LoanableQuantity(string? type, int quantity, int thermocoupledCount)
        {
            if (IsControlType(type)) return quantity;
            if (IsMotorType(type)) return System.Math.Min(thermocoupledCount, quantity);

            // Pass 6B: compressors count too, but the meaning differs. A Control
            // or Motor is a library book -- LoanOutstanding means "expected back".
            // A compressor is normally CONSUMED; here the same counter means
            // "not yet dispositioned", i.e. nobody has pressed Done Using on it.
            // Deliberately reusing the field rather than adding a parallel
            // PendingDisposition column: one counter that cannot drift beats two
            // that have to be kept in lockstep across PickUpOrder / ReturnLoan /
            // ScrapLoan. The My Orders UI carries the wording for users.
            if (IsCompressorType(type)) return quantity;

            return 0;
        }

        public (InventoryItem item, int oldQty, int newQty)? ModifyStock(string itemId, string actionType, int quantity, string? newGroup, string? newTeam,
            string? newParent = null, string? newMajor = null, string? newSub = null, string? newRack = null, string? newRow = null,
            string? targetVariant = null, int? transferQty = null, int thermocoupledQty = 0,
            string? newLine = null)
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
                // A scrap past what's on the shelf clamps to what's actually there;
                // the log (and ExportToCsv's ScrappedQty, which sums these) must
                // record the clamped amount, not the request.
                int actualScrap = System.Math.Min(quantity, pv!.Quantity);
                qtyChange = -actualScrap;
                // How many scrapped units are TC. Floor: if there aren't enough
                // non-TC units to cover the scrap, the overflow MUST come out of
                // TC (you can't leave more TC than total). Ceiling: the TC on hand.
                int nonTc = pv.Quantity - pv.ThermocoupledQty;
                int forcedTc = System.Math.Max(0, actualScrap - nonTc);
                int tcScrap = System.Math.Min(pv.ThermocoupledQty,
                                System.Math.Max(forcedTc, System.Math.Min(thermocoupledQty, actualScrap)));
                pv.Quantity -= actualScrap;
                pv.ThermocoupledQty = System.Math.Max(0, pv.ThermocoupledQty - tcScrap);
                string tcNote = (isMotor && tcScrap > 0) ? $" ({tcScrap} thermocoupled)" : "";
                string clampNote = actualScrap < quantity
                    ? $" ({quantity} requested, only {actualScrap} were on hand)" : "";
                details = $"Scrapped {actualScrap} unit(s){tcNote}{clampNote} (Variant {pv.VariantNumber}, {pv.FdaString}).";
            }
            else if (actionType == "Adjustment")
            {
                qtyChange = quantity;
                pv!.Quantity += quantity;
                // [decided] Adjustment can also RECLASSIFY existing on-hand stock
                // as thermocoupled without changing the total count -- "take my
                // existing N, mark X of those as TC" is a different action from
                // Add's "these NEW units include X that are TC". thermocoupledQty
                // here is a delta onto whatever's already on the shelf, not tied
                // to `quantity` at all (quantity can be 0 -- a pure reclassify).
                if (isMotor && thermocoupledQty > 0)
                    pv.ThermocoupledQty = System.Math.Min(pv.ThermocoupledQty + thermocoupledQty, pv.Quantity);
                // A downward adjust can still push Qty below the TC count --
                // clamp so TC <= Qty holds regardless of which direction got it there.
                if (pv.ThermocoupledQty > pv.Quantity)
                    pv.ThermocoupledQty = System.Math.Max(0, pv.Quantity);
                string tcNote = (isMotor && thermocoupledQty > 0)
                    ? $"; {thermocoupledQty} of the existing stock reclassified as thermocoupled" : "";
                details = $"Manual adjustment of {(quantity >= 0 ? "+" : "")}{quantity} unit(s) (Variant {pv.VariantNumber}){tcNote}.";
            }
            else if (actionType == "Ownership")
            {
                // Pass 7B: ownership means LINE now, not Group.
                //
                // The old "New Group" picker offered Commercial / Residential /
                // International -- two BRANCHES and one LINE, flattened as peers.
                // Group is also derived from the registering user's Line and frozen
                // at creation (it mints the ItemId prefix), so a transfer must not
                // move it or an item's Group would contradict its own id.
                //
                // newGroup is still accepted by the signature for call compatibility
                // and is deliberately IGNORED.
                string oldLine = string.IsNullOrWhiteSpace(item.Line) ? "unassigned" : item.Line;
                string oldTeam = string.IsNullOrWhiteSpace(item.Team) ? "no team" : item.Team;

                if (newLine != null)
                {
                    string ln = newLine.Trim();
                    // Blank is legal -- it means unassigned, which fails OPEN
                    // (visible to everyone), the Pass 3 rollout default.
                    // An unrecognized Line used to return the success-shaped tuple
                    // with nothing changed/logged/saved, so the controller toasted
                    // "applied" over a no-op. Throw instead -- the controller's
                    // catch turns this into a visible error toast.
                    if (ln.Length > 0 && !OrgStructure.IsValidLine(ln))
                        throw new System.InvalidOperationException(
                            $"'{ln}' isn't a recognized Line — the item was left unchanged.");
                    item.Line = ln;
                }
                if (newTeam != null)
                {
                    // Pass 7A: was
                    //   ProjectCode = newTeam.ToLower() == "ninja" ? "7165" : "7166";
                    // -- which handed every team that wasn't Ninja Samurai's project
                    // code, silently, forever. Now the code travels with the team row.
                    //
                    // newTeam == "" is a real choice (team is optional), so this
                    // tests for null rather than blank.
                    string t = newTeam.Trim();
                    item.Team = t;
                    item.ProjectCode = t.Length == 0
                        ? ""
                        : (_db.Teams.FirstOrDefault(x => x.Name == t)?.ProjectCode ?? "");
                }
                string nowLine = string.IsNullOrWhiteSpace(item.Line) ? "unassigned" : item.Line;
                string nowTeam = string.IsNullOrWhiteSpace(item.Team) ? "no team" : item.Team;
                details = $"Moved from '{oldLine}' ({oldTeam}) to '{nowLine}' ({nowTeam}).";
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
                ItemName = item.ItemName,
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
                    ItemName = inv.ItemName,
                    QuantityChange = qty,
                    Details = $"Quick add of {qty} unit(s).",
                    User = _currentUser.Name
                });

                _db.SaveChanges();
            }
        }
    }
}