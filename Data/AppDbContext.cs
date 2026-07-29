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
        }
    }
}
