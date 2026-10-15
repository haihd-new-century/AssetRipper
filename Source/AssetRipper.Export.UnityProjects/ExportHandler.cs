using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.PathIdMapping;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Export.UnityProjects.Scripts;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure;
using AssetRipper.Processing;
using AssetRipper.Processing.AnimatorControllers;
using AssetRipper.Processing.Assemblies;
using AssetRipper.Processing.AudioMixers;
using AssetRipper.Processing.Configuration;
using AssetRipper.Processing.Editor;
using AssetRipper.Processing.Prefabs;
using AssetRipper.Processing.Scenes;
using AssetRipper.Processing.ScriptableObject;
using AssetRipper.Processing.Textures;

namespace AssetRipper.Export.UnityProjects;

public class ExportHandler
{
	protected FullConfiguration Settings { get; }

	public ExportHandler(FullConfiguration settings)
	{
		Settings = settings;
	}

	/// <summary>
	/// Threads used to prepare CPU-heavy collections (textures) during Unity project export. 1 disables parallelism.
	/// </summary>
	public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

	/// <summary>
	/// Upper bound for raw texture data held in memory while preparing collections in parallel.
	/// </summary>
	public long PreparationMemoryBudget { get; set; } = 256L * 1024 * 1024;

	public GameData Load(IReadOnlyList<string> paths, FileSystem fileSystem)
	{
		if (paths.Count == 1)
		{
			Logger.Info(LogCategory.Import, $"Attempting to read files from {paths[0]}");
		}
		else
		{
			Logger.Info(LogCategory.Import, $"Attempting to read files from {paths.Count} paths...");
		}

		GameStructure gameStructure = GameStructure.Load(paths, fileSystem, Settings);
		GameData gameData = GameData.FromGameStructure(gameStructure);
		Logger.Info(LogCategory.Import, "Finished reading files");
		return gameData;
	}

	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Processing loaded assets...");
		foreach (IAssetProcessor processor in GetProcessors())
		{
			processor.Process(gameData);
		}
		Logger.Info(LogCategory.Processing, "Finished processing assets");
	}

	protected virtual IEnumerable<IAssetProcessor> GetProcessors()
	{
		// Assembly processors
		yield return new AttributePolyfillGenerator();
		yield return new MonoExplicitPropertyRepairProcessor();
		yield return new ObfuscationRepairProcessor();
		yield return new ForwardingAssemblyGenerator();
		if (Settings.ImportSettings.ScriptContentLevel == ScriptContentLevel.Level1)
		{
			yield return new MethodStubbingProcessor();
		}
		yield return new NullRefReturnProcessor(Settings.ImportSettings.ScriptContentLevel);
		yield return new UnmanagedConstraintRecoveryProcessor();
		if (Settings.ProcessingSettings.RemoveNullableAttributes)
		{
			yield return new NullableRemovalProcessor();
		}
		if (Settings.ProcessingSettings.PublicizeAssemblies)
		{
			yield return new SafeAssemblyPublicizingProcessor();
		}
		yield return new RemoveAssemblyKeyFileAttributeProcessor();
		yield return new InternalsVisibileToPublicKeyRemover();
		yield return new FieldInitializerRecoveryProcessor(Settings.ImportSettings.ScriptContentLevel);

		// Asset processors
		yield return new SceneDefinitionProcessor();
		yield return new OriginalPathProcessor(Settings.ProcessingSettings.BundledAssetsExportMode);
		yield return new MainAssetProcessor();
		yield return new AnimatorControllerProcessor();
		yield return new AudioMixerProcessor();
		yield return new EditorFormatProcessor(Settings.ProcessingSettings.BundledAssetsExportMode);
		if (Settings.ProcessingSettings.EnableStaticMeshSeparation)
		{
			yield return new StaticMeshSeparationProcessor();
		}
		yield return new LightingDataProcessor();//Needs to be after static mesh separation
		PrefabOutliningProcessor? outliner = Settings.ProcessingSettings.EnablePrefabOutlining ? new PrefabOutliningProcessor() : null;
		if (outliner is not null)
		{
			yield return outliner;//Must be before PrefabProcessor, which turns the outlined hierarchies into prefabs
		}
		yield return new PrefabProcessor();
		if (outliner is not null)
		{
			yield return new PrefabInstanceLinkingProcessor(outliner);//Must be after PrefabProcessor, which creates the scene and prefab hierarchies
		}
		yield return new SpriteProcessor();
		yield return new ScriptableObjectProcessor();
		yield return new AssetPathOverrideProcessor(Settings.AssetPathOverrides);
	}

	public void Export(GameData gameData, string outputPath, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "Starting export");
		Logger.Info(LogCategory.Export, $"Attempting to export assets to {outputPath}...");
		Logger.Info(LogCategory.Export, $"Game files have these Unity versions: {GetListOfVersions(gameData.GameBundle)}");
		Logger.Info(LogCategory.Export, $"Exporting to Unity version {gameData.ProjectVersion}");

		Settings.ExportRootPath = outputPath;
		Settings.SetProjectSettings(gameData.ProjectVersion);

		ProjectExporter projectExporter = new(Settings, gameData.AssemblyManager)
		{
			MaxDegreeOfParallelism = Math.Max(1, MaxDegreeOfParallelism),
			PreparationMemoryBudget = PreparationMemoryBudget,
			DeduplicateAssets = Settings.ProcessingSettings.EnableAssetDeduplication,
			PackageMappings = Settings.PackageMappings,
		};
		if (!Settings.ExportFilter.IsEmpty)
		{
			Logger.Info(LogCategory.Export, $"Export filter: {Settings.ExportFilter}");
			projectExporter.AssetFilter = Settings.ExportFilter.Allows;
		}
		BeforeExport(projectExporter);
		projectExporter.DoFinalOverrides(Settings);
		projectExporter.Export(gameData.GameBundle, Settings, fileSystem);

		Logger.Info(LogCategory.Export, "Finished exporting assets");

		foreach (IPostExporter postExporter in GetPostExporters())
		{
			postExporter.DoPostExport(gameData, Settings, fileSystem);
		}
		Logger.Info(LogCategory.Export, "Finished post-export");

		static string GetListOfVersions(GameBundle gameBundle)
		{
			return string.Join(' ', gameBundle
				.FetchAssetCollections()
				.Select(c => c.Version)
				.Distinct()
				.Select(v => v.ToString()));
		}
	}

	protected virtual void BeforeExport(ProjectExporter projectExporter)
	{
		// Needed for the premium edition
	}

	protected virtual IEnumerable<IPostExporter> GetPostExporters()
	{
		yield return new ProjectVersionPostExporter();
		yield return new PackageManifestPostExporter();
		yield return new StreamingAssetsPostExporter();
		yield return new DllPostExporter();
		yield return new PathIdMapExporter();
	}

	public GameData LoadAndProcess(IReadOnlyList<string> paths, FileSystem fileSystem)
	{
		GameData gameData = Load(paths, fileSystem);
		if (gameData.GameBundle.HasAnyAssetCollections())
		{
			Process(gameData);
		}
		return gameData;
	}

	public void LoadProcessAndExport(IReadOnlyList<string> inputPaths, string outputPath, FileSystem fileSystem)
	{
		GameData gameData = LoadAndProcess(inputPaths, fileSystem);
		Export(gameData, outputPath, fileSystem);
	}

	public void ThrowIfSettingsDontMatch(FullConfiguration settings)
	{
		if (Settings != settings)
		{
			throw new ArgumentException("Settings don't match");
		}
	}
}
