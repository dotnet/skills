using Microsoft.EntityFrameworkCore;
class MigrationRunner
{
    void ApplyMigrations(AppDbContext db)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        strategy.Execute(() =>
        {
            using var transaction = db.Database.BeginTransaction();
            db.Database.Migrate();
            transaction.Commit();
        });
    }
}
