using Microsoft.AspNetCore.Http;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Who is at the keyboard, held in the session for the audit trail. Not authentication.
    /// RequireNameFilter re-reads level, Line and Branch from the roster on every request.
    /// </summary>
    public class CurrentUserService
    {
        public const string SessionKey = "_CurrentUser";
        public const string LevelKey = "_Level";
        public const string ThemeKey = "_Theme";
        public const string LineKey = "_Line";
        public const string BranchKey = "_Branch";

        private readonly IHttpContextAccessor _ctx;

        public CurrentUserService(IHttpContextAccessor ctx)
        {
            _ctx = ctx;
        }

        /// <summary>The stored name (e.g. "Jane.Doe"), or "Unknown" if none set.</summary>
        public string Name =>
            _ctx.HttpContext?.Session?.GetString(SessionKey) is string s && !string.IsNullOrWhiteSpace(s)
                ? s
                : "Unknown";

        /// <summary>Access tier for this session. Defaults to Viewer (1) if unknown.</summary>
        public int Level =>
            _ctx.HttpContext?.Session?.GetInt32(LevelKey) ?? AccessLevels.Viewer;

        /// <summary>Per-user UI theme ("dark" or "light"). Defaults to dark if unset.</summary>
        public string Theme =>
            _ctx.HttpContext?.Session?.GetString(ThemeKey) is string s && !string.IsNullOrWhiteSpace(s)
                ? s
                : "dark";

        /// <summary>One of OrgStructure.AllLines, or "" = unassigned, which fails open.</summary>
        public string Line =>
            _ctx.HttpContext?.Session?.GetString(LineKey) is string s && !string.IsNullOrWhiteSpace(s)
                ? s
                : "";

        /// <summary>A whole Branch this user sees, or "". Only read when Line is blank.</summary>
        public string Branch =>
            _ctx.HttpContext?.Session?.GetString(BranchKey) is string s && !string.IsNullOrWhiteSpace(s)
                ? s
                : "";

        public bool IsSet =>
            !string.IsNullOrWhiteSpace(_ctx.HttpContext?.Session?.GetString(SessionKey));

        public void Set(string name) =>
            _ctx.HttpContext?.Session?.SetString(SessionKey, name);

        public void SetLevel(int level) =>
            _ctx.HttpContext?.Session?.SetInt32(LevelKey, level);

        public void SetTheme(string theme) =>
            _ctx.HttpContext?.Session?.SetString(ThemeKey, theme);

        public void SetLine(string line) =>
            _ctx.HttpContext?.Session?.SetString(LineKey, line ?? "");

        public void SetBranch(string branch) =>
            _ctx.HttpContext?.Session?.SetString(BranchKey, branch ?? "");

        public void Clear()
        {
            _ctx.HttpContext?.Session?.Remove(SessionKey);
            _ctx.HttpContext?.Session?.Remove(LevelKey);
            _ctx.HttpContext?.Session?.Remove(ThemeKey);
            _ctx.HttpContext?.Session?.Remove(LineKey);
            _ctx.HttpContext?.Session?.Remove(BranchKey);
        }
    }
}
