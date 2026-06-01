
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Models;

namespace StarComplexAPI.Data
{
    public class StarComplexContext : DbContext
    {
        public StarComplexContext(DbContextOptions<StarComplexContext> options)
            : base(options)
        {
        }

        public DbSet<SecurityLog> SecurityLogs { get; set; }
        public DbSet<HousingUnit> HousingUnits { get; set; }
        public DbSet<Resident> Residents { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<FinancialPayment> FinancialPayments { get; set; }
        public DbSet<FinancialConstant> FinancialConstants { get; set; }
        public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
        public DbSet<Visit> Visits { get; set; }
        public DbSet<Blacklist> Blacklist { get; set; }
        public DbSet<FamilyMember> FamilyMembers { get; set; }
        public DbSet<ResidentArchive> ResidentArchives { get; set; }
        public DbSet<EmployeeArchive> EmployeesArchive { get; set; }
        public DbSet<ResidentReport> ResidentReport { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── جداول محددة الأسماء ──
            modelBuilder.Entity<FinancialConstant>().ToTable("financial_constants");
            modelBuilder.Entity<MaintenanceRequest>().ToTable("maintenance_requests");

            // ── EmployeeArchive ──
            // نحدد كل شيء بشكل صريح لمنع EF من توليد أسماء أعمدة غلط
            modelBuilder.Entity<EmployeeArchive>(entity =>
            {
                entity.ToTable("employees_archive");
                entity.HasKey(e => e.archive_id);
                entity.Property(e => e.archive_id)
                      .HasColumnName("archive_id")
                      .ValueGeneratedOnAdd();  // auto increment
                entity.Property(e => e.employee_id).HasColumnName("employee_id");
                entity.Property(e => e.first_name).HasColumnName("first_name");
                entity.Property(e => e.second_name).HasColumnName("second_name");
                entity.Property(e => e.third_name).HasColumnName("third_name");
                entity.Property(e => e.job_title).HasColumnName("job_title");
                entity.Property(e => e.phone_number).HasColumnName("phone_number");
                entity.Property(e => e.archived_at).HasColumnName("archived_at");
            });

            // ── FinancialConstant ──
            modelBuilder.Entity<FinancialConstant>()
                .HasKey(f => f.service_id);

            // ── MaintenanceRequest ──
            modelBuilder.Entity<MaintenanceRequest>()
                .HasKey(m => m.request_id);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne<FinancialConstant>()
                .WithMany()
                .HasForeignKey(m => m.service_id);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne<HousingUnit>()
                .WithMany()
                .HasForeignKey(m => m.unit_id);
        }
    }
}
