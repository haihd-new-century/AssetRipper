using AssetRipper.Assets;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.SerializedShader;
using AssetRipper.SourceGenerated.Subclasses.SerializedPass;
using AssetRipper.SourceGenerated.Subclasses.SerializedProgram;
using AssetRipper.SourceGenerated.Subclasses.SerializedShaderRTBlendState;
using AssetRipper.SourceGenerated.Subclasses.SerializedShaderState;
using AssetRipper.SourceGenerated.Subclasses.SerializedSubShader;
using AssetRipper.SourceGenerated.Subclasses.SerializedTagMap;

namespace AssetRipper.Export.UnityProjects.Shaders;

/// <summary>
/// Shader decompilation for GLSL platforms (OpenGL / GLES): rebuilds a ShaderLab file with the original
/// properties, sub-shaders, passes and render state, and puts each pass's GLSL in a <c>GLSLPROGRAM</c> block.
/// </summary>
/// <remarks>
/// On GL platforms the compiled sub-program is the GLSL source itself (both stages, wrapped in <c>#ifdef VERTEX</c> /
/// <c>#ifdef FRAGMENT</c>), so this is a faithful reconstruction rather than a translation.
/// Only the variant without keywords (or the first one) is written per pass; other variants are listed in a comment.
/// Shaders without GLSL data (DirectX, Metal, Vulkan only builds) fall back to <see cref="DummyShaderTextExporter"/>.
/// </remarks>
public sealed class GlslShaderExporter : ShaderExporterBase
{
	public int DecompiledCount { get; private set; }
	public int FallbackCount { get; private set; }

	public override bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
	{
		IShader shader = (IShader)asset;
		string? text = null;
		try
		{
			text = TryDecompile(shader);
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Shader '{shader.GetBestName()}' could not be decompiled ({ex.GetType().Name}: {ex.Message}); exporting a dummy shader.");
		}
		if (text is null)
		{
			FallbackCount++;
			return DummyShaderTextExporter.ExportShader(shader, path, fileSystem);
		}
		DecompiledCount++;
		fileSystem.File.WriteAllText(path, text);
		return true;
	}

	/// <summary>Returns ShaderLab source, or null if the shader has no GLSL sub-programs.</summary>
	public static string? TryDecompile(IShader shader)
	{
		if (!shader.Has_ParsedForm())
		{
			return null;
		}
		ShaderBlobReader? blob = ShaderBlobReader.GlslPlatforms
			.Select(platform => ShaderBlobReader.TryOpen(shader, platform))
			.FirstOrDefault(reader => reader is not null);
		if (blob is null)
		{
			return null;
		}

		StringWriter writer = new() { NewLine = "\n" };
		int glslPasses = 0;
		writer.Write($"Shader \"{shader.ParsedForm.Name}\" {{\n");
		DummyShaderTextExporter.WriteProperties(shader.ParsedForm.PropInfo, writer);
		foreach (ISerializedSubShader subShader in shader.ParsedForm.SubShaders)
		{
			writer.Write("\tSubShader {\n");
			WriteTags(writer, subShader.Tags, 2);
			if (subShader.LOD != 0)
			{
				writer.Write($"\t\tLOD {subShader.LOD}\n");
			}
			foreach (ISerializedPass pass in subShader.Passes)
			{
				glslPasses += WritePass(writer, pass, blob) ? 1 : 0;
			}
			writer.Write("\t}\n");
		}
		if (!shader.ParsedForm.FallbackName.IsEmpty)
		{
			writer.Write($"\tFallback \"{shader.ParsedForm.FallbackName}\"\n");
		}
		if (!shader.ParsedForm.CustomEditorName.IsEmpty)
		{
			writer.Write($"\t//CustomEditor \"{shader.ParsedForm.CustomEditorName}\"\n");
		}
		writer.Write("}\n");
		return glslPasses > 0 ? writer.ToString() : null;
	}

	private static bool WritePass(TextWriter writer, ISerializedPass pass, ShaderBlobReader blob)
	{
		SerializedPassType type = pass.GetType_();
		if (type is SerializedPassType.UsePass)
		{
			writer.Write($"\t\tUsePass \"{pass.UseName}\"\n");
			return false;
		}
		if (type is SerializedPassType.GrabPass)
		{
			writer.Write(pass.TextureName.IsEmpty ? "\t\tGrabPass { }\n" : $"\t\tGrabPass {{ \"{pass.TextureName}\" }}\n");
			return false;
		}

		writer.Write("\t\tPass {\n");
		ISerializedShaderState state = pass.State;
		if (!state.Name_R.IsEmpty)
		{
			writer.Write($"\t\t\tName \"{state.Name_R}\"\n");
		}
		WriteTags(writer, state.Tags, 3);
		WriteState(writer, state);

		List<(int BlobIndex, int KeywordCount)> variants = GetVariants(pass.ProgVertex)
			.Concat(GetVariants(pass.ProgFragment))
			.Distinct()
			.OrderBy(v => v.KeywordCount)
			.ToList();
		string? glsl = null;
		foreach ((int blobIndex, _) in variants)
		{
			glsl = blob.TryGetGlsl(blobIndex);
			if (glsl is not null)
			{
				break;
			}
		}

		if (glsl is null)
		{
			writer.Write("\t\t\t// No GLSL program was found for this pass.\n\t\t}\n");
			return false;
		}
		if (variants.Count > 1)
		{
			writer.Write($"\t\t\t// {variants.Count} compiled variants exist; the one with the fewest keywords is shown.\n");
		}
		writer.Write("\t\t\tGLSLPROGRAM\n");
		writer.Write(glsl);
		writer.Write("\t\t\tENDGLSL\n");
		writer.Write("\t\t}\n");
		return true;
	}

