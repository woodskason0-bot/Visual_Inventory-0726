using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Visual_Inventory_System.Services;

namespace Visual_Inventory_System.Controllers
{
    /// <summary>
    /// Unit Tracker holdover: a plain list of whole AC units (model, Package/Split,
    /// serial, sample, Line/Team, notes) that people can start filling while the
    /// full tracker is scoped. Information only -- units can't be ordered and
    /// aren't counted anywhere else in VIS.
    ///
    /// Every signed-in user sees the page and every row (the global
    /// RequireNameFilter already covers this controller). Who may do what:
    ///   Add                       Standard+
    ///   Update (model/pkg/serial/sample/notes)   Standard+
    ///   Update changing Line/Team Engineer+ (checked inside the service)
    ///   UpdateNotes               any signed-in user, Viewer included
    ///   SetWhereabouts            Standard+ (the In Lab / In Storage / Outbound boxes)
    ///   Delete                    Engineer+
    /// All of it is enforced here and in the service, not by hidden buttons. The
    /// controller holds no LINQ of its own -- every query and write is in
    /// TrackedUnitService.
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

        // Filters live in the query string so a filtered view can be linked, and
        // SmartRedirect keeps the user on it after a write.
        public IActionResult Index(string? q, string? pkg, string? team, string? line,
            [FromQuery(Name = "where")] string? whereabouts)
        {
            var units = _units.Query(q, pkg, team, line, whereabouts);
            ViewBag.Units = units;

            ViewBag.FilterQ = (q ?? "").Trim();
            ViewBag.FilterPkg = TrackedUnitPackageName(pkg);
            ViewBag.FilterTeam = (team ?? "").Trim();
            ViewBag.FilterLine = (line ?? "").Trim();
            ViewBag.FilterWhere = string.Equals((whereabouts ?? "").Trim(), Models.TrackedUnitWhereabouts.NotSet,
                    System.StringComparison.OrdinalIgnoreCase)
                ? Models.TrackedUnitWhereabouts.NotSet
                : (Models.TrackedUnitWhereabouts.Canonical(whereabouts) ?? "");
            ViewBag.TeamOptions = _units.TeamsInUse();
            ViewBag.LineOptions = _units.LinesInUse();

            // The Edit modal opens from this, so a click costs no second request.
            // JsonSerializer escapes "<", which is what keeps free text inert inside
            // the page's <script> block.
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

            // Branch -> Line -> Team autofill: the same four feeds HomeController.Intake()
            // gives Bulk Intake, so the two pages behave identically.
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

            // Package/Split, Line, Team and Model stay for the next one; Serial,
            // Sample and Notes start clean (they're what differs between units).
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
            // MVC folds a posted "" to null, so "left alone" (a disabled select posts
            // nothing at all) and "set to blank" (N/A) can only be told apart by
            // whether the field was posted.
            string? lineArg = Request.Form.ContainsKey("line") ? (line ?? "") : null;
            string? teamArg = Request.Form.ContainsKey("team") ? (team ?? "") : null;

            return Finish(_units.Update(id, modelNumber, packageType, serialNumber, sampleNumber, lineArg, teamArg, notes));
        }

        // Deliberately no level attribute: Notes is the one field a Viewer may edit.
        // Takes id + notes only, so nothing else can change through it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateNotes(int id, string? notes)
        {
            return Finish(_units.UpdateNotes(id, notes));
        }

        // The row boxes save on click, so this answers JSON instead of redirecting
        // (a redirect would reload a long list and lose the user's place). Sent with
        // X-Requested-With so [RequireLevel] answers a refusal with a 403 the page
        // can read instead of redirecting to Command Center. Takes id + the one
        // value and nothing else.
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

        // _Layout writes the toast text into a JS string literal. Razor HTML-encodes
        // it, so "<" and quotes are safe, but a backslash or a line break in a
        // typed model/serial would end that literal early or malform an escape and
        // the toast would silently never show. Only the toast text is touched; the
        // stored value and the log row keep exactly what was typed. Capped so a
        // hostile raw POST can't push the TempData cookie past its size limit.
        private static string ToastText(string message)
        {
            var flat = message.Replace('\\', '/').Replace('\r', ' ').Replace('\n', ' ');
            return flat.Length > 400 ? flat.Substring(0, 400) + "…" : flat;
        }

        private static string TrackedUnitPackageName(string? pkg)
        {
            var v = (pkg ?? "").Trim();
            return Models.TrackedUnitPackage.All
                .FirstOrDefault(p => string.Equals(p, v, System.StringComparison.OrdinalIgnoreCase)) ?? "";
        }

        // Same three lines as HomeController.SmartRedirect (private there): back to
        // the page the request came from, so the user keeps their filtered view.
        private IActionResult SmartRedirect()
        {
            string referer = Request.Headers["Referer"].ToString();
            return string.IsNullOrEmpty(referer) ? RedirectToAction("Index") : Redirect(referer);
        }
    }
}
