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

            // D5/D6 em design.md: coluna gerada pelo banco a partir do id, para que data_criacao
            // nunca possa divergir do instante codificado na identidade v7. Premissa de que
            // uuid_extract_timestamp é aceita em GENERATED ... STORED validada em 1.2 (PG 18.4).
            migrationBuilder.Sql(
                """
                ALTER TABLE lancamentos
                    ADD COLUMN data_criacao timestamptz
                    GENERATED ALWAYS AS (uuid_extract_timestamp(id)) STORED NOT NULL;
                """);

            // D7 em design.md: garantia de append-only reforçada pelo banco. TRUNCATE fica fora
            // do escopo — é operação row-level, e TRUNCATE não dispara trigger BEFORE UPDATE/DELETE.
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

            // D9 em design.md: índice de cobertura do extrato — a chave filtra e ordena,
            // o INCLUDE cobre a listagem sem tocar o heap (Index Only Scan, Heap Fetches: 0).
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_lancamentos_extrato
                    ON lancamentos (conta_id, id DESC)
                    INCLUDE (valor, contraparte_nome);
                """);

            // Idempotência da liquidação (PRD-2): a mesma liquidação não pode produzir mais de
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
