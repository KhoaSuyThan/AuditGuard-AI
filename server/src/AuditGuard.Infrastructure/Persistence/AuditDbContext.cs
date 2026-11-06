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

        // Kích hoạt pgvector extension trong PostgreSQL
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("uuid-ossp");

        // Cấu hình bảng Vendor
        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.ToTable("vendors");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TaxCode).IsUnique();
        });

        // Cấu hình bảng Invoice (Bỏ unique cứng để hỗ trợ lưu vết duplicate)
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.VendorTaxCode, e.InvoiceNumber });
            entity.HasIndex(e => e.PaymentStatus);
        });

        modelBuilder.Entity<AuditSession>(entity =>
        {
            entity.ToTable("audit_sessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionCode).IsUnique();
        });

        modelBuilder.Entity<AuditDiscrepancy>(entity =>
        {
            entity.ToTable("audit_discrepancies");
            entity.HasKey(e => e.Id);
        });
    }
}
