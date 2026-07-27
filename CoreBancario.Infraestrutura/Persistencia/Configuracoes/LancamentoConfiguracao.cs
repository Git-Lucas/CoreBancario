using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBancario.Infraestrutura.Persistencia.Configuracoes;

public class LancamentoConfiguracao : IEntityTypeConfiguration<Lancamento>
{
    public void Configure(EntityTypeBuilder<Lancamento> builder)
    {
        builder.ToTable("lancamentos");

        builder.HasKey(l => l.Id).HasName("pk_lancamentos");

        // Sem DEFAULT no banco: a ausência de geração pela aplicação deve falhar como NOT NULL,
        // nunca ser mascarada por um id gerado tardiamente pelo banco — o id precisa existir
        // antes da persistência para servir de correlation id e chave de idempotência.
        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Valor, valor => new LancamentoId(valor))
            .ValueGeneratedNever();

        builder.Property(l => l.ContaId)
            .HasColumnName("conta_id")
            .HasConversion(id => id.Valor, valor => new ContaId(valor))
            .IsRequired();

        builder.Property(l => l.LiquidacaoId)
            .HasColumnName("liquidacao_id")
            .HasConversion(id => id.Valor, valor => new LiquidacaoId(valor))
            .IsRequired();

        // Complex type, não OwnsOne: duas colunas na própria tabela, sem entidade-sombra com
        // identidade própria.
        builder.ComplexProperty(l => l.Valor, valorBuilder =>
        {
            valorBuilder.Property(v => v.Valor)
                .HasColumnName("valor")
                .HasColumnType("numeric(19,2)")
                .IsRequired();

            valorBuilder.Property(v => v.Moeda)
                .HasColumnName("moeda")
                .HasConversion<string>()
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(l => l.ContraparteId)
            .HasColumnName("contraparte_id")
            .HasConversion(id => id.Valor, valor => new ContaId(valor))
            .IsRequired();

        builder.Property(l => l.ContraparteNome)
            .HasColumnName("contraparte_nome")
            .HasMaxLength(200)
            .IsRequired();

        // Shadow property: data_criacao não é estado de domínio, é derivada pelo banco a partir
        // do id (GENERATED ALWAYS AS uuid_extract_timestamp(id) STORED) — se fosse escrita pela
        // aplicação minutos depois de o id nascer, filtrar por data e filtrar por id-como-tempo
        // dariam respostas diferentes. A coluna gerada em si vem em SQL bruto na migration; aqui
        // só descrevemos o mapeamento para o EF não tentar escrevê-la.
        builder.Property<DateTimeOffset>("DataCriacao")
            .HasColumnName("data_criacao")
            .HasColumnType("timestamptz")
            .ValueGeneratedOnAdd();
    }
}