	private static IEnumerable<(int BlobIndex, int KeywordCount)> GetVariants(ISerializedProgram program)
	{
		if (program.Has_PlayerSubPrograms())
		{
			foreach (var tier in program.PlayerSubPrograms)
			{
				foreach (var subProgram in tier)
				{
					yield return ((int)subProgram.BlobIndex, subProgram.KeywordIndices.Count);
				}
			}
		}
		foreach (var subProgram in program.SubPrograms)
		{
			int keywords = subProgram.Has_KeywordIndices() ? subProgram.KeywordIndices.Count
				: subProgram.Has_GlobalKeywordIndices() ? subProgram.GlobalKeywordIndices.Count : 0;
			yield return ((int)subProgram.BlobIndex, keywords);
		}
	}

	private static void WriteTags(TextWriter writer, ISerializedTagMap tags, int indent)
	{
		if (tags.Tags.Count == 0)
		{
			return;
		}
		writer.Write(new string('\t', indent));
		writer.Write("Tags {");
		foreach (var pair in tags.Tags)
		{
			writer.Write($" \"{pair.Key}\"=\"{pair.Value}\"");
		}
		writer.Write(" }\n");
	}

	private static void WriteState(TextWriter writer, ISerializedShaderState state)
	{
		const string indent = "\t\t\t";
		writer.Write($"{indent}Cull {Value(state.CullingName, state.CullingValue == CullMode.Unknown ? "Back" : state.CullingValue.ToString())}\n");
		writer.Write($"{indent}ZWrite {Value(state.ZWriteName, state.ZWriteValue.ToString())}\n");
		ZTest zTest = state.ZTestValue;
		if (IsNamed(state.ZTestName) || zTest is not (ZTest.LEqual or ZTest.Disabled))
		{
			writer.Write($"{indent}ZTest {Value(state.ZTestName, zTest == ZTest.Disabled ? "Always" : zTest.ToString())}\n");
		}
		ISerializedShaderRTBlendState blend = state.RtBlend0;
		bool defaultBlend = !IsNamed(blend.SrcBlendName) && !IsNamed(blend.DestBlendName)
			&& blend.SrcBlendValue == BlendMode.One && blend.DestBlendValue == BlendMode.Zero;
		if (!defaultBlend)
		{
			writer.Write($"{indent}Blend {Value(blend.SrcBlendName, blend.SrcBlendValue.ToString())} {Value(blend.DestBlendName, blend.DestBlendValue.ToString())}");
			bool separateAlpha = IsNamed(blend.SrcBlendAlphaName) || IsNamed(blend.DestBlendAlphaName)
				|| blend.SrcBlendAlphaValue != blend.SrcBlendValue || blend.DestBlendAlphaValue != blend.DestBlendValue;
			if (separateAlpha)
			{
				writer.Write($", {Value(blend.SrcBlendAlphaName, blend.SrcBlendAlphaValue.ToString())} {Value(blend.DestBlendAlphaName, blend.DestBlendAlphaValue.ToString())}");
			}
			writer.Write('\n');
		}
		ColorWriteMask mask = blend.ColMaskValue;
		if (IsNamed(blend.ColMaskName) || mask != ColorWriteMask.All)
		{
			writer.Write($"{indent}ColorMask {Value(blend.ColMaskName, FormatColorMask(mask))}\n");
		}
		if (state.AlphaToMaskValue)
		{
			writer.Write($"{indent}AlphaToMask On\n");
		}
	}

	/// <summary>Material property references are written as <c>[_Property]</c>, constants as their keyword.</summary>
	private static string Value(Utf8String propertyName, string constant) => IsNamed(propertyName) ? $"[{propertyName}]" : constant;

	/// <summary>Unity stores "&lt;noninit&gt;" when a state value is a constant rather than a material property.</summary>
	internal static bool IsNamed(Utf8String name) => !name.IsEmpty && name.String != "<noninit>";

	internal static string FormatColorMask(ColorWriteMask mask)
	{
		if (mask == ColorWriteMask.None)
		{
			return "0";
		}
		string result = "";
		if ((mask & ColorWriteMask.Red) != 0) { result += "R"; }
		if ((mask & ColorWriteMask.Green) != 0) { result += "G"; }
		if ((mask & ColorWriteMask.Blue) != 0) { result += "B"; }
		if ((mask & ColorWriteMask.Alpha) != 0) { result += "A"; }
		return result;
	}
}
