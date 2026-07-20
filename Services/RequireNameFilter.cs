using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Mark an action with [AllowWithoutName] to let it run before a name has
    /// been entered (the name-entry page, sign-out, and the error page).
    /// </summary>
    public class AllowWithoutNameAttribute : System.Attribute { }

    /// <summary>
    /// Global gate: if no name is stored in the session, redirect to the
    /// Identify page (carrying a return URL) instead of running the action.
    /// Actions marked [AllowWithoutName] are skipped so we don't loop.
    /// Only runs for MVC actions, so static files are unaffected.
    /// </summary>
    public class RequireNameFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            bool allowed = context.ActionDescriptor.EndpointMetadata
                .OfType<AllowWithoutNameAttribute>().Any();
            if (allowed) return;

            var name = context.HttpContext.Session.GetString(CurrentUserService.SessionKey);
            if (string.IsNullOrWhiteSpace(name))
            {
                var returnUrl = context.HttpContext.Request.Path
                                + context.HttpContext.Request.QueryString;
                context.Result = new RedirectToActionResult(
                    "Identify", "Home", new { returnUrl });
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
