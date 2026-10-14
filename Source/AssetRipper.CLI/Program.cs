using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.Processing.Configuration;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class Program
{
	internal const int ExitSuccess = 0;
	internal const int ExitUsage = 1;
	internal const int ExitNothingLoaded = 2;
	internal const int ExitExportFailed = 3;

	public static int Main(string[] args)
	{
		CliOptions options;
		try
		{
			options = CliOptions.Parse(args);
		}
		catch (CliUsageException ex)
		{
			Console.Error.WriteLine($"error: {ex.Message}");
			Console.Error.WriteLine();
			Console.Error.WriteLine(CliOptions.Usage);
			return ExitUsage;
		}

		if (options.Help)
		{
			Console.WriteLine(CliOptions.Usage);
			return ExitSuccess;
		}

		CountingLogger counter = new();
		Logger.Add(counter);
		Logger.Add(options.Quiet || options.Json ? new FilteredConsoleLogger(LogType.Warning) : new ConsoleLogger());
		if (!string.IsNullOrEmpty(options.LogFile))
		{
			Logger.Add(new FileLogger(Path.GetFullPath(options.LogFile)));
		}

		try
		{
			return Run(options, counter);
		}
		catch (CliUsageException ex)
		{
			Console.Error.WriteLine($"error: {ex.Message}");
			return ExitUsage;
		}
		catch (Exception ex)
		{
			Logger.Error(ex);
			Console.Error.WriteLine($"error: {ex.Message}");
			return ExitExportFailed;
		}
	}

	private static int Run(CliOptions options, CountingLogger counter)
	{
		foreach (string input in options.Inputs)
		{
			if (!File.Exists(input) && !Directory.Exists(input))
			{
				throw new CliUsageException($"Input '{input}' does not exist.");
			}
		}

		FullConfiguration settings = CreateSettings(options);
		Stopwatch stopwatch = Stopwatch.StartNew();
		ExportHandler handler = new(settings);
		string[] inputs = options.Inputs.Select(Path.GetFullPath).ToArray();

		if (!options.Json)
		{
			settings.LogConfigurationValues();
		}
		GameData gameData = handler.LoadAndProcess(inputs, LocalFileSystem.Instance);
		if (!gameData.GameBundle.HasAnyAssetCollections())
		{
			Console.Error.WriteLine("error: no Unity assets were found in the input.");
			return ExitNothingLoaded;
		}

		if (options.Command == "info")
		{
			GameSummary summary = GameSummary.Create(gameData);
			Console.WriteLine(options.Json ? summary.ToJson() : summary.ToText());
			return ExitSuccess;
		}

		string output = Path.GetFullPath(options.Output!);
		PrepareOutputDirectory(output, options.Force);
		if (options.Mode == "primary")
		{
			settings.ExportRootPath = output;
			PrimaryContentExporter.CreateDefault(gameData, settings).Export(gameData.GameBundle, settings, LocalFileSystem.Instance);
		}
		else
		{
			handler.Export(gameData, output, LocalFileSystem.Instance);
		}

		stopwatch.Stop();
		Process process = Process.GetCurrentProcess();
		Console.WriteLine();
		Console.WriteLine($"Exported to {output}");
		long peakBytes = process.PeakWorkingSet64 > 0 ? process.PeakWorkingSet64 : Environment.WorkingSet; // PeakWorkingSet64 is not available on macOS
		string memoryLabel = process.PeakWorkingSet64 > 0 ? "Peak memory" : "Memory";
		Console.WriteLine($"Time: {stopwatch.Elapsed:hh\\:mm\\:ss\\.f} | {memoryLabel}: {peakBytes / (1024 * 1024)} MB | Managed heap peak: {GC.GetGCMemoryInfo().HeapSizeBytes / (1024 * 1024)} MB | Warnings: {counter.Warnings} | Errors: {counter.Errors}");
		return ExitSuccess;
	}

	internal static FullConfiguration CreateSettings(CliOptions options)
	{
		FullConfiguration settings = new();
		if (options.SettingsFile is not null)
		{
			SerializedSettings serialized = SerializedSettings.Load(options.SettingsFile);
			settings.ImportSettings = serialized.Import;
			settings.ProcessingSettings = serialized.Processing;
			settings.ExportSettings = serialized.Export;
		}
		if (options.ScriptLevel is { } level)
		{
			settings.ImportSettings.ScriptContentLevel = level;
		}

		ExportFilter filter = new();
		filter.IncludeClasses.AddRange(options.IncludeClasses);
		filter.ExcludeClasses.AddRange(options.ExcludeClasses);
		filter.IncludePaths.AddRange(options.IncludePaths);
		filter.ExcludePaths.AddRange(options.ExcludePaths);
		settings.ExportFilter = filter;

		if (options.PathOverridesFile is not null)
		{
			settings.AssetPathOverrides = AssetPathOverrides.Load(options.PathOverridesFile);
		}
		return settings;
	}

	/// <summary>
	/// Refuses dangerous targets and only deletes existing content when <paramref name="force"/> is set.
	/// </summary>
	internal static void PrepareOutputDirectory(string output, bool force)
	{
		string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
		string home = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
		string root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? "/");
		string name = Path.GetFileName(full);
		if (full == root || string.Equals(full, home, StringComparison.OrdinalIgnoreCase) || name is "Desktop" or "Documents" or "Downloads")
		{
			throw new CliUsageException($"Refusing to export into '{full}'. Choose a dedicated directory.");
		}
		if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
		{
			if (!force)
			{
				throw new CliUsageException($"Output directory '{full}' is not empty. Use --force to replace its contents.");
			}
			Directory.Delete(full, true);
		}
		Directory.CreateDirectory(full);
	}
}

