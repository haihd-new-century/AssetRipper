using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_1001;
using AssetRipper.SourceGenerated.Classes.ClassID_1660057539;
using AssetRipper.SourceGenerated.Classes.ClassID_18;
using AssetRipper.SourceGenerated.Classes.ClassID_2;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.PropertyModification;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace AssetRipper.Processing.Prefabs;

/// <summary>
/// Prefab outlining, phase 2: replaces scene hierarchies with instances of the prefabs created by <see cref="PrefabOutliningProcessor"/>.
/// </summary>
/// <remarks>
/// For every scene instance whose content is identical to the prefab (apart from the root's name, position, rotation and scale):
/// <list type="bullet">
/// <item>a <c>PrefabInstance</c> is added to the scene, parented like the original, with the root's transform and name as modifications;</item>
/// <item>objects of the instance that are referenced from elsewhere in the scene (for example the root transform in its parent's children list)
/// are kept as <em>stripped</em> objects pointing at the corresponding prefab object, exactly like Unity serializes them;</item>
/// <item>all other objects of the instance are no longer written to the scene.</item>
/// </list>
/// Instances that differ in any other way stay inline, so no data is lost. Requires Unity 2018.3+ (nested prefab format).
/// Must run after <see cref="PrefabProcessor"/>.
/// </remarks>
public sealed class PrefabInstanceLinkingProcessor(PrefabOutliningProcessor outliner) : IAssetProcessor
{
	public int LinkedInstanceCount { get; private set; }
	public int SkippedInstanceCount { get; private set; }

	public void Process(GameData gameData)
	{
		if (outliner.Outlines.Count == 0)
		{
			return;
		}
		Logger.Info(LogCategory.Processing, "Linking scene instances to outlined prefabs");
		foreach ((IGameObject prefabRoot, List<IGameObject> instances) in outliner.Outlines)
		{
			if (prefabRoot.MainAsset is not PrefabHierarchyObject prefabHierarchy)
			{
				continue;
			}
			foreach (IGameObject instance in instances)
			{
				string? reason = TryLink(instance, prefabRoot, prefabHierarchy);
				if (reason is null)
				{
					LinkedInstanceCount++;
				}
				else
				{
					SkippedInstanceCount++;
					Logger.Verbose(LogCategory.Processing, $"Prefab instance '{instance.Name}' left inline: {reason}");
				}
			}
		}
		Logger.Info(LogCategory.Processing, $"Prefab outlining: {LinkedInstanceCount} scene instance(s) linked, {SkippedInstanceCount} left inline");
	}

