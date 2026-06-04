using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BackEnd.Tests;

/// <summary>
/// Boots the real BackEnd minimal API in-memory. Points the content root at a throwaway temp
/// directory so the SQLite database (derived from ContentRootPath in Program.cs) is created fresh
/// per test run and the developer's budgeting.db is never touched.
/// </summary>
public sealed class UploadApiFactory : WebApplicationFactory<Program>
{
    public string TempRoot { get; } =
        Path.Combine(Path.GetTempPath(), "bk-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(TempRoot);
        builder.UseContentRoot(TempRoot);     // => budgeting.db lives under TempRoot
        builder.UseEnvironment("Production");  // skip OpenAPI/Scalar mapping
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(TempRoot, recursive: true); } catch { /* best effort cleanup */ }
    }
}
