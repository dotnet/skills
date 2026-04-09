using System;
using System.Linq;
using ContosoUniversity.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoUniversity.Data
{
    public class SchoolContext : DbContext
    {
        public SchoolContext(DbContextOptions<SchoolContext> options) : base(options)
        {
        }

        public DbSet<Course> Courses { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<OfficeAssignment> OfficeAssignments { get; set; }
        public DbSet<CourseAssignment> CourseAssignments { get; set; }
        public DbSet<Person> People { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Instructor> Instructors { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure all DateTime properties to use datetime2
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?));

                foreach (var property in properties)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasColumnType("datetime2");
                }
            }

            modelBuilder.Entity<Course>().ToTable("Course");
            modelBuilder.Entity<Enrollment>().ToTable("Enrollment");
            modelBuilder.Entity<Department>().ToTable("Department");
            modelBuilder.Entity<OfficeAssignment>().ToTable("OfficeAssignment");
            modelBuilder.Entity<CourseAssignment>().ToTable("CourseAssignment");
            modelBuilder.Entity<Notification>().ToTable("Notification");

            // Configure Table-per-Hierarchy (TPH) inheritance for Person
            // Map the base Person class and its derived classes to a single table
            modelBuilder.Entity<Person>()
                .ToTable("Person")
                .HasDiscriminator<string>("Discriminator")
                .HasValue<Student>("Student")
                .HasValue<Instructor>("Instructor");

            // Configure composite key for CourseAssignment
            modelBuilder.Entity<CourseAssignment>()
                .HasKey(c => new { c.CourseID, c.InstructorID });

            // Configure relationships
            modelBuilder.Entity<CourseAssignment>()
                .HasOne(m => m.Course)
                .WithMany(t => t.CourseAssignments)
                .HasForeignKey(m => m.CourseID);

            modelBuilder.Entity<CourseAssignment>()
                .HasOne(m => m.Instructor)
                .WithMany(t => t.CourseAssignments)
                .HasForeignKey(m => m.InstructorID);

            // Configure one-to-one relationship
            modelBuilder.Entity<Instructor>()
                .HasOne(s => s.OfficeAssignment)
                .WithOne(ad => ad.Instructor)
                .HasForeignKey<OfficeAssignment>(ad => ad.InstructorID);
        }
    }
}
