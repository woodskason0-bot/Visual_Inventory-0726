using Microsoft.EntityFrameworkCore;
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

            foreach (var (full, level) in seedUsers)
            {
                var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                db.Users.Add(new Visual_Inventory_System.Models.User
                {
                    DisplayName = string.Join(" ", parts),
                    UserName = parts[0] + "." + parts[parts.Length - 1], // First.Last
                    Theme = "dark",
                    IsActive = true,
                    AccessLevel = level
                });
            }
            db.SaveChanges();
            Console.WriteLine(">>> Seeded starter user roster.");
        }
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
