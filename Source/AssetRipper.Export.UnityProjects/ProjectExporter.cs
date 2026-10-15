using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;

namespace AssetRipper.Export.UnityProjects;

public sealed partial class ProjectExporter
{
	public event Action? EventExportPreparationStarted;
	public event Action? EventExportPreparationFinished;
	public event Action? EventExportStarted;
	public event Action<int, int>? EventExportProgressUpdated;
	public event Action? EventExportFinished;

	private readonly ObjectHandlerStack<IAssetExporter> assetExporterStack = new();

	/// <summary>
	/// If set, only collections containing at least one asset accepted by this predicate are written.
	/// </summary>
	/// <remarks>
	/// Filtered collections are still registered with the container, so references to them keep stable GUIDs.
	/// </remarks>
	public Func<IUnityObjectBase, bool>? AssetFilter { get; set; }

	/// <summary>
	/// Maximum number of threads used to prepare CPU-heavy collections (texture decoding and encoding).
	/// 1 disables parallel preparation.
	/// </summary>
	public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

	/// <summary>
	/// Merge identical assets duplicated across bundles into one exported file (see <see cref="AssetDeduplication"/>).
	/// </summary>
	public bool DeduplicateAssets { get; set; }

	/// <summary>
	/// Assets provided by user-defined packages are referenced by the package's GUIDs instead of being exported.
	/// </summary>
	public PackageMappings? PackageMappings { get; set; }

	/// <summary>
	/// Upper bound for raw input data held in memory by one preparation window.
	/// </summary>
	public long PreparationMemoryBudget { get; set; } = 256L * 1024 * 1024;

	/// <summary>Adds an exporter to the stack of exporters for this asset type.</summary>
	/// <typeparam name="T">The c sharp type of this asset type. Any inherited types also get this exporter.</typeparam>
	/// <param name="exporter">The new exporter. If it doesn't work, the next one in the stack is used.</param>
	/// <param name="allowInheritance">Should types that inherit from this type also use the exporter?</param>
	public void OverrideExporter<T>(IAssetExporter exporter, bool allowInheritance = true)
	{
		assetExporterStack.OverrideHandler(typeof(T), exporter, allowInheritance);
	}

	/// <summary>Adds an exporter to the stack of exporters for this asset type.</summary>
	/// <param name="type">The c sharp type of this asset type. Any inherited types also get this exporter.</param>
	/// <param name="exporter">The new exporter. If it doesn't work, the next one in the stack is used.</param>
	/// <param name="allowInheritance">Should types that inherit from this type also use the exporter?</param>
	public void OverrideExporter(Type type, IAssetExporter exporter, bool allowInheritance)
	{
		assetExporterStack.OverrideHandler(type, exporter, allowInheritance);
	}

	/// <summary>
	/// Use the <see cref="DummyExporter"/> for the specified class type.
	/// </summary>
	/// <typeparam name="T">The base type for assets of that <paramref name="classType"/>.</typeparam>
	/// <param name="isEmptyCollection">
	/// True: an exception will be thrown if the asset is referenced by another asset.<br/>
	/// False: any references to this asset will be replaced with a missing reference.
	/// </param>
	/// <param name="isMetaType"><see cref="AssetType.Meta"/> or <see cref="AssetType.Serialized"/>?</param>
	private void OverrideDummyExporter<T>(bool isEmptyCollection, bool isMetaType)
	{
		OverrideExporter<T>(DummyAssetExporter.Get(isEmptyCollection, isMetaType), true);
	}

	public AssetType ToExportType(Type type)
	{
		foreach (IAssetExporter exporter in assetExporterStack.GetHandlerStack(type))
		{
			if (exporter.ToUnknownExportType(type, out AssetType assetType))
			{
				return assetType;
			}
		}
		throw new NotSupportedException($"There is no exporter that know {nameof(AssetType)} for unknown asset '{type}'");
	}

	private IExportCollection CreateCollection(IUnityObjectBase asset)
	{
		foreach (IAssetExporter exporter in assetExporterStack.GetHandlerStack(asset.GetType()))
		{
			if (exporter.TryCreateCollection(asset, out IExportCollection? collection))
			{
				return collection;
			}
		}
		throw new Exception($"There is no exporter that can handle '{asset}'");
	}

