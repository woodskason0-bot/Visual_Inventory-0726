using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;

namespace Visual_Inventory_System.Services
{
    /// <summary>[AllowWithoutName] lets an action run before a name is entered: Identify, sign-out, the error page.</summary>
    public class AllowWithoutNameAttribute : System.Attribute { }

    /// <summary>
    /// Global gate. No name in the session: redirect to Identify with a return URL.
    /// Name present: re-read the roster row so level, Line and Branch are current on every click.
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

            // Case-insensitive, active rows only, same as Identify. The column compares binary.
            // No row = an off-roster name: Viewer, blank Line and Branch.
            string lookup = _user.Name.ToLower();
            var row = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == lookup && u.IsActive);

            int level = row?.AccessLevel ?? AccessLevels.Viewer;
            string line = row?.Line ?? "";
            string branch = row?.Branch ?? "";

            // Write only what changed: a session write re-sends the cookie.
            if (_user.Level != level) _user.SetLevel(level);
            if (_user.Line != line) _user.SetLine(line);
            if (_user.Branch != branch) _user.SetBranch(branch);

            await next();
        }
    }
}
