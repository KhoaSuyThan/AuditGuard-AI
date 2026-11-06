using System.Text.RegularExpressions;
using AuditGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuditGuard.Infrastructure.Persistence;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<POItem> POItems => Set<POItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GRItem> GRItems => Set<GRItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<AuditSession> AuditSessions => Set<AuditSession>();
    public DbSet<AuditDiscrepancy> AuditDiscrepancies => Set<AuditDiscrepancy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Kích hoạt extension pgvector và uuid-ossp
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("uuid-ossp");

        // Cấu hình bảng vendors
        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.ToTable("vendors");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TaxCode).IsUnique();
        });

        // Cấu hình bảng purchase_orders & po_items
        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.ToTable("purchase_orders");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PoNumber).IsUnique();
            entity.HasMany(e => e.Items)
                  .WithOne(e => e.PurchaseOrder)
                  .HasForeignKey(e => e.PoId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<POItem>(entity =>
        {
            entity.ToTable("po_items");
            entity.HasKey(e => e.Id);
        });

        // Cấu hình bảng goods_receipts & gr_items
        modelBuilder.Entity<GoodsReceipt>(entity =>
        {
            entity.ToTable("goods_receipts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GrnNumber).IsUnique();
            entity.HasMany(e => e.Items)
                  .WithOne(e => e.GoodsReceipt)
                  .HasForeignKey(e => e.GrId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GRItem>(entity =>
        {
            entity.ToTable("gr_items");
            entity.HasKey(e => e.Id);
        });

        // Cấu hình bảng invoices & invoice_items
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.VendorTaxCode, e.InvoiceNumber });
            entity.HasMany(e => e.Items)
                  .WithOne(e => e.Invoice)
                  .HasForeignKey(e => e.InvoiceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.ToTable("invoice_items");
            entity.HasKey(e => e.Id);
        });

        // Cấu hình bảng audit_sessions
        modelBuilder.Entity<AuditSession>(entity =>
        {
            entity.ToTable("audit_sessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionCode).IsUnique();
            entity.Property(e => e.Status)
                  .HasConversion<string>();
            entity.HasMany(e => e.Discrepancies)
                  .WithOne(e => e.Session)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Cấu hình bảng audit_discrepancies
        modelBuilder.Entity<AuditDiscrepancy>(entity =>
        {
            entity.ToTable("audit_discrepancies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DiscrepancyType)
                  .HasConversion<string>();
            entity.Property(e => e.Severity)
                  .HasConversion<string>();
        });

        // Tự động chuyển đổi tất cả tên bảng và tên cột sang snake_case để khớp 100% với 01-init.sql
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (!string.IsNullOrEmpty(tableName))
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}
