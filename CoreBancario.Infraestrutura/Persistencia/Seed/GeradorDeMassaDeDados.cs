using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CoreBancario.Infraestrutura.Persistencia.Seed;

/// <summary>
/// Ciclo: TRUNCATE + remoção dos índices secundários (a PK fica — v7 insere no fim), carga em SQL
/// puro (uuidv7 do PG gera a massa dentro do banco, sem tráfego de rede para os 1.200.000
/// lançamentos), recriação dos índices (build ordenado, sem bloat; a criação do índice único
/// também valida ausência de duplicatas), ANALYZE, VACUUM. As migrations criam o esquema
/// completo, inclusive os três índices — isso vale para qualquer ambiente; é o comando de seed
/// que otimiza para si mesmo dropando e recriando.
/// </summary>
public static class GeradorDeMassaDeDados
{
    public static async Task ExecutarAsync(string connectionString, ILogger log, CancellationToken cancellationToken = default)
    {
        var cronometro = Stopwatch.StartNew();

        await using var conexao = new NpgsqlConnection(connectionString);
        await conexao.OpenAsync(cancellationToken);

        log.LogInformation("Esvaziando lancamentos e removendo índices secundários...");
        await ExecutarAsync(conexao, "TRUNCATE lancamentos;", cancellationToken);
        await ExecutarAsync(conexao, "DROP INDEX IF EXISTS ix_lancamentos_extrato;", cancellationToken);
        await ExecutarAsync(conexao, "DROP INDEX IF EXISTS ux_lancamentos_idempotencia;", cancellationToken);
        await ExecutarAsync(conexao, "DROP INDEX IF EXISTS ix_lancamentos_contraparte;", cancellationToken);

        log.LogInformation("Gerando contas, pareando liquidações e inserindo 1.200.000 lançamentos...");
        await ExecutarAsync(conexao, ScriptDeGeracao, cancellationToken);

        log.LogInformation("Recriando índices secundários...");
        await ExecutarAsync(
            conexao,
            """
            CREATE INDEX ix_lancamentos_extrato
                ON lancamentos (conta_id, id DESC)
                INCLUDE (valor, contraparte_nome);
            """,
            cancellationToken);
        await ExecutarAsync(
            conexao,
            "CREATE UNIQUE INDEX ux_lancamentos_idempotencia ON lancamentos (liquidacao_id, conta_id);",
            cancellationToken);
        await ExecutarAsync(
            conexao,
            """
            CREATE INDEX ix_lancamentos_contraparte
                ON lancamentos (contraparte_id)
                INCLUDE (contraparte_nome);
            """,
            cancellationToken);

        log.LogInformation("Atualizando estatísticas do planejador (ANALYZE)...");
        await ExecutarAsync(conexao, "ANALYZE lancamentos;", cancellationToken);

        // VACUUM não pode rodar dentro de bloco de transação: precisa ser o único comando do
        // seu próprio NpgsqlCommand, sem ser combinado com outras instruções. ANALYZE e VACUUM
        // não são higiene, são pré-condição dos critérios de aceite: sem estatísticas o planner
        // pode escolher seq scan por enxergar cardinalidade errada, e sem o visibility map
        // preenchido, qualquer index-only scan passa a visitar o heap — ambos falham
        // silenciosamente e por motivo que nada tem a ver com o índice em si.
        log.LogInformation("Preenchendo o mapa de visibilidade (VACUUM)...");
        await ExecutarAsync(conexao, "VACUUM lancamentos;", cancellationToken);

        cronometro.Stop();
        log.LogInformation("Massa de dados gerada em {Duracao}.", cronometro.Elapsed);
    }

