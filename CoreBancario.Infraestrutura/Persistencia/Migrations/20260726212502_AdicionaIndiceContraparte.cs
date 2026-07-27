using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBancario.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaIndiceContraparte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D10/D4 em design.md (PRD-2): a assimetria da partida dobrada faz o nome de uma
            // conta só existir nas linhas em que ela figura como contraparte — nenhum índice
            // anterior (pk, ix_lancamentos_extrato, ux_lancamentos_idempotencia) chaveia por
            // contraparte_id. Sem este índice, a resolução do nome seria varredura sequencial.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_lancamentos_contraparte
                    ON lancamentos (contraparte_id)
                    INCLUDE (contraparte_nome);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_lancamentos_contraparte;");
        }
    }
}
