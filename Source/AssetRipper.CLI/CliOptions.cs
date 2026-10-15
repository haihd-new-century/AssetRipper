using AssetRipper.Import.Configuration;

namespace AssetRipper.CLI;

/// <summary>
/// Parsed command line for the AssetRipper CLI.
/// </summary>
internal sealed class CliOptions
{
	public string Command { get; set; } = "";
	public List<string> Inputs { get; } = [];
	public string? Output { get; set; }
	public string Mode { get; set; } = "unity";
	public string? SettingsFile { get; set; }
	public ScriptContentLevel? ScriptLevel { get; set; }
	public List<string> IncludeClasses { get; } = [];
	public List<string> ExcludeClasses { get; } = [];
	public List<string> IncludePaths { get; } = [];
	public List<string> ExcludePaths { get; } = [];
	public string? PathOverridesFile { get; set; }
	public string? LogFile { get; set; }
	public bool Force { get; set; }
	public bool Json { get; set; }
	public bool Quiet { get; set; }
	public bool Help { get; set; }
	public bool LowMemory { get; set; }
	public bool Deduplicate { get; set; }
	public string? PackagesFile { get; set; }
	public string? ShaderMode { get; set; }
	public int? Threads { get; set; }

	public const string Usage = """
		AssetRipper command line interface

		Usage:
		  AssetRipper.CLI export <input>... -o <output> [options]
		  AssetRipper.CLI info   <input>... [--json]
		  AssetRipper.CLI --help

		<input> is a game folder, a file (apk, xapk, zip, bundle, assets...), or several of them.

		Export options:
		  -o, --output <dir>          Output directory (required for export)
		  -m, --mode <unity|primary>  unity: Unity project (default). primary: raw content (png, wav, glb, json...)
		  -f, --force                 Delete the output directory first if it is not empty
		      --settings <file.json>  AssetRipper.Settings.json to use (same format as the GUI)
		      --script-level <0-3>    Script content level (0 = no scripts, 2 = default)
		      --include-class <A,B>   Only export these asset classes (e.g. Texture2D,AudioClip)
		      --exclude-class <A,B>   Do not export these asset classes
		      --include-path <glob>   Only export assets whose path matches (repeatable, e.g. "Assets/Texture2D/**")
		      --exclude-path <glob>   Do not export assets whose path matches (repeatable)
		      --path-overrides <file> JSON file with per-asset export paths (see documentation)
		  -j, --threads <n>           Threads for texture conversion (default: CPU count, 1 = sequential)
		      --low-memory            Keep less data in RAM (temp files for bundles, smaller batches)
		      --dedupe                Merge identical assets duplicated across bundles into one file
		      --shaders <mode>        dummy | yaml | decompile (GLSL reconstruction for GL/GLES builds)
		      --packages <file.json>  Reference scripts/assets from user-defined Unity packages instead of exporting them

		Common options:
		      --log <file>            Also write the log to a file
		  -q, --quiet                 Only print warnings and errors
		      --json                  (info) print machine readable JSON

		Exit codes: 0 success, 1 usage error, 2 nothing could be loaded, 3 export failed.
		""";

	/// <summary>
	/// Parses <paramref name="args"/>. Throws <see cref="CliUsageException"/> on invalid input.
	/// </summary>
	public static CliOptions Parse(IReadOnlyList<string> args)
	{
		CliOptions options = new();
		if (args.Count == 0)
		{
			options.Help = true;
			return options;
		}

		int i = 0;
		string first = args[0];
		if (first is "-h" or "--help" or "help")
		{
			options.Help = true;
			return options;
		}
		options.Command = first.ToLowerInvariant();
		if (options.Command is not ("export" or "info"))
		{
			throw new CliUsageException($"Unknown command '{first}'. Expected 'export' or 'info'.");
		}
		i++;

		string NextValue(string name)
		{
			if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
			{
				throw new CliUsageException($"Option '{name}' requires a value.");
			}
			i++;
			return args[i];
		}

		static IEnumerable<string> SplitList(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		for (; i < args.Count; i++)
		{
			string arg = args[i];
			switch (arg)
			{
				case "-h" or "--help":
					options.Help = true;
					break;
				case "-o" or "--output":
					options.Output = NextValue(arg);
					break;
				case "-m" or "--mode":
					options.Mode = NextValue(arg).ToLowerInvariant();
					if (options.Mode is not ("unity" or "primary"))
					{
						throw new CliUsageException($"Invalid mode '{options.Mode}'. Expected 'unity' or 'primary'.");
					}
					break;
				case "-f" or "--force":
					options.Force = true;
					break;
				case "--settings":
					options.SettingsFile = NextValue(arg);
					break;
				case "--script-level":
					string level = NextValue(arg);
					if (!int.TryParse(level, out int levelNumber) || levelNumber is < 0 or > 3)
					{
						throw new CliUsageException($"Invalid script level '{level}'. Expected 0, 1, 2 or 3.");
					}
					options.ScriptLevel = (ScriptContentLevel)levelNumber;
					break;
				case "--include-class":
					options.IncludeClasses.AddRange(SplitList(NextValue(arg)));
					break;
				case "--exclude-class":
					options.ExcludeClasses.AddRange(SplitList(NextValue(arg)));
					break;
				case "--include-path":
					options.IncludePaths.Add(NextValue(arg));
					break;
				case "--exclude-path":
					options.ExcludePaths.Add(NextValue(arg));
					break;
				case "--path-overrides":
					options.PathOverridesFile = NextValue(arg);
					break;
				case "-j" or "--threads":
					string threads = NextValue(arg);
					if (!int.TryParse(threads, out int threadCount) || threadCount < 1)
					{
						throw new CliUsageException($"Invalid thread count '{threads}'.");
					}
					options.Threads = threadCount;
					break;
				case "--shaders":
					options.ShaderMode = NextValue(arg).ToLowerInvariant();
					if (options.ShaderMode is not ("dummy" or "yaml" or "decompile"))
					{
						throw new CliUsageException($"Invalid shader mode '{options.ShaderMode}'. Expected dummy, yaml or decompile.");
					}
					break;
				case "--packages":
					options.PackagesFile = NextValue(arg);
					break;
				case "--dedupe":
					options.Deduplicate = true;
					break;
				case "--low-memory":
					options.LowMemory = true;
					break;
				case "--log":
					options.LogFile = NextValue(arg);
					break;
				case "-q" or "--quiet":
					options.Quiet = true;
					break;
				case "--json":
					options.Json = true;
					break;
				default:
					if (arg.StartsWith('-') && arg.Length > 1)
					{
						throw new CliUsageException($"Unknown option '{arg}'.");
					}
					options.Inputs.Add(arg);
					break;
			}
		}

		if (options.Help)
		{
			return options;
		}
		if (options.Inputs.Count == 0)
		{
			throw new CliUsageException("At least one input path is required.");
		}
		if (options.Command == "export" && string.IsNullOrWhiteSpace(options.Output))
		{
			throw new CliUsageException("The export command requires --output.");
		}
		return options;
	}
}

internal sealed class CliUsageException(string message) : Exception(message);
