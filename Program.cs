using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Services;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. SERVICE REGISTRATION
// ==========================================
builder.Services.AddControllersWithViews(options =>
{
    // Require a name (see Identify page) before any action runs.
    options.Filters.Add<Visual_Inventory_System.Services.RequireNameFilter>();
});

// Configure DbContext (SQLite)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Business Logic Services as Scoped
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SuperuserGateService>();

// Configure Session (The "Locker")
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ==========================================
// 2. DATABASE INITIALIZATION
// ==========================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();

    try
    {
        // Apply migrations
        db.Database.Migrate();
        Console.WriteLine(">>> Database connection established successfully.");

        // --- AUTO-SEED EMAIL ROUTING DATA (first run only) ---
        if (!db.EmailRoutings.Any())
        {
            db.EmailRoutings.AddRange(
                new Visual_Inventory_System.Models.EmailRouting
                {
                    Group = "Commercial",
                    Team = "Samurai",
                    ManagerName = "Kevin Ray",
                    ManagerEmail = "kevin.ray@rheem.com",
                    SupervisorName = "Conner Walworth",
                    SupervisorEmail = "conner.walworth@rheem.com"
                },
                new Visual_Inventory_System.Models.EmailRouting
                {
                    Group = "Commercial",
                    Team = "Ninja",
                    ManagerName = "Kevin Ray",
                    ManagerEmail = "kevin.ray@rheem.com",
                    SupervisorName = "James Masters",
                    SupervisorEmail = "james.masters@rheem.com"
                }
            );
            db.SaveChanges();
            Console.WriteLine(">>> Automatically seeded dummy Email Routing data.");
        }
        // -----------------------------------------

        // --- SEED STARTER USER ROSTER (name picker) ---
        if (!db.Users.Any())
        {
            // ===== EDIT THIS LIST: your team, in "First Last" form =====
            // Levels: 1 Viewer, 2 Standard, 3 Engineer, 4 Management, 5 Admin.
            // A fresh database MUST seed at least one Admin -- levels can only
            // be changed from inside the app, so an all-Viewer roster would
            // leave no one able to raise anyone's level (or do anything else).
            var seedUsers = new (string FullName, int Level)[]
            {
                ("Kason Woods",     AccessLevels.Admin),
                ("Kevin Ray",       AccessLevels.Management),
                ("Conner Walworth", AccessLevels.Management),
                ("James Masters",   AccessLevels.Management)
            };

            var newlySeeded = new List<Visual_Inventory_System.Models.User>();
            foreach (var (full, level) in seedUsers)
            {
                var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var seededUser = new Visual_Inventory_System.Models.User
                {
                    DisplayName = string.Join(" ", parts),
                    UserName = parts[0] + "." + parts[parts.Length - 1], // First.Last
                    Theme = "dark",
                    IsActive = true,
                    AccessLevel = level
                };
                db.Users.Add(seededUser);
                newlySeeded.Add(seededUser);
            }
            db.SaveChanges(); // assigns Ids before the subscription pass below

            // Standard tier used to mean "gets pickup alerts" for free (old
            // AccessLevel-band behavior). Preserve that for anyone seeded at
            // Standard now that alerts are an individual opt-in.
            foreach (var u in newlySeeded.Where(u => u.AccessLevel == AccessLevels.Standard))
            {
                db.NotificationSubscriptions.Add(new Visual_Inventory_System.Models.NotificationSubscription
                {
                    UserId = u.Id,
                    Category = "PickupRequested",
                    Enabled = true
                });
            }
            db.SaveChanges();
            Console.WriteLine(">>> Seeded starter user roster.");
        }
        // -----------------------------------------

        // --- BACKFILL: existing Standard-tier users keep their pickup alerts ---
        // Runs every launch but is a no-op after the first pass -- only inserts
        // a subscription row where one is missing, so it can't create duplicates
        // or override anyone who has since turned their alerts off on purpose.
        var standardUserIds = db.Users
            .Where(u => u.AccessLevel == AccessLevels.Standard)
            .Select(u => u.Id)
            .ToList();
        var missingSub = standardUserIds
            .Where(id => !db.NotificationSubscriptions.Any(s => s.UserId == id && s.Category == "PickupRequested"))
            .ToList();
        if (missingSub.Count > 0)
        {
            foreach (var id in missingSub)
            {
                db.NotificationSubscriptions.Add(new Visual_Inventory_System.Models.NotificationSubscription
                {
                    UserId = id,
                    Category = "PickupRequested",
                    Enabled = true
                });
            }
            db.SaveChanges();
            Console.WriteLine($">>> Backfilled PickupRequested subscription for {missingSub.Count} existing Standard-tier user(s).");
        }
        // -----------------------------------------

        // --- REFRESH DB-BACKED VOCABULARY CACHES ---
        // OrgStructure (Branches/Lines) and LocationCodec (Locations) are both
        // static classes read from all over the app with no DB access of their
        // own -- they're loaded here once at boot, then re-loaded after any
        // Settings edit (see SettingsController.RefreshOrgStructure /
        // RefreshLocationCodec). Without this call each fresh process start
        // would run on the compiled-in seed values until someone happened to
        // save something in Settings.
        OrgStructure.Refresh(db.OrgLines.Where(l => l.IsActive && l.Branch!.IsActive)
            .Select(l => new ValueTuple<string, string>(l.Branch!.Name, l.Name)).ToList());
        LocationCodec.Refresh(db.Locations.Where(l => l.IsActive).Select(l => l.Name).ToList());
        // -----------------------------------------
    }
    catch (Exception ex)
    {
        Console.WriteLine(">>> Critical Error during Database Initialization: " + ex.Message);
    }
}

// ==========================================
// 3. MIDDLEWARE PIPELINE
// ==========================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Delivery photos live outside the publish folder (C:\VIS_Image-Uploads), same
// "moved deliberately" reasoning as inventory.db, but still served over a
// normal URL so they're viewable in the app without sitting in wwwroot.
Directory.CreateDirectory(DeliveryPhotoStorage.RootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(DeliveryPhotoStorage.RootPath),
    RequestPath = DeliveryPhotoStorage.RequestPathPrefix
});

app.UseRouting();

// Session MUST be between Routing and Authorization
app.UseSession();
app.UseAuthorization();


//Support for ASP.NET Core 9.0 Optimized Static Assets
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
