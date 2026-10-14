using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_128;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Classes.ClassID_72;
using AssetRipper.SourceGenerated.Classes.ClassID_83;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// An export collection for an asset that is identical to another asset which is exported elsewhere.
/// References to the duplicate are redirected to the file of the canonical asset.
/// </summary>
public sealed class AliasExportCollection : IExportCollection
{
	private readonly Dictionary<IUnityObjectBase, IUnityObjectBase> canonicalAssets = new();

	public AliasExportCollection(IExportCollection target)
	{
		Target = target;
	}

	/// <summary>The collection that writes the canonical asset.</summary>
	public IExportCollection Target { get; }

	public void Add(IUnityObjectBase duplicate, IUnityObjectBase canonical) => canonicalAssets.Add(duplicate, canonical);

	public IEnumerable<IUnityObjectBase> Assets => canonicalAssets.Keys;

	public string Name => $"Alias of {Target.Name}";

	public bool Exportable => false;

	public AssetCollection File => Target.File;

	public TransferInstructionFlags Flags => Target.Flags;

	public bool Contains(IUnityObjectBase asset) => canonicalAssets.ContainsKey(asset);

	public long GetExportID(IExportContainer container, IUnityObjectBase asset) => Target.GetExportID(container, canonicalAssets[asset]);

	public MetaPtr CreateExportPointer(IExportContainer container, IUnityObjectBase asset, bool isLocal)
	{
		// A duplicate is never in the same file as the referencing asset, because it is not exported at all.
		return Target.CreateExportPointer(container, canonicalAssets[asset], false);
	}

	public bool Export(IExportContainer container, string projectDirectory, FileSystem fileSystem) => throw new NotSupportedException();
}

/// <summary>
/// Finds export collections whose assets are identical and replaces all but one with <see cref="AliasExportCollection"/>s.
/// </summary>
/// <remarks>
/// Unity duplicates shared assets into every asset bundle that uses them. This reverses that for asset types
/// that are self-contained: text assets, audio clips, meshes, shaders and textures without sprites.
/// Equality is decided by <see cref="AssetEqualityComparer"/>, which compares all serialized fields
/// (including referenced assets), so only true duplicates are merged.
/// </remarks>
public static class AssetDeduplication
{
	public static bool IsCandidate(IUnityObjectBase asset) => asset is ITextAsset or IAudioClip or IMesh or IShader
		|| (asset is ITexture2D && asset.MainAsset is SpriteInformationObject { Sprites.Count: 0 });

	/// <summary>
	/// Replaces duplicate collections in place.
	/// </summary>
	/// <returns>The number of collections that were replaced.</returns>
	public static int Deduplicate(List<IExportCollection> collections)
	{
		// Candidate collections export a single eligible asset (plus, for textures, its sprite information object).
		List<(int Index, IUnityObjectBase Asset)> candidates = [];
		for (int i = 0; i < collections.Count; i++)
		{
			IExportCollection collection = collections[i];
			if (!collection.Exportable)
			{
				continue;
			}
			IUnityObjectBase[] assets = collection.Assets.Where(a => a is not SpriteInformationObject).ToArray();
			if (assets.Length == 1 && IsCandidate(assets[0]))
			{
				candidates.Add((i, assets[0]));
			}
		}

		AssetEqualityComparer comparer = new();
		int replaced = 0;
		foreach (IGrouping<(Type, string), (int Index, IUnityObjectBase Asset)> group in candidates.GroupBy(c => (c.Asset.GetType(), c.Asset.GetBestName())))
		{
			List<(int Index, IUnityObjectBase Asset)> canonicals = [];
			foreach ((int index, IUnityObjectBase asset) in group)
			{
				(int Index, IUnityObjectBase Asset)? match = null;
				foreach ((int Index, IUnityObjectBase Asset) canonical in canonicals)
				{
					if (canonical.Asset.Collection != asset.Collection && SafeEquals(comparer, canonical.Asset, asset))
					{
						match = canonical;
						break;
					}
				}
				if (match is { } found)
				{
					IExportCollection original = collections[index];
					AliasExportCollection alias = new(collections[found.Index]);
					foreach (IUnityObjectBase element in original.Assets)
					{
						// Sprite information objects of a duplicate texture map to the canonical texture's one.
						IUnityObjectBase target = element == asset ? found.Asset : (found.Asset.MainAsset ?? found.Asset);
						alias.Add(element, target);
					}
					collections[index] = alias;
					replaced++;
				}
				else
				{
					canonicals.Add((index, asset));
				}
			}
		}
		if (replaced > 0)
		{
			Logger.Info(LogCategory.Export, $"Asset deduplication: {replaced} duplicate asset(s) will reference a single exported copy.");
		}
		return replaced;
	}

	private static bool SafeEquals(AssetEqualityComparer comparer, IUnityObjectBase x, IUnityObjectBase y)
	{
		try
		{
			return comparer.Equals(x, y);
		}
		catch (Exception)
		{
			return false;
		}
	}
}
