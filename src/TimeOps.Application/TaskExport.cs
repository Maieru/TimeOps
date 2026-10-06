using TimeOps.Domain;

namespace TimeOps.Application;

public sealed record TaskExportRow(int TaskId, int UpdateId, Person? Author, string TaskTitle,
    string? FeatureTitle, DateTimeOffset ChangedAt, DateOnly Date, decimal Hours)
{
    public string AuthorName => Author?.Name ?? "Autor não informado";
}

public sealed record TaskExportData(Sprint Sprint, DateOnly From, DateOnly To,
    DateTimeOffset CollectedAt, IReadOnlyList<TaskExportRow> Rows)
{
    // null selects everyone; the empty identity selects entries without an author.
    public IReadOnlyList<TaskExportRow> ForAuthor(string? authorId) => authorId is null ? Rows
        : Rows.Where(row => string.Equals(row.Author?.Id ?? "", authorId, StringComparison.OrdinalIgnoreCase)).ToArray();
}

public interface ITaskExportWriter
{
    Result<byte[]> Write(IReadOnlyList<TaskExportRow> rows);
}
