using DantesRoleplay.MCPServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DantesRoleplay.Tests;

public sealed class RuntimeUpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"runtime-update-{Guid.NewGuid():N}");

    public RuntimeUpdateTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void Update_mode_requires_all_four_inputs()
    {
        Assert.False(RuntimeUpdateRequest.TryRead(new ConfigurationBuilder().Build(), out _));
        var partial = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RuntimeUpdate:Manifest"] = Path.Combine(root, "installation.json")
        }).Build();
        Assert.Throws<InvalidOperationException>(() => RuntimeUpdateRequest.TryRead(partial, out _));
    }

    [Fact]
    public async Task Existing_candidate_root_is_refused_without_changing_it()
    {
        var target = Path.Combine(root, "candidate");
        Directory.CreateDirectory(target);
        var witness = Path.Combine(target, "witness.bin");
        await File.WriteAllBytesAsync(witness, [4, 3, 2, 1]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await RuntimeUpdate.RunAsync(new(
            Path.Combine(root, "missing-manifest.json"), Path.Combine(root, "missing-profile.json"),
            target, root), output, error);

        Assert.Equal(1, exit);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, await File.ReadAllBytesAsync(witness));
    }

    [Fact]
    public async Task Nonempty_blob_tree_is_copied_byte_for_byte()
    {
        var source = Path.Combine(root, "old-blobs");
        var destination = Path.Combine(root, "new-blobs");
        Directory.CreateDirectory(Path.Combine(source, "ab"));
        await File.WriteAllBytesAsync(Path.Combine(source, "ab", "blob.bin"), [0, 1, 2, 3, 255]);

        await RuntimeUpdate.CopyBlobsAsync(source, destination, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 1, 2, 3, 255 },
            await File.ReadAllBytesAsync(Path.Combine(destination, "ab", "blob.bin")));
    }

    [Fact]
    public void Full_profile_shape_and_runtime_settings_are_preserved()
    {
        var json = """
        {"schemaVersion":1,"hostRoot":"C:/old/host","sourceRoot":"C:/old/source","executable":"server.exe",
         "database":"C:/old/database.db","blobRoot":"C:/old/blobs","applicationId":"sample",
         "listenUrl":"http://127.0.0.1:6217","targets":[{"origin":"http://127.0.0.1:6217","expected":{}}],
         "environment":{"Sources__AllowedRoots__repository":"C:/old/source","ApplicationValidationWorker__Enabled":"true"},
         "hostFiles":[{"path":"server.exe","length":1,"sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}],
         "sourceFiles":[{"path":"catalog/manifest.json","length":1,"sha256":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"}]}
        """;
        var profile = RuntimeUpdate.DeserializeProfile(System.Text.Encoding.UTF8.GetBytes(json));
        var environment = RuntimeUpdate.EffectiveEnvironment(profile,
            new("C:/package/installation.json", "C:/old/profile.json", "C:/candidate", "C:/new/source"),
            "C:/candidate/database.db", "C:/candidate", offline: false);

        Assert.Equal("C:/new/source", environment["Sources:AllowedRoots:repository"]);
        Assert.Equal("true", environment["ApplicationValidationWorker:Enabled"]);
        Assert.Equal("C:/candidate/database.db", environment["ConnectionStrings:Kernel"]);
        profile.Environment["Catalogs__PublishedApplications__0"] = "custom-app";
        Assert.Equal(["custom-app", "sample"], RuntimeUpdate.PublishedApplications(profile, "sample"));
    }

    [Fact]
    public void Candidate_paths_cannot_overlap_preserved_blob_source_or_host_roots()
    {
        var host = Path.Combine(root, "old-host");
        var source = Path.Combine(root, "old-source");
        var blobs = Path.Combine(root, "old-blobs");
        Directory.CreateDirectory(host);
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(blobs);
        var database = Path.Combine(root, "old.db");
        File.WriteAllBytes(database, [1]);
        var profile = new RuntimeUpdateProfile
        {
            SchemaVersion = 1, HostRoot = host, SourceRoot = source, Database = database,
            BlobRoot = blobs, Executable = "server.exe", ApplicationId = "sample",
            ListenUrl = "http://127.0.0.1:6217",
            Environment = new(), Targets = [new("http://127.0.0.1:6217", JsonDocument.Parse("{}").RootElement.Clone())],
            HostFiles = [new("server.exe", 1, new string('A', 64))],
            SourceFiles = [new("catalog/a", 1, new string('B', 64))]
        };
        var package = Path.Combine(root, "package");
        Directory.CreateDirectory(package);

        Assert.Throws<RuntimeUpdateException>(() => RuntimeUpdate.ValidateProfile(profile,
            new(Path.Combine(package, "installation.json"), Path.Combine(root, "profile.json"),
                Path.Combine(blobs, "candidate"), Path.Combine(root, "new-source"))));
    }

    [Fact]
    public void Snapshot_copies_real_sqlite_data_and_reopens_read_only_for_verification()
    {
        var source = Path.Combine(root, "source.db");
        var destination = Path.Combine(root, "candidate.db");
        using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE witness(value TEXT NOT NULL); INSERT INTO witness VALUES ('preserved');";
            command.ExecuteNonQuery();
        }

        RuntimeUpdate.Snapshot(source, destination);

        using var verification = new SqliteConnection($"Data Source={destination};Mode=ReadOnly;Pooling=False");
        verification.Open();
        using var read = verification.CreateCommand();
        read.CommandText = "SELECT value FROM witness;";
        Assert.Equal("preserved", read.ExecuteScalar());
    }
}
