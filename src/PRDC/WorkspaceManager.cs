using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace PRDC;

internal sealed class WorkspaceDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = WorkspaceManager.CurrentSchemaVersion;

    [JsonPropertyName("workspaceId")]
    public Guid WorkspaceId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("lastModifiedUtc")]
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sources")]
    public List<WorkspaceSourceEntry> Sources { get; set; } = [];

    [JsonPropertyName("layout")]
    public WorkspaceLayout Layout { get; set; } = new();

    [JsonPropertyName("state")]
    public WorkspaceState State { get; set; } = new();

    [JsonPropertyName("extensions")]
    public Dictionary<string, object?> Extensions { get; set; } = [];

    [JsonIgnore]
    public bool IsRecoveryMode { get; set; }
}

internal sealed class WorkspaceSourceEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public Guid? Id { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("portalId")]
    public Guid? PortalId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("isPinned")]
    public bool? IsPinned { get; set; }
}

internal sealed class WorkspaceLayout
{
    [JsonPropertyName("window")]
    public WindowBounds? Window { get; set; }

    [JsonPropertyName("connectionPane")]
    public ConnectionPaneLayout? ConnectionPane { get; set; }

    [JsonPropertyName("splitterDistance")]
    public int? SplitterDistance { get; set; }
}

internal sealed class WindowBounds
{
    [JsonPropertyName("x")]
    public int? X { get; set; }

    [JsonPropertyName("y")]
    public int? Y { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("isMaximized")]
    public bool? IsMaximized { get; set; }
}

internal sealed class ConnectionPaneLayout
{
    [JsonPropertyName("isVisible")]
    public bool? IsVisible { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }
}

internal sealed class WorkspaceState
{
    [JsonPropertyName("activeSourceId")]
    public Guid? ActiveSourceId { get; set; }
}

internal sealed class LoadWorkspaceResult
{
    public bool IsSuccess { get; init; }
    public WorkspaceDocument Workspace { get; init; } = null!;
    public bool IsRecoveryMode { get; init; }
    public bool IsCorrupted { get; init; }
    public string? ErrorMessage { get; init; }
    public string? CorruptedBackupPath { get; init; }

    public static LoadWorkspaceResult Success(WorkspaceDocument workspace) =>
        new() { IsSuccess = true, Workspace = workspace };

    public static LoadWorkspaceResult RecoveryMode(string message, WorkspaceDocument workspace) =>
        new() { IsSuccess = true, IsRecoveryMode = true, ErrorMessage = message, Workspace = workspace };

    public static LoadWorkspaceResult Corrupted(string message, string? backupPath, WorkspaceDocument fallback) =>
        new() { IsSuccess = false, IsCorrupted = true, ErrorMessage = message, CorruptedBackupPath = backupPath, Workspace = fallback };

    public static LoadWorkspaceResult Failed(string message, WorkspaceDocument fallback) =>
        new() { IsSuccess = false, ErrorMessage = message, Workspace = fallback };
}

