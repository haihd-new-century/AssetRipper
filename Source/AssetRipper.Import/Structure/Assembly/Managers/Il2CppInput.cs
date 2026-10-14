using AssetRipper.Import.Logging;
using System.Buffers.Binary;

namespace AssetRipper.Import.Structure.Assembly.Managers;

/// <summary>
/// The raw inputs given to Cpp2IL for an IL2CPP game.
/// </summary>
/// <remarks>
/// Instances are passed through every registered <see cref="IIl2CppInputPreprocessor"/> before analysis,
/// so a preprocessor can replace the binary or metadata bytes (for example, to undo a custom container format)
/// or correct the Unity version.
/// </remarks>
public sealed class Il2CppInput
{
	public Il2CppInput(string binaryPath, byte[] binary, string metadataPath, byte[] metadata, UnityVersion unityVersion)
	{
		BinaryPath = binaryPath;
		Binary = binary;
		MetadataPath = metadataPath;
		Metadata = metadata;
		UnityVersion = unityVersion;
	}

	/// <summary>The path of the native binary (GameAssembly.dll, libil2cpp.so, ...). Informational only.</summary>
	public string BinaryPath { get; }
	/// <summary>The contents of the native binary.</summary>
	public byte[] Binary { get; set; }
	/// <summary>The path of global-metadata.dat. Informational only.</summary>
	public string MetadataPath { get; }
	/// <summary>The contents of global-metadata.dat.</summary>
	public byte[] Metadata { get; set; }
	/// <summary>The Unity version used to interpret the inputs.</summary>
	public UnityVersion UnityVersion { get; set; }

	/// <summary>
	/// The magic number at the start of an unmodified global-metadata.dat file.
	/// </summary>
	public const uint MetadataMagic = 0xFAB11BAF;

	/// <summary>
	/// Whether <see cref="Metadata"/> starts with the standard IL2CPP metadata magic number.
	/// </summary>
	public bool HasStandardMetadataHeader => Metadata.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(Metadata) == MetadataMagic;

	/// <summary>
	/// The metadata format version stored after the magic number, or -1 if the header is not standard.
	/// </summary>
	public int MetadataVersion => HasStandardMetadataHeader ? BinaryPrimitives.ReadInt32LittleEndian(Metadata.AsSpan(4)) : -1;
}

/// <summary>
/// An extension point that runs before Cpp2IL parses an IL2CPP game.
/// </summary>
/// <remarks>
/// Register implementations in <see cref="IL2CppManager.InputPreprocessors"/>.
/// Preprocessors run in registration order; each one sees the output of the previous one.
/// </remarks>
public interface IIl2CppInputPreprocessor
{
	/// <summary>A short name used in log messages.</summary>
	string Name { get; }

	/// <summary>
	/// Inspects and optionally modifies <paramref name="input"/>.
	/// </summary>
	/// <returns><see langword="true"/> if the input was modified.</returns>
	bool Process(Il2CppInput input);
}

/// <summary>
/// A built-in preprocessor that never modifies the input but explains why Cpp2IL is likely to fail
/// when global-metadata.dat does not have the standard header.
/// </summary>
public sealed class MetadataHeaderDiagnostic : IIl2CppInputPreprocessor
{
	public string Name => "Metadata header check";

	public bool Process(Il2CppInput input)
	{
		if (input.HasStandardMetadataHeader)
		{
			Logger.Info(LogCategory.Import, $"IL2CPP metadata header OK (format version {input.MetadataVersion}).");
		}
		else
		{
			string start = Convert.ToHexString(input.Metadata.AsSpan(0, Math.Min(8, input.Metadata.Length)));
			Logger.Warning(LogCategory.Import,
				$"'{Path.GetFileName(input.MetadataPath)}' does not start with the IL2CPP magic number {Il2CppInput.MetadataMagic:X8} (found {start}). " +
				"The metadata is probably encrypted or uses a custom format, so script analysis will likely fail. " +
				$"A custom {nameof(IIl2CppInputPreprocessor)} can be registered to restore the standard format.");
		}
		return false;
	}
}
