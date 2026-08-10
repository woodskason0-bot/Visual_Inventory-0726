using Visual_Inventory_System.Models;
using Microsoft.EntityFrameworkCore;

namespace Visual_Inventory_System.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<InventoryItem> InventoryItems { get; set; } = null!;
        public DbSet<ItemVariant> ItemVariants { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;
        //set email functionality
        public DbSet<EmailRouting> EmailRoutings { get; set; } = null!;
        public DbSet<TransactionLog> TransactionLogs { get; set; } = null!;
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<VisTask> VisTasks { get; set; } = null!;
        public DbSet<AppSetting> AppSettings { get; set; } = null!;
        public DbSet<NotificationSubscription> NotificationSubscriptions { get; set; } = null!;
        public DbSet<CompressorUnit> CompressorUnits { get; set; } = null!;
        public DbSet<MotorUnit> MotorUnits { get; set; } = null!;
        public DbSet<Team> Teams { get; set; } = null!;
        public DbSet<Location> Locations { get; set; } = null!;
        public DbSet<LocationZone> LocationZones { get; set; } = null!;
        public DbSet<IntakeBatch> IntakeBatches { get; set; } = null!;
        public DbSet<IntakeRow> IntakeRows { get; set; } = null!;
        public DbSet<Branch> Branches { get; set; } = null!;
        public DbSet<OrgLine> OrgLines { get; set; } = null!;
        public DbSet<UserTeam> UserTeams { get; set; } = null!;


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<InventoryItem>(b =>
            {
                // Rheem PN is a business identifier like ItemId: no two items
                // may share one. Partial index so blanks and the shared "N/A"
                // sentinel (legacy blanks were folded into "N/A") both coexist.
                b.HasIndex(i => i.RheemPartNumber)
                 .IsUnique()
                 .HasFilter("\"RheemPartNumber\" <> '' AND \"RheemPartNumber\" <> 'N/A'");
            });

            modelBuilder.Entity<ItemVariant>(b =>
            {
                b.HasKey(v => v.Id);

                b.HasOne(v => v.InventoryItem)
                 .WithMany(i => i.Variants)
                 .HasForeignKey(v => v.InventoryItemId)
                 .OnDelete(DeleteBehavior.Cascade);

                // Non-unique on purpose: retired rows KEEP their VariantNumber for
                // history, and a freed number may be reused by a later variant.
                b.HasIndex(v => new { v.InventoryItemId, v.VariantNumber });
            });

            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.CreatedAt).IsRequired();
                b.Property(o => o.Status).HasMaxLength(50);
            });

            modelBuilder.Entity<OrderItem>(b =>
            {
                b.HasKey(oi => oi.Id);

                // Explicitly define the relationship using the properties in the class
                b.HasOne(oi => oi.Order)
                 .WithMany(o => o.Items)
                 .HasForeignKey(oi => oi.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AppSetting>(b =>
            {
                // One row per key -- UpdateSetting() upserts on this.
                b.HasIndex(s => s.Key).IsUnique();
            });

            modelBuilder.Entity<NotificationSubscription>(b =>
            {
                // One row per (user, category) -- UpdateNotificationSubscription() upserts on this.
                b.HasIndex(s => new { s.UserId, s.Category }).IsUnique();

                b.HasOne(s => s.User)
                 .WithMany()
                 .HasForeignKey(s => s.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<IntakeBatch>(b =>
            {
                // Only PENDING batches live here, so the queue is the table -- an
                // index on Status keeps "show me what's waiting" cheap as approved
                // ones accumulate behind it.
                b.HasIndex(x => x.Status);
                b.HasMany(x => x.Rows)
                 .WithOne(r => r.Batch!)
                 .HasForeignKey(r => r.IntakeBatchId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LocationZone>(b =>
            {
                // Cascade: deleting a Parent takes its map rectangles with it --
                // a zone pointing at nothing would render an unclickable ghost.
                b.HasOne<Location>()
                 .WithMany()
                 .HasForeignKey(z => z.LocationId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(z => z.LocationId);
            });

            modelBuilder.Entity<Location>(b =>
            {
                // Unique per (level, owner, name): two "Rack A" Subs under different
                // Majors are legitimate, a duplicate under the same one is not.
                b.HasIndex(l => new { l.Level, l.ParentId, l.Name }).IsUnique();
            });

            modelBuilder.Entity<Team>(b =>
            {
                // One row per team name. Vocabulary table -- items still store the
                // NAME as a plain string (same convention as Line), so this is a
                // picker source, not a foreign key.
                b.HasIndex(t => t.Name).IsUnique();
            });

            modelBuilder.Entity<Branch>(b =>
            {
                // One row per branch name, same vocabulary-table convention as Team.
                b.HasIndex(x => x.Name).IsUnique();
            });

            modelBuilder.Entity<OrgLine>(b =>
            {
                // Global uniqueness, not per-branch: User/InventoryItem/Team.Line
                // are flat strings with no branch qualifier, so two branches can't
                // share a Line name without an ambiguous reverse lookup.
                b.HasIndex(x => x.Name).IsUnique();
                b.HasIndex(x => x.BranchId);

                b.HasOne(x => x.Branch)
                 .WithMany()
                 .HasForeignKey(x => x.BranchId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CompressorUnit>(b =>
            {
                // Every read of this table is "all units for this ItemId"
                // (GetCompressorUnitsGrouped, the sidebar Compressors modal),
                // so ItemId carries an index.
                //
                // This block is not optional. Migration 20260727 creates
                // IX_CompressorUnits_ItemId, so the snapshot declares it;
                // without a matching HasIndex the runtime model and the
                // snapshot disagree, EF raises PendingModelChangesWarning, and
                // Migrate() aborts before applying ANY migration -- which is
                // what silently blocked Passes 3, 4 and 5.
                b.HasIndex(c => c.ItemId);

                // A model may not carry the same serial twice; two DIFFERENT
                // models may share one, because LG reuses serials across model
                // lines. So the unique key is the PAIR. Blank/NULL exempt --
                // most units have no serial captured yet.
                b.HasIndex(c => new { c.ItemId, c.SerialNumber })
                 .IsUnique()
                 .HasFilter("\"SerialNumber\" IS NOT NULL AND \"SerialNumber\" <> ''");
            });

            modelBuilder.Entity<MotorUnit>(b =>
            {
                // Same reasoning as CompressorUnit above -- every read is "all
                // units for this ItemId", and an un-declared index here is
                // exactly the PendingModelChangesWarning trap that blocked
                // several earlier passes. No SerialNumber-style unique index:
                // LabNumber is the only identifying field and won't always be
                // present, so it can't carry a uniqueness constraint.
                b.HasIndex(c => c.ItemId);
            });

            modelBuilder.Entity<UserTeam>(b =>
            {
                // One membership row per (user, team) -- the team-centric picker's
                // Save diffs against this.
                b.HasIndex(x => new { x.UserId, x.TeamName }).IsUnique();

                b.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
