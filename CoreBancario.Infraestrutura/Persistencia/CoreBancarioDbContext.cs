using CoreBancario.Dominio.Ledger;
using Microsoft.EntityFrameworkCore;

namespace CoreBancario.Infraestrutura.Persistencia;

public class CoreBancarioDbContext(DbContextOptions<CoreBancarioDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreBancarioDbContext).Assembly);
    }
}
