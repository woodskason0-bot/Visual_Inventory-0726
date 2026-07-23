using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// A second, independent lock on the Settings area -- separate from the
    /// AccessLevel system on purpose, since Settings is what edits AccessLevel.
    /// The passcode and the allowed name both live in appsettings.json
    /// (Superuser:UserName / Superuser:Passcode), never in the database, so
    /// neither can be changed or discovered through the mechanism it guards.
    /// This is NOT meant to be rotated from inside the app -- if you ever
    /// need to change it, edit appsettings.json directly.
    /// </summary>
    public class SuperuserGateService
    {
        public const string UnlockKey = "_SuperuserUnlocked";

        private readonly IHttpContextAccessor _ctx;
        private readonly IConfiguration _config;

        public SuperuserGateService(IHttpContextAccessor ctx, IConfiguration config)
        {
            _ctx = ctx;
            _config = config;
        }

        /// <summary>The one session-name allowed to even attempt the passcode.</summary>
        public string SuperuserName => _config["Superuser:UserName"] ?? "Kason.Woods";

        /// <summary>True once this browser session has entered the correct passcode.</summary>
        public bool IsUnlocked =>
            _ctx.HttpContext?.Session?.GetString(UnlockKey) == "1";

        public bool TryUnlock(string passcodeAttempt)
        {
            var real = _config["Superuser:Passcode"] ?? "";
            if (!string.IsNullOrEmpty(real) && passcodeAttempt == real)
            {
                _ctx.HttpContext?.Session?.SetString(UnlockKey, "1");
                return true;
            }
            return false;
        }

        public void Lock() => _ctx.HttpContext?.Session?.Remove(UnlockKey);
    }
}
