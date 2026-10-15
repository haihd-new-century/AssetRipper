using AssetRipper.Assets.Collections;
using AssetRipper.Primitives;
using AssetRipper.Processing.Prefabs;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Tests;

public class PrefabOutliningTests
{
	private static IGameObject CreateObject(ProcessedAssetCollection collection, string name, ITransform? parent = null)
	{
		IGameObject gameObject = collection.CreateGameObject();
		gameObject.Name = name;
		ITransform transform = collection.CreateTransform();
		transform.GameObject_C4P = gameObject;
		gameObject.AddComponent(ClassIDType.Transform, transform);
		transform.LocalPosition_C4.X = 5;
		if (parent is not null)
		{
			transform.Father_C4P = parent;
			parent.Children_C4P.Add(transform);
		}
		return gameObject;
	}

	[TestCase("Tree (3)", "Tree")]
	[TestCase("Tree(Clone)", "Tree")]
	[TestCase("Tree_12", "Tree")]
	[TestCase("Rock 2", "Rock")]
	[TestCase("Wall", "Wall")]
	[TestCase("(1)", "(1)")]
	public void NamesAreCleaned(string input, string expected)
	{
		Assert.That(PrefabOutliningProcessor.CleanName(input), Is.EqualTo(expected));
	}

	[Test]
	public void SignaturesIgnoreInstanceSuffixesButNotStructure()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		IGameObject a = CreateObject(collection, "Lamp (1)");
		CreateObject(collection, "Bulb", a.GetTransform());
		IGameObject b = CreateObject(collection, "Lamp (2)");
		CreateObject(collection, "Bulb", b.GetTransform());
		IGameObject c = CreateObject(collection, "Lamp (3)"); // no child

		string? sa = PrefabOutliningProcessor.TryComputeSignature(a, 0);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(sa, Is.Not.Null);
			Assert.That(PrefabOutliningProcessor.TryComputeSignature(b, 0), Is.EqualTo(sa));
			Assert.That(PrefabOutliningProcessor.TryComputeSignature(c, 0), Is.Not.EqualTo(sa));
		}
	}

	[Test]
	public void CloneHierarchyRemapsInternalReferences()
	{
		ProcessedAssetCollection scene = AssetCreator.CreateCollection(UnityVersion.V_2022);
		IGameObject world = CreateObject(scene, "World");
		IGameObject root = CreateObject(scene, "Lamp", world.GetTransform());
		IGameObject child = CreateObject(scene, "Bulb", root.GetTransform());
		ProcessedAssetCollection output = ((AssetRipper.Assets.Bundles.GameBundle)scene.Bundle).AddNewProcessedCollection("out", UnityVersion.V_2022);

		IGameObject clone = PrefabOutliningProcessor.CloneHierarchy(root, output);

		ITransform cloneTransform = clone.GetTransform();
		ITransform cloneChild = cloneTransform.Children_C4P.Single()!;
		using (Assert.EnterMultipleScope())
		{
			Assert.That(clone, Is.Not.SameAs(root));
			Assert.That(clone.Collection, Is.SameAs(output));
			Assert.That(clone.Name.String, Is.EqualTo("Lamp"));
			Assert.That(cloneTransform.Father_C4P, Is.Null, "prefab root has no parent");
			Assert.That(cloneTransform.LocalPosition_C4.X, Is.Zero, "prefab root transform is reset");
			Assert.That(cloneChild, Is.Not.SameAs(child.GetTransform()), "children are cloned");
			Assert.That(cloneChild.Father_C4P, Is.SameAs(cloneTransform), "child points at the cloned root");
			Assert.That(cloneChild.GameObject_C4P!.Name.String, Is.EqualTo("Bulb"));
			Assert.That(cloneChild.LocalPosition_C4.X, Is.EqualTo(5), "child values are copied");
			Assert.That(root.GetTransform().Father_C4P, Is.SameAs(world.GetTransform()), "scene objects are untouched");
		}
	}
}
