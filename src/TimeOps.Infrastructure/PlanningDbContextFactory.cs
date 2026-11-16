using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace TimeOps.Infrastructure;

public sealed class PlanningDbContextFactory : IDesignTimeDbContextFactory<PlanningDbContext>
{
    public PlanningDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PlanningDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__TimeOps") ?? "Host=localhost;Database=timeops;Username=timeops").Options);
}