	public void Export(GameBundle fileCollection, CoreConfiguration options, FileSystem fileSystem)
	{
		EventExportPreparationStarted?.Invoke();
		List<IExportCollection> collections = CreateCollections(fileCollection);
		if (DeduplicateAssets)
		{
			AssetDeduplication.Deduplicate(collections);
		}
		if (PackageMappings is { IsEmpty: false })
		{
			RedirectPackageAssets(collections, PackageMappings);
		}
		EventExportPreparationFinished?.Invoke();

		EventExportStarted?.Invoke();
		ProjectAssetContainer container = new ProjectAssetContainer(this, options, fileCollection.FetchAssets(), collections);
		int exportableCount = collections.Count(c => c.Exportable && PassesFilter(c));
		int skippedByFilter = collections.Count(c => c.Exportable) - exportableCount;
		if (skippedByFilter > 0)
		{
			Logger.Info(LogCategory.Export, $"{skippedByFilter} collection(s) skipped by the export filter");
		}
		int currentExportable = 0;

		int preparedUntil = 0;
		for (int i = 0; i < collections.Count; i++)
		{
			if (i >= preparedUntil && MaxDegreeOfParallelism > 1)
			{
				preparedUntil = PrepareWindow(collections, i);
			}
			IExportCollection collection = collections[i];
			container.CurrentCollection = collection;
			if (collection.Exportable && PassesFilter(collection))
			{
				currentExportable++;
				Logger.Info(LogCategory.ExportProgress, $"({currentExportable}/{exportableCount}) Exporting '{collection.Name}'");
				bool exportedSuccessfully = collection.Export(container, options.ProjectRootPath, fileSystem);
				if (!exportedSuccessfully)
				{
					Logger.Warning(LogCategory.ExportProgress, $"Failed to export '{collection.Name}' ({collection.GetType().Name})");
				}
			}
			EventExportProgressUpdated?.Invoke(i, collections.Count);
		}
		EventExportFinished?.Invoke();
	}

	/// <summary>
	/// Loads input for upcoming <see cref="IPreparableExportCollection"/>s sequentially, then prepares them in parallel.
	/// </summary>
	/// <returns>The index after the last collection in the window.</returns>
	private int PrepareWindow(List<IExportCollection> collections, int start)
	{
		List<IPreparableExportCollection> window = [];
		long bytes = 0;
		int maxCount = Math.Max(2, MaxDegreeOfParallelism * 4);
		int end = start;
		for (; end < collections.Count && window.Count < maxCount && bytes < PreparationMemoryBudget; end++)
		{
			if (collections[end] is IPreparableExportCollection preparable && preparable.Exportable && PassesFilter(preparable))
			{
				long size = preparable.LoadPreparationInput();
				if (size > 0)
				{
					window.Add(preparable);
					bytes += size;
				}
			}
		}
		if (window.Count > 1)
		{
			Parallel.ForEach(window, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism }, static c => c.Prepare());
		}
		else if (window.Count == 1)
		{
			window[0].Prepare();
		}
		return Math.Max(end, start + 1);
	}

	/// <summary>
	/// Replaces collections whose assets are all provided by user-defined packages with redirections to the package GUIDs.
	/// </summary>
	internal static int RedirectPackageAssets(List<IExportCollection> collections, PackageMappings mappings)
	{
		int redirected = 0;
		for (int i = 0; i < collections.Count; i++)
		{
			IExportCollection collection = collections[i];
			if (!collection.Exportable)
			{
				continue;
			}
			List<(IUnityObjectBase Asset, UnityGuid Guid, long FileID)> targets = [];
			bool allMapped = true;
			foreach (IUnityObjectBase asset in collection.Assets)
			{
				if (mappings.TryGetAsset(asset.Collection.Name, asset.PathID, out UnityGuid guid, out long fileID))
				{
					targets.Add((asset, guid, fileID));
				}
				else if (asset is not AssetRipper.Processing.Textures.SpriteInformationObject)
				{
					allMapped = false;
					break;
				}
			}
			if (!allMapped || targets.Count == 0)
			{
				continue;
			}
			RedirectExportCollection redirect = new();
			foreach ((IUnityObjectBase asset, UnityGuid guid, long fileID) in targets)
			{
				redirect.Add(asset, fileID, guid, AssetType.Serialized);
			}
			foreach (IUnityObjectBase asset in collection.Assets.Except(targets.Select(t => t.Asset)))
			{
				redirect.AddMissing(asset, AssetType.Serialized);
			}
			collections[i] = redirect;
			redirected++;
		}
		if (redirected > 0)
		{
			Logger.Info(LogCategory.Export, $"User-defined packages: {redirected} asset collection(s) are referenced from packages instead of exported.");
		}
		return redirected;
	}

	private bool PassesFilter(IExportCollection collection)
	{
		return AssetFilter is null || collection.Assets.Any(AssetFilter);
	}

	private List<IExportCollection> CreateCollections(GameBundle fileCollection)
	{
		List<IExportCollection> collections = new();
		HashSet<IUnityObjectBase> queued = new();

		foreach (IUnityObjectBase asset in fileCollection.FetchAssets())
		{
			if (!queued.Contains(asset))
			{
				IExportCollection collection = CreateCollection(asset);
				foreach (IUnityObjectBase element in collection.Assets)
				{
					queued.Add(element);
				}
				collections.Add(collection);
			}
		}

		return collections;
	}
}
