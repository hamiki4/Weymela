using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Weymela.Infrastructure.Persistence;

// Offline design-time only: this address is intentionally unusable. Migrations do not connect.
public sealed class WeymelaDbContextFactory : IDesignTimeDbContextFactory<WeymelaDbContext>
{
    public WeymelaDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<WeymelaDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=weymela_v3_design_only;Username=design_only").Options);
}
