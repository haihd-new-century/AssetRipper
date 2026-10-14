using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.Numerics;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_23;
using AssetRipper.SourceGenerated.Classes.ClassID_25;
using AssetRipper.SourceGenerated.Classes.ClassID_33;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using System.Numerics;
using System.Text.RegularExpressions;

namespace AssetRipper.Processing;

/// <summary>
/// Reverses Unity's static batching by extracting each renderer's part of a combined mesh into its own mesh.
/// </summary>
/// <remarks>
/// When a scene is built, renderers marked as batching static share one "Combined Mesh (root: scene)" whose vertices
/// are in world space; each renderer only remembers which submeshes belong to it (<c>StaticBatchInfo</c>).
/// For every such renderer this processor:
/// <list type="number">
/// <item>collects the triangles of its submeshes and the vertices they use,</item>
/// <item>transforms positions, normals and tangents back into the renderer's local space,</item>
/// <item>reuses an identical previously separated mesh, or an original mesh with the same name and geometry if one exists in the game files,
/// otherwise creates a new mesh named after the GameObject,</item>
/// <item>points the MeshFilter at that mesh and clears the static batch information.</item>
/// </list>
/// The GameObject stays marked as static, so Unity re-batches it when the exported project is built.
/// </remarks>
public sealed partial class StaticMeshSeparationProcessor : IAssetProcessor
{
	public int SeparatedRendererCount { get; private set; }
	public int CreatedMeshCount { get; private set; }

	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Static mesh separation");
		ProcessedAssetCollection? output = null;
		Dictionary<IMesh, MeshData?> combinedData = new();
		Dictionary<string, IMesh> separatedByHash = new();
		ILookup<string, IMesh> originalMeshesByName = gameData.GameBundle.FetchAssets()
			.OfType<IMesh>()
			.Where(m => !IsCombinedMesh(m))
			.ToLookup(m => m.Name.String, StringComparer.Ordinal);

		foreach (IMeshRenderer renderer in gameData.GameBundle.FetchAssets().OfType<IMeshRenderer>().ToList())
		{
			if (!TryGetSubMeshRange(renderer, out int firstSubMesh, out int subMeshCount))
			{
				continue;
			}
			IGameObject? gameObject = renderer.GameObject_C25P;
			IMeshFilter? meshFilter = gameObject?.TryGetComponent<IMeshFilter>();
			ITransform? transform = gameObject?.TryGetComponent<ITransform>();
			if (gameObject is null || meshFilter?.MeshP is not { } combined || transform is null || !IsCombinedMesh(combined))
			{
				continue;
			}

			if (!combinedData.TryGetValue(combined, out MeshData? data))
			{
				data = MeshData.TryMakeFromMesh(combined, out MeshData made) ? made : null;
				combinedData.Add(combined, data);
			}
			if (data is null || firstSubMesh + subMeshCount > data.Value.SubMeshes.Length)
			{
				continue;
			}

			MeshData? extracted = Extract(data.Value, firstSubMesh, subMeshCount, GetLocalToWorld(transform));
			if (extracted is null)
			{
				continue;
			}

			string name = CleanName(gameObject.Name.String);
			string hash = ComputeGeometryHash(extracted.Value);
			if (!separatedByHash.TryGetValue(hash, out IMesh? mesh))
			{
				mesh = FindMatchingOriginal(originalMeshesByName[name], extracted.Value);
				if (mesh is null)
				{
					output ??= gameData.AddNewProcessedCollection("Separated Static Meshes");
					mesh = output.CreateMesh();
					mesh.Name = name;
					mesh.FillWithCompressedMeshData(extracted.Value);
					CreatedMeshCount++;
				}
				separatedByHash.Add(hash, mesh);
			}

			meshFilter.MeshP = mesh;
			ClearStaticBatchInfo(renderer);
			SeparatedRendererCount++;
		}

