using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Mark an action with [AllowWithoutName] to let it run before a name has
    /// been entered (the name-entry page, sign-out, and the error page).
    /// </summary>
    public class AllowWithoutNameAttribute : System.Attribute { }

    /// <summary>
    /// Global gate. No name in the session -> redirect to the Identify page
    /// (carrying a return URL) instead of running the action. Actions marked
    /// [AllowWithoutName] are skipped so we don't loop. Only runs for MVC
    /// actions, so static files are unaffected.
    ///
    /// Name present -> re-read that person's roster row and bring the session's
    /// Level, Line and Branch in line with it, so a demotion, a Line change or a
    /// hide in Settings applies on their very next click. Sign-in used to be the
    /// only place those were read, and the idle timeout slides, so an active
    /// session kept its old rights indefinitely. Global filters run before every
    /// [RequireLevel], which therefore sees the fresh level.
    /// </summary>
    public class RequireNameFilter : IAsyncActionFilter
    {
        private readonly AppDbContext _db;
        private readonly CurrentUserService _user;

        public RequireNameFilter(AppDbContext db, CurrentUserService user)
        {
            _db = db;
            _user = user;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            bool allowed = context.ActionDescriptor.EndpointMetadata
                .OfType<AllowWithoutNameAttribute>().Any();
            if (allowed)
            {
                await next();
                return;
            }

            if (!_user.IsSet)
            {
                var returnUrl = context.HttpContext.Request.Path
                                + context.HttpContext.Request.QueryString;
                context.Result = new RedirectToActionResult(
                    "Identify", "Home", new { returnUrl });
                return;
            }

            // Same lookup Identify does: case-insensitive (this column compares
            // BINARY, and a name typed in another case must still find its row),
            // active rows only. No row means an off-roster name, which is Viewer
            // with a blank Line and Branch -- exactly what sign-in gives it.
            string lookup = _user.Name.ToLower();
            var row = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == lookup && u.IsActive);

            int level = row?.AccessLevel ?? AccessLevels.Viewer;
            string line = row?.Line ?? "";
            string branch = row?.Branch ?? "";

            // Write only what changed: a session write sends a fresh Set-Cookie
            // and re-commits the session, which every unchanged click doesn't need.
            if (_user.Level != level) _user.SetLevel(level);
            if (_user.Line != line) _user.SetLine(line);
            if (_user.Branch != branch) _user.SetBranch(branch);

            await next();
        }
    }
}
