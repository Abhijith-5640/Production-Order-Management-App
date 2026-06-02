using System.Reflection;
using System.Text.Json;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Persistence;

/// <summary>
/// Reads and writes <c>db_config.json</c> next to the running exe.
/// On first run, if the file is missing, the default config embedded as
/// a resource is copied into the install directory so the operator
/// always has something editable to start with.
/// </summary>
public sealed class FileDbConfigStore : IDbConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _configPath;
    private readonly ILogger<FileDbConfigStore> _logger;

    public FileDbConfigStore(IConfiguration configuration, ILogger<FileDbConfigStore> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        // AppContext.BaseDirectory points at the running exe's folder
        // both during `dotnet run` (bin/Debug/net8.0) and in the
        // published single-file output.
        var dir = AppContext.BaseDirectory;
        _configPath = Path.Combine(dir, "db_config.json");

        if (!File.Exists(_configPath))
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("NexusProd.Api.Resources.default_db_config.json")
                                   ?? throw new InvalidOperationException("default_db_config.json resource missing");
                using var reader = new StreamReader(stream);
                File.WriteAllText(_configPath, reader.ReadToEnd());
                _logger.LogInformation("Created default db_config.json at {Path}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize default db_config.json");
            }
        }
    }

    public Task<DbConfigSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_configPath)) return Task.FromResult<DbConfigSnapshot?>(null);
        try
        {
            var json = File.ReadAllText(_configPath);
            var dto = JsonSerializer.Deserialize<ConfigDto>(json, JsonOptions);
            if (dto is null) return Task.FromResult<DbConfigSnapshot?>(null);
            return Task.FromResult<DbConfigSnapshot?>(new DbConfigSnapshot(
                dto.use_mock_db,
                new DbConfig(dto.config.host, dto.config.port, dto.config.user, dto.config.password, dto.config.database)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read db_config.json");
            return Task.FromResult<DbConfigSnapshot?>(null);
        }
    }

    public Task WriteAsync(DbConfigSnapshot snapshot, CancellationToken cancellationToken)
    {
        var dto = new ConfigDto
        {
            use_mock_db = snapshot.UseMockDb,
            config = new ConfigDto.Inner
            {
                host = snapshot.Config.Host,
                port = snapshot.Config.Port,
                user = snapshot.Config.User,
                password = snapshot.Config.Password,
                database = snapshot.Config.Database
            }
        };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(dto, JsonOptions));
        return Task.CompletedTask;
    }

    private sealed class ConfigDto
    {
        public bool use_mock_db { get; set; }
        public Inner config { get; set; } = new();
        public sealed class Inner
        {
            public string host { get; set; } = "localhost";
            public int port { get; set; } = 3306;
            public string user { get; set; } = "root";
            public string password { get; set; } = string.Empty;
            public string database { get; set; } = "prod_app";
        }
    }
}
