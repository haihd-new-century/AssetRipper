using AssetRipper.Numerics;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using System.Numerics;

namespace AssetRipper.Tests;

public class StaticMeshSeparationTests
{
	private static MeshData CombinedMesh(Matrix4x4 localToWorld)
	{
		// Two submeshes: a triangle owned by our renderer, followed by another renderer's triangle.
		Vector3[] local = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
		Vector3[] world = local.Select(v => Vector3.Transform(v, localToWorld))
			.Concat([new Vector3(100, 0, 0), new Vector3(101, 0, 0), new Vector3(100, 1, 0)])
			.ToArray();
		Vector3[] normals = Enumerable.Repeat(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, localToWorld)), 6).ToArray();
		SubMeshData[] subMeshes =
		[
			new(0, 0, 0, 3, 1, 3, MeshTopology.Triangles, default),
			new(0, 3, 3, 3, 1, 3, MeshTopology.Triangles, default),
		];
		return MeshData.Empty with { Vertices = world, Normals = normals, ProcessedIndexBuffer = [0, 1, 2, 3, 4, 5], SubMeshes = subMeshes };
	}

	[Test]
	public void ExtractedSubmeshIsBackInLocalSpace()
	{
		Matrix4x4 localToWorld = Matrix4x4.CreateScale(2) * Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(10, 3, -4);

		MeshData? extracted = StaticMeshSeparationProcessor.Extract(CombinedMesh(localToWorld), 0, 1, localToWorld);

		Assert.That(extracted, Is.Not.Null);
		MeshData mesh = extracted!.Value;
		using (Assert.EnterMultipleScope())
		{
			Assert.That(mesh.Vertices, Has.Length.EqualTo(3), "only the vertices of our submesh are kept");
			Assert.That(Vector3.Distance(mesh.Vertices[1], new Vector3(1, 0, 0)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(mesh.Vertices[2], new Vector3(0, 1, 0)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(mesh.Normals![0], Vector3.UnitZ), Is.LessThan(1e-4f), "normals are rotated back and normalized");
			Assert.That(mesh.ProcessedIndexBuffer, Is.EqualTo(new uint[] { 0, 1, 2 }));
			Assert.That(mesh.SubMeshes, Has.Length.EqualTo(1));
		}
	}

	[Test]
	public void IdenticalGeometryProducesTheSameHash()
	{
		Matrix4x4 a = Matrix4x4.CreateTranslation(5, 0, 0);
		Matrix4x4 b = Matrix4x4.CreateRotationZ(1.2f) * Matrix4x4.CreateTranslation(-7, 2, 9);
		string hashA = StaticMeshSeparationProcessor.ComputeGeometryHash(StaticMeshSeparationProcessor.Extract(CombinedMesh(a), 0, 1, a)!.Value);
		string hashB = StaticMeshSeparationProcessor.ComputeGeometryHash(StaticMeshSeparationProcessor.Extract(CombinedMesh(b), 0, 1, b)!.Value);
		Assert.That(hashA, Is.EqualTo(hashB));
	}

	[TestCase("Rock (3)", "Rock")]
	[TestCase("Tree(Clone)", "Tree")]
	[TestCase("Wall", "Wall")]
	[TestCase(" (1)", "Separated Mesh")]
	public void NamesAreCleaned(string input, string expected)
	{
		Assert.That(StaticMeshSeparationProcessor.CleanName(input), Is.EqualTo(expected));
	}
}
