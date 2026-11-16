using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// The one answer to "how much of this item can this person still take".
    /// Supply is the active stacks by Team. Claims are live holds and Pending order lines.
    /// Available = the most that can be added without raising Deficit().
    /// </summary>
    public sealed class StockClaims
    {
        public sealed class Stack { public int Id; public int Number; public string Team = ""; public int Qty; public int Tc; }
        public sealed class Hold { public int ReservationId; public string By = ""; public int VariantId; public int Qty; public int Tc; public DateTime CreatedAt; }
        public sealed class Line { public int OrderItemId; public string By = ""; public string Team = ""; public int Qty; public int Tc; public DateTime CreatedAt; }

        public string ItemId { get; }
        public List<Stack> Stacks { get; }
        public List<Hold> Holds { get; }
        public List<Line> Lines { get; }

        public StockClaims(string itemId, List<Stack> stacks, List<Hold> holds, List<Line> lines)
        {
            ItemId = itemId;
            Stacks = stacks;
            Holds = holds;
            Lines = lines;
        }

        // Loading

        /// <summary>Everything from the database, untracked.</summary>
        public static StockClaims Load(AppDbContext db, string itemId)
        {
            var stacks = db.ItemVariants.AsNoTracking()
                .Where(v => !v.IsRetired && v.InventoryItem.ItemId == itemId)
                .Select(v => new Stack { Id = v.Id, Number = v.VariantNumber, Team = v.Team ?? "", Qty = v.Quantity, Tc = v.ThermocoupledQty })
                .ToList();
            return new StockClaims(itemId, stacks, LoadHolds(db, itemId), LoadLines(db, itemId));
        }

        /// <summary>Stacks from already-loaded (possibly tracked and just changed) variants.</summary>
        public static StockClaims FromVariants(AppDbContext db, string itemId, IEnumerable<ItemVariant> variants)
        {
            var stacks = variants.Where(v => !v.IsRetired)
                .Select(v => new Stack { Id = v.Id, Number = v.VariantNumber, Team = v.Team ?? "", Qty = v.Quantity, Tc = v.ThermocoupledQty })
                .ToList();
            return new StockClaims(itemId, stacks, LoadHolds(db, itemId), LoadLines(db, itemId));
        }

        public static List<Hold> LoadHolds(AppDbContext db, string itemId)
        {
            var today = DateTime.Today;
            return (from l in db.ReservationLines.AsNoTracking()
                    join r in ReservationService.Live(db.Reservations.AsNoTracking(), today) on l.ReservationId equals r.Id
                    where r.ItemId == itemId && l.Quantity > 0
                    select new Hold
                    {
                        ReservationId = r.Id,
                        By = r.ReservedBy,
                        VariantId = l.ItemVariantId,
                        Qty = l.Quantity,
                        Tc = l.TcQuantity,
                        CreatedAt = r.CreatedAt
                    }).ToList();
        }

        public static List<Line> LoadLines(AppDbContext db, string itemId)
        {
            // Line status too: a Split line's units have left even while its order waits on another line.
            return db.OrderItems.AsNoTracking()
                .Where(oi => oi.ItemId == itemId && oi.Order.Status == "Pending" && oi.Status == "Pending")
                .Select(oi => new Line
                {
                    OrderItemId = oi.Id,
                    By = oi.Order.RequestedBy ?? "",
                    Team = oi.Team ?? "",
                    Qty = oi.Quantity,
                    Tc = oi.ThermocoupledCount,
                    CreatedAt = oi.Order.CreatedAt
                })
                .ToList();
        }

        // The check

        private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>FulfillOrderItem's own rule: a scoped line pulls only from stacks of exactly its Team.</summary>
        private static bool Compatible(Stack s, string lineTeam) => lineTeam.Length == 0 || s.Team == lineTeam;

        /// <summary>How far over the claims are. 0 = everything fits.</summary>
        public int Deficit() => Deficit(Holds, Lines);

        public int Deficit(IEnumerable<Hold> holds, IEnumerable<Line> lines)
        {
            var holdList = holds.Where(h => h.Qty > 0 || h.Tc > 0).ToList();

            // Pinned to stacks, and what each reserver still has to cover their own lines with.
            var pinQ = new Dictionary<int, int>();
            var pinT = new Dictionary<int, int>();
            var capQ = new Dictionary<(string, int), int>();
            var capT = new Dictionary<(string, int), int>();
            foreach (var h in holdList)
            {
                pinQ[h.VariantId] = pinQ.GetValueOrDefault(h.VariantId) + h.Qty;
                pinT[h.VariantId] = pinT.GetValueOrDefault(h.VariantId) + h.Tc;
                var key = ((h.By ?? "").ToLowerInvariant(), h.VariantId);
                capQ[key] = capQ.GetValueOrDefault(key) + h.Qty;
                capT[key] = capT.GetValueOrDefault(key) + h.Tc;
            }

            var groupQ = new Dictionary<string, int>(StringComparer.Ordinal);
            var groupT = new Dictionary<string, int>(StringComparer.Ordinal);
            int unscopedQ = 0, unscopedT = 0;

            foreach (var s in Stacks)
            {
                groupQ[s.Team] = groupQ.GetValueOrDefault(s.Team) + pinQ.GetValueOrDefault(s.Id);
                groupT[s.Team] = groupT.GetValueOrDefault(s.Team) + pinT.GetValueOrDefault(s.Id);
            }

            var orderedStacks = Stacks.OrderBy(s => s.Number).ToList();
            foreach (var l in lines.OrderBy(l => l.CreatedAt).ThenBy(l => l.OrderItemId))
            {
                int covQ = 0, covT = 0;
                string by = (l.By ?? "").ToLowerInvariant();
                foreach (var s in orderedStacks)
                {
                    if (covQ >= l.Qty) break;
                    if (!Compatible(s, l.Team)) continue;
                    var key = (by, s.Id);
                    int cq = capQ.GetValueOrDefault(key);
                    if (cq <= 0) continue;
                    int take = Math.Min(cq, l.Qty - covQ);
                    int tcTake = Math.Min(Math.Min(capT.GetValueOrDefault(key), Math.Max(0, l.Tc - covT)), take);
                    capQ[key] = cq - take;
                    capT[key] = capT.GetValueOrDefault(key) - tcTake;
                    covQ += take;
                    covT += tcTake;
                }
                int uncT = Math.Max(0, l.Tc - covT);
                int uncQ = Math.Max(l.Qty - covQ, uncT);
                if (uncQ <= 0 && uncT <= 0) continue;
                if (l.Team.Length == 0) { unscopedQ += uncQ; unscopedT += uncT; }
                else
                {
                    groupQ[l.Team] = groupQ.GetValueOrDefault(l.Team) + uncQ;
                    groupT[l.Team] = groupT.GetValueOrDefault(l.Team) + uncT;
                }
            }

            int deficit = 0;
            foreach (var s in Stacks)
            {
                deficit += Math.Max(0, pinQ.GetValueOrDefault(s.Id) - s.Qty);
                deficit += Math.Max(0, pinT.GetValueOrDefault(s.Id) - s.Tc);
            }
            // A hold on a stack that is no longer active can't be honoured at all.
            foreach (var h in holdList.Where(h => !Stacks.Any(s => s.Id == h.VariantId)))
                deficit += h.Qty;

            int freeQ = 0, freeT = 0;
            foreach (var g in groupQ.Keys.Union(groupT.Keys).Union(Stacks.Select(s => s.Team)).Distinct().ToList())
            {
                int sq = Stacks.Where(s => s.Team == g).Sum(s => s.Qty);
                int st = Stacks.Where(s => s.Team == g).Sum(s => s.Tc);
                int cq = groupQ.GetValueOrDefault(g);
                int ct = groupT.GetValueOrDefault(g);
                deficit += Math.Max(0, cq - sq) + Math.Max(0, ct - st);
                freeQ += Math.Max(0, sq - cq);
                // TC left over: the group's own non-TC demand uses non-TC units first.
                int nonTcNeed = Math.Max(0, cq - ct);
                int nonTcHave = Math.Max(0, sq - st);
                freeT += Math.Max(0, st - ct - Math.Max(0, nonTcNeed - nonTcHave));
            }
            deficit += Math.Max(0, unscopedQ - freeQ) + Math.Max(0, unscopedT - freeT);
            return deficit;
        }

        // Questions

        private int MaxAdd(Func<int, int> deficitWith, int upper)
        {
            int baseD = Deficit();
            if (upper <= 0) return 0;
            if (deficitWith(upper) <= baseD) return upper;
            int lo = 0, hi = upper;   // deficitWith(lo) <= baseD < deficitWith(hi)
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (deficitWith(mid) <= baseD) lo = mid; else hi = mid;
            }
            return lo;
        }

        private Line Probe(string by, string team, int qty, int tc) =>
            new Line { OrderItemId = int.MaxValue, By = by ?? "", Team = team ?? "", Qty = qty, Tc = tc, CreatedAt = DateTime.MaxValue };

        /// <summary>The most `by` could order now, for a line resolved to `team` ("" = unscoped).</summary>
        public int AvailableQty(string by, string team)
        {
            int upper = Stacks.Sum(s => s.Qty);
            if (Holds.Count == 0 && Lines.Count == 0)
                return string.IsNullOrEmpty(team) ? upper : Stacks.Where(s => s.Team == team).Sum(s => s.Qty);
            return MaxAdd(x => Deficit(Holds, Lines.Append(Probe(by, team, x, 0))), upper);
        }

        /// <summary>The most thermocoupled units `by` could order now.</summary>
        public int AvailableTc(string by, string team)
        {
            int upper = Stacks.Sum(s => s.Tc);
            if (Holds.Count == 0 && Lines.Count == 0)
                return string.IsNullOrEmpty(team) ? upper : Stacks.Where(s => s.Team == team).Sum(s => s.Tc);
            return MaxAdd(t => Deficit(Holds, Lines.Append(Probe(by, team, t, t))), upper);
        }

        /// <summary>Would adding these lines (a cart, in order) keep the claims at or under today's deficit?</summary>
        public bool Fits(IEnumerable<Line> newLines) => Deficit(Holds, Lines.Concat(newLines)) <= Deficit();

        public static Line NewLine(string by, string team, int qty, int tc, int seq = 0) =>
            new Line { OrderItemId = int.MaxValue - 1000 + seq, By = by ?? "", Team = team ?? "", Qty = qty, Tc = tc, CreatedAt = DateTime.MaxValue };

        /// <summary>The most `by` could newly hold on one stack. excludeReservationId = the hold being edited.</summary>
        public (int Qty, int Tc) Holdable(string by, int variantId, int? excludeReservationId)
        {
            var stack = Stacks.FirstOrDefault(s => s.Id == variantId);
            if (stack == null) return (0, 0);
            var baseHolds = Holds.Where(h => excludeReservationId == null || h.ReservationId != excludeReservationId.Value).ToList();
            var view = new StockClaims(ItemId, Stacks, baseHolds, Lines);
            int q = view.MaxAdd(h => view.Deficit(baseHolds.Append(NewHold(by, variantId, h, 0)), Lines), stack.Qty);
            int t = view.MaxAdd(h => view.Deficit(baseHolds.Append(NewHold(by, variantId, h, h)), Lines), stack.Tc);
            return (q, Math.Min(t, q));
        }

        public static Hold NewHold(string by, int variantId, int qty, int tc) =>
            new Hold { ReservationId = 0, By = by ?? "", VariantId = variantId, Qty = qty, Tc = tc, CreatedAt = DateTime.MaxValue };

        /// <summary>Would replacing reservation `reservationId`'s holds (0 = a new one) with these keep the claims at or under today's deficit?</summary>
        public bool HoldsFit(int reservationId, IEnumerable<Hold> replacement)
        {
            var after = Holds.Where(h => reservationId == 0 || h.ReservationId != reservationId).Concat(replacement);
            return Deficit(after, Lines) <= Deficit();
        }

        /// <summary>What other people hold on one stack (the physical floor a pull for `by` must leave).</summary>
        public (int Qty, int Tc) HeldByOthers(int variantId, string by) =>
            (Holds.Where(h => h.VariantId == variantId && !Same(h.By, by)).Sum(h => h.Qty),
             Holds.Where(h => h.VariantId == variantId && !Same(h.By, by)).Sum(h => h.Tc));

        /// <summary>Everything held on one stack, by anyone.</summary>
        public (int Qty, int Tc) HeldOn(int variantId) =>
            (Holds.Where(h => h.VariantId == variantId).Sum(h => h.Qty),
             Holds.Where(h => h.VariantId == variantId).Sum(h => h.Tc));
    }
}
