using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.SerializedShader;
using System.Text;

namespace AssetRipper.Tests;

public class ShaderDecompilationTests
{
	private static byte[] BuildSegment(bool withSegmentIndex, params byte[][] programs)
	{
		int stride = withSegmentIndex ? 12 : 8;
		int header = 4 + programs.Length * stride;
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream);
		writer.Write(programs.Length);
		int offset = header;
		foreach (byte[] program in programs)
		{
			writer.Write(offset);
			writer.Write(program.Length);
			if (withSegmentIndex)
			{
				writer.Write(0);
			}
			offset += program.Length;
		}
		foreach (byte[] program in programs)
		{
			writer.Write(program);
		}
		return stream.ToArray();
	}

	private static byte[] Program(string glsl)
	{
		// Unity sub-program header (version, type, stats...) followed by the length-prefixed source.
		byte[] text = Encoding.ASCII.GetBytes(glsl);
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream);
		writer.Write(202012090);
		writer.Write(4);
		writer.Write(new byte[16]);
		writer.Write(text.Length);
		writer.Write(text);
		writer.Write(new byte[] { 0, 0, 1, 0 });
		return stream.ToArray();
	}

	private const string Glsl = "#ifdef VERTEX\n#version 300 es\nvoid main() { gl_Position = vec4(0); }\n#endif\n#ifdef FRAGMENT\n#version 300 es\nout vec4 c; void main() { c = vec4(1); }\n#endif\n";

	[TestCase(true)]
	[TestCase(false)]
	public void GlslIsExtractedFromSubProgram(bool withSegmentIndex)
	{
		ShaderBlobReader reader = ShaderBlobReader.FromSegments(9, BuildSegment(withSegmentIndex, Program("unused"), Program(Glsl)));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(reader.Count, Is.EqualTo(2));
			Assert.That(reader.TryGetGlsl(0), Is.Null, "no GLSL markers");
			string? glsl = reader.TryGetGlsl(1);
			Assert.That(glsl, Does.StartWith("#ifdef VERTEX"));
			Assert.That(glsl, Does.Contain("#ifdef FRAGMENT"));
			Assert.That(glsl!.TrimEnd(), Does.EndWith("#endif"), "trailing binary data is cut off");
			Assert.That(reader.TryGetGlsl(5), Is.Null, "out of range");
		}
	}

	[TestCase(ColorWriteMask.All, "RGBA")]
	[TestCase(ColorWriteMask.None, "0")]
	[TestCase(ColorWriteMask.Red | ColorWriteMask.Alpha, "RA")]
	public void ColorMaskIsFormatted(ColorWriteMask mask, string expected)
	{
		Assert.That(GlslShaderExporter.FormatColorMask(mask), Is.EqualTo(expected));
	}

	[TestCase("", false)]
	[TestCase("<noninit>", false)]
	[TestCase("_SrcBlend", true)]
	public void PropertyReferencesAreRecognized(string name, bool named)
	{
		Assert.That(GlslShaderExporter.IsNamed(new AssetRipper.Primitives.Utf8String(name)), Is.EqualTo(named));
	}
}