    private static async Task ExecutarAsync(NpgsqlConnection conexao, string sql, CancellationToken cancellationToken)
    {
        await using var comando = new NpgsqlCommand(sql, conexao) { CommandTimeout = 600 };
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    // Distribuição enviesada para que a demonstração seja significativa: uma distribuição
    // uniforme daria ~10 lançamentos por conta em 100 mil contas, e "página profunda" deixaria
    // de existir; concentrar demais faria o planner escolher sequential scan e estar certo.
    // Hub (2 contas monstro) só liquida contra a cauda longa (500.000 liquidações); a cauda
    // liquida entre si à parte (100.000) — nunca as duas contas monstro entre elas, senão ~17%
    // das liquidações cairiam entre as mesmas duas contas, estatisticamente correto mas
    // narrativamente absurdo. A técnica de agrupar por conta_id antes de indexar e parear i com
    // i+N (grupos de tamanho bem menor que N) garante, por construção geométrica, que nenhuma
    // conta pareia consigo mesma — sem loop de correção.
    private const string ScriptDeGeracao =
        """
        DROP TABLE IF EXISTS contas_seed;
        CREATE TEMP TABLE contas_seed AS
        WITH faixas(faixa, quantidade, peso) AS (
            VALUES ('monstro', 2, 250000),
                   ('gorda', 8, 25000),
                   ('media', 200, 1000),
                   ('magra', 50000, 6)
        ),
        nomes AS (
            SELECT
                ARRAY['Ana','Bruno','Carla','Daniel','Eduarda','Felipe','Gabriela','Hugo','Isabela','Joao',
                      'Karina','Lucas','Mariana','Nicolas','Olivia','Pedro','Queila','Rafael','Sofia','Thiago',
                      'Ursula','Vitor','Wesley','Ximena','Yasmin','Zeca','Beatriz','Caio','Debora','Enzo'] AS primeiros,
                ARRAY['Silva','Souza','Oliveira','Santos','Pereira','Costa','Rodrigues','Almeida','Nascimento','Lima',
                      'Araujo','Fernandes','Carvalho','Gomes','Martins','Rocha','Ribeiro','Alves','Monteiro','Cardoso',
                      'Teixeira','Correia','Dias','Castro','Campos','Cavalcanti','Duarte','Barros','Freitas','Moreira'] AS sobrenomes
        )
        SELECT
            gen_random_uuid() AS conta_id,
            f.faixa,
            f.peso,
            n.primeiros[1 + floor(random() * array_length(n.primeiros, 1))::int]
                || ' ' ||
            n.sobrenomes[1 + floor(random() * array_length(n.sobrenomes, 1))::int] AS titular
        FROM faixas f
        CROSS JOIN nomes n
        CROSS JOIN generate_series(1, f.quantidade);

        DROP TABLE IF EXISTS participacoes_seed;
        CREATE TEMP TABLE participacoes_seed AS
        SELECT c.conta_id, c.titular, c.faixa
        FROM contas_seed c
        CROSS JOIN LATERAL generate_series(1, c.peso);

        DROP TABLE IF EXISTS participacoes_monstro;
        CREATE TEMP TABLE participacoes_monstro AS
        SELECT conta_id, titular, row_number() OVER (ORDER BY random()) AS idx
        FROM participacoes_seed
        WHERE faixa = 'monstro';

        DROP TABLE IF EXISTS participacoes_cauda;
        CREATE TEMP TABLE participacoes_cauda AS
        SELECT conta_id, titular, row_number() OVER (ORDER BY random()) AS idx
        FROM participacoes_seed
        WHERE faixa <> 'monstro';

        -- Hub -> cauda: 500.000 liquidações. Contas monstro nunca aparecem no pool da cauda,
        -- então a colisão consigo mesma é impossível aqui por construção (conjuntos disjuntos).
        DROP TABLE IF EXISTS liquidacoes_hub;
        CREATE TEMP TABLE liquidacoes_hub AS
        SELECT
            m.conta_id AS conta_1, m.titular AS nome_1,
            c.conta_id AS conta_2, c.titular AS nome_2
        FROM participacoes_monstro m
        JOIN participacoes_cauda c ON c.idx = m.idx
        WHERE c.idx <= 500000;

        -- Sobra da cauda (200.000 participações) para o pareamento cauda <-> cauda.
        DROP TABLE IF EXISTS participacoes_cauda_sobra;
        CREATE TEMP TABLE participacoes_cauda_sobra AS
        SELECT conta_id, titular
        FROM participacoes_cauda
        WHERE idx > 500000;

        DROP TABLE IF EXISTS grupos_cauda_sobra;
        CREATE TEMP TABLE grupos_cauda_sobra AS
        SELECT conta_id, random() AS ordem_grupo
        FROM (SELECT DISTINCT conta_id FROM participacoes_cauda_sobra) contas_distintas;

        -- Índice 0-based agrupado por conta (cada conta ocupa um bloco contíguo, pois todas as
        -- suas linhas compartilham o mesmo ordem_grupo). Nenhuma conta tem peso >= 25.000, bem
        -- abaixo de N=100.000: pareando idx i com idx i+N, nenhum bloco de conta cabe nos dois
        -- lados do par ao mesmo tempo (a distância N excede o tamanho de qualquer bloco).
        DROP TABLE IF EXISTS participacoes_cauda_sobra_indexada;
        CREATE TEMP TABLE participacoes_cauda_sobra_indexada AS
        SELECT p.conta_id, p.titular,
               row_number() OVER (ORDER BY g.ordem_grupo, random()) - 1 AS idx
        FROM participacoes_cauda_sobra p
        JOIN grupos_cauda_sobra g USING (conta_id);

        DROP TABLE IF EXISTS liquidacoes_cauda_cauda;
        CREATE TEMP TABLE liquidacoes_cauda_cauda AS
        SELECT
            a.conta_id AS conta_1, a.titular AS nome_1,
            b.conta_id AS conta_2, b.titular AS nome_2
        FROM participacoes_cauda_sobra_indexada a
        JOIN participacoes_cauda_sobra_indexada b ON b.idx = a.idx + 100000
        WHERE a.idx < 100000;

        DROP TABLE IF EXISTS liquidacoes_seed;
        CREATE TEMP TABLE liquidacoes_seed AS
        SELECT * FROM liquidacoes_hub
        UNION ALL
        SELECT * FROM liquidacoes_cauda_cauda;

        -- O shift é sorteado por liquidação: os dois lançamentos irmãos são o mesmo evento e
        -- precisam do mesmo milissegundo. liquidacao_id é materializado aqui para não ser
        -- recalculado em cada ramo do UNION ALL final (cada chamada a uuidv7 produz um Guid
        -- diferente).
        DROP TABLE IF EXISTS liquidacoes_com_id;
        CREATE TEMP TABLE liquidacoes_com_id AS
        SELECT
            uuidv7(deslocamento) AS liquidacao_id,
            conta_1, nome_1, conta_2, nome_2, deslocamento, valor, conta_1_e_debito
        FROM (
            SELECT
                conta_1, nome_1, conta_2, nome_2,
                -(random() * interval '24 months') AS deslocamento,
                round((random() * 49999 + 1)::numeric, 2) AS valor,
                random() < 0.5 AS conta_1_e_debito
            FROM liquidacoes_seed
        ) detalhadas;

        INSERT INTO lancamentos (id, conta_id, liquidacao_id, contraparte_id, contraparte_nome, moeda, valor)
        SELECT
            uuidv7(deslocamento),
            CASE WHEN conta_1_e_debito THEN conta_1 ELSE conta_2 END,
            liquidacao_id,
            CASE WHEN conta_1_e_debito THEN conta_2 ELSE conta_1 END,
            CASE WHEN conta_1_e_debito THEN nome_2 ELSE nome_1 END,
            'BRL',
            -valor
        FROM liquidacoes_com_id
        UNION ALL
        SELECT
            uuidv7(deslocamento),
            CASE WHEN conta_1_e_debito THEN conta_2 ELSE conta_1 END,
            liquidacao_id,
            CASE WHEN conta_1_e_debito THEN conta_1 ELSE conta_2 END,
            CASE WHEN conta_1_e_debito THEN nome_1 ELSE nome_2 END,
            'BRL',
            valor
        FROM liquidacoes_com_id;
        """;
}
