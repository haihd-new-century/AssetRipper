using AssetRipper.Assets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.Configuration;

/// <summary>
/// Restricts which assets are written during export.
/// </summary>
/// <remarks>
/// An asset passes when:
/// <list type="bullet">
/// <item><see cref="IncludeClasses"/> is empty, or contains the asset's class name;</item>
/// <item><see cref="ExcludeClasses"/> does not contain the asset's class name;</item>
/// <item><see cref="IncludePaths"/> is empty, or one of its glob patterns matches the asset's export path;</item>
/// <item>none of the <see cref="ExcludePaths"/> glob patterns match the asset's export path.</item>
/// </list>
/// Class names are compared case-insensitively (for example "Texture2D", "AudioClip", "MonoScript").
/// Paths are relative to the project root and use forward slashes (for example "Assets/Texture2D/Logo").
/// Glob syntax: <c>*</c> matches within a path segment, <c>**</c> matches across segments, <c>?</c> matches one character.
/// Filtered assets are still known to the exporter, so references to them are kept as GUIDs but their files are not written.
/// </remarks>
public sealed class ExportFilter
{
	public List<string> IncludeClasses { get; set; } = [];
	public List<string> ExcludeClasses { get; set; } = [];
	public List<string> IncludePaths { get; set; } = [];
	public List<string> ExcludePaths { get; set; } = [];

	[JsonIgnore]
	public bool IsEmpty => IncludeClasses.Count == 0 && ExcludeClasses.Count == 0 && IncludePaths.Count == 0 && ExcludePaths.Count == 0;

	private Regex[]? includePathRegexes;
	private Regex[]? excludePathRegexes;

	public bool Allows(IUnityObjectBase asset)
	{
		return Allows(asset.ClassName, GetExportPath(asset));
	}

	public bool Allows(string className, string path)
	{
		if (IncludeClasses.Count > 0 && !IncludeClasses.Contains(className, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		if (ExcludeClasses.Contains(className, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		path = path.Replace('\\', '/');
		includePathRegexes ??= IncludePaths.Select(GlobToRegex).ToArray();
		excludePathRegexes ??= ExcludePaths.Select(GlobToRegex).ToArray();
		if (includePathRegexes.Length > 0 && !includePathRegexes.Any(r => r.IsMatch(path)))
		{
			return false;
		}
		return !excludePathRegexes.Any(r => r.IsMatch(path));
	}

	/// <summary>
	/// The path used for filtering: <c>directory/name</c> without extension, relative to the project root.
	/// </summary>
	public static string GetExportPath(IUnityObjectBase asset) => $"{asset.GetBestDirectory()}/{asset.GetBestName()}".Replace('\\', '/');

	public static Regex GlobToRegex(string glob)
	{
		string pattern = Regex.Escape(glob.Replace('\\', '/'))
			.Replace(@"\*\*/", "(?:.*/)?")
			.Replace(@"\*\*", ".*")
			.Replace(@"\*", "[^/]*")
			.Replace(@"\?", "[^/]");
		return new Regex($"^{pattern}(?:\\.[^/]*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	public static ExportFilter Parse(string json) => JsonSerializer.Deserialize(json, ExportFilterContext.Default.ExportFilter) ?? new();

	public string ToJson() => JsonSerializer.Serialize(this, ExportFilterContext.Default.ExportFilter);

	public override string ToString()
	{
		List<string> parts = [];
		if (IncludeClasses.Count > 0) { parts.Add($"classes={string.Join(',', IncludeClasses)}"); }
		if (ExcludeClasses.Count > 0) { parts.Add($"exclude-classes={string.Join(',', ExcludeClasses)}"); }
		if (IncludePaths.Count > 0) { parts.Add($"paths={string.Join(',', IncludePaths)}"); }
		if (ExcludePaths.Count > 0) { parts.Add($"exclude-paths={string.Join(',', ExcludePaths)}"); }
		return parts.Count == 0 ? "(none)" : string.Join("; ", parts);
	}
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExportFilter))]
public partial class ExportFilterContext : JsonSerializerContext
{
}
