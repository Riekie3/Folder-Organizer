using System.Text.Json.Serialization;
using GlassFolders.Core.Backup;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Config;

[JsonSerializable(typeof(FoldersConfiguration))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(FolderDef))]
[JsonSerializable(typeof(FolderItem))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ConfigurationJsonContext : JsonSerializerContext;
