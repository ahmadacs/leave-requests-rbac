using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LeaveRequests.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ICurrentUser? _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            entity.HasIndex(t => t.Slug).IsUnique();
        });

        builder.Entity<Department>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(d => new { d.TenantId, d.Name }).IsUnique();

            entity.HasOne(d => d.Tenant)
                .WithMany(t => t.Departments)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();

            // Emails are unique across the whole system.
            entity.HasIndex(u => u.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();

            entity.HasOne(u => u.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(u => u.Department)
                .WithMany(d => d.Users)
                .HasForeignKey(u => u.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.Property(r => r.Description).HasMaxLength(500);

            // Role names are unique per tenant, not globally. This replaces
            // Identity's default global unique RoleNameIndex.
            entity.HasIndex(r => r.NormalizedName)
                .HasDatabaseName("RoleNameIndex")
                .IsUnique(false);
            entity.HasIndex(r => new { r.TenantId, r.NormalizedName })
                .HasDatabaseName("RoleName_Tenant_Index")
                .IsUnique();

            entity.HasOne(r => r.Tenant)
                .WithMany(t => t.Roles)
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Permission>(entity =>
        {
            entity.Property(p => p.Code).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Group).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(300).IsRequired();
            entity.HasIndex(p => p.Code).IsUnique();
        });

        builder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(rp => new { rp.RoleId, rp.PermissionId });

            entity.HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();

            entity.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LeaveRequest>(entity =>
        {
            entity.Property(r => r.Reason).HasMaxLength(1000).IsRequired();

            entity.HasIndex(r => new { r.TenantId, r.DepartmentId });
            entity.HasIndex(r => new { r.EmployeeId, r.Status });

            entity.HasOne(r => r.Tenant)
                .WithMany(t => t.LeaveRequests)
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Department)
                .WithMany(d => d.LeaveRequests)
                .HasForeignKey(r => r.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Employee)
                .WithMany(u => u.LeaveRequests)
                .HasForeignKey(r => r.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.ReviewedBy)
                .WithMany()
                .HasForeignKey(r => r.ReviewedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant isolation: a request from another company is never
            // visible. When there is no tenant in context (seeding, migrations,
            // background work) the filter is disabled.
            entity.HasQueryFilter(r =>
                _currentUser == null
                || _currentUser.TenantId == null
                || r.TenantId == _currentUser.TenantId);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.ActorName).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Details).HasMaxLength(4000).IsRequired();
            entity.HasIndex(a => new { a.TenantId, a.CreatedAtUtc });

            entity.HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(a =>
                _currentUser == null
                || _currentUser.TenantId == null
                || a.TenantId == _currentUser.TenantId);
        });
    }
}