internal static class WorkspaceManager
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? _customWorkspaceFilePath;

    public static string DefaultWorkspaceFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PRDC",
        "workspace.json");

    public static string WorkspaceFilePath => _customWorkspaceFilePath ?? DefaultWorkspaceFilePath;

    internal static void SetCustomWorkspaceFilePath(string? path) => _customWorkspaceFilePath = path;
    internal static void ResetCustomWorkspaceFilePath() => _customWorkspaceFilePath = null;

    public static LoadWorkspaceResult LoadWorkspace(string? customPath = null)
    {
        string targetPath = customPath ?? WorkspaceFilePath;

        if (!File.Exists(targetPath))
        {
            return LoadWorkspaceResult.Success(new WorkspaceDocument
            {
                WorkspaceId = Guid.NewGuid(),
                SchemaVersion = CurrentSchemaVersion,
                LastModifiedUtc = DateTime.UtcNow
            });
        }

        string json;
        try
        {
            json = File.ReadAllText(targetPath);
        }
        catch (Exception ex)
        {
            return LoadWorkspaceResult.Failed(
                $"Não foi possível ler o arquivo de workspace: {ex.Message}",
                new WorkspaceDocument { WorkspaceId = Guid.NewGuid(), SchemaVersion = CurrentSchemaVersion });
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            string corruptPath = QuarantineCorruptFile(targetPath);
            return LoadWorkspaceResult.Corrupted(
                "Arquivo de workspace vazio ou truncado.",
                corruptPath,
                new WorkspaceDocument { WorkspaceId = Guid.NewGuid(), SchemaVersion = CurrentSchemaVersion });
        }

        WorkspaceDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<WorkspaceDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            string corruptPath = QuarantineCorruptFile(targetPath);
            return LoadWorkspaceResult.Corrupted(
                $"Arquivo de workspace corrompido: {ex.Message}",
                corruptPath,
                new WorkspaceDocument { WorkspaceId = Guid.NewGuid(), SchemaVersion = CurrentSchemaVersion });
        }

        if (doc is null)
        {
            string corruptPath = QuarantineCorruptFile(targetPath);
            return LoadWorkspaceResult.Corrupted(
                "Arquivo de workspace desserializado como nulo.",
                corruptPath,
                new WorkspaceDocument { WorkspaceId = Guid.NewGuid(), SchemaVersion = CurrentSchemaVersion });
        }

        // Validação de versão de schema
        if (doc.SchemaVersion > CurrentSchemaVersion)
        {
            doc.IsRecoveryMode = true;
            return LoadWorkspaceResult.RecoveryMode(
                $"A versão do esquema ({doc.SchemaVersion}) é superior à suportada por esta versão do PRDC ({CurrentSchemaVersion}). Modo somente-leitura ativado para evitar perda de dados.",
                doc);
        }

        if (doc.WorkspaceId == Guid.Empty)
        {
            doc.WorkspaceId = Guid.NewGuid();
        }

        doc.Sources ??= [];
        doc.Layout ??= new();
        doc.State ??= new();
        doc.Extensions ??= [];

        return LoadWorkspaceResult.Success(doc);
    }

    public static void SaveWorkspace(WorkspaceDocument workspace, string? customPath = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.IsRecoveryMode)
        {
            // Proteção estrita: nunca sobrescrever arquivo de schema desconhecido
            return;
        }

        string targetPath = customPath ?? WorkspaceFilePath;
        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        workspace.LastModifiedUtc = DateTime.UtcNow;
        if (workspace.WorkspaceId == Guid.Empty)
        {
            workspace.WorkspaceId = Guid.NewGuid();
        }

        string tmpPath = $"{targetPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            using (FileStream fs = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(fs, workspace, JsonOptions);
                fs.Flush(true);
            }

            if (File.Exists(targetPath))
            {
                try
                {
                    File.Replace(tmpPath, targetPath, null);
                }
                catch
                {
                    File.Copy(tmpPath, targetPath, overwrite: true);
                    File.Delete(tmpPath);
                }
            }
            else
            {
                File.Move(tmpPath, targetPath);
            }
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { }
            }
        }
    }

    public static bool IsWindowBoundsValid(Rectangle bounds)
    {
        if (bounds.Width < 300 || bounds.Height < 200)
        {
            return false;
        }

        Rectangle checkArea = new(bounds.X, bounds.Y, Math.Min(bounds.Width, 100), Math.Min(bounds.Height, 40));
        foreach (Screen screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(checkArea))
            {
                return true;
            }
        }

        return false;
    }

    private static string QuarantineCorruptFile(string targetPath)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        string corruptPath = $"{targetPath}.corrupt.{timestamp}";
        try
        {
            if (File.Exists(corruptPath))
            {
                corruptPath = $"{targetPath}.corrupt.{timestamp}.{Guid.NewGuid():N}";
            }
            File.Move(targetPath, corruptPath);
        }
        catch
        {
            // Falha não-fatal de movimentação
        }
        return corruptPath;
    }
}