	private static string? TryLink(IGameObject instance, IGameObject prefabRoot, PrefabHierarchyObject prefabHierarchy)
	{
		if (instance.MainAsset is not SceneHierarchyObject scene)
		{
			return "not part of a scene hierarchy";
		}
		if (!instance.Collection.Version.GreaterThanOrEquals(2018, 3))
		{
			return "Unity version older than 2018.3";
		}
		if (scene.Collection is not ProcessedAssetCollection sceneCollection)
		{
			return "scene collection is not writable";
		}

		Dictionary<IUnityObjectBase, IUnityObjectBase> map = new();
		if (!TryMap(instance, prefabRoot, map))
		{
			return "structure differs from the prefab";
		}
		ITransform instanceTransform = instance.GetTransform();
		foreach ((IUnityObjectBase source, IUnityObjectBase target) in map)
		{
			if (!ContentEquals(source, target, map, isRoot: source == instance || source == instanceTransform))
			{
				return $"{source.ClassName} '{source.GetBestName()}' differs from the prefab";
			}
		}

		// Objects referenced from outside the instance must stay as stripped objects.
		HashSet<IUnityObjectBase> internalObjects = map.Keys.ToHashSet();
		HashSet<IUnityObjectBase> referenced = [];
		IEnumerable<IUnityObjectBase> sceneObjects = ((IEnumerable<IUnityObjectBase>)scene.GameObjects).Concat(scene.Components).ToList();
		foreach (IUnityObjectBase asset in sceneObjects)
		{
			if (internalObjects.Contains(asset) || scene.HiddenAssets.Contains(asset))
			{
				continue;
			}
			foreach ((_, PPtr pptr) in asset.FetchDependencies())
			{
				if (asset.Collection.TryGetAsset(pptr) is { } target && internalObjects.Contains(target))
				{
					referenced.Add(target);
				}
			}
		}

		IPrefabInstance prefabInstance = sceneCollection.CreatePrefabInstance();
		prefabInstance.SourcePrefabP = prefabHierarchy.Prefab;
		prefabInstance.Modification.TransformParent.SetAsset(prefabInstance.Collection, instanceTransform.Father_C4P);
		ITransform prefabTransform = prefabRoot.GetTransform();
		AddModification(prefabInstance, prefabTransform, "m_LocalPosition.x", instanceTransform.LocalPosition_C4.X);
		AddModification(prefabInstance, prefabTransform, "m_LocalPosition.y", instanceTransform.LocalPosition_C4.Y);
		AddModification(prefabInstance, prefabTransform, "m_LocalPosition.z", instanceTransform.LocalPosition_C4.Z);
		AddModification(prefabInstance, prefabTransform, "m_LocalRotation.w", instanceTransform.LocalRotation_C4.W);
		AddModification(prefabInstance, prefabTransform, "m_LocalRotation.x", instanceTransform.LocalRotation_C4.X);
		AddModification(prefabInstance, prefabTransform, "m_LocalRotation.y", instanceTransform.LocalRotation_C4.Y);
		AddModification(prefabInstance, prefabTransform, "m_LocalRotation.z", instanceTransform.LocalRotation_C4.Z);
		AddModification(prefabInstance, prefabTransform, "m_LocalScale.x", instanceTransform.LocalScale_C4.X);
		AddModification(prefabInstance, prefabTransform, "m_LocalScale.y", instanceTransform.LocalScale_C4.Y);
		AddModification(prefabInstance, prefabTransform, "m_LocalScale.z", instanceTransform.LocalScale_C4.Z);
		if (instance.Name != prefabRoot.Name)
		{
			AddModification(prefabInstance, prefabRoot, "m_Name", instance.Name.String);
		}
		if (instance.GetIsActive() != prefabRoot.GetIsActive())
		{
			AddModification(prefabInstance, prefabRoot, "m_IsActive", instance.GetIsActive() ? "1" : "0");
		}

		foreach ((IUnityObjectBase source, IUnityObjectBase target) in map)
		{
			if (referenced.Contains(source) && source is IEditorExtension extension && target is IEditorExtension prefabObject)
			{
				extension.CorrespondingSourceObject_C18P = prefabObject;
				extension.PrefabInstance_C18P = prefabInstance;
				scene.StrippedAssets.Add(source);
			}
			else
			{
				scene.HiddenAssets.Add(source);
			}
		}

		// Unity 2022.2+: scene roots list the prefab instance instead of its root transform.
		if (scene.SceneRoots is ISceneRoots roots && instanceTransform.Father_C4P is null)
		{
			for (int i = 0; i < roots.RootsP.Count; i++)
			{
				if (roots.RootsP[i] == instanceTransform)
				{
					roots.Roots[i].SetAsset(roots.Collection, prefabInstance);
				}
			}
		}

		scene.PrefabInstances.Add(prefabInstance);
		scene.SetMainAsset();
		return null;
	}

	private static void AddModification(IPrefabInstance prefabInstance, AssetRipper.SourceGenerated.Classes.ClassID_0.IObject target, string propertyPath, float value)
	{
		AddModification(prefabInstance, target, propertyPath, value.ToString("R", CultureInfo.InvariantCulture));
	}

	private static void AddModification(IPrefabInstance prefabInstance, AssetRipper.SourceGenerated.Classes.ClassID_0.IObject target, string propertyPath, string value)
	{
		IPropertyModification modification = prefabInstance.Modification.Modifications.AddNew();
		modification.Target.SetAsset(prefabInstance.Collection, target);
		modification.PropertyPath = propertyPath;
		modification.Value = value;
	}

