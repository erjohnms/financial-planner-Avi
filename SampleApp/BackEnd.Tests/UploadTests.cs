using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BackEnd.Tests;

/// <summary>
/// Drives the real POST /transactions/upload and GET /transactions endpoints with CSV fixtures to
/// prove the customer-reported "missing transactions" behaviour.
///
/// Tests are written against the INTENDED post-fix contract, so several are RED today (that is the
/// point — they demonstrate the bugs) and turn GREEN once the fixes land:
///   * Off-by-one: a valid file with N data rows must import exactly N.
///   * Malformed row: reject the whole file with HTTP 400 and a "Line {n}: ..." message; persist nothing.
/// Each test uses its own userName so GET round-trips are independent on the shared temp database.
/// </summary>
public class UploadTests : IClassFixture<UploadApiFactory>
{
    private readonly UploadApiFactory _factory;

    public UploadTests(UploadApiFactory factory) => _factory = factory;

    private sealed record UploadResult(int ImportedCount);
    private sealed record Txn(string? Date, string Description, decimal Amount, string Category);

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string user, string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", fixture);
        return await client.PostAsync($"/transactions/upload?userName={Uri.EscapeDataString(user)}", form);
    }

    private static async Task<int> ImportedCount(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<UploadResult>())!.ImportedCount;

    private async Task<List<Txn>> GetTransactions(HttpClient client, string user)
        => (await client.GetFromJsonAsync<List<Txn>>($"/transactions?userName={Uri.EscapeDataString(user)}"))!;

    // ===================== Off-by-one (RED now -> GREEN after fix) =====================

    [Fact]
    public async Task ValidFile_ImportsEveryRow()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(ValidFile_ImportsEveryRow), "valid-checking-12.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, await ImportedCount(response)); // today: 11 (first data row dropped)
    }

    [Fact]
    public async Task ValidFile_FirstDataRowIsPersisted()
    {
        var user = nameof(ValidFile_FirstDataRowIsPersisted);
        var client = _factory.CreateClient();

        await Upload(client, user, "valid-checking-12.csv");
        var transactions = await GetTransactions(client, user);

        // The very first row of the file is the payroll/income line - it must not vanish.
        Assert.Contains(transactions, t => t.Description == "PAYROLL DIRECT DEP");
    }

    [Fact]
    public async Task SingleRowFile_ImportsThatRow()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(SingleRowFile_ImportsThatRow), "single-row.csv");

        Assert.Equal(1, await ImportedCount(response)); // today: 0 (the only row is skipped)
    }

    [Fact]
    public async Task BlankLines_AreSkipped_ButRealRowsAreKept()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(BlankLines_AreSkipped_ButRealRowsAreKept), "blank-lines.csv");

        Assert.Equal(3, await ImportedCount(response)); // today: 2 (first data row dropped)
    }

    // ============ Malformed row -> reject whole file with clear error (RED now -> GREEN) ============

    [Fact]
    public async Task BadAmount_RejectsWholeFile_WithLineNumber()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(BadAmount_RejectsWholeFile_WithLineNumber), "bad-amount-line4.csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Line 4", body); // today: "Invalid amount value 'N/A'." - no line number
    }

    [Fact]
    public async Task MissingColumns_RejectsWholeFile_WithLineNumber()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(MissingColumns_RejectsWholeFile_WithLineNumber), "missing-columns-line3.csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Line 3", body); // today: "One or more rows have missing columns." - no line number
    }

    [Fact]
    public async Task MalformedFile_PersistsNothing()
    {
        var user = nameof(MalformedFile_PersistsNothing);
        var client = _factory.CreateClient();

        await Upload(client, user, "bad-amount-line4.csv");
        var transactions = await GetTransactions(client, user);

        Assert.Empty(transactions); // whole-file reject => no partial import
    }

    // ===================== Guards / characterization (GREEN now, must stay GREEN) =====================

    [Fact]
    public async Task HeaderOnlyFile_ImportsZero_WithoutError()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(HeaderOnlyFile_ImportsZero_WithoutError), "header-only.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await ImportedCount(response));
    }

    [Fact]
    public async Task WhitespaceOnlyFile_IsRejectedAsEmpty()
    {
        var client = _factory.CreateClient();
        var response = await Upload(client, nameof(WhitespaceOnlyFile_IsRejectedAsEmpty), "whitespace-only.csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("empty", body, StringComparison.OrdinalIgnoreCase);
    }
}
