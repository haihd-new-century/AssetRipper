using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetRipper.Processing.Configuration;

/// <summary>
/// User supplied export destinations for individual assets.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "Files": {
///     "level1.assets": { "12": "Assets/Images/MyTexture.png" },
///     "cab-bcaf22789432bda1e5d0eea9d2521ddd": { "4476349470337976665": "Assets/AssetRenamed.txt" }
///   }
/// }
/// </code>
/// Keys of <see cref="Files"/> are asset collection names (case-insensitive).
/// Inner keys are path IDs. Values are paths relative to the project root.
/// </remarks>
public sealed class AssetPathOverrides
{
	public Dictionary<string, Dictionary<string, string>> Files { get; set; } = [];

	public static AssetPathOverrides Parse(string json)
	{
		return JsonSerializer.Deserialize(json, AssetPathOverridesContext.Default.AssetPathOverrides)
			?? throw new JsonException("The path override file is empty.");
	}

	public static AssetPathOverrides Load(string path) => Parse(File.ReadAllText(path));

	public string ToJson() => JsonSerializer.Serialize(this, AssetPathOverridesContext.Default.AssetPathOverrides);

	/// <summary>
	/// Validates a single override path and normalizes it to forward slashes.
	/// </summary>
	/// <returns>The normalized path, or null if the path is unsafe (absolute, contains "..", or empty).</returns>
	public static string? NormalizePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string normalized = path.Replace('\\', '/').Trim();
		if (Path.IsPathRooted(normalized) || normalized.StartsWith('/') || normalized.Contains(':'))
		{
			return null;
		}
		string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
		{
			return null;
		}
		return string.Join('/', segments);
	}
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AssetPathOverrides))]
public partial class AssetPathOverridesContext : JsonSerializerContext
{
}

/// <summary>
/// Applies <see cref="AssetPathOverrides"/> by setting <see cref="IUnityObjectBase.OverridePath"/>.
/// </summary>
public sealed class AssetPathOverrideProcessor(AssetPathOverrides? overrides) : IAssetProcessor
{
	public void Process(GameData gameData)
	{
		if (overrides is null || overrides.Files.Count == 0)
		{
			return;
		}

		Logger.Info(LogCategory.Processing, "Applying asset path overrides");
		Dictionary<string, List<AssetCollection>> collectionsByName = new(StringComparer.OrdinalIgnoreCase);
		foreach (AssetCollection collection in gameData.GameBundle.FetchAssetCollections())
		{
			if (!collectionsByName.TryGetValue(collection.Name, out List<AssetCollection>? list))
			{
				list = [];
				collectionsByName.Add(collection.Name, list);
			}
			list.Add(collection);
		}

		int applied = 0, missing = 0, invalid = 0;
		foreach ((string collectionName, Dictionary<string, string> entries) in overrides.Files)
		{
			if (!collectionsByName.TryGetValue(collectionName, out List<AssetCollection>? collections))
			{
				Logger.Warning(LogCategory.Processing, $"Path override: collection '{collectionName}' was not found ({entries.Count} entries skipped).");
				missing += entries.Count;
				continue;
			}
			foreach ((string pathIdText, string targetPath) in entries)
			{
				string? normalized = AssetPathOverrides.NormalizePath(targetPath);
				if (!long.TryParse(pathIdText, out long pathId) || normalized is null)
				{
					Logger.Warning(LogCategory.Processing, $"Path override: invalid entry '{collectionName}/{pathIdText}' -> '{targetPath}'.");
					invalid++;
					continue;
				}
				IUnityObjectBase? asset = collections.Select(c => c.TryGetAsset(pathId)).FirstOrDefault(a => a is not null);
				if (asset is null)
				{
					Logger.Warning(LogCategory.Processing, $"Path override: asset {pathId} was not found in '{collectionName}'.");
					missing++;
					continue;
				}
				asset.OverridePath = normalized;
				applied++;
			}
		}
		Logger.Info(LogCategory.Processing, $"Asset path overrides: {applied} applied, {missing} not found, {invalid} invalid.");
	}
}
