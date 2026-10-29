using Login.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics; // Required namespace for warnings

namespace Login.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Student> Students { get; set; }
        // Add this under your Student DbSet property to store course configurations
       
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Suppresses the pending model changes exception crash safely
            optionsBuilder.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // This configures SQL Server to automatically insert the current system timestamp
            modelBuilder.Entity<Student>()
                .Property(s => s.RegistrationDate)
                .HasDefaultValueSql("GETDATE()");
        }
    }
}