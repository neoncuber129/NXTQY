using Inventory.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<IssueLine> IssueLines => Set<IssueLine>();
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();
    public DbSet<StockPeriod> StockPeriods => Set<StockPeriod>();
    public DbSet<SubordinateUnit> SubordinateUnits => Set<SubordinateUnit>();
    public DbSet<IssueLineAllocation> IssueLineAllocations => Set<IssueLineAllocation>();
    public DbSet<AppMeta> AppMeta => Set<AppMeta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppMeta>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ItemName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(255);
            entity.Property(x => x.Unit).HasMaxLength(50);
            entity.Property(x => x.UsageType).HasMaxLength(255);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.NoteManualAt);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.HasIndex(x => x.ItemCode).IsUnique();
            // Biến thể: cùng tên + đơn giá được phép nếu khác danh mục và/hoặc loại hàng.
            entity.HasIndex(x => new { x.ItemName, x.UnitPrice, x.Category, x.UsageType }).IsUnique();
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ReceiptNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DeliveredBy).HasMaxLength(255).IsRequired();
            entity.Property(x => x.DeliveredUnit).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ReceivedBy).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.TotalAmountText).HasMaxLength(500);
            entity.HasIndex(x => x.ReceiptNumber).IsUnique();
            entity.HasIndex(x => x.ReceiptDate);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReceiptLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.HasIndex(x => x.ItemId);
        });

        modelBuilder.Entity<Issue>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IssueNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ReceivedBy).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ReceivedUnit).HasMaxLength(255).IsRequired();
            entity.Property(x => x.DeliveredBy).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.TotalAmountText).HasMaxLength(500);
            entity.HasIndex(x => x.IssueNumber).IsUnique();
            entity.HasIndex(x => x.ParentIssueId);
            entity.HasIndex(x => x.IssueDate);
            entity.HasIndex(x => new { x.SubordinateUnitId, x.IssueDate });
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Issue>().WithMany().HasForeignKey(x => x.ParentIssueId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IssueLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.HasIndex(x => x.ItemId);
        });

        modelBuilder.Entity<StockLedgerEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TransactionType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DocumentNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.QuantityIn).HasPrecision(18, 3);
            entity.Property(x => x.QuantityOut).HasPrecision(18, 3);
            entity.Property(x => x.BalanceAfter).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.ItemId, x.TransactionDate });
            entity.HasIndex(x => new { x.TransactionType, x.DocumentNumber });
        });

        modelBuilder.Entity<StockPeriod>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OpeningQuantity).HasPrecision(18, 3);
            entity.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
            entity.Property(x => x.IssuedQuantity).HasPrecision(18, 3);
            entity.Property(x => x.ClosingQuantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.HasIndex(x => new { x.Year, x.ItemId }).IsUnique();
        });

        modelBuilder.Entity<SubordinateUnit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UnitCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ReceivedBy).HasMaxLength(255).IsRequired();
            entity.Property(x => x.DeliveredBy).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.HasIndex(x => x.UnitCode);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<IssueLineAllocation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.HasIndex(x => new { x.IssueLineId, x.SubordinateUnitId }).IsUnique();
        });

        modelBuilder.Entity<IssueLine>()
            .HasMany(x => x.Allocations)
            .WithOne()
            .HasForeignKey(x => x.IssueLineId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}