	/// <summary>Maps every object of the instance hierarchy to the corresponding prefab object.</summary>
	internal static bool TryMap(IGameObject source, IGameObject target, Dictionary<IUnityObjectBase, IUnityObjectBase> map)
	{
		map[source] = target;
		List<IComponent> sourceComponents = source.GetComponentAccessList().WhereNotNull().ToList();
		List<IComponent> targetComponents = target.GetComponentAccessList().WhereNotNull().ToList();
		if (sourceComponents.Count != targetComponents.Count)
		{
			return false;
		}
		foreach (IGrouping<string, IComponent> group in sourceComponents.GroupBy(c => c.ClassName))
		{
			List<IComponent> candidates = targetComponents.Where(c => c.ClassName == group.Key).ToList();
			List<IComponent> sources = group.ToList();
			if (candidates.Count != sources.Count)
			{
				return false;
			}
			for (int i = 0; i < sources.Count; i++)
			{
				map[sources[i]] = candidates[i];
			}
		}

		List<IGameObject> sourceChildren = source.GetChildren().ToList();
		List<IGameObject> targetChildren = target.GetChildren().ToList();
		if (sourceChildren.Count != targetChildren.Count)
		{
			return false;
		}
		HashSet<IGameObject> used = [];
		foreach (IGameObject child in sourceChildren)
		{
			string? signature = PrefabOutliningProcessor.TryComputeSignature(child, 0);
			IGameObject? match = targetChildren.FirstOrDefault(t => !used.Contains(t) && PrefabOutliningProcessor.TryComputeSignature(t, 0) == signature);
			if (match is null || !TryMap(child, match, map))
			{
				return false;
			}
			used.Add(match);
		}
		return true;
	}

	/// <summary>
	/// Compares the serialized content of an instance object with its prefab counterpart.
	/// References inside the hierarchy must point to mapped objects; references outside must be identical.
	/// For the instance root, its name, active state and transform values are ignored (they become modifications).
	/// </summary>
	internal static bool ContentEquals(IUnityObjectBase source, IUnityObjectBase target, IReadOnlyDictionary<IUnityObjectBase, IUnityObjectBase> map, bool isRoot)
	{
		HashSet<string> ignored = isRoot ? RootIgnoredFields : CommonIgnoredFields;
		string a = ContentWalker.Describe(source, map, ignored, resolveToTarget: true);
		string b = ContentWalker.Describe(target, map, ignored, resolveToTarget: false);
		return a == b;
	}

	private static readonly HashSet<string> CommonIgnoredFields =
	[
		"m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_PrefabInternal", "m_PrefabParentObject", "m_ObjectHideFlags",
		"m_Father", "m_StaticBatchInfo", "m_StaticBatchRoot",
	];

	private static readonly HashSet<string> RootIgnoredFields =
	[
		.. CommonIgnoredFields,
		"m_Name", "m_IsActive", "m_LocalPosition", "m_LocalRotation", "m_LocalScale", "m_RootOrder", "m_LocalEulerAnglesHint",
	];

	/// <summary>Builds a canonical string of an asset's fields; references are written as identities.</summary>
	private sealed class ContentWalker(IUnityObjectBase owner, IReadOnlyDictionary<IUnityObjectBase, IUnityObjectBase> map, HashSet<string> ignored, bool resolveToTarget) : AssetWalker
	{
		private readonly StringBuilder builder = new();
		private int ignoreDepth;

		public static string Describe(IUnityObjectBase asset, IReadOnlyDictionary<IUnityObjectBase, IUnityObjectBase> map, HashSet<string> ignored, bool resolveToTarget)
		{
			ContentWalker walker = new(asset, map, ignored, resolveToTarget);
			asset.WalkRelease(walker);
			return walker.builder.ToString();
		}

		public override bool EnterField(IUnityAssetBase asset, string name)
		{
			if (ignoreDepth == 0 && ReferenceEquals(asset, owner) && ignored.Contains(name))
			{
				return false;
			}
			builder.Append(name).Append('=');
			return true;
		}

		public override void VisitPrimitive<T>(T value)
		{
			builder.Append(value switch
			{
				float f => f.ToString("R", CultureInfo.InvariantCulture),
				double d => d.ToString("R", CultureInfo.InvariantCulture),
				byte[] bytes => Convert.ToHexString(bytes),
				_ => value.ToString(),
			}).Append(';');
		}

		public override void VisitPPtr<TAsset>(PPtr<TAsset> pptr)
		{
			IUnityObjectBase? target = owner.Collection.TryGetAsset(pptr);
			if (target is null)
			{
				builder.Append("null;");
				return;
			}
			if (resolveToTarget && map.TryGetValue(target, out IUnityObjectBase? mapped))
			{
				target = mapped;
			}
			builder.Append('#').Append(RuntimeHelpers.GetHashCode(target)).Append(';');
		}
	}
}
