using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InventoryDevTwo.Services
{
    /// <summary>
    /// Put [RequireLevel(AccessLevels.Engineer)] on an action to block anyone
    /// below that tier. Blocked users get a friendly banner via TempData and are
    /// sent back to the dashboard (or a 403 for AJAX calls).
    /// </summary>
    public class RequireLevelAttribute : ActionFilterAttribute
    {
        private readonly int _min;

        public RequireLevelAttribute(int minLevel)
        {
            _min = minLevel;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            int level = context.HttpContext.Session.GetInt32(CurrentUserService.LevelKey)
                        ?? AccessLevels.Viewer;

            if (level < _min)
            {
                string message =
                    $"Sorry, you're not authorized to perform this action " +
                    $"(requires {AccessLevels.Name(_min)} access or higher). " +
                    $"Please contact your supervisor or manager for assistance.";

                bool isAjax = context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest";
                if (isAjax)
                {
                    context.Result = new ContentResult
                    {
                        StatusCode = 403,
                        Content = message
                    };
                }
                else
                {
                    if (context.Controller is Controller c)
                        c.TempData["AuthError"] = message;
                    context.Result = new RedirectToActionResult("Index", "Home", null);
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
