using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// The second lock on Settings, separate from AccessLevel because Settings edits AccessLevel.
    /// Name and passcode come from configuration, never the database.
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
