using AssetRipper.SourceGenerated.Classes.ClassID_48;
using K4os.Compression.LZ4;
using System.Buffers.Binary;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Shaders;

/// <summary>
/// Reads compiled sub-programs from <see cref="IShader.CompressedBlob"/>.
/// </summary>
/// <remarks>
/// For every compiler platform the blob holds one or more LZ4-compressed segments. A decompressed segment starts with
/// <c>int count</c> followed by <c>count</c> entries of <c>(int offset, int length[, int segment])</c>; each entry is one
/// sub-program (header + bytecode). Sub-programs are addressed by <c>BlobIndex</c> from the serialized passes.
/// </remarks>
public sealed class ShaderBlobReader
{
	/// <summary>Compiler platforms whose bytecode is GLSL source text.</summary>
	public static readonly uint[] GlslPlatforms = [0 /*OpenGL*/, 5 /*GLES20*/, 9 /*GLES3x*/, 15 /*OpenGLCore*/];

	private readonly List<byte[]> segments = [];
	private readonly List<(int Segment, int Offset, int Length)> entries = [];

	public uint Platform { get; }
	public int Count => entries.Count;

	private ShaderBlobReader(uint platform) => Platform = platform;

	public static IEnumerable<uint> GetPlatforms(IShader shader) => shader.Platforms;

	/// <summary>Opens the blob of <paramref name="platform"/>, or returns null if the shader has no data for it.</summary>
	public static ShaderBlobReader? TryOpen(IShader shader, uint platform)
	{
		int index = shader.Platforms.IndexOf(platform);
		if (index < 0 || shader.CompressedBlob.Length == 0)
		{
			return null;
		}

		ShaderBlobReader reader = new(platform);
		if (shader.Has_Offsets_AssetList_AssetList_UInt32())
		{
			var offsets = shader.Offsets_AssetList_AssetList_UInt32[index];
			var compressed = shader.CompressedLengths_AssetList_AssetList_UInt32[index];
			var decompressed = shader.DecompressedLengths_AssetList_AssetList_UInt32[index];
			for (int s = 0; s < offsets.Count; s++)
			{
				reader.segments.Add(Decompress(shader.CompressedBlob, offsets[s], compressed[s], decompressed[s]));
			}
		}
		else if (shader.Has_Offsets_AssetList_UInt32())
		{
			reader.segments.Add(Decompress(shader.CompressedBlob,
				shader.Offsets_AssetList_UInt32[index],
				shader.CompressedLengths_AssetList_UInt32[index],
				shader.DecompressedLengths_AssetList_UInt32[index]));
		}
		else
		{
			return null;
		}
		reader.ReadDirectory();
		return reader;
	}

	internal static ShaderBlobReader FromSegments(uint platform, params byte[][] decompressedSegments)
	{
		ShaderBlobReader reader = new(platform);
		reader.segments.AddRange(decompressedSegments);
		reader.ReadDirectory();
		return reader;
	}

	private static byte[] Decompress(byte[] blob, uint offset, uint compressedLength, uint decompressedLength)
	{
		byte[] result = new byte[decompressedLength];
		int written = LZ4Codec.Decode(blob, (int)offset, (int)compressedLength, result, 0, result.Length);
		if (written != result.Length)
		{
			throw new InvalidDataException($"Shader blob segment decompressed to {written} bytes, expected {decompressedLength}.");
		}
		return result;
	}

	private void ReadDirectory()
	{
		if (segments.Count == 0 || segments[0].Length < 4)
		{
			return;
		}
		byte[] directory = segments[0];
		int count = BinaryPrimitives.ReadInt32LittleEndian(directory);
		if (count <= 0 || count > 1_000_000)
		{
			return;
		}
		// Newer versions add a segment index to each entry (12 bytes), older ones only have offset + length (8 bytes).
		int firstOffset = count * 8 + 4 <= directory.Length ? BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(4)) : 0;
		int stride = firstOffset >= 4 + count * 12 ? 12 : 8;
		for (int i = 0; i < count; i++)
		{
			int position = 4 + i * stride;
			if (position + stride > directory.Length)
			{
				break;
			}
			int offset = BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(position));
			int length = BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(position + 4));
			int segment = stride == 12 ? BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(position + 8)) : 0;
			entries.Add((segment, offset, length));
		}
	}

	/// <summary>The raw bytes of sub-program <paramref name="blobIndex"/>.</summary>
	public ReadOnlySpan<byte> GetEntry(int blobIndex)
	{
		if ((uint)blobIndex >= (uint)entries.Count)
		{
			return default;
		}
		(int segment, int offset, int length) = entries[blobIndex];
		if ((uint)segment >= (uint)segments.Count || offset < 0 || length < 0 || offset + length > segments[segment].Length)
		{
			return default;
		}
		return segments[segment].AsSpan(offset, length);
	}

	/// <summary>
	/// Extracts the GLSL source of a GLSL sub-program: everything from the first <c>#ifdef VERTEX</c> / <c>#version</c>
	/// up to the last <c>#endif</c>, which is how Unity stores both stages of a GLES/GL program.
	/// </summary>
	public string? TryGetGlsl(int blobIndex)
	{
		ReadOnlySpan<byte> entry = GetEntry(blobIndex);
		if (entry.IsEmpty)
		{
			return null;
		}
		string text = Encoding.Latin1.GetString(entry);
		int start = IndexOfAny(text, "#ifdef VERTEX", "#version");
		if (start < 0)
		{
			return null;
		}
		int end = text.LastIndexOf("#endif", StringComparison.Ordinal);
		int stop = end > start ? end + "#endif".Length : text.IndexOf('\0', start);
		if (stop <= start)
		{
			stop = text.Length;
		}
		return text[start..stop].Replace("\r", "").TrimEnd('\0', '\n', ' ') + "\n";
	}

	private static int IndexOfAny(string text, params string[] candidates)
	{
		int best = -1;
		foreach (string candidate in candidates)
		{
			int index = text.IndexOf(candidate, StringComparison.Ordinal);
			if (index >= 0 && (best < 0 || index < best))
			{
				best = index;
			}
		}
		return best;
	}
}
