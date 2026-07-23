using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>Marks the Unlock action as reachable before the passcode gate passes.</summary>
    public class AllowWithoutSuperuserAttribute : Attribute { }

    /// <summary>
    /// Put on SettingsController (class-level). Blocks anyone whose session
    /// name isn't the configured Superuser:UserName, AND anyone who hasn't
    /// entered the passcode this session -- two independent checks, neither
    /// derived from AccessLevel, so this can't be opened by raising someone's
    /// tier in the Users table.
    /// </summary>
    public class RequireSuperuserAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            bool allowed = context.ActionDescriptor.EndpointMetadata
                .OfType<AllowWithoutSuperuserAttribute>().Any();

            var currentUser = context.HttpContext.RequestServices.GetService(typeof(CurrentUserService)) as CurrentUserService;
            var gate = context.HttpContext.RequestServices.GetService(typeof(SuperuserGateService)) as SuperuserGateService;

            if (currentUser == null || gate == null)
            {
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            bool nameMatches = string.Equals(currentUser.Name, gate.SuperuserName, StringComparison.OrdinalIgnoreCase);
            if (!nameMatches)
            {
                if (context.Controller is Controller c)
                    c.TempData["AuthError"] = "That page isn't available.";
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            if (allowed)
            {
                base.OnActionExecuting(context);
                return;
            }

            if (!gate.IsUnlocked)
            {
                context.Result = new RedirectToActionResult("Unlock", "Settings", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
