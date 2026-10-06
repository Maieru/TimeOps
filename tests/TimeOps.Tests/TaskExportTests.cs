using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using TimeOps.Application;
using TimeOps.Domain;
using TimeOps.Infrastructure;

namespace TimeOps.Tests;

public sealed class TaskExportTests
{
    private static readonly Sprint Sprint = new("s1", "Sprint / Setembro", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25));
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
    private static readonly Person Author = new("author", "João");
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public async Task Exporta_so_aumentos_com_autor_data_local_e_features_diretas_e_via_historia()
    {
        var gateway = new FakeGateway
        {
            Changes = [
                Change(1, 1, 0, 1.25m), Change(1, 2, 1.25m, 3),
                Change(2, 1, null, 2.5m), Change(3, 1, 0, 1) with { ChangedBy = null },
                Change(1, 3, 3, 2), Change(1, 4, 2, 2),
                Change(1, 5, 0, 10) with { Field = EffortField.Original },
                Change(1, 6, 0, 10) with { Field = EffortField.Remaining }
            ]
        };
        var result = await Service(gateway).LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(gateway.Forced);
        Assert.Same(gateway.Snapshot, gateway.HistorySnapshot);
        Assert.Equal(4, result.Value.Rows.Count);
        var authored = result.Value.ForAuthor("AUTHOR");
        Assert.Equal(3, authored.Count);
        Assert.All(authored, row => Assert.Equal("João", row.AuthorName));
        Assert.Equal(new[] { 1.25m, 1.75m, 2.5m }, authored.Select(row => row.Hours));
        Assert.Equal("Feature através da história", authored[0].FeatureTitle);
        Assert.Equal("Feature direta", authored[2].FeatureTitle);
        Assert.All(authored, row => Assert.Equal(new DateOnly(2026, 9, 27), row.Date));
        Assert.Equal("Nome atual 1", authored[0].TaskTitle);
        var unknown = Assert.Single(result.Value.ForAuthor(""));
        Assert.Equal("Autor não informado", unknown.AuthorName);
        Assert.Null(unknown.FeatureTitle);
        Assert.Empty(result.Value.ForAuthor("assignee"));
        Assert.Equal(result.Value.Rows, result.Value.ForAuthor(null));
    }

    [Fact]
    public async Task Inclui_os_dois_dias_inteiros_no_fuso_e_exclui_fora_do_intervalo()
    {
        var start = new DateTimeOffset(2026, 9, 26, 3, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero).AddTicks(-1);
        var gateway = new FakeGateway { Changes = [
            Change(1, 1, 0, 1) with { ChangedAt = start.AddTicks(-1) },
            Change(1, 2, 0, 1) with { ChangedAt = start },
            Change(1, 3, 0, 1) with { ChangedAt = end },
            Change(1, 4, 0, 1) with { ChangedAt = end.AddTicks(1) }
        ] };
        var result = await Service(gateway).LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));

        Assert.True(result.IsSuccess);
        Assert.Equal(start, gateway.From);
        Assert.Equal(end, gateway.To);
        Assert.Equal(new[] { 2, 3 }, result.Value.Rows.Select(row => row.UpdateId));
        Assert.Equal(new[] { new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 27) }, result.Value.Rows.Select(row => row.Date));
    }

    [Fact]
    public async Task Dia_atual_limita_historico_ao_instante_da_coleta()
    {
        var gateway = new FakeGateway { Changes = [Change(1, 1, 0, 1) with { ChangedAt = Now.AddTicks(1) }] };
        var result = await Service(gateway).LoadAsync("p", "t", Sprint, new(2026, 9, 28), new(2026, 9, 28));
        Assert.True(result.IsSuccess);
        Assert.Equal(Now, gateway.To);
        Assert.Equal(Now, result.Value.CollectedAt);
        Assert.Empty(result.Value.Rows);
    }

    [Theory]
    [InlineData("", "t", 26, 27, "context.invalid")]
    [InlineData("p", " ", 26, 27, "context.invalid")]
    [InlineData("p", "t", 27, 26, "export.dates")]
    [InlineData("p", "t", 26, 29, "export.future")]
    public async Task Valida_contexto_e_datas_antes_de_consultar(string project, string team, int from, int to, string code)
    {
        var gateway = new FakeGateway();
        var result = await Service(gateway).LoadAsync(project, team, Sprint, new(2026, 9, from), new(2026, 9, to));
        Assert.Equal(code, result.Error!.Code);
        Assert.False(gateway.Forced);
    }

    [Fact]
    public async Task Fuso_invalido_nao_consulta_gateway()
    {
        var gateway = new FakeGateway();
        var result = await new TaskExportService(gateway, new FixedClock(), "TimeOps/Invalid")
            .LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));
        Assert.Equal("timezone.invalid", result.Error!.Code);
        Assert.False(gateway.Forced);
    }

    [Fact]
    public async Task Dados_incompletos_impedem_exportacao_parcial()
    {
        foreach (var snapshot in new[] {
            Snapshot() with { HierarchyError = "Sem acesso" },
            Snapshot() with { Parents = null },
            Snapshot() with { Fields = new(false, true, true) }
        })
        {
            var gateway = new FakeGateway { Snapshot = snapshot };
            var result = await Service(gateway).LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));
            Assert.True(result.IsFailure);
            Assert.Null(gateway.HistorySnapshot);
        }
        var error = new Error("devops.incomplete", ErrorCategory.Incomplete, "Histórico incompleto.");
        var historyResult = await Service(new() { HistoryError = error }).LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));
        Assert.Same(error, historyResult.Error);
        var snapshotResult = await Service(new() { SnapshotError = error }).LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));
        Assert.Same(error, snapshotResult.Error);
    }

    [Fact]
    public async Task Ordenacao_preserva_eventos_distintos_e_filtro_usa_identidade_mesmo_com_nomes_iguais()
    {
        var gateway = new FakeGateway { Changes = [
            Change(2, 9, 0, 1), Change(1, 8, 0, 1), Change(1, 7, 0, 1),
            Change(1, 6, 0, 1) with { ChangedBy = new("another-author", "João") }
        ] };
        var result = await Service(gateway).LoadAsync("p", "t", Sprint, new(2026, 9, 26), new(2026, 9, 27));
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 6, 7, 8, 9 }, result.Value.Rows.Select(row => row.UpdateId));
        Assert.Single(result.Value.ForAuthor("another-author"));
        Assert.Equal(3, result.Value.ForAuthor("author").Count);
    }

    [Fact]
    public void Excel_tem_colunas_estilos_datas_numericas_e_texto_literal()
    {
        var date = new DateOnly(2026, 9, 27);
        var result = new TaskExportWriter().Write([new(1, 1, Author, "=Atividade & ação <nova>", "Feature ç", Now, date, 1.25m)]);
        Assert.True(result.IsSuccess, result.Error?.Message);
        using var zip = new ZipArchive(new MemoryStream(result.Value));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("_rels/.rels"));
        Assert.NotNull(zip.GetEntry("xl/_rels/workbook.xml.rels"));
        var workbook = Read(zip, "xl/workbook.xml");
        Assert.Equal("Tarefas", Assert.Single(workbook.Descendants(Ns + "sheet")).Attribute("name")!.Value);
        var sheet = Read(zip, "xl/worksheets/sheet1.xml");
        var rows = sheet.Descendants(Ns + "row").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new[] { "x", "Atividade", "OP/PMC/PGP", "Nome da OP/PMC/PGP", "Data", "Horas", "Comentários" },
            rows[0].Elements(Ns + "c").Select(cell => cell.Value));
        Assert.All(rows, row => Assert.Equal(7, row.Elements(Ns + "c").Count()));
        var cells = rows[1].Elements(Ns + "c").ToDictionary(cell => cell.Attribute("r")!.Value);
        Assert.Equal("João", cells["A2"].Value);
        Assert.Equal("=Atividade & ação <nova>", cells["B2"].Value);
        Assert.Equal("inlineStr", cells["B2"].Attribute("t")!.Value);
        Assert.Empty(sheet.Descendants(Ns + "f"));
        Assert.Equal("Feature ç", cells["C2"].Value);
        Assert.Equal("", cells["D2"].Value);
        Assert.Equal("", cells["G2"].Value);
        Assert.Null(cells["E2"].Attribute("t"));
        Assert.Equal(date.ToDateTime(TimeOnly.MinValue).ToOADate(), double.Parse(cells["E2"].Value, CultureInfo.InvariantCulture));
        Assert.Equal(1.25m, decimal.Parse(cells["F2"].Value, CultureInfo.InvariantCulture));
        var styles = Read(zip, "xl/styles.xml");
        Assert.All(styles.Descendants(Ns + "font"), font => {
            Assert.Equal("Arial", font.Element(Ns + "name")!.Attribute("val")!.Value);
            Assert.Equal("10", font.Element(Ns + "sz")!.Attribute("val")!.Value);
        });
        Assert.NotNull(styles.Descendants(Ns + "b").Single());
        Assert.Contains(styles.Descendants(Ns + "fgColor"), color => color.Attribute("rgb")?.Value == "FF808080");
        Assert.Contains(styles.Descendants(Ns + "numFmt"), format => format.Attribute("formatCode")?.Value == "dd/mm/yyyy");
        Assert.Equal(7, sheet.Descendants(Ns + "col").Count());
    }

    [Fact]
    public void Nome_do_arquivo_e_sanitizado_e_dados_nao_serializaveis_retornam_result()
    {
        var data = new TaskExportData(Sprint, new(2026, 9, 26), new(2026, 9, 27), Now, []);
        Assert.Equal("TimeOps_Sprint _ Setembro_2026-09-26_2026-09-27.xlsx", TaskExportService.FileName(data));
        Assert.True(new TaskExportWriter().Write([new(1, 1, Author, "inválido\u0001", null, Now, new(2026, 9, 27), 1)]).IsFailure);
    }

    private static XDocument Read(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }
    private static EffortChange Change(int task, int update, decimal? before, decimal? after)
        => new(task, "Nome antigo", "https://example.test/" + task, update,
            new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), Author, EffortField.Completed, before, after);
    private static TaskExportService Service(FakeGateway gateway) => new(gateway, new FixedClock(), "America/Sao_Paulo");
    private static SprintSnapshot Snapshot() => new(Sprint, new(new HashSet<DayOfWeek> { DayOfWeek.Monday }, []), [],
        [Task(1, 10), Task(2, 30), Task(3, null)], new(true, true, true), Now,
        [new(10, "História", "User Story", 20, ""), new(20, "Feature através da história", "Feature", null, ""), new(30, "Feature direta", "Feature", null, "")]);
    private static TaskWork Task(int id, int? parent) => new(id, "Nome atual " + id, new("assignee", "Responsável atual"),
        "Active", "InProgress", "Área", Sprint.Path, 8, 10, 2, "", parent, 3, Now);
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeGateway : IDevOpsGateway
    {
        public SprintSnapshot Snapshot { get; init; } = TaskExportTests.Snapshot();
        public IReadOnlyList<EffortChange> Changes { get; init; } = [];
        public Error? SnapshotError { get; init; }
        public Error? HistoryError { get; init; }
        public bool Forced { get; private set; }
        public SprintSnapshot? HistorySnapshot { get; private set; }
        public DateTimeOffset? From { get; private set; }
        public DateTimeOffset? To { get; private set; }
        public Task<Result<SprintSnapshot>> LoadSnapshotAsync(string projectId, string teamId, Sprint sprint, bool forceRefresh, CancellationToken cancellationToken)
        {
            Forced = forceRefresh;
            return System.Threading.Tasks.Task.FromResult(SnapshotError is { } error ? Result<SprintSnapshot>.Failure(error) : Result<SprintSnapshot>.Success(Snapshot));
        }
        public Task<Result<EffortHistory>> LoadCompletedHistoryAsync(string projectId, string teamId, SprintSnapshot snapshot, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            HistorySnapshot = snapshot; From = from; To = to;
            return System.Threading.Tasks.Task.FromResult(HistoryError is { } error ? Result<EffortHistory>.Failure(error) : Result<EffortHistory>.Success(new(from, to, Now, Changes)));
        }
        public Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<EffortHistory>> LoadEffortHistoryAsync(string projectId, string teamId, Sprint sprint, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
