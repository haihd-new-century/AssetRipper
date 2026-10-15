using AssetRipper.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetRipper.Export.Configuration;

/// <summary>
/// User supplied knowledge about Unity packages used by the game (User-Defined Package Export).
/// </summary>
/// <remarks>
/// Without this, scripts and assets that came from packages (TextMeshPro, Input System, ...) are exported into the project,
/// and conflict with the real packages once they are installed. With a mapping, AssetRipper:
/// <list type="bullet">
/// <item>does not export the package's assemblies or scripts,</item>
/// <item>points every reference to them at the package's own GUIDs,</item>
/// <item>adds the package to <c>Packages/manifest.json</c>.</item>
/// </list>
/// <code>
/// {
///   "Packages": [{
///     "Name": "com.unity.textmeshpro", "Version": "3.0.6",
///     "Assemblies": ["Unity.TextMeshPro"],
///     "Scripts": { "TMPro.TextMeshProUGUI": "f4688fdb7df04437aeb418b961361dc5" },
///     "AssemblyGuid": null,
///     "Assets": { "sharedassets0.assets/12": "8f586378b4e144a9851e7b34d9b748ee:11400000" }
///   }]
/// }
/// </code>
/// <c>Scripts</c> maps a script's full name to the GUID of its <c>.cs</c> file in the package (file ID 11500000).
/// <c>AssemblyGuid</c> is for packages that ship precompiled DLLs: scripts then use the DLL's GUID and the usual script file ID.
/// <c>Assets</c> maps <c>collection/pathID</c> to <c>guid:fileID</c> of an asset inside the package.
/// </remarks>
public sealed class PackageMappings
{
	public List<PackageDefinition> Packages { get; set; } = [];

	[JsonIgnore]
	public bool IsEmpty => Packages.Count == 0;

	public PackageDefinition? FindByAssembly(string assemblyName)
	{
		foreach (PackageDefinition package in Packages)
		{
			if (package.Assemblies.Contains(assemblyName, StringComparer.Ordinal))
			{
				return package;
			}
		}
		return null;
	}

	public bool TryGetAsset(string collectionName, long pathID, out UnityGuid guid, out long fileID)
	{
		string key = $"{collectionName}/{pathID}";
		foreach (PackageDefinition package in Packages)
		{
			if (package.Assets.TryGetValue(key, out string? value) && TryParseReference(value, out guid, out fileID))
			{
				return true;
			}
		}
		guid = default;
		fileID = 0;
		return false;
	}

	/// <summary>Parses "guid" or "guid:fileID".</summary>
	public static bool TryParseReference(string value, out UnityGuid guid, out long fileID)
	{
		string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
		fileID = parts.Length == 2 && long.TryParse(parts[1], out long id) ? id : 11400000;
		if (TryParseGuid(parts[0], out guid) && fileID != 0)
		{
			return true;
		}
		guid = default;
		return false;
	}

	public static bool TryParseGuid(string? text, out UnityGuid guid)
	{
		if (text is { Length: 32 } && text.All(Uri.IsHexDigit))
		{
			guid = UnityGuid.Parse(text);
			return guid != UnityGuid.Zero;
		}
		guid = default;
		return false;
	}

	public static PackageMappings Parse(string json) => JsonSerializer.Deserialize(json, PackageMappingsContext.Default.PackageMappings) ?? new();

	public static PackageMappings Load(string path) => Parse(File.ReadAllText(path));

	public string ToJson() => JsonSerializer.Serialize(this, PackageMappingsContext.Default.PackageMappings);
}

public sealed class PackageDefinition
{
	public string Name { get; set; } = "";
	public string Version { get; set; } = "";
	public List<string> Assemblies { get; set; } = [];
	public Dictionary<string, string> Scripts { get; set; } = [];
	public string? AssemblyGuid { get; set; }
	public Dictionary<string, string> Assets { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackageMappings))]
public partial class PackageMappingsContext : JsonSerializerContext
{
}
