using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using AssetRipper.Processing;
using AssetRipper.Processing.Configuration;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Tests;

/// <summary>
/// Tests for export filtering (class / path globs) and user defined asset path overrides.
/// </summary>
public class ExportCustomizationTests
{
	private static GameData CreateGameData(ProcessedAssetCollection collection)
	{
		return new((GameBundle)collection.Bundle, collection.Version, new BaseManager(_ => { }), null);
	}

	private static VirtualFileSystem Export(ProcessedAssetCollection collection, Action<ExportHandlerSettings>? configure = null)
	{
		ExportHandlerSettings settings = new();
		configure?.Invoke(settings);
		VirtualFileSystem fileSystem = new();
		ExportHandler handler = new(settings.Configuration);
		GameData gameData = CreateGameData(collection);
		handler.Process(gameData);
		handler.Export(gameData, "output", fileSystem);
		return fileSystem;
	}

	private sealed class ExportHandlerSettings
	{
		public FullConfiguration Configuration { get; } = new();
	}

	[TestCase("Assets/Mesh/Mesh", "Assets/Mesh/**", true)]
	[TestCase("Assets/Mesh/Mesh", "Assets/*/Mesh", true)]
	[TestCase("Assets/Mesh/Sub/Mesh", "Assets/*/Mesh", false)]
	[TestCase("Assets/Mesh/Sub/Mesh", "Assets/**/Mesh", true)]
	[TestCase("Assets/Texture2D/Logo", "**/Logo", true)]
	[TestCase("Assets/Texture2D/Logo", "Assets/Texture2D/Log?", true)]
	[TestCase("Assets/Texture2D/Logo.png", "Assets/Texture2D/Logo", true)]
	[TestCase("Assets/Texture2D/Logo", "Assets/Audio/**", false)]
	public void GlobMatching(string path, string glob, bool expected)
	{
		Assert.That(ExportFilter.GlobToRegex(glob).IsMatch(path), Is.EqualTo(expected));
	}

	[Test]
	public void FilterCombinesClassAndPathRules()
	{
		ExportFilter filter = new()
		{
			IncludeClasses = ["Mesh", "Texture2D"],
			ExcludePaths = ["Assets/Texture2D/Secret*"],
		};
		using (Assert.EnterMultipleScope())
		{
			Assert.That(filter.Allows("mesh", "Assets/Mesh/A"), Is.True, "class names are case-insensitive");
			Assert.That(filter.Allows("AudioClip", "Assets/AudioClip/A"), Is.False);
			Assert.That(filter.Allows("Texture2D", "Assets/Texture2D/SecretLogo"), Is.False);
			Assert.That(filter.Allows("Texture2D", "Assets/Texture2D/Logo"), Is.True);
			Assert.That(new ExportFilter().IsEmpty, Is.True);
		}
	}

	[Test]
	public void FilterRoundTripsThroughJson()
	{
		ExportFilter filter = new() { IncludeClasses = ["Mesh"], ExcludePaths = ["**/Temp/**"] };
		ExportFilter parsed = ExportFilter.Parse(filter.ToJson());
		Assert.That(parsed.ToString(), Is.EqualTo(filter.ToString()));
	}

	[Test]
	public void ExcludedClassesAreNotWritten()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		IMesh mesh = collection.CreateMesh();
		mesh.Name = "KeepMe";
		collection.CreateMonoBehaviour().Name = "DropMe";

		VirtualFileSystem fileSystem = Export(collection, s => s.Configuration.ExportFilter = new() { IncludeClasses = ["Mesh"] });

		using (Assert.EnterMultipleScope())
		{
			Assert.That(fileSystem.File.Exists("/output/ExportedProject/Assets/Mesh/KeepMe.asset"), Is.True);
			Assert.That(fileSystem.File.Exists("/output/ExportedProject/Assets/MonoBehaviour/DropMe.asset"), Is.False);
		}
	}

	[Test]
	public void PathOverrideChangesExportLocation()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		IMesh mesh = collection.CreateMesh();
		mesh.Name = "Original";

		AssetPathOverrides overrides = new()
		{
			Files = { [collection.Name] = new() { [mesh.PathID.ToString()] = "Assets/Models/Renamed.asset" } },
		};
		VirtualFileSystem fileSystem = Export(collection, s => s.Configuration.AssetPathOverrides = AssetPathOverrides.Parse(overrides.ToJson()));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(fileSystem.File.Exists("/output/ExportedProject/Assets/Models/Renamed.asset"), Is.True);
			Assert.That(fileSystem.File.Exists("/output/ExportedProject/Assets/Mesh/Original.asset"), Is.False);
		}
	}

	[TestCase(true, 1)]
	[TestCase(false, 2)]
	public void IdenticalAssetsInDifferentBundlesAreDeduplicated(bool enabled, int expectedFiles)
	{
		GameBundle bundle = new();
		ProcessedAssetCollection first = bundle.AddNewProcessedCollection("bundle_a", UnityVersion.V_2022);
		ProcessedAssetCollection second = bundle.AddNewProcessedCollection("bundle_b", UnityVersion.V_2022);
		first.CreateMesh().Name = "Shared";
		second.CreateMesh().Name = "Shared";

		VirtualFileSystem fileSystem = Export(first, s => s.Configuration.ProcessingSettings.EnableAssetDeduplication = enabled);

		string[] meshes = fileSystem.Directory.GetFiles("/output/ExportedProject/Assets/Mesh", "*.asset", SearchOption.TopDirectoryOnly);
		Assert.That(meshes, Has.Length.EqualTo(expectedFiles));
	}

	[Test]
	public void DifferentAssetsWithSameNameAreKept()
	{
		GameBundle bundle = new();
		ProcessedAssetCollection first = bundle.AddNewProcessedCollection("bundle_a", UnityVersion.V_2022);
		ProcessedAssetCollection second = bundle.AddNewProcessedCollection("bundle_b", UnityVersion.V_2022);
		first.CreateMesh().Name = "Shared";
		IMesh other = second.CreateMesh();
		other.Name = "Shared";
		other.FillWithCompressedMeshData(MeshData.CreateTriangleMesh());

		VirtualFileSystem fileSystem = Export(first, s => s.Configuration.ProcessingSettings.EnableAssetDeduplication = true);

		Assert.That(fileSystem.Directory.GetFiles("/output/ExportedProject/Assets/Mesh", "*.asset", SearchOption.TopDirectoryOnly), Has.Length.EqualTo(2));
	}

	[TestCase("Assets/A.png", "Assets/A.png")]
	[TestCase("Assets\\Sub\\A.png", "Assets/Sub/A.png")]
	[TestCase("/etc/passwd", null)]
	[TestCase("C:/Windows/A.png", null)]
	[TestCase("Assets/../../A.png", null)]
	[TestCase("", null)]
	public void PathOverridesRejectUnsafePaths(string input, string? expected)
	{
		Assert.That(AssetPathOverrides.NormalizePath(input), Is.EqualTo(expected));
	}

	[Test]
	public void InvalidOverrideEntriesAreIgnored()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		IMesh mesh = collection.CreateMesh();
		AssetPathOverrides overrides = new()
		{
			Files =
			{
				[collection.Name] = new() { ["not-a-number"] = "Assets/X.asset", [mesh.PathID.ToString()] = "../escape.asset", ["987654"] = "Assets/Missing.asset" },
				["unknown-collection"] = new() { ["1"] = "Assets/Y.asset" },
			},
		};
		Assert.DoesNotThrow(() => new AssetPathOverrideProcessor(overrides).Process(CreateGameData(collection)));
		Assert.That(mesh.OverridePath, Is.Null);
	}
}
