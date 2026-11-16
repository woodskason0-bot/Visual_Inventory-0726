using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Visual_Inventory_System.Services;

namespace Visual_Inventory_System.Controllers
{
    /// <summary>
    /// The Unit Tracker list: whole AC units, information only. Every signed-in user sees every row.
    /// Add, Update and the row boxes are Standard+; moving Line or Team and Delete are Engineer+;
    /// Notes is open to everyone. Enforced here and in TrackedUnitService, which holds all the LINQ.
    /// </summary>
    public class UnitTrackerController : Controller
    {
        private readonly TrackedUnitService _units;
        private readonly CurrentUserService _currentUser;

        public UnitTrackerController(TrackedUnitService units, CurrentUserService currentUser)
        {
            _units = units;
            _currentUser = currentUser;
        }

        // Filters live in the query string so a filtered view can be linked and survives a write.
        public IActionResult Index(string? q, string? pkg, string? team, string? line,
            [FromQuery(Name = "where")] string? whereabouts,
            [FromQuery(Name = "build")] string? buildStatus)
        {
            var units = _units.Query(q, pkg, team, line, whereabouts, buildStatus);
            ViewBag.Units = units;

            ViewBag.FilterQ = (q ?? "").Trim();
            ViewBag.FilterPkg = TrackedUnitPackageName(pkg);
            ViewBag.FilterTeam = (team ?? "").Trim();
            ViewBag.FilterLine = (line ?? "").Trim();
            ViewBag.FilterWhere = string.Equals((whereabouts ?? "").Trim(), Models.TrackedUnitWhereabouts.NotSet,
                    System.StringComparison.OrdinalIgnoreCase)
                ? Models.TrackedUnitWhereabouts.NotSet
                : (Models.TrackedUnitWhereabouts.Canonical(whereabouts) ?? "");
            ViewBag.FilterBuild = string.Equals((buildStatus ?? "").Trim(), Models.TrackedUnitBuildStatus.NotSet,
                    System.StringComparison.OrdinalIgnoreCase)
                ? Models.TrackedUnitBuildStatus.NotSet
                : (Models.TrackedUnitBuildStatus.Canonical(buildStatus) ?? "");
            ViewBag.TeamOptions = _units.TeamsInUse();
            ViewBag.LineOptions = _units.LinesInUse();

            // Feeds the Edit modal with no second request.
            // JsonSerializer escapes "<", which keeps free text inert inside the script block.
            ViewBag.UnitRowsJson = JsonSerializer.Serialize(units.ToDictionary(
                u => u.Id.ToString(),
                u => new
                {
                    id = u.Id,
                    model = u.ModelNumber,
                    pkg = u.PackageType,
                    serial = u.SerialNumber,
                    sample = u.SampleNumber,
                    line = u.Line,
                    team = u.Team,
                    notes = u.Notes
                }));

            // Branch -> Line -> Team autofill, the same four feeds Bulk Intake gets.
            ViewBag.ActiveTeams = _units.ActiveTeams();
            ViewBag.OrgStructureJson = JsonSerializer.Serialize(OrgStructure.BranchLines);
            ViewBag.TeamLinesJson = JsonSerializer.Serialize(_units.TeamLines());
            ViewBag.MyLineJson = JsonSerializer.Serialize(_currentUser.Line ?? "");
            ViewBag.MyBranchJson = JsonSerializer.Serialize(
                !string.IsNullOrWhiteSpace(_currentUser.Line)
                    ? (OrgStructure.BranchFor(_currentUser.Line) ?? "")
                    : (_currentUser.Branch ?? ""));

            // "Save & add another" leaves the kept values here for exactly one load.
            ViewBag.UnitAddCarryJson = TempData["UnitAddCarry"] as string ?? "null";

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult Add(string? modelNumber, string? packageType, string? serialNumber,
            string? sampleNumber, string? line, string? team, string? notes, string? addAnother)
        {
            var result = _units.Add(modelNumber, packageType, serialNumber, sampleNumber, line, team, notes);

            // Model, Package/Split, Line and Team carry over. Serial, Sample and Notes start clean.
            if (result.Ok && result.Unit != null && addAnother == "1")
            {
                TempData["UnitAddCarry"] = JsonSerializer.Serialize(new
                {
                    model = result.Unit.ModelNumber,
                    pkg = result.Unit.PackageType,
                    line = result.Unit.Line,
                    team = result.Unit.Team
                });
            }
            return Finish(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult Update(int id, string? modelNumber, string? packageType, string? serialNumber,
            string? sampleNumber, string? line, string? team, string? notes)
        {
            // MVC folds a posted "" to null, so "not posted" and "set to blank" are told apart by the form keys.
            string? lineArg = Request.Form.ContainsKey("line") ? (line ?? "") : null;
            string? teamArg = Request.Form.ContainsKey("team") ? (team ?? "") : null;
            string? pkgArg = Request.Form.ContainsKey("packageType") ? (packageType ?? "") : null;

            return Finish(_units.Update(id, modelNumber, pkgArg, serialNumber, sampleNumber, lineArg, teamArg, notes));
        }

        // No level attribute: Notes is the one field a Viewer may edit. Takes id and notes only.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateNotes(int id, string? notes)
        {
            return Finish(_units.UpdateNotes(id, notes));
        }

        // Saves on click, so it answers JSON: a redirect would reload a long list and lose the user's place.
        // Sent with X-Requested-With so a refusal is a 403 the page can read.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SetWhereabouts(int id, string? whereabouts)
        {
            var result = _units.SetWhereabouts(id, whereabouts);
            var unit = result.Unit;
            return Json(new
            {
                ok = result.Ok,
                message = result.Message,
                whereabouts = unit?.Whereabouts ?? "",
                updatedDate = unit?.UpdatedAt?.ToLocalTime().ToString("MMM d, yyyy"),
                updatedTitle = unit?.UpdatedAt?.ToLocalTime().ToString("g"),
                updatedBy = unit?.UpdatedBy ?? ""
            });
        }

        // Same shape as SetWhereabouts.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Standard)]
        public IActionResult SetBuildStatus(int id, string? buildStatus)
        {
            var result = _units.SetBuildStatus(id, buildStatus);
            var unit = result.Unit;
            return Json(new
            {
                ok = result.Ok,
                message = result.Message,
                buildStatus = unit?.BuildStatus ?? "",
                updatedDate = unit?.UpdatedAt?.ToLocalTime().ToString("MMM d, yyyy"),
                updatedTitle = unit?.UpdatedAt?.ToLocalTime().ToString("g"),
                updatedBy = unit?.UpdatedBy ?? ""
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireLevel(AccessLevels.Engineer)]
        public IActionResult Delete(int id)
        {
            return Finish(_units.Delete(id));
        }

        private IActionResult Finish(TrackedUnitResult result)
        {
            var message = ToastText(result.Message);
            if (result.Ok) TempData["Success"] = message;
            else TempData["Error"] = message;
            return SmartRedirect();
        }

        // Caps the toast text so a raw POST cannot overflow the TempData cookie. The stored value is not capped.
        private static string ToastText(string message)
        {
            return message.Length > 400 ? message.Substring(0, 400) + "…" : message;
        }

        // The Package/Split filter's selected value: "none", the canonical label, or "" for Any.
        private static string TrackedUnitPackageName(string? pkg)
        {
            var v = (pkg ?? "").Trim();
            if (string.Equals(v, Models.TrackedUnitPackage.NotSet, System.StringComparison.OrdinalIgnoreCase))
                return Models.TrackedUnitPackage.NotSet;
            return Models.TrackedUnitPackage.Canonical(v) ?? "";
        }

        // Back to the referring page, so the user keeps their filtered view.
        private IActionResult SmartRedirect()
        {
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Index") : Redirect(referer);
        }
    }
}
