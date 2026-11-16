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

        // Line visibility. Browsing only (GetAll, ExportToCsv).
        // GetById, FindByRheemPart and the quantity math see the whole org.
        private IQueryable<InventoryItem> ApplyLineVisibility(IQueryable<InventoryItem> query)
        {
            // Admin sees everything.
            if (_currentUser.Level >= AccessLevels.Admin) return query;

            var myLine = (_currentUser.Line ?? "").Trim();
            if (myLine.Length > 0)
            {
                var myLineLower = myLine.ToLower();
                // A blank item Line fails open.
                return query.Where(i => i.Line == "" || i.Line.ToLower() == myLineLower);
            }

            // No Line but a Branch: every Line under it, plus unassigned items.
            var myBranch = (_currentUser.Branch ?? "").Trim();
            if (myBranch.Length > 0 && OrgStructure.BranchLines.TryGetValue(myBranch, out var branchLines))
            {
                var branchLinesLower = branchLines.Select(l => l.ToLower()).ToList();
                return query.Where(i => i.Line == "" || branchLinesLower.Contains(i.Line.ToLower()));
            }

            // No Line and no Branch: fail open.
            return query;
        }

        /// <summary>ApplyLineVisibility's rule for one loaded item. Kept separate: that one must stay SQL-translatable.</summary>
        public bool IsOwnLine(string? itemLine)
        {
            if (_currentUser.Level >= AccessLevels.Admin) return true;

            var line = (itemLine ?? "").Trim();
            var myLine = (_currentUser.Line ?? "").Trim();
            if (myLine.Length > 0)
                return line.Length == 0 || string.Equals(line, myLine, System.StringComparison.OrdinalIgnoreCase);

            var myBranch = (_currentUser.Branch ?? "").Trim();
            if (myBranch.Length > 0 && OrgStructure.BranchLines.TryGetValue(myBranch, out var branchLines))
                return line.Length == 0 || branchLines.Any(l => string.Equals(l, line, System.StringComparison.OrdinalIgnoreCase));

            // No Line and no Branch: fail open.
            return true;
        }

        /// <summary>The teams this user is on.</summary>
        public List<string> GetUserTeamNames(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return new List<string>();
            return (from u in _db.Users.AsNoTracking()
                    join ut in _db.UserTeams.AsNoTracking() on u.Id equals ut.UserId
                    where u.UserName == userName
                    select ut.TeamName).ToList();
        }

        /// <summary>Teams with a claim on this item: non-blank Team across its active variants. One or none = not split.</summary>
        private static List<string> ActiveVariantTeams(InventoryItem item) =>
            item.ActiveVariants
                .Select(v => (v.Team ?? "").Trim())
                .Where(t => t.Length > 0)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// The team a transfer request is aimed at, or "" = no team boundary (fails open).
        /// The single definition: approval rights, the pull loop and the serial picklists all use it.
        /// </summary>
        public string ResolveTransferTeam(InventoryItem item, int? requestedVariantId)
        {
            if (ActiveVariantTeams(item).Count <= 1) return "";
            if (!requestedVariantId.HasValue) return "";
            var target = item.ActiveVariants
                .FirstOrDefault(v => v.Id == requestedVariantId.Value)?.Team;
            return string.IsNullOrWhiteSpace(target) ? "" : target.Trim();
        }

        /// <summary>
        /// Can this user approve or deny a transfer against this item? Own Line, and
        /// once the item is split across teams, membership of the target team too.
        /// </summary>
        public bool CanApproveTransfer(InventoryItem item, int? requestedVariantId)
        {
            if (_currentUser.Level >= AccessLevels.Admin) return true;
            if (!IsOwnLine(item.Line)) return false;

            string targetTeam = ResolveTransferTeam(item, requestedVariantId);
            if (targetTeam.Length == 0) return true;

            var myTeams = GetUserTeamNames(_currentUser.Name);
            return myTeams.Any(t => string.Equals(t, targetTeam, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The team an order line resolves against. Ambiguous = more than one of the requester's
        /// own teams has a claim and none was picked. Does not decide what to do about it.
        /// </summary>
        public (string Team, bool Ambiguous, List<string> Applicable) ResolveOrderingTeam(
            InventoryItem item, string? requestedTeam)
        {
            var variantTeams = ActiveVariantTeams(item);
            if (variantTeams.Count <= 1)
                return (variantTeams.FirstOrDefault() ?? "", false, variantTeams);

            var myTeams = GetUserTeamNames(_currentUser.Name);
            var applicable = variantTeams
                .Where(vt => myTeams.Any(mt => string.Equals(mt, vt, System.StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (applicable.Count == 0) return ("", false, applicable);              // none of my teams claim this item -- fails open
            if (applicable.Count == 1) return (applicable[0], false, applicable);   // exactly one applies -- no need to ask

            if (!string.IsNullOrWhiteSpace(requestedTeam)
                && applicable.Any(t => string.Equals(t, requestedTeam, System.StringComparison.OrdinalIgnoreCase)))
                return (requestedTeam!.Trim(), false, applicable);

            return ("", true, applicable);
        }

        /// <summary>What this viewer could order now. Falls back to the item total when no single team applies.</summary>
        public int GetAvailableForViewer(InventoryItem item)
        {
            var (team, _, _) = ResolveOrderingTeam(item, null);
            return GetAvailableQuantity(item.ItemId, team);
        }

        /// <summary>
        /// Line visibility for logs. A row shows if it names no item, a deleted item,
        /// or an item this viewer can see.
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

        // New item registry
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

        // Item id: <GroupInitial><Type first+last>-NNNN, e.g. Commercial + Valve -> "CVE-0001".
        // Numbered per prefix from the highest ever used, not the highest on a live item:
        // a deleted item's id still owns its logs, orders and unit rows.
        public string GenerateItemId(string? group, string? type)
        {
            string prefix = BuildPrefix(group, type);   // includes trailing '-'

            int max = 0;
            var matches = _db.InventoryItems.AsNoTracking()
                .Where(i => i.ItemId.StartsWith(prefix)).Select(i => i.ItemId).ToList();
            matches.AddRange(_db.TransactionLogs.AsNoTracking()
                .Where(t => t.ItemId.StartsWith(prefix)).Select(t => t.ItemId).Distinct().ToList());
            matches.AddRange(_db.OrderItems.AsNoTracking()
                .Where(o => o.ItemId.StartsWith(prefix)).Select(o => o.ItemId).Distinct().ToList());
            matches.AddRange(_db.CompressorUnits.AsNoTracking()
                .Where(c => c.ItemId.StartsWith(prefix)).Select(c => c.ItemId).Distinct().ToList());
            matches.AddRange(_db.MotorUnits.AsNoTracking()
                .Where(m => m.ItemId.StartsWith(prefix)).Select(m => m.ItemId).Distinct().ToList());
            matches.AddRange(_db.TransferRequests.AsNoTracking()
                .Where(t => t.ItemId.StartsWith(prefix)).Select(t => t.ItemId).Distinct().ToList());
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

        // Group initial + the type's first and last letter, uppercased, plus '-'.
        private static string BuildPrefix(string? group, string? type)
        {
            string g = string.IsNullOrWhiteSpace(group) ? "Commercial" : group.Trim();
            string t = string.IsNullOrWhiteSpace(type) ? "General" : type.Trim();

            char gi = char.ToUpperInvariant(g[0]);
            char tFirst = char.ToUpperInvariant(t[0]);
            char tLast = char.ToUpperInvariant(t[t.Length - 1]);
            return $"{gi}{tFirst}{tLast}-";
        }

        // serials: compressors only, one CompressorUnit each.
        // thermocoupledQty: motors only, clamped to Quantity, one blank MotorUnit each.
        public void CreateItem(InventoryItem newItem, List<string>? serials = null, int thermocoupledQty = 0)
        {
            // The whole item is model-bound, so reset what the form never posts.
            newItem.Id = 0;
            newItem.Variants = new List<ItemVariant>();
            newItem.RegisteredAt = null;
            newItem.AlertThreshold = 0;

            // form.submit() skips the form's min="0", so this is the only check.
            if (newItem.Quantity < 0)
                throw new System.InvalidOperationException(
                    "Quantity can't be negative -- nothing was registered.");

            newItem.LastUpdated = System.DateTime.UtcNow;
            newItem.UpdatedBy = _currentUser.Name;
            // Set once, never changed.
            if (newItem.RegisteredAt == null)
                newItem.RegisteredAt = System.DateTime.UtcNow;

            // Read the staged values before adding the variant: afterwards the getters read the variant.
            int qty = newItem.Quantity;
            int tcQty = System.Math.Max(0, System.Math.Min(thermocoupledQty, qty));
            var variantOne = new ItemVariant
            {
                VariantNumber = 1,
                Quantity = qty,
                ThermocoupledQty = tcQty,
                // Inherits the family's Team. Left blank, the stack is invisible to team-scoped ordering.
                Team = newItem.Team ?? "",
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

        // Search
        /// <summary>
        /// Search Center's query. Filters win over omni text. Location filters read the
        /// Parent/Major/Sub columns, never FdaString: its shape is not consistent in real data.
        /// </summary>
        public List<InventoryItem> Search(string? omni, string? rheemPart, string? type, string? brand,
                                          string? branch = null,
                                          string? locParent = null, string? locMajor = null, string? locSub = null)
        {
            // Variants are loaded because the Quantity and location pass-throughs read them.
            // No Line filter, on purpose: Search is where another Line's items show up for a transfer request.
            var query = _db.InventoryItems.AsNoTracking()
                .Include(i => i.Variants)
                .AsQueryable();

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
            if (!string.IsNullOrWhiteSpace(locParent))
            {
                // One Any() for all three levels, so Parent and Major must match on the same variant.
                string p = locParent.Trim().ToLower();
                string m = (locMajor ?? "").Trim().ToLower();
                string s2 = (locSub ?? "").Trim().ToLower();
                query = query.Where(i => i.Variants.Any(v => !v.IsRetired
                    && v.Parent.ToLower() == p
                    && (m == "" || v.Major.ToLower() == m)
                    && (s2 == "" || v.Sub.ToLower() == s2)));
                isFilterActive = true;
            }
            if (!string.IsNullOrWhiteSpace(branch) && OrgStructure.BranchLines.TryGetValue(branch, out var branchLines))
            {
                // Branch is not a column. Match on the branch's Lines.
                query = query.Where(i => branchLines.Contains(i.Line));
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
                    i.Description.ToLower().Contains(s) ||
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

        /// <summary>The Export wizard's CSV. Location filters match the Parent/Major/Sub/Rack/Row columns, not FdaString.</summary>
        public byte[] ExportToCsv(string? group, string? team, string? type, string? brand,
                                  string? locParent, string? locMajor, string? locSub,
                                  string? rack, string? row,
                                  bool expAvailable, bool expAlerts, bool expScrap, bool expOwnership, string expTimeFrame)
        {
            var query = ApplyLineVisibility(_db.InventoryItems.AsNoTracking()
                .Include(i => i.Variants)
                .AsQueryable());

            // Top half of the wizard: every filter must match.
            if (!string.IsNullOrWhiteSpace(group)) query = query.Where(i => i.Group.ToLower() == group.ToLower());
            // Team matches the family's Team or any active variant's Team, so a slice owner is found too.
            if (!string.IsNullOrWhiteSpace(team))
                query = query.Where(i => i.Team.ToLower() == team.ToLower()
                    || i.Variants.Any(v => !v.IsRetired && v.Team.ToLower() == team.ToLower()));
            if (!string.IsNullOrWhiteSpace(type)) query = query.Where(i => i.Type.ToLower().Contains(type.ToLower()));
            if (!string.IsNullOrWhiteSpace(brand)) query = query.Where(i => i.Brand.ToLower().Contains(brand.ToLower()));

            // One Any() for every level, as in Search. Each level is optional.
            string lp = (locParent ?? "").Trim().ToLower();
            string lm = (locMajor ?? "").Trim().ToLower();
            string ls = (locSub ?? "").Trim().ToLower();
            string lrk = (rack ?? "").Trim().ToLower();
            string lrw = (row ?? "").Trim().ToLower();
            if (lp.Length > 0 || lm.Length > 0 || ls.Length > 0 || lrk.Length > 0 || lrw.Length > 0)
            {
                query = query.Where(i => i.Variants.Any(v => !v.IsRetired
                    && (lp == "" || v.Parent.ToLower() == lp)
                    && (lm == "" || v.Major.ToLower() == lm)
                    && (ls == "" || v.Sub.ToLower() == ls)
                    && (lrk == "" || v.Rack.ToLower() == lrk)
                    && (lrw == "" || v.Row.ToLower() == lrw)));
            }

            var items = query.ToList();
            var itemIds = items.Select(i => i.ItemId).ToList();

            // History, inside the time frame if one was picked.
            DateTime? since = null;
            if (expTimeFrame == "7") since = DateTime.UtcNow.AddDays(-7);
            else if (expTimeFrame == "30") since = DateTime.UtcNow.AddDays(-30);
            else if (expTimeFrame == "90") since = DateTime.UtcNow.AddDays(-90);

            var logsQuery = _db.TransactionLogs.AsNoTracking().Where(t => itemIds.Contains(t.ItemId));
            if (since.HasValue) logsQuery = logsQuery.Where(t => t.Timestamp >= since.Value);

            var allLogs = logsQuery.ToList();

            var builder = new StringBuilder();

            builder.AppendLine("ItemID,RheemPartNumber,ItemName,Type,Brand,Location,CurrentQuantity,Group,Line,Team,ProjectCode,StatusTags,ScrappedQty,OwnershipChanges");

            foreach (var i in items)
            {
                var myLogs = allLogs.Where(t => t.ItemId == i.ItemId).ToList();

                int scrapQty = myLogs.Where(t => t.ActionType == "Scrap").Sum(t => System.Math.Abs(t.QuantityChange));
                int ownCount = myLogs.Count(t => t.ActionType == "Ownership");

                bool isAvailable = i.Quantity > 0;
                bool isAlert = i.AlertThreshold > 0 && i.Quantity <= i.AlertThreshold;
                bool isScrap = scrapQty > 0;
                bool isOwn = ownCount > 0;

                // Bottom half of the wizard: any ticked box qualifies.
                bool include = false;
                if (expAvailable && isAvailable) include = true;
                if (expAlerts && isAlert) include = true;
                if (expScrap && isScrap) include = true;
                if (expOwnership && isOwn) include = true;

                if (!include) continue;

                var tags = new List<string>();
                if (isAvailable) tags.Add("Available");
                if (isAlert) tags.Add("Low Stock Alert");
                if (isScrap) tags.Add("Scrapped");
                if (isOwn) tags.Add("Ownership Changed");
                string tagString = string.Join(" | ", tags);

                // Commas would break the CSV.
                string safeName = i.ItemName?.Replace(",", ";") ?? "";
                string safeBrand = i.Brand?.Replace(",", ";") ?? "";
                string safeRpn = i.RheemPartNumber?.Replace(",", ";") ?? "";
                string safeLine = string.IsNullOrWhiteSpace(i.Line) ? "" : i.Line.Replace(",", ";");

                // Every team with a claim on an active variant. Unsplit items fall back to the family's Team.
                var activeTeams = i.ActiveVariants.Select(v => v.Team).Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string safeTeam = (activeTeams.Count > 0 ? string.Join("; ", activeTeams) : (i.Team ?? "")).Replace(",", ";");

                builder.AppendLine($"{i.ItemId},{safeRpn},{safeName},{i.Type},{safeBrand},{i.FdaString},{i.Quantity},{i.Group},{safeLine},{safeTeam},{i.ProjectCode},{tagString},{scrapQty},{ownCount}");
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>
        /// What the signed-in person could order of this item now, across every team's stock.
        /// All counts go through StockClaims.
        /// </summary>
        public int GetAvailableQuantity(string itemId) => GetAvailableQuantity(itemId, null);

        /// <summary>The same, for a line resolved to `team` ("" = unscoped).</summary>
        public int GetAvailableQuantity(string itemId, string? team) =>
            GetAvailableQuantity(itemId, team, _currentUser.Name);

        /// <summary>The same, for a named person. A transfer is measured for its requester, not the approver.</summary>
        public int GetAvailableQuantity(string itemId, string? team, string? forUser)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            return StockClaims.Load(_db, itemId).AvailableQty(forUser ?? "", (team ?? "").Trim());
        }

        /// <summary>The thermocoupled part of the same question.</summary>
        public int GetAvailableTc(string itemId, string? team, string? forUser = null)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            return StockClaims.Load(_db, itemId).AvailableTc(forUser ?? _currentUser.Name, (team ?? "").Trim());
        }

        /// <summary>
        /// Pickup Queue's "can this line be filled". Only holds and orders made before
        /// this order compete with it.
        /// </summary>
        public int GetAvailableForOrder(string itemId, System.DateTime orderCreatedAt, string? team, string? requestedBy, int excludeOrderItemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            var all = StockClaims.Load(_db, itemId);
            var earlier = new StockClaims(itemId, all.Stacks,
                all.Holds.Where(h => h.CreatedAt < orderCreatedAt).ToList(),
                all.Lines.Where(l => l.CreatedAt < orderCreatedAt && l.OrderItemId != excludeOrderItemId).ToList());
            return earlier.AvailableQty(requestedBy ?? "", (team ?? "").Trim());
        }

        /// <summary>The same line measured against every other claim, earlier or not.</summary>
        public int GetAvailableIgnoringPriority(string itemId, string? team, string? requestedBy, int excludeOrderItemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            var all = StockClaims.Load(_db, itemId);
            var others = new StockClaims(itemId, all.Stacks, all.Holds,
                all.Lines.Where(l => l.OrderItemId != excludeOrderItemId).ToList());
            return others.AvailableQty(requestedBy ?? "", (team ?? "").Trim());
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
        /// Hard-deletes an item once nothing about it is live. Logs and old unit rows stay
        /// under the old ItemId. Numbers are never reused.
        /// </summary>
        public (bool ok, string message) DeleteItem(string itemId)
        {
            var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return (false, "Item not found.");

            int totalQty = item.Variants.Where(v => !v.IsRetired).Sum(v => v.Quantity);
            if (totalQty != 0)
                return (false, $"Can't delete -- {itemId} still has {totalQty} unit(s) on hand.");

            // LoanOutstanding is the authority. Controls have no unit rows, so the next check cannot see them.
            bool loanOutstanding = _db.OrderItems.Any(oi => oi.ItemId == itemId && oi.LoanOutstanding > 0);
            if (loanOutstanding)
                return (false, $"Can't delete -- {itemId} still has a loan out. Return or scrap it first.");

            bool outOnLoan = _db.CompressorUnits.Any(c => c.ItemId == itemId && c.Status == UnitStatus.PickedUp)
                || _db.MotorUnits.Any(m => m.ItemId == itemId && m.Status == UnitStatus.PickedUp);
            if (outOnLoan)
                return (false, $"Can't delete -- {itemId} has unit(s) still out on loan. Return or scrap them first.");

            // Line status too: a Split line has already pulled its units.
            bool onPendingOrder = _db.OrderItems.Any(oi => oi.ItemId == itemId && oi.Order.Status == "Pending" && oi.Status == "Pending");
            if (onPendingOrder)
                return (false, $"Can't delete -- {itemId} is on a pending order.");

            // An undecided transfer request is open work against the item, like a Pending order.
            bool onOpenTransfer = _db.TransferRequests.Any(t => t.ItemId == itemId && t.Status == TransferStatus.Requested);
            if (onOpenTransfer)
                return (false, $"Can't delete -- {itemId} has an open transfer request. Approve or deny it first.");

            // So is a live hold.
            bool reserved = ReservationService.Live(_db.Reservations.AsNoTracking(), System.DateTime.Today).Any(r => r.ItemId == itemId);
            if (reserved)
                return (false, $"Can't delete -- {itemId} has a live reservation. Release it first (Reserved Items).");

            string itemName = item.ItemName;

            _db.ItemVariants.RemoveRange(item.Variants);
            _db.InventoryItems.Remove(item);

            // The last log this ItemId will ever get. The ItemName snapshot keeps it readable.
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

        /// <summary>
        /// Hard-deletes one empty stack and leaves the rest of the item alone.
        /// Delete Item cannot do this: it needs the item's total to be 0.
        /// </summary>
        public (bool ok, string message) DeleteVariant(string itemId, int variantId)
        {
            var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return (false, "Item not found.");

            var variant = item.Variants.FirstOrDefault(v => v.Id == variantId && !v.IsRetired);
            if (variant == null) return (false, "That stack no longer exists.");

            if (variant.Quantity != 0)
                return (false, $"Can't delete -- Variant {variant.VariantNumber} still has {variant.Quantity} unit(s) on hand.");

            int activeCount = item.Variants.Count(v => !v.IsRetired);
            if (activeCount <= 1)
                return (false, $"Variant {variant.VariantNumber} is {itemId}'s only stack -- use Delete Item instead.");

            bool onHandUnits = _db.CompressorUnits.Any(c => c.ItemVariantId == variant.Id && c.Status == UnitStatus.OnHand)
                || _db.MotorUnits.Any(m => m.ItemVariantId == variant.Id && m.Status == UnitStatus.OnHand);
            if (onHandUnits)
                return (false, $"Can't delete -- Variant {variant.VariantNumber} still has a recorded unit on it. Log/move it first.");

            bool onPendingOrder = _db.OrderItems.Any(oi => oi.RequestedVariantId == variant.Id && oi.Order.Status == "Pending" && oi.Status == "Pending");
            if (onPendingOrder)
                return (false, $"Can't delete -- Variant {variant.VariantNumber} is requested on a pending order.");

            // At quantity 0 a live hold here holds nothing, but it would be left pointing nowhere.
            bool held = (from l in _db.ReservationLines.AsNoTracking()
                         join r in ReservationService.Live(_db.Reservations.AsNoTracking(), System.DateTime.Today) on l.ReservationId equals r.Id
                         where l.ItemVariantId == variant.Id && l.Quantity > 0
                         select l.Id).Any();
            if (held)
                return (false, $"Can't delete -- Variant {variant.VariantNumber} has a live reservation on it. Release it first (Reserved Items).");

            // Trap: ResolveTransferTeam reads the requested variant. Delete it and an open
            // team-gated request resolves to "", which any Engineer on the Line can approve.
            bool onOpenTransfer = _db.TransferRequests.Any(t => t.ItemId == itemId
                && t.RequestedVariantId == variant.Id && t.Status == TransferStatus.Requested);
            if (onOpenTransfer)
                return (false, $"Can't delete -- Variant {variant.VariantNumber} is the requested pull location on an open transfer request. Approve or deny it first.");

            string fda = variant.FdaString;
            int number = variant.VariantNumber;

            _db.ItemVariants.Remove(variant);

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = System.DateTime.UtcNow,
                ActionType = "Stack Deleted",
                ItemId = itemId,
                ItemName = item.ItemName,
                QuantityChange = 0,
                Details = $"Variant {number} ({fda}) deleted -- quantity was 0, nothing outstanding there.",
                User = _currentUser.Name
            });

            _db.SaveChanges();

            return (true, $"Variant {number} deleted from {itemId}.");
        }

        public IEnumerable<InventoryItem> GetAll() =>
            ApplyLineVisibility(_db.InventoryItems.AsNoTracking().Include(i => i.Variants)).ToList();

        public InventoryItem? GetById(string itemId) =>
            _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);

        // Case-insensitive lookup by Rheem PN, for the duplicate checks. "N/A" never matches.
        public InventoryItem? FindByRheemPart(string? rheemPartNumber)
        {
            if (string.IsNullOrWhiteSpace(rheemPartNumber)) return null;
            var pn = rheemPartNumber.Trim().ToLower();
            if (pn == "n/a") return null; // shared "no PN yet" sentinel -- never a real owner
            return _db.InventoryItems.AsNoTracking()
                .FirstOrDefault(i => i.RheemPartNumber.ToLower() == pn);
        }

        // Edit Details: family-level identity. Rheem PN is rejected if another item owns it.
        // Brand is fill-if-blank only. Description is a free edit. Variants are untouched.
        public (bool ok, string message) UpdateItemDetails(string itemId, string? rheemPartNumber, string? description, string? brand, string? line = null)
        {
            // Not Line-filtered: reassigning ownership means acting on an item outside your own Line.
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
                int oldThreshold = item.AlertThreshold;
                item.AlertThreshold = threshold;

                // Logged only on a real change: Save fires even when the modal was opened just to read.
                if (oldThreshold != threshold)
                {
                    _db.TransactionLogs.Add(new TransactionLog
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionType = "Alert Threshold Changed",
                        ItemId = item.ItemId,
                        ItemName = item.ItemName,
                        QuantityChange = 0,
                        Details = $"Alert threshold: {oldThreshold} -> {threshold}.",
                        User = _currentUser.Name
                    });
                }

                _db.SaveChanges();
            }
        }

        // Bulk-set the alert threshold. With teams, only their items; without, every item. Returns the count.
        public int SetDefaultThreshold(List<string>? teams, int threshold)
        {
            var q = _db.InventoryItems.AsQueryable();
            if (teams != null && teams.Count > 0)
                q = q.Where(i => teams.Contains(i.Team));

            var items = q.ToList();
            int changed = items.Count(it => it.AlertThreshold != threshold);
            foreach (var it in items)
                it.AlertThreshold = threshold;

            // Blank ItemId = not about one item. Logged even when nothing changed:
            // "Apply to all" is a confirmed overwrite, so the click is the event.
            string scope = (teams != null && teams.Count > 0) ? string.Join("/", teams) : "all teams";
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Alert Threshold Bulk Set",
                ItemId = "",
                QuantityChange = 0,
                Details = $"Alert threshold set to {threshold} for {items.Count} item(s) ({scope}); {changed} value(s) actually changed.",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            return items.Count;
        }

        // Smallest positive number no active variant uses. A retired row's number is reusable.
        private static int NextFreeVariantNumber(InventoryItem item)
        {
            var used = item.ActiveVariants.Select(v => v.VariantNumber).ToHashSet();
            int n = 1;
            while (used.Contains(n)) n++;
            return n;
        }

        // TC-eligible = the type ends with "Motor" (Motor, ID Motor, OD Motor).
        // Keep in step with the client-side isMotorType().
        public static bool IsMotorType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().ToLowerInvariant().EndsWith("motor");

        // Loanable controls = the type ends with "Control". EEV, VFD, Valve and TXV are not matched.
        // The one place the rule lives.
        public static bool IsControlType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().ToLowerInvariant().EndsWith("control");

        // Exact match, not a suffix: "Compressor" is the literal Type the sidebar filter uses.
        public static bool IsCompressorType(string? type) =>
            !string.IsNullOrWhiteSpace(type)
            && type.Trim().Equals("Compressor", System.StringComparison.OrdinalIgnoreCase);

        // All CompressorUnit rows by ItemId, newest first. A roster: on-hand units too.
        // Ordered by RecordedAt because PickedUpAt is null for anything still on a shelf.
        public Dictionary<string, List<CompressorUnit>> GetCompressorUnitsGrouped()
        {
            return _db.CompressorUnits
                .AsNoTracking()
                .OrderByDescending(c => c.RecordedAt)
                .ToList()
                .GroupBy(c => c.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // On-hand units for one family, oldest first: the pool a pickup draws from. Serial-less stock has no row.
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
        /// Logs or corrects On Hand compressor units from the roster. A row edits an existing
        /// unit (unitId > 0) or adds one at a location (unitId == 0). Both fields blank = no-op.
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

        // All MotorUnit rows by ItemId, newest first.
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
        /// Logs or corrects TC motor units. Edits reach On Hand and Picked Up rows, since a
        /// lab number is the only identity a motor has. New rows are On Hand only.
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


        // Bulk intake. One commit path, two entrances: straight from the Intake screen,
        // or after the superuser resolves a held batch's location.

        public class IntakeLine
        {
            public string ItemName { get; set; } = "";
            public string Type { get; set; } = "";
            public string Brand { get; set; } = "";
            public string RheemPartNumber { get; set; } = "N/A";
            public int Quantity { get; set; } = 1;
            // Compressors only. Up to Quantity serials per row.
            public List<string> Serials { get; set; } = new();
            // Motors only. A count, not per-unit identity. Clamped to Quantity server-side.
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

        // Same shelf = the five location columns match, blank counting as "0", case ignored.
        // Never compare FdaString: the app writes it in more than one shape.
        private static bool SameShelf(ItemVariant v, string? parent, string? major, string? sub, string? rack, string? row)
        {
            static string Norm(string? s) => string.IsNullOrWhiteSpace(s) ? "0" : s.Trim().ToUpperInvariant();
            return Norm(v.Parent) == Norm(parent) && Norm(v.Major) == Norm(major) && Norm(v.Sub) == Norm(sub)
                && Norm(v.Rack) == Norm(rack) && Norm(v.Row) == Norm(row);
        }

        /// <summary>
        /// Commits a batch of rows at one location, under one Line and Team.
        /// preview = run every check and report, write nothing. Group comes from the batch's Line.
        /// </summary>
        public IntakeResult CommitIntake(
            IEnumerable<IntakeLine> lines, string line, string team,
            string parentCode, string majorCode, string subCode, string rack, string row,
            string submittedBy, bool preview)
        {
            var res = new IntakeResult();
            string Seg(string? v) => string.IsNullOrWhiteSpace(v) ? "" : v.Trim();
            parentCode = Seg(parentCode); majorCode = Seg(majorCode); subCode = Seg(subCode);
            rack = Seg(rack); row = Seg(row);

            if (parentCode.Length == 0) { res.Errors.Add("No location chosen."); return res; }

            string fda = string.Join(".", new[] { parentCode, majorCode, subCode, rack, row }
                                          .Where(x => x.Length > 0));
            string grp = OrgStructure.GroupFor(line);
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
                // Clamped here too. The posted count is not trusted.
                int tcQty = System.Math.Max(0, System.Math.Min(l.ThermocoupledQty, qty));

                // The one thing that blocks: a real PN owned by another family. "N/A" is exempt.
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
                    // Known model: the stock lands as a variant at this location, never a second item.
                    var here = existing.Variants.FirstOrDefault(v => !v.IsRetired
                        && SameShelf(v, parentCode, majorCode, subCode, rack, row));
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
                        // The item's own Team, not the batch's: an import must not split an item across teams.
                        Team = existing.Team ?? "",
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
                        Team = team,   // brand-new item: variant 1 inherits the family Team, same as CreateItem
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

        // Intake serials are stock on the shelf, so each unit is recorded On Hand.
        // A duplicate (item, serial) is skipped, not thrown. Shared by Intake and CreateItem.
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

        // Motors: a count, not identity. Mints that many blank MotorUnit rows for a lab number to land on later.
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

        // Intake stock batch: rows whose name matched an existing item. Add or Adjustment only,
        // reviewed on the Intake page before anything is applied.

        public class StockBatchUpdate
        {
            public string ItemId { get; set; } = "";
            public string ActionType { get; set; } = "Add";   // "Add" | "Adjustment"
            public int Quantity { get; set; }
            public int ThermocoupledQty { get; set; }
            public List<string> Serials { get; set; } = new();
        }

        /// <summary>
        /// Applies every update in one transaction, all or nothing, through ModifyStock.
        /// Refuses an Adjustment at a location the item does not occupy. Throws on the first failure.
        /// </summary>
        public List<(InventoryItem Item, int OldQty, int NewQty)> CommitIntakeStockBatch(
            List<StockBatchUpdate> updates, string parentCode, string majorCode, string subCode, string rack, string row)
        {
            var results = new List<(InventoryItem Item, int OldQty, int NewQty)>();

            using var tx = _db.Database.BeginTransaction();
            try
            {
                foreach (var u in updates)
                {
                    var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == u.ItemId);
                    if (item == null)
                        throw new InvalidOperationException($"{u.ItemId} no longer exists -- nothing in this batch was applied.");

                    if (u.ActionType == "Add" && u.Quantity <= 0)
                        throw new InvalidOperationException($"{item.ItemName} ({u.ItemId}): quantity must be positive to add stock.");

                    var match = item.ActiveVariants.FirstOrDefault(v => SameShelf(v, parentCode, majorCode, subCode, rack, row));

                    if (u.ActionType == "Adjustment" && match == null)
                        throw new InvalidOperationException(
                            $"{item.ItemName} ({u.ItemId}): no existing stock at this location to adjust -- " +
                            "use Add instead, or pick the location where it's already stocked.");

                    string targetVariant = match != null ? match.Id.ToString() : "NEW";

                    var result = ModifyStock(u.ItemId, u.ActionType, u.Quantity, null, null,
                        newParent: parentCode, newMajor: majorCode, newSub: subCode, newRack: rack, newRow: row,
                        targetVariant: targetVariant, thermocoupledQty: u.ThermocoupledQty, serials: u.Serials);
                    if (result == null)
                        throw new InvalidOperationException($"{u.ItemId} no longer exists -- nothing in this batch was applied.");

                    results.Add((result.Value.item, result.Value.oldQty, result.Value.newQty));
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }

            return results;
        }

        // Loanable units of an order line: a Control loans all, a Motor its TC subset,
        // a Compressor all (meaning "not yet dispositioned"), everything else none.
        public static int LoanableQuantity(string? type, int quantity, int thermocoupledCount)
        {
            if (IsControlType(type)) return quantity;
            if (IsMotorType(type)) return System.Math.Min(thermocoupledCount, quantity);

            // Compressors reuse the counter: here it means nobody has pressed Done Using yet.
            if (IsCompressorType(type)) return quantity;

            return 0;
        }

        /// <summary>
        /// The recorded units a partial Location Transfer takes along, from the ones ticked.
        /// Throws, with nothing changed, when the ticks do not fit what moves and what stays.
        /// </summary>
        private static List<T> TickedUnitsMoving<T>(List<T> here, System.Func<T, int> idOf,
            IReadOnlyCollection<int>? ticked, int moving, int staying, string noun)
        {
            var ids = (ticked ?? System.Array.Empty<int>()).Distinct().ToList();
            var picked = here.Where(u => ids.Contains(idOf(u))).ToList();

            if (picked.Count != ids.Count)
                throw new InvalidOperationException(
                    "One or more ticked units aren't recorded on that stack any more -- refresh and try again. Nothing was moved.");
            if (picked.Count > moving)
                throw new InvalidOperationException(
                    $"{picked.Count} recorded {noun}s are ticked but only {moving} {(moving == 1 ? "is" : "are")} moving -- untick the ones staying. Nothing was moved.");

            int mustTick = System.Math.Min(moving, System.Math.Max(0, here.Count - staying));
            if (picked.Count < mustTick)
            {
                int left = here.Count - picked.Count;
                int more = mustTick - picked.Count;
                throw new InvalidOperationException(
                    $"Only {staying} {noun}{(staying == 1 ? " stays" : "s stay")} behind but {left} recorded {noun}{(left == 1 ? "" : "s")} would stay on record there -- tick which {more} more {(more == 1 ? "is" : "are")} moving. Nothing was moved.");
            }
            return picked;
        }

        /// <summary>
        /// The recorded units that leave when a stack's count drops below its rows.
        /// Throws, with nothing changed, unless exactly `mustLeave` are ticked.
        /// </summary>
        private static List<T> TickedUnitsLeaving<T>(List<T> here, System.Func<T, int> idOf,
            IReadOnlyCollection<int>? ticked, int mustLeave, string noun)
        {
            var ids = (ticked ?? System.Array.Empty<int>()).Distinct().ToList();
            var picked = here.Where(u => ids.Contains(idOf(u))).ToList();

            if (picked.Count != ids.Count)
                throw new InvalidOperationException(
                    "One or more ticked units aren't recorded on that stack any more -- refresh and try again. Nothing was changed.");
            if (picked.Count != mustLeave)
            {
                int room = here.Count - mustLeave;
                throw new InvalidOperationException(
                    $"That leaves room for {room} recorded {noun}{(room == 1 ? "" : "s")} but {here.Count} {(here.Count == 1 ? "is" : "are")} on record there -- "
                    + $"tick exactly which {mustLeave} {(mustLeave == 1 ? "is" : "are")} leaving ({picked.Count} ticked). Nothing was changed.");
            }
            return picked;
        }

        /// <summary>
        /// After a stack's count drops, surplus recorded units leave as Scrapped. Call after the
        /// new counts are set. Throws before changing any row when the ticks do not fit.
        /// </summary>
        public static string RetireSurplusUnits(AppDbContext db, InventoryItem item, ItemVariant v,
            IReadOnlyCollection<int>? leavingCompressorIds, IReadOnlyCollection<int>? leavingMotorIds,
            bool heldMayLeave = false)
        {
            var names = new List<string>();
            int count = 0;

            // A unit named in a live hold cannot be ticked as leaving. A recount (heldMayLeave) can take it.
            var held = heldMayLeave ? new Dictionary<int, string>() : ReservationService.PinOwners(db, item.ItemId);
            void RefuseHeld(int? reservationId, string name)
            {
                if (reservationId.HasValue && held.TryGetValue(reservationId.Value, out var who))
                    throw new InvalidOperationException(
                        $"{name} is reserved for {ReservationService.Person(who)} -- release or edit that hold before it can leave. Nothing was changed.");
            }

            if (IsCompressorType(item.Type))
            {
                var rows = db.CompressorUnits
                    .Where(c => c.ItemId == item.ItemId && c.ItemVariantId == v.Id && c.Status == UnitStatus.OnHand)
                    .OrderBy(c => c.RecordedAt)
                    .ToList()
                    // Rechecked in memory: an earlier step of this request may have changed a tracked row.
                    .Where(c => c.Status == UnitStatus.OnHand && c.ItemVariantId == v.Id)
                    .ToList();
                int surplus = rows.Count - v.Quantity;
                if (surplus > 0)
                {
                    var leaving = v.Quantity <= 0
                        ? rows
                        : TickedUnitsLeaving(rows, c => c.Id, leavingCompressorIds, surplus, "unit");
                    foreach (var c in leaving)
                        RefuseHeld(c.ReservationId, string.IsNullOrWhiteSpace(c.SerialNumber) ? "A recorded unit" : $"SN {c.SerialNumber}");
                    foreach (var c in leaving)
                    {
                        c.ReservationId = null;
                        c.Status = UnitStatus.Scrapped;
                        c.ItemVariantId = null;
                        names.Add(string.IsNullOrWhiteSpace(c.SerialNumber) ? (string.IsNullOrWhiteSpace(c.LabNumber) ? "no serial" : c.LabNumber!) : c.SerialNumber!);
                    }
                    count = leaving.Count;
                }
            }
            else if (IsMotorType(item.Type))
            {
                var rows = db.MotorUnits
                    .Where(m => m.ItemId == item.ItemId && m.ItemVariantId == v.Id && m.Status == UnitStatus.OnHand)
                    .OrderBy(m => m.RecordedAt)
                    .ToList()
                    .Where(m => m.Status == UnitStatus.OnHand && m.ItemVariantId == v.Id)
                    .ToList();
                int surplus = rows.Count - v.ThermocoupledQty;
                if (surplus > 0)
                {
                    var leaving = v.ThermocoupledQty <= 0
                        ? rows
                        : TickedUnitsLeaving(rows, m => m.Id, leavingMotorIds, surplus, "TC unit");
                    foreach (var m in leaving)
                        RefuseHeld(m.ReservationId, string.IsNullOrWhiteSpace(m.LabNumber) ? "A recorded TC motor" : $"Lab {m.LabNumber}");
                    foreach (var m in leaving)
                    {
                        m.ReservationId = null;
                        m.Status = UnitStatus.Scrapped;
                        m.ItemVariantId = null;
                        names.Add(string.IsNullOrWhiteSpace(m.LabNumber) ? "no lab #" : m.LabNumber!);
                    }
                    count = leaving.Count;
                }
            }

            if (count == 0) return "";
            return $" {count} recorded unit{(count == 1 ? "" : "s")} scrapped [{string.Join(", ", names)}].";
        }

        // serials: compressors, Add only. moving*Ids: units ticked on a partial Location Transfer.
        // leaving*Ids: units ticked as gone on a Scrap or downward Adjustment.
        public (InventoryItem item, int oldQty, int newQty)? ModifyStock(string itemId, string actionType, int quantity, string? newGroup, string? newTeam,
            string? newParent = null, string? newMajor = null, string? newSub = null, string? newRack = null, string? newRow = null,
            string? targetVariant = null, int? transferQty = null, int thermocoupledQty = 0,
            string? newLine = null, List<string>? serials = null, string? variantTeam = null,
            IReadOnlyCollection<int>? movingCompressorIds = null, IReadOnlyCollection<int>? movingMotorIds = null,
            IReadOnlyCollection<int>? leavingCompressorIds = null, IReadOnlyCollection<int>? leavingMotorIds = null)
        {
            var item = _db.InventoryItems.Include(i => i.Variants).FirstOrDefault(i => i.ItemId == itemId);
            if (item == null) return null;

            // TC exists on motors only.
            bool isMotor = IsMotorType(item.Type);
            if (!isMotor) thermocoupledQty = 0;
            if (thermocoupledQty < 0) thermocoupledQty = 0;

            // A blank segment becomes "0" so every level is explicit, e.g. PATS.0.0.0.0.
            string Seg(string? v) => string.IsNullOrWhiteSpace(v) ? "0" : v.Trim().ToUpperInvariant();
            string FiveSeg(string? p, string? m, string? s, string? rk, string? rw)
                => string.Join(".", new[] { Seg(p), Seg(m), Seg(s), Seg(rk), Seg(rw) });

            // targetVariant: blank = primary, a number = that ItemVariant.Id, "NEW" (Add only) = a new variant.
            bool wantsNewLocation = string.Equals(targetVariant, "NEW", System.StringComparison.OrdinalIgnoreCase);
            ItemVariant? pv = null;
            if (!wantsNewLocation && int.TryParse(targetVariant, out int tvId))
                pv = item.ActiveVariants.FirstOrDefault(v => v.Id == tvId);
            pv ??= item.PrimaryVariant;
            if (pv == null && !wantsNewLocation)
            {
                // Self-heal: an item with no variants gets a #1 at quantity 0.
                pv = new ItemVariant { VariantNumber = 1, Quantity = 0, Team = item.Team ?? "", IsRetired = false };
                item.Variants.Add(pv);
            }

            int oldQty = item.Quantity;
            int qtyChange = 0;
            string details = "";
            // The stack's numbers before the action, restored if a later check refuses.
            int pvQtyBefore = pv?.Quantity ?? 0;
            int pvTcBefore = pv?.ThermocoupledQty ?? 0;
            // The variant an Add landed on, for serial logging after the save.
            ItemVariant? addedVariant = null;
            // Location Transfer: unit rows that follow the stock, re-pointed once a split has its Id.
            ItemVariant? unitDest = null;
            var movingCompressors = new List<CompressorUnit>();
            var movingMotors = new List<MotorUnit>();

            // Add and Scrap take a positive amount. Only Adjustment is signed.
            if ((actionType == "Add" || actionType == "Scrap") && quantity < 1)
                throw new System.InvalidOperationException(
                    $"{actionType} needs a quantity of at least 1 — nothing was changed.");

            if (actionType == "Add")
            {
                qtyChange = quantity;
                // Of the units being added, at most `quantity` can be TC.
                int tcAdd = System.Math.Min(thermocoupledQty, quantity);
                string tcNote = (isMotor && tcAdd > 0) ? $" ({tcAdd} thermocoupled)" : "";
                if (wantsNewLocation)
                {
                    // A new spot, or another team's slice: both are a new variant. Blank team = the item's Team.
                    string team = string.IsNullOrWhiteSpace(variantTeam) ? (item.Team ?? "") : variantTeam.Trim();
                    var nv = new ItemVariant
                    {
                        VariantNumber = NextFreeVariantNumber(item),
                        Quantity = quantity,
                        ThermocoupledQty = tcAdd,
                        Team = team,
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
                    string teamNote = string.IsNullOrWhiteSpace(team) ? "" : $" for {team}";
                    details = $"Added {quantity} unit(s){tcNote} at NEW location {nv.FdaString}{teamNote} (Variant {nv.VariantNumber}).";
                    addedVariant = nv;
                }
                else
                {
                    pv!.Quantity += quantity;
                    // New TC adds to the stack's TC, never past its total.
                    pv.ThermocoupledQty = System.Math.Min(pv.ThermocoupledQty + tcAdd, pv.Quantity);
                    details = $"Added {quantity} unit(s){tcNote} to stock (Variant {pv.VariantNumber}, {pv.FdaString}).";
                    addedVariant = pv;
                }
            }
            else if (actionType == "Scrap")
            {
                // Clamped to what is on the shelf. The log records the clamped amount.
                int actualScrap = System.Math.Min(quantity, pv!.Quantity);
                qtyChange = -actualScrap;
                // TC scrapped: at least the overflow past the non-TC units, at most the TC on hand.
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
                // Floored at 0. The log records what was applied.
                int requested = quantity;
                quantity = System.Math.Max(quantity, -pv!.Quantity);
                qtyChange = quantity;
                pv.Quantity += quantity;
                // Can also reclassify on-hand stock as TC without changing the count (quantity may be 0).
                if (isMotor && thermocoupledQty > 0)
                    pv.ThermocoupledQty = System.Math.Min(pv.ThermocoupledQty + thermocoupledQty, pv.Quantity);
                // Keep TC <= Qty.
                if (pv.ThermocoupledQty > pv.Quantity)
                    pv.ThermocoupledQty = System.Math.Max(0, pv.Quantity);
                string tcNote = (isMotor && thermocoupledQty > 0)
                    ? $"; {thermocoupledQty} of the existing stock reclassified as thermocoupled" : "";
                string floorNote = quantity != requested
                    ? $" ({requested} requested, stack only held {-quantity})" : "";
                details = $"Manual adjustment of {(quantity >= 0 ? "+" : "")}{quantity} unit(s){floorNote} (Variant {pv.VariantNumber}){tcNote}.";
            }
            else if (actionType == "Ownership")
            {
                // Ownership means Line. Group is frozen at creation with the ItemId prefix. newGroup is ignored.
                string oldLine = string.IsNullOrWhiteSpace(item.Line) ? "unassigned" : item.Line;
                string oldTeam = string.IsNullOrWhiteSpace(item.Team) ? "no team" : item.Team;

                if (newLine != null)
                {
                    string ln = newLine.Trim();
                    // Blank is legal: unassigned, fails open. An unknown Line throws so the caller shows an error.
                    if (ln.Length > 0 && !OrgStructure.IsValidLine(ln))
                        throw new System.InvalidOperationException(
                            $"'{ln}' isn't a recognized Line — the item was left unchanged.");
                    item.Line = ln;
                }
                if (newTeam != null)
                {
                    // "" is a real choice (no team), so this tests for null. The project code comes from the team row.
                    string t = newTeam.Trim();
                    string previousTeam = (item.Team ?? "").Trim();
                    item.Team = t;
                    item.ProjectCode = t.Length == 0
                        ? ""
                        : (_db.Teams.FirstOrDefault(x => x.Name == t)?.ProjectCode ?? "");

                    // The family's stock moves with it: variants on the previous team, and only those.
                    if (t != previousTeam)
                    {
                        foreach (var av in item.ActiveVariants
                                     .Where(v => string.Equals((v.Team ?? "").Trim(), previousTeam,
                                                               System.StringComparison.OrdinalIgnoreCase))
                                     .ToList())
                            av.Team = t;
                    }
                }
                string nowLine = string.IsNullOrWhiteSpace(item.Line) ? "unassigned" : item.Line;
                string nowTeam = string.IsNullOrWhiteSpace(item.Team) ? "no team" : item.Team;
                details = $"Moved from '{oldLine}' ({oldTeam}) to '{nowLine}' ({nowTeam}).";
            }
            else if (actionType == "Location Transfer")
            {
                // Three outcomes: merge into a same-team stack at the destination,
                // relocate the whole stack in place, or split off a new variant.
                // The item total never changes.
                string dP = Seg(newParent), dM = Seg(newMajor), dS = Seg(newSub), dRk = Seg(newRack), dRw = Seg(newRow);
                string destFda = string.Join(".", new[] { dP, dM, dS, dRk, dRw });

                string oldFda = string.IsNullOrWhiteSpace(pv!.FdaString)
                    ? FiveSeg(pv.Parent, pv.Major, pv.Sub, pv.Rack, pv.Row)
                    : pv.FdaString;

                int moveQty = (transferQty.HasValue && transferQty.Value > 0)
                    ? System.Math.Min(transferQty.Value, pv.Quantity)
                    : pv.Quantity;

                // TC moved: enough to keep the source's TC <= what stays, at most what it has.
                int srcNonTc = pv.Quantity - pv.ThermocoupledQty;
                int forcedTcMove = System.Math.Max(0, moveQty - srcNonTc);
                int tcMove = System.Math.Min(pv.ThermocoupledQty,
                                System.Math.Max(forcedTcMove, System.Math.Min(thermocoupledQty, moveQty)));
                // Whole-pile relocate carries ALL the source TC by definition.
                if (moveQty >= pv.Quantity) tcMove = pv.ThermocoupledQty;
                string tcMoveNote = (isMotor && tcMove > 0) ? $" [{tcMove} TC]" : "";

                // A merge target must be the same team's stack, not just the same shelf.
                string srcTeam = (pv!.Team ?? "").Trim();
                var mergeTarget = item.ActiveVariants.FirstOrDefault(v => v.Id != pv.Id
                    && SameShelf(v, dP, dM, dS, dRk, dRw)
                    && string.Equals((v.Team ?? "").Trim(), srcTeam, System.StringComparison.OrdinalIgnoreCase));

                // Unit rows point at a variant, so they must follow the stock. A drained stack
                // (or all its TC, for motors) takes every row. Anything less: the person ticks which.
                bool sourceDrained = moveQty >= pv.Quantity;
                bool tcDrained = pv.ThermocoupledQty > 0 && tcMove >= pv.ThermocoupledQty;
                if (mergeTarget != null || !sourceDrained)
                {
                    var compressorsHere = _db.CompressorUnits
                        .Where(c => c.ItemId == itemId && c.ItemVariantId == pv.Id && c.Status == UnitStatus.OnHand)
                        .ToList();
                    var motorsHere = _db.MotorUnits
                        .Where(m => m.ItemId == itemId && m.ItemVariantId == pv.Id && m.Status == UnitStatus.OnHand)
                        .ToList();
                    movingCompressors = sourceDrained ? compressorsHere
                        : TickedUnitsMoving(compressorsHere, c => c.Id, movingCompressorIds,
                            moveQty, pv.Quantity - moveQty, "unit");
                    movingMotors = (sourceDrained || tcDrained) ? motorsHere
                        : TickedUnitsMoving(motorsHere, m => m.Id, movingMotorIds,
                            tcMove, pv.ThermocoupledQty - tcMove, "TC unit");
                }
                int movingUnits = movingCompressors.Count + movingMotors.Count;
                var movingNames = movingCompressors
                    .Select(c => string.IsNullOrWhiteSpace(c.SerialNumber) ? c.LabNumber : c.SerialNumber)
                    .Concat(movingMotors.Select(m => m.LabNumber))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                string unitMoveNote = movingUnits > 0
                    ? $" {movingUnits} recorded unit{(movingUnits == 1 ? "" : "s")} moved with {(movingUnits == 1 ? "it" : "them")}"
                      + (movingNames.Count > 0 ? $" [{string.Join(", ", movingNames)}]" : "") + "."
                    : "";

                if (mergeTarget != null)
                {
                    unitDest = mergeTarget;
                    mergeTarget.Quantity += moveQty;
                    mergeTarget.ThermocoupledQty += tcMove;
                    pv.Quantity -= moveQty;
                    pv.ThermocoupledQty -= tcMove;
                    if (pv.Quantity <= 0)
                    {
                        pv.Quantity = 0;
                        pv.ThermocoupledQty = 0;
                        pv.IsRetired = true;   // fully drained -> retire (kept for history, number freed)
                        details = $"Moved {moveQty}{tcMoveNote} from {oldFda} into Variant {mergeTarget.VariantNumber} ({destFda}); Variant {pv.VariantNumber} fully merged and retired.{unitMoveNote}";
                    }
                    else
                    {
                        details = $"Moved {moveQty}{tcMoveNote} from Variant {pv.VariantNumber} ({oldFda}) into Variant {mergeTarget.VariantNumber} ({destFda}); {pv.Quantity} remain at source.{unitMoveNote}";
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
                        // The split keeps the source's Team. Team-less, its own team could not order it.
                        Team = srcTeam,
                        Parent = dP, Major = dM, Sub = dS, Rack = dRk, Row = dRw,
                        FdaString = destFda,
                        RegisteredAt = System.DateTime.UtcNow,
                        IsRetired = false
                    };
                    item.Variants.Add(nv);
                    unitDest = nv;
                    pv.Quantity -= moveQty;
                    pv.ThermocoupledQty -= tcMove;
                    details = $"Split {moveQty}{tcMoveNote} from Variant {pv.VariantNumber} ({oldFda}) to NEW Variant {nv.VariantNumber} ({destFda}); {pv.Quantity} remain at source.{unitMoveNote}";
                }
            }
            else
            {
                // The controller compares case-insensitively and these branches do not, so reject anything else.
                throw new System.InvalidOperationException(
                    $"'{actionType}' isn't a recognized stock action -- nothing was changed.");
            }

            // Hard hold: a Scrap, Adjustment or partial move cannot take a stack below what is held on it.
            // Named held units stay on a partial move. A whole-stack merge carries its holds along.
            if (pv != null && (actionType == "Scrap" || actionType == "Adjustment" || actionType == "Location Transfer"))
            {
                var holdView = StockClaims.FromVariants(_db, itemId, item.Variants);
                var (heldQ, heldT) = holdView.HeldOn(pv.Id);
                if (actionType == "Location Transfer" && pv.IsRetired && unitDest != null)
                {
                    ReservationService.MoveLines(_db, itemId, pv.Id, unitDest.Id);
                }
                else if ((heldQ > 0 || heldT > 0) && (pv.Quantity < heldQ || pv.ThermocoupledQty < heldT))
                {
                    string who = ReservationService.DescribeHoldsOn(_db, itemId, pv.Id);
                    pv.Quantity = pvQtyBefore;
                    pv.ThermocoupledQty = pvTcBefore;
                    throw new System.InvalidOperationException(
                        $"Variant {pv.VariantNumber} has {heldQ}{(heldT > 0 ? $" ({heldT} TC)" : "")} reserved ({who}), so it can't go below that. "
                        + "Release or edit the hold first. Nothing was changed.");
                }
                if (actionType == "Location Transfer" && !pv.IsRetired && unitDest != null)
                {
                    var pinned = ReservationService.PinOwners(_db, itemId);
                    var heldMover = movingCompressors.Select(c => (c.ReservationId, Name: "SN " + (c.SerialNumber ?? c.LabNumber ?? "(no serial)")))
                        .Concat(movingMotors.Select(m => (m.ReservationId, Name: "Lab " + (m.LabNumber ?? "(none)"))))
                        .FirstOrDefault(x => x.ReservationId.HasValue && pinned.ContainsKey(x.ReservationId.Value));
                    if (heldMover.ReservationId.HasValue)
                        throw new System.InvalidOperationException(
                            $"{heldMover.Name} is reserved for {ReservationService.Person(pinned[heldMover.ReservationId.Value])} and stays with Variant {pv.VariantNumber} -- untick it, or move the whole stack. Nothing was moved.");
                }
            }

            // A stack that shrank may hold more recorded units than its new count. The surplus leaves.
            if ((actionType == "Scrap" || actionType == "Adjustment") && pv != null
                && (pv.Quantity < pvQtyBefore || pv.ThermocoupledQty < pvTcBefore))
            {
                try
                {
                    details += RetireSurplusUnits(_db, item, pv, leavingCompressorIds, leavingMotorIds);
                }
                catch
                {
                    pv.Quantity = pvQtyBefore;
                    pv.ThermocoupledQty = pvTcBefore;
                    throw;
                }
            }

            item.LastUpdated = System.DateTime.UtcNow;
            item.UpdatedBy = _currentUser.Name;

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

            // Unit rows are re-pointed in the same transaction as the stock move.
            // Two saves: ItemVariantId has no navigation, so EF cannot fill in a split's new Id.
            bool movesUnits = unitDest != null && (movingCompressors.Count > 0 || movingMotors.Count > 0);
            var unitTx = movesUnits && _db.Database.CurrentTransaction == null ? _db.Database.BeginTransaction() : null;
            try
            {
                _db.SaveChanges();   // need addedVariant.Id (NEW-location case) before serials can reference it
                if (movesUnits)
                {
                    foreach (var cu in movingCompressors) cu.ItemVariantId = unitDest!.Id;
                    foreach (var mu in movingMotors) mu.ItemVariantId = unitDest!.Id;
                    _db.SaveChanges();
                }
                unitTx?.Commit();
            }
            catch { unitTx?.Rollback(); throw; }
            finally { unitTx?.Dispose(); }

            if (addedVariant != null && IsCompressorType(item.Type))
            {
                var cleanSerials = (serials ?? new List<string>())
                    .Select(s => (s ?? "").Trim()).Where(s => s.Length > 0)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
                if (cleanSerials.Count > 0)
                    LogIntakeSerials(itemId, addedVariant.Id, cleanSerials, _currentUser.Name,
                        System.DateTime.UtcNow, new IntakeResult());
            }

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
                    // Self-heal, as in ModifyStock.
                    pv = new ItemVariant { VariantNumber = 1, Quantity = 0, Team = inv.Team ?? "", IsRetired = false };
                    inv.Variants.Add(pv);
                }
                pv.Quantity += qty;
                inv.LastUpdated = System.DateTime.UtcNow;
                inv.UpdatedBy = _currentUser.Name;

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