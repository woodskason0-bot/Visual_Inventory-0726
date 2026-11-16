using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Services;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllersWithViews(options =>
{
    // Name required before any action (Identify page).
    options.Filters.Add<Visual_Inventory_System.Services.RequireNameFilter>();
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SuperuserGateService>();
builder.Services.AddScoped<TrackedUnitService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddHostedService<ReservationSweepService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    // Own name: cookies scope by host, not port. Sourcing Tool shares the host.
    options.Cookie.Name = ".VisualInventory.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Database: migrate, first-run seed, vocabulary caches
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();

    try
    {
        db.Database.Migrate();
        Console.WriteLine(">>> Database connection established successfully.");

        // First run only: email routing.
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

        // First run only: starter roster.
        if (!db.Users.Any())
        {
            // Seed at least one Admin: levels only change from inside the app.
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
            db.SaveChanges(); // Ids needed below

            // Seeded Standard: pickup alerts on.
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

        // Standard users with no pickup-alert row get one. Opt-outs are left alone.
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

        // Static caches with no db access. Loaded here, reloaded on Settings edits.
        OrgStructure.Refresh(db.OrgLines.Where(l => l.IsActive && l.Branch!.IsActive)
            .Select(l => new ValueTuple<string, string>(l.Branch!.Name, l.Name)).ToList());
        LocationCodec.Refresh(db.Locations.Where(l => l.IsActive).Select(l => l.Name).ToList());
    }
    catch (Exception ex)
    {
        Console.WriteLine(">>> Critical Error during Database Initialization: " + ex.Message);
    }
}

// Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Delivery photos: stored outside the publish folder, served by URL.
Directory.CreateDirectory(DeliveryPhotoStorage.RootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(DeliveryPhotoStorage.RootPath),
    RequestPath = DeliveryPhotoStorage.RequestPathPrefix
});

app.UseRouting();

// Session: after Routing, before Authorization.
app.UseSession();
app.UseAuthorization();


app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
