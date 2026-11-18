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
        public DbSet<Delivery> Deliveries { get; set; } = null!;
        public DbSet<TransferRequest> TransferRequests { get; set; } = null!;
        public DbSet<TrackedUnit> TrackedUnits { get; set; } = null!;
        public DbSet<Reservation> Reservations { get; set; } = null!;
        public DbSet<ReservationLine> ReservationLines { get; set; } = null!;


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<InventoryItem>(b =>
            {
                // Unique, but blank and "N/A" are exempt.
                b.HasIndex(i => i.RheemPartNumber)
                 .IsUnique()
                 .HasFilter("\"RheemPartNumber\" <> '' AND \"RheemPartNumber\" <> 'N/A'");

                // One item per ItemId. Two registrations at once can't both keep a number.
                b.HasIndex(i => i.ItemId).IsUnique();
            });

            modelBuilder.Entity<ItemVariant>(b =>
            {
                b.HasKey(v => v.Id);

                b.HasOne(v => v.InventoryItem)
                 .WithMany(i => i.Variants)
                 .HasForeignKey(v => v.InventoryItemId)
                 .OnDelete(DeleteBehavior.Cascade);

                // Not unique: retired rows keep their number and a freed number can be reused.
                b.HasIndex(v => new { v.InventoryItemId, v.VariantNumber });
            });

            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.CreatedAt).IsRequired();
                b.Property(o => o.Status).HasMaxLength(50);

                // OrderDetails looks up "what split off this order" on every view.
                b.HasIndex(o => o.SplitFromOrderId);
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
                // One row per key. UpdateSetting upserts on it.
                b.HasIndex(s => s.Key).IsUnique();
            });

            modelBuilder.Entity<NotificationSubscription>(b =>
            {
                // One row per (user, category). UpdateNotificationSubscription upserts on it.
                b.HasIndex(s => new { s.UserId, s.Category }).IsUnique();

                b.HasOne(s => s.User)
                 .WithMany()
                 .HasForeignKey(s => s.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<IntakeBatch>(b =>
            {
                // The table is the queue. Keeps "what is waiting" cheap.
                b.HasIndex(x => x.Status);
                b.HasMany(x => x.Rows)
                 .WithOne(r => r.Batch!)
                 .HasForeignKey(r => r.IntakeBatchId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LocationZone>(b =>
            {
                // Cascade: a zone pointing at no Parent would be an unclickable ghost.
                b.HasOne<Location>()
                 .WithMany()
                 .HasForeignKey(z => z.LocationId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(z => z.LocationId);
            });

            modelBuilder.Entity<Location>(b =>
            {
                // Unique per (level, owner, name): the same Sub name under two Majors is fine.
                b.HasIndex(l => new { l.Level, l.ParentId, l.Name }).IsUnique();
            });

            modelBuilder.Entity<Team>(b =>
            {
                // One row per name. A picker source, not a foreign key.
                b.HasIndex(t => t.Name).IsUnique();
            });

            modelBuilder.Entity<Branch>(b =>
            {
                // One row per name, like Team.
                b.HasIndex(x => x.Name).IsUnique();
            });

            modelBuilder.Entity<OrgLine>(b =>
            {
                // Unique across all branches: Line strings carry no branch.
                b.HasIndex(x => x.Name).IsUnique();
                b.HasIndex(x => x.BranchId);

                b.HasOne(x => x.Branch)
                 .WithMany()
                 .HasForeignKey(x => x.BranchId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CompressorUnit>(b =>
            {
                // Every read is "all units for this ItemId".
                // Trap: the snapshot declares this index. Drop it here and Migrate() aborts.
                b.HasIndex(c => c.ItemId);

                // Unique per (ItemId, SerialNumber): LG reuses serials across models. Blank is exempt.
                b.HasIndex(c => new { c.ItemId, c.SerialNumber })
                 .IsUnique()
                 .HasFilter("\"SerialNumber\" IS NOT NULL AND \"SerialNumber\" <> ''");

                // "Which units does this hold name".
                b.HasIndex(c => c.ReservationId);
            });

            modelBuilder.Entity<MotorUnit>(b =>
            {
                // Same index and same Migrate() trap as CompressorUnit. No unique key: LabNumber is often blank.
                b.HasIndex(c => c.ItemId);
                b.HasIndex(c => c.ReservationId);
            });

            modelBuilder.Entity<UserTeam>(b =>
            {
                // One row per (user, team). The Settings team picker diffs against it.
                b.HasIndex(x => new { x.UserId, x.TeamName }).IsUnique();

                b.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Delivery>(b =>
            {
                // The board reads "not Done, for this recipient or the Unknown bucket".
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.RecipientUserName);
            });

            modelBuilder.Entity<TransferRequest>(b =>
            {
                // Read by requester (My Requests) and by item (Awaiting Approval).
                b.HasIndex(x => x.RequesterUserName);
                b.HasIndex(x => x.ItemId);
            });

            modelBuilder.Entity<TrackedUnit>(b =>
            {
                // The list filters on Line. Declared before `migrations add`: same Migrate() trap.
                b.HasIndex(u => u.Line);
            });

            // Reservations: read by item, by status, by who made them. Lines go with their reservation.
            modelBuilder.Entity<Reservation>(b =>
            {
                b.HasIndex(r => r.ItemId);
                b.HasIndex(r => r.Status);
                b.HasIndex(r => r.ReservedBy);
            });

            modelBuilder.Entity<ReservationLine>(b =>
            {
                b.HasOne(l => l.Reservation)
                 .WithMany(r => r.Lines)
                 .HasForeignKey(l => l.ReservationId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(l => l.ItemVariantId);
            });
        }
    }
}