/// <summary>
/// A compact description of loaded game data, used by the <c>info</c> command.
/// </summary>
internal sealed record GameSummary(
	string? Platform,
	string ScriptingBackend,
	string ProjectVersion,
	IReadOnlyList<string> UnityVersions,
	int CollectionCount,
	int AssetCount,
	int UnreadableCount,
	IReadOnlyList<KeyValuePair<string, int>> AssetsByClass)
{
	public static GameSummary Create(GameData gameData)
	{
		List<AssetCollection> collections = gameData.GameBundle.FetchAssetCollections().ToList();
		List<IUnityObjectBase> assets = gameData.GameBundle.FetchAssets().ToList();
		return new GameSummary(
			gameData.PlatformStructure?.GetType().Name.Replace("GameStructure", ""),
			gameData.AssemblyManager.ScriptingBackend.ToString(),
			gameData.ProjectVersion.ToString(),
			collections.Select(c => c.Version.ToString()).Distinct().Order().ToList(),
			collections.Count,
			assets.Count,
			assets.Count(a => a is UnreadableObject),
			assets.GroupBy(a => a.ClassName).Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
				.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList());
	}

	public string ToText()
	{
		StringBuilder sb = new();
		sb.AppendLine($"Platform:          {Platform ?? "unknown"}");
		sb.AppendLine($"Scripting backend: {ScriptingBackend}");
		sb.AppendLine($"Unity versions:    {string.Join(", ", UnityVersions)} (project: {ProjectVersion})");
		sb.AppendLine($"Collections:       {CollectionCount}");
		sb.AppendLine($"Assets:            {AssetCount} ({UnreadableCount} unreadable)");
		sb.AppendLine("Assets by class:");
		foreach ((string className, int count) in AssetsByClass)
		{
			sb.AppendLine($"  {count,8}  {className}");
		}
		return sb.ToString();
	}

	public string ToJson()
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteString("platform", Platform);
			writer.WriteString("scriptingBackend", ScriptingBackend);
			writer.WriteString("projectVersion", ProjectVersion);
			writer.WriteStartArray("unityVersions");
			foreach (string version in UnityVersions)
			{
				writer.WriteStringValue(version);
			}
			writer.WriteEndArray();
			writer.WriteNumber("collections", CollectionCount);
			writer.WriteNumber("assets", AssetCount);
			writer.WriteNumber("unreadable", UnreadableCount);
			writer.WriteStartObject("assetsByClass");
			foreach ((string className, int count) in AssetsByClass)
			{
				writer.WriteNumber(className, count);
			}
			writer.WriteEndObject();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}
}

/// <summary>Counts warnings and errors for the final summary.</summary>
internal sealed class CountingLogger : ILogger
{
	public int Warnings { get; private set; }
	public int Errors { get; private set; }

	public void Log(LogType type, LogCategory category, string message)
	{
		if (type == LogType.Warning)
		{
			Warnings++;
		}
		else if (type == LogType.Error)
		{
			Errors++;
		}
	}

	public void BlankLine(int numLines) { }
}

/// <summary>Writes only messages at or above a minimum severity to standard error.</summary>
internal sealed class FilteredConsoleLogger(LogType minimum) : ILogger
{
	public void Log(LogType type, LogCategory category, string message)
	{
		if (type is LogType.Warning or LogType.Error && (minimum != LogType.Error || type == LogType.Error))
		{
			Console.Error.WriteLine($"{category} [{type}] : {message}");
		}
	}

	public void BlankLine(int numLines) { }
}
