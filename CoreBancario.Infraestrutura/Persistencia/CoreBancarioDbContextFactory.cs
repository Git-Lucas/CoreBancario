using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBancario.Infraestrutura.Persistencia;

/// <summary>
/// Usada apenas pela ferramenta `dotnet ef` para gerar migrations sem depender de um host
/// completo. A connection string aqui não precisa apontar para um banco real.
/// </summary>
public class CoreBancarioDbContextFactory : IDesignTimeDbContextFactory<CoreBancarioDbContext>
{
    public CoreBancarioDbContext CreateDbContext(string[] args)
    {
        var opcoes = new DbContextOptionsBuilder<CoreBancarioDbContext>()
            .UseNpgsql("Host=localhost;Database=corebancario")
            .Options;

        return new CoreBancarioDbContext(opcoes);
    }
}
