using AssetRipper.CLI;
using AssetRipper.Import.Configuration;

namespace AssetRipper.Tests;

public class CliOptionsTests
{
	private static string[] Split(string args) => args.Length == 0 ? [] : args.Split('|');

	[Test]
	public void ExportWithAllOptionsIsParsed()
	{
		CliOptions options = CliOptions.Parse([
			"export", "game.apk", "extra.obb", "-o", "out", "--mode", "primary", "-f",
			"--script-level", "1", "--include-class", "Texture2D, AudioClip", "--exclude-class", "Shader",
			"--include-path", "Assets/**", "--exclude-path", "**/Temp/**", "--path-overrides", "p.json", "--log", "x.log", "-q",
		]);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(options.Command, Is.EqualTo("export"));
			Assert.That(options.Inputs, Is.EqualTo(new[] { "game.apk", "extra.obb" }));
			Assert.That(options.Output, Is.EqualTo("out"));
			Assert.That(options.Mode, Is.EqualTo("primary"));
			Assert.That(options.Force, Is.True);
			Assert.That(options.ScriptLevel, Is.EqualTo(ScriptContentLevel.Level1));
			Assert.That(options.IncludeClasses, Is.EqualTo(new[] { "Texture2D", "AudioClip" }));
			Assert.That(options.ExcludeClasses, Is.EqualTo(new[] { "Shader" }));
			Assert.That(options.IncludePaths, Is.EqualTo(new[] { "Assets/**" }));
			Assert.That(options.ExcludePaths, Is.EqualTo(new[] { "**/Temp/**" }));
			Assert.That(options.PathOverridesFile, Is.EqualTo("p.json"));
			Assert.That(options.LogFile, Is.EqualTo("x.log"));
			Assert.That(options.Quiet, Is.True);
		}
	}

	[Test]
	public void ThreadsAndLowMemoryAreParsed()
	{
		CliOptions options = CliOptions.Parse(["export", "g", "-o", "o", "-j", "3", "--low-memory"]);
		Assert.That(options.Threads, Is.EqualTo(3));
		Assert.That(options.LowMemory, Is.True);
		Assert.Throws<CliUsageException>(() => CliOptions.Parse(["export", "g", "-o", "o", "-j", "0"]));
	}

	[Test]
	public void BundleMemoryOptionsCanBeLoweredAndReset()
	{
		try
		{
			AssetRipper.IO.Files.BundleFiles.BundleMemoryOptions.UseLowMemoryLimits();
			Assert.That(AssetRipper.IO.Files.BundleFiles.BundleMemoryOptions.MaxInMemoryDecompressedSize, Is.EqualTo(4 * 1024 * 1024));
			AssetRipper.IO.Files.BundleFiles.BundleMemoryOptions.MaxInMemoryDecompressedSize = -5;
			Assert.That(AssetRipper.IO.Files.BundleFiles.BundleMemoryOptions.MaxInMemoryDecompressedSize, Is.Zero);
		}
		finally
		{
			AssetRipper.IO.Files.BundleFiles.BundleMemoryOptions.Reset();
		}
		Assert.That(AssetRipper.IO.Files.BundleFiles.BundleMemoryOptions.MaxInMemoryDecompressedSize, Is.EqualTo(50 * 1024 * 1024));
	}

	[TestCase("")]
	[TestCase("--help")]
	[TestCase("export|--help")]
	public void HelpIsRecognized(string args)
	{
		Assert.That(CliOptions.Parse(Split(args)).Help, Is.True);
	}

	[TestCase("unknown")]
	[TestCase("export|game.apk")]
	[TestCase("export|-o|out")]
	[TestCase("export|game.apk|-o")]
	[TestCase("export|game.apk|-o|out|--mode|zip")]
	[TestCase("export|game.apk|-o|out|--script-level|7")]
	[TestCase("info|game.apk|--bogus")]
	public void InvalidArgumentsAreRejected(string args)
	{
		Assert.Throws<CliUsageException>(() => CliOptions.Parse(Split(args)));
	}

	[Test]
	public void SettingsCarryFilterAndScriptLevel()
	{
		CliOptions options = CliOptions.Parse(["export", "g", "-o", "o", "--include-class", "Mesh", "--script-level", "0"]);
		var settings = Program.CreateSettings(options);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(settings.ExportFilter.IncludeClasses, Is.EqualTo(new[] { "Mesh" }));
			Assert.That(settings.ImportSettings.ScriptContentLevel, Is.EqualTo(ScriptContentLevel.Level0));
		}
	}

	[Test]
	public void OutputDirectorySafety()
	{
		string temp = Path.Combine(Path.GetTempPath(), "ar-cli-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(temp);
			File.WriteAllText(Path.Combine(temp, "keep.txt"), "x");
			Assert.Throws<CliUsageException>(() => Program.PrepareOutputDirectory(temp, force: false));
			Assert.That(File.Exists(Path.Combine(temp, "keep.txt")), Is.True, "nothing is deleted without --force");

			Program.PrepareOutputDirectory(temp, force: true);
			Assert.That(Directory.EnumerateFileSystemEntries(temp), Is.Empty);

			Assert.Throws<CliUsageException>(() => Program.PrepareOutputDirectory(Path.GetPathRoot(temp)!, force: true));
			Assert.Throws<CliUsageException>(() => Program.PrepareOutputDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), force: true));
		}
		finally
		{
			if (Directory.Exists(temp))
			{
				Directory.Delete(temp, true);
			}
		}
	}
}
