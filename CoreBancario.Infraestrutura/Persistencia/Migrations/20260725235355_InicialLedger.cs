using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBancario.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class InicialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lancamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contraparte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contraparte_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    moeda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(19,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos", x => x.id);
                });

            // Coluna gerada pelo banco a partir do id, para que data_criacao nunca possa divergir
            // do instante codificado na identidade v7 — se fosse escrita pela aplicação minutos
            // depois de o id nascer, filtrar por data e filtrar por id-como-tempo dariam
            // respostas diferentes. Validado empiricamente (PG 18.4): uuid_extract_timestamp é
            // aceita dentro de GENERATED ... STORED, que exige função imutável.
            migrationBuilder.Sql(
                """
                ALTER TABLE lancamentos
                    ADD COLUMN data_criacao timestamptz
                    GENERATED ALWAYS AS (uuid_extract_timestamp(id)) STORED NOT NULL;
                """);

            // Garantia de append-only reforçada pelo banco, não só por convenção de código —
            // domínio sem caminho de mutação seria violável por qualquer UPDATE manual e não
            // seria demonstrável. TRUNCATE fica fora do escopo — é operação row-level, e TRUNCATE
            // não dispara trigger BEFORE UPDATE/DELETE; deixá-lo livre permite recriar a massa de
            // dados durante o desenvolvimento sem um caminho oficial de reset.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION fn_lancamentos_bloqueia_update_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'lancamentos é append-only: % não é permitido', TG_OP;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_lancamentos_bloqueia_update_delete
                BEFORE UPDATE OR DELETE ON lancamentos
                FOR EACH ROW
                EXECUTE FUNCTION fn_lancamentos_bloqueia_update_delete();
                """);

            // Índice de cobertura do extrato — a chave filtra e ordena, o INCLUDE cobre a
            // listagem sem tocar o heap (Index Only Scan, Heap Fetches: 0), o que torna o custo
            // de acesso da página 5.000 igual ao da página 1.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_lancamentos_extrato
                    ON lancamentos (conta_id, id DESC)
                    INCLUDE (valor, contraparte_nome);
                """);

            // Idempotência da liquidação: a mesma liquidação não pode produzir mais de
            // um lançamento para a mesma conta.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_lancamentos_idempotencia
                    ON lancamentos (liquidacao_id, conta_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_lancamentos_idempotencia;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_lancamentos_extrato;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_lancamentos_bloqueia_update_delete ON lancamentos;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_lancamentos_bloqueia_update_delete();");

            migrationBuilder.Sql("ALTER TABLE lancamentos DROP COLUMN data_criacao;");

            migrationBuilder.DropTable(
                name: "lancamentos");
        }
    }
}
