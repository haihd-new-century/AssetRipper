using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_25;
using AssetRipper.SourceGenerated.Classes.ClassID_2;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Extensions;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Processing.Prefabs;

/// <summary>
/// Prefab outlining: finds GameObject hierarchies that occur several times in the game's scenes and creates a prefab for each,
/// reversing the inlining Unity does when it builds scenes.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Every root GameObject and every direct child of a root in a scene is a candidate.</item>
/// <item>Candidates get a structural signature: cleaned name ("Tree (3)" → "Tree"), component types and the signatures of their children.</item>
/// <item>Signatures that occur at least <see cref="MinimumInstances"/> times, and that contain a renderer or more than one GameObject, become prefabs.</item>
/// <item>The first instance is cloned (GameObjects and components, references inside the hierarchy remapped, references to
/// meshes/materials/scripts kept) into a new collection; the root transform is reset. <see cref="PrefabProcessor"/> then exports it as a prefab.</item>
/// </list>
/// Scene objects are left untouched, so scenes export exactly as before; the outlined prefabs are additional assets
/// under <c>Assets/Prefabs/Outlined</c>. Replacing scene instances with prefab instances is not done.
/// </remarks>
public sealed partial class PrefabOutliningProcessor : IAssetProcessor
{
	public const int MinimumInstances = 2;
	public const string OutputDirectory = "Assets/Prefabs/Outlined";

	public int CreatedPrefabCount { get; private set; }

	/// <summary>
	/// The outlined prefabs: the cloned prefab root and the scene hierarchies it was created from (the first one is the clone's source).
	/// </summary>
	public List<(IGameObject PrefabRoot, List<IGameObject> SceneInstances)> Outlines { get; } = [];

	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Prefab outlining");

		List<IGameObject> candidates = [];
		foreach (AssetCollection collection in gameData.GameBundle.FetchAssetCollections().Where(c => c.IsScene))
		{
			foreach (IGameObject root in collection.OfType<IGameObject>().Where(g => g.IsRoot()))
			{
				candidates.Add(root);
				candidates.AddRange(SafeChildren(root));
			}
		}

		HashSet<string> existingPrefabNames = gameData.GameBundle.FetchAssets()
			.OfType<IGameObject>()
			.Where(g => !g.Collection.IsScene && g.IsRoot())
			.Select(g => CleanName(g.Name.String))
			.ToHashSet(StringComparer.Ordinal);

		Dictionary<string, List<IGameObject>> groups = new(StringComparer.Ordinal);
		foreach (IGameObject candidate in candidates)
		{
			string? signature = TryComputeSignature(candidate, 0);
			if (signature is null)
			{
				continue;
			}
			if (!groups.TryGetValue(signature, out List<IGameObject>? list))
			{
				list = [];
				groups.Add(signature, list);
			}
			list.Add(candidate);
		}

		ProcessedAssetCollection? output = null;
		HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string signature, List<IGameObject> instances) in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
		{
			IGameObject first = instances[0];
			string name = CleanName(first.Name.String);
			if (instances.Count < MinimumInstances || !IsInteresting(first) || existingPrefabNames.Contains(name))
			{
				continue;
			}
			output ??= gameData.AddNewProcessedCollection("Outlined Prefabs");
			try
			{
				IGameObject clone = CloneHierarchy(first, output);
				string uniqueName = name;
				for (int i = 1; !usedNames.Add(uniqueName); i++)
				{
					uniqueName = $"{name}_{i}";
				}
				clone.Name = uniqueName;
				clone.OverrideDirectory = OutputDirectory;
				clone.OverrideName = uniqueName;
				Outlines.Add((clone, instances));
				CreatedPrefabCount++;
				Logger.Verbose(LogCategory.Processing, $"Outlined prefab '{uniqueName}' from {instances.Count} scene instances");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Processing, $"Prefab outlining skipped '{name}': {ex.Message}");
			}
		}
		Logger.Info(LogCategory.Processing, $"Prefab outlining: {CreatedPrefabCount} prefab(s) created from repeated scene hierarchies");
	}

	private static IEnumerable<IGameObject> SafeChildren(IGameObject gameObject)
	{
		try
		{
			return gameObject.GetChildren().ToList();
		}
		catch
		{
			return [];
		}
	}

	/// <summary>
	/// A signature describing the structure of a hierarchy, independent of positions and instance suffixes.
	/// </summary>
	internal static string? TryComputeSignature(IGameObject gameObject, int depth)
	{
		if (depth > 32)
		{
			return null;
		}
		StringBuilder builder = new();
		builder.Append(CleanName(gameObject.Name.String)).Append('[');
		List<string> components = [];
		foreach (IComponent? component in gameObject.GetComponentAccessList())
		{
			if (component is null)
			{
				return null; // broken reference: do not outline
			}
			components.Add(component.ClassName);
		}
		components.Sort(StringComparer.Ordinal);
		builder.AppendJoin(',', components).Append(']');
		List<string> children = [];
		foreach (IGameObject child in SafeChildren(gameObject))
		{
			string? childSignature = TryComputeSignature(child, depth + 1);
			if (childSignature is null)
			{
				return null;
			}
			children.Add(childSignature);
		}
		if (children.Count > 0)
		{
			builder.Append('{').AppendJoin(';', children).Append('}');
		}
		return builder.ToString();
	}

	/// <summary>Only hierarchies that render something or have more than one object are worth a prefab.</summary>
	private static bool IsInteresting(IGameObject gameObject)
	{
		if (SafeChildren(gameObject).Any())
		{
			return true;
		}
		return gameObject.GetComponentAccessList().Any(c => c is IRenderer);
	}

	/// <summary>
	/// Clones <paramref name="root"/> with all descendants and components into <paramref name="collection"/>.
	/// References between cloned objects point to the clones; all other references are kept.
	/// </summary>
	internal static IGameObject CloneHierarchy(IGameObject root, ProcessedAssetCollection collection)
	{
		List<IUnityObjectBase> sources = root.FetchHierarchy().Cast<IUnityObjectBase>().ToList();
		Dictionary<IUnityObjectBase, IUnityObjectBase> clones = new();
		foreach (IUnityObjectBase source in sources)
		{
			IUnityObjectBase clone = collection.CreateAsset(source.ClassID, info => AssetFactory.Create(info, collection.Version));
			clones.Add(source, clone);
		}

		MultipleReplacementAssetResolver resolver = new(clones);
		foreach ((IUnityObjectBase source, IUnityObjectBase clone) in clones)
		{
			clone.CopyValues(source, new PPtrConverter(source.Collection, collection, resolver));
		}

		IGameObject rootClone = (IGameObject)clones[root];
		ITransform transform = rootClone.GetTransform();
		transform.Father_C4P = null;
		transform.InitializeDefault();
		return rootClone;
	}

	/// <summary>Removes suffixes Unity adds to duplicated objects: "Tree (3)", "Tree(Clone)", "Tree 3" are all "Tree".</summary>
	internal static string CleanName(string name)
	{
		string cleaned = CloneSuffix().Replace(name, "");
		cleaned = NumberSuffix().Replace(cleaned, "").Trim();
		return cleaned.Length == 0 ? name.Trim() : cleaned;
	}

	[GeneratedRegex(@"\s*\(Clone\)", RegexOptions.CultureInvariant)]
	private static partial Regex CloneSuffix();

	[GeneratedRegex(@"\s*\(\d+\)$|[ _]\d+$", RegexOptions.CultureInvariant)]
	private static partial Regex NumberSuffix();
}