		Logger.Info(LogCategory.Processing, $"Static mesh separation: {SeparatedRendererCount} renderer(s), {CreatedMeshCount} new mesh(es)");
	}

	public static bool IsCombinedMesh(IMesh mesh) => mesh.Name.String.StartsWith("Combined Mesh", StringComparison.Ordinal);

	private static bool TryGetSubMeshRange(IRenderer renderer, out int first, out int count)
	{
		if (renderer.Has_StaticBatchInfo_C25() && !renderer.StaticBatchInfo_C25.IsDefault())
		{
			first = renderer.StaticBatchInfo_C25.FirstSubMesh;
			count = renderer.StaticBatchInfo_C25.SubMeshCount;
			return count > 0;
		}
		if (renderer.Has_SubsetIndices_C25() && renderer.SubsetIndices_C25.Count > 0)
		{
			// Older versions store the list of submesh indices; they are consecutive in practice.
			first = (int)renderer.SubsetIndices_C25[0];
			count = renderer.SubsetIndices_C25.Count;
			return true;
		}
		first = count = 0;
		return false;
	}

	private static void ClearStaticBatchInfo(IRenderer renderer)
	{
		if (renderer.Has_StaticBatchInfo_C25())
		{
			renderer.StaticBatchInfo_C25.FirstSubMesh = 0;
			renderer.StaticBatchInfo_C25.SubMeshCount = 0;
		}
		if (renderer.Has_SubsetIndices_C25())
		{
			renderer.SubsetIndices_C25.Clear();
		}
	}

	/// <summary>
	/// The local-to-world matrix (row-vector convention, as used by <see cref="System.Numerics"/>).
	/// </summary>
	internal static Matrix4x4 GetLocalToWorld(ITransform transform)
	{
		Matrix4x4 result = transform.ToTransformation();
		int guard = 0;
		for (ITransform? parent = transform.Father_C4P; parent is not null && guard < 256; parent = parent.Father_C4P, guard++)
		{
			result *= (Matrix4x4)parent.ToTransformation();
		}
		return result;
	}

	/// <summary>
	/// Extracts submeshes <paramref name="first"/>..<paramref name="first"/>+<paramref name="count"/> and converts them to local space.
	/// </summary>
	internal static MeshData? Extract(MeshData combined, int first, int count, Matrix4x4 localToWorld)
	{
		if (!Matrix4x4.Invert(localToWorld, out Matrix4x4 worldToLocal))
		{
			return null;
		}
		// Normals transform with the inverse transpose of the position matrix: worldToLocal for positions means transpose(localToWorld) for normals.
		Matrix4x4 normalMatrix = Matrix4x4.Transpose(localToWorld);
		bool mirrored = localToWorld.GetDeterminant() < 0;

		Dictionary<uint, uint> remap = new();
		List<uint> oldVertexOrder = [];
		List<uint> indices = [];
		List<SubMeshData> subMeshes = [];

		for (int s = first; s < first + count; s++)
		{
			SubMeshData source = combined.SubMeshes[s];
			int startIndex = indices.Count;
			for (int i = 0; i < source.IndexCount; i++)
			{
				int bufferIndex = source.FirstIndex + i;
				if ((uint)bufferIndex >= (uint)combined.ProcessedIndexBuffer.Length)
				{
					return null;
				}
				uint oldVertex = combined.ProcessedIndexBuffer[bufferIndex] + source.BaseVertex;
				if (oldVertex >= combined.Vertices.Length)
				{
					return null;
				}
				if (!remap.TryGetValue(oldVertex, out uint newVertex))
				{
					newVertex = (uint)oldVertexOrder.Count;
					remap.Add(oldVertex, newVertex);
					oldVertexOrder.Add(oldVertex);
				}
				indices.Add(newVertex);
			}
			subMeshes.Add(new SubMeshData(0, startIndex, 0, source.IndexCount, source.TriangleCount, 0, source.Topology, default));
		}
		if (oldVertexOrder.Count == 0)
		{
			return null;
		}

		T[]? Pick<T>(T[]? array) => array is not null && array.Length == combined.Vertices.Length
			? oldVertexOrder.Select(i => array[i]).ToArray()
			: null;

		Vector3[] vertices = oldVertexOrder.Select(i => Vector3.Transform(combined.Vertices[i], worldToLocal)).ToArray();
		Vector3[]? normals = Pick(combined.Normals)?.Select(n => SafeNormalize(Vector3.TransformNormal(n, normalMatrix))).ToArray();
		Vector4[]? tangents = Pick(combined.Tangents)?.Select(t =>
		{
			Vector3 direction = SafeNormalize(Vector3.TransformNormal(new Vector3(t.X, t.Y, t.Z), worldToLocal));
			return new Vector4(direction, mirrored ? -t.W : t.W);
		}).ToArray();

		// Recompute per-submesh vertex ranges and bounds in local space.
		for (int s = 0; s < subMeshes.Count; s++)
		{
			SubMeshData sub = subMeshes[s];
			IEnumerable<uint> used = indices.Skip(sub.FirstIndex).Take(sub.IndexCount);
			uint min = used.Min(), max = used.Max();
			Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
			foreach (uint v in used)
			{
				lo = Vector3.Min(lo, vertices[v]);
				hi = Vector3.Max(hi, vertices[v]);
			}
			subMeshes[s] = sub with
			{
				FirstVertex = (int)min,
				VertexCount = (int)(max - min + 1),
				LocalBounds = new Bounds((lo + hi) / 2, (hi - lo) / 2),
			};
		}

		return new MeshData(
			vertices, normals, tangents,
			Pick(combined.Colors),
			Pick(combined.UV0), Pick(combined.UV1), Pick(combined.UV2), Pick(combined.UV3),
			Pick(combined.UV4), Pick(combined.UV5), Pick(combined.UV6), Pick(combined.UV7),
			null, null,
			indices.ToArray(),
			subMeshes.ToArray());
	}

	private static Vector3 SafeNormalize(Vector3 v)
	{
		float length = v.Length();
		return length > 1e-12f ? v / length : v;
	}

	/// <summary>
	/// A hash of the local-space geometry, rounded so that float noise from the world-space round trip does not matter.
	/// </summary>
	internal static string ComputeGeometryHash(MeshData data)
	{
		HashCode hash = new();
		hash.Add(data.Vertices.Length);
		hash.Add(data.ProcessedIndexBuffer.Length);
		foreach (Vector3 v in data.Vertices)
		{
			hash.Add(MathF.Round(v.X, 3));
			hash.Add(MathF.Round(v.Y, 3));
			hash.Add(MathF.Round(v.Z, 3));
		}
		foreach (uint i in data.ProcessedIndexBuffer)
		{
			hash.Add(i);
		}
		return $"{data.Vertices.Length}:{data.ProcessedIndexBuffer.Length}:{hash.ToHashCode():X8}";
	}

	private static IMesh? FindMatchingOriginal(IEnumerable<IMesh> candidates, MeshData extracted)
	{
		foreach (IMesh candidate in candidates)
		{
			if (candidate.VertexData.VertexCount == extracted.Vertices.Length
				&& MeshData.TryMakeFromMesh(candidate, out MeshData original)
				&& original.ProcessedIndexBuffer.Length == extracted.ProcessedIndexBuffer.Length)
			{
				return candidate;
			}
		}
		return null;
	}

	/// <summary>Removes suffixes Unity adds to duplicated objects, e.g. "Rock (3)" or "Rock(Clone)".</summary>
	internal static string CleanName(string name)
	{
		string cleaned = CloneSuffix().Replace(name, "");
		cleaned = InstanceSuffix().Replace(cleaned, "").Trim();
		return string.IsNullOrEmpty(cleaned) ? "Separated Mesh" : cleaned;
	}

	[GeneratedRegex(@"\(Clone\)", RegexOptions.CultureInvariant)]
	private static partial Regex CloneSuffix();

	[GeneratedRegex(@"\s*\(\d+\)$", RegexOptions.CultureInvariant)]
	private static partial Regex InstanceSuffix();
}
