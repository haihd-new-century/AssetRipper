using AsmResolver.DotNet;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Platforms;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using LibCpp2IL;
using Cpp2IlApi = Cpp2IL.Core.Cpp2IlApi;

namespace AssetRipper.Import.Structure.Assembly.Managers;

public sealed class IL2CppManager : BaseManager
{
	static IL2CppManager()
	{
		InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
		InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
		InstructionSetRegistry.RegisterInstructionSet<WasmInstructionSet>(DefaultInstructionSets.WASM);
		InstructionSetRegistry.RegisterInstructionSet<ArmV7InstructionSet>(DefaultInstructionSets.ARM_V7);
		bool useNewArm64 = false;
		if (useNewArm64)
		{
			InstructionSetRegistry.RegisterInstructionSet<NewArmV8InstructionSet>(DefaultInstructionSets.ARM_V8);
		}
		else
		{
			InstructionSetRegistry.RegisterInstructionSet<Arm64InstructionSet>(DefaultInstructionSets.ARM_V8);
		}

		LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
	}

	public static List<Cpp2IlProcessingLayer> DefaultProcessingLayers { get; } =
	[
		new AttributeAnalysisProcessingLayer(),
		new MethodOverrideNameFixer(),
	];

	public static AsmResolverDllOutputFormatDefault DefaultOutputFormat { get; } = new();

	public static List<Cpp2IlProcessingLayer>? RecoveryProcessingLayers { get; set; }

	public static AsmResolverDllOutputFormat? RecoveryOutputFormat { get; set; }

	public static event Action? ClearStaticState;

	public string? GameAssemblyPath { get; private set; }
	public string? UnityPlayerPath { get; private set; }
	public string? GameDataPath { get; private set; }
	public string? MetaDataPath { get; private set; }
	public UnityVersion UnityVersion { get; private set; }
	/// <summary>
	/// For when analysis is reimplimented in Cpp2IL.
	/// </summary>
	private readonly ScriptContentLevel contentLevel;

	public IL2CppManager(Action<string> requestAssemblyCallback, ScriptContentLevel level) : base(requestAssemblyCallback)
	{
		contentLevel = level;
	}

	public override ScriptingBackend ScriptingBackend => ScriptingBackend.IL2Cpp;

	/// <summary>
	/// Preprocessors that run on the raw binary and metadata before Cpp2IL parses them.
	/// </summary>
	/// <remarks>
	/// <see cref="MetadataHeaderDiagnostic"/> is registered by default. Additional preprocessors can be added by hosts or plugins.
	/// </remarks>
	public static List<IIl2CppInputPreprocessor> InputPreprocessors { get; } = [new MetadataHeaderDiagnostic()];

	public override void Initialize(PlatformGameStructure gameStructure)
	{
		string? gameDataPath = gameStructure.GameDataPath;
		if (string.IsNullOrWhiteSpace(gameDataPath))
		{
			throw new ArgumentException($"{nameof(gameStructure.GameDataPath)} cannot be null or whitespace.", nameof(gameStructure));
		}

		GameDataPath = gameDataPath;
		UnityPlayerPath = gameStructure.UnityPlayerPath;
		MetaDataPath = gameStructure.Il2CppMetaDataPath;

		IReadOnlyList<string> binaryCandidates = gameStructure.Il2CppGameAssemblyCandidates;
		if (binaryCandidates.Count == 0 || string.IsNullOrEmpty(MetaDataPath))
		{
			throw new Exception("No IL2CPP binary or metadata file was found.");
		}

		List<UnityVersion> versionCandidates = GetVersionCandidates(gameStructure);
		if (versionCandidates.Count == 0)
		{
			throw new Exception("Could not determine the unity version");
		}
		UnityVersion = versionCandidates[0];
		Logger.Info(LogCategory.Import, $"During Il2Cpp initialization, found Unity version: {UnityVersion}");

		Logger.SendStatusChange("loading_step_parse_il2cpp_metadata");

		byte[] metadata = gameStructure.FileSystem.File.ReadAllBytes(MetaDataPath);
		List<Exception> failures = [];
		foreach (string binaryPath in binaryCandidates)
		{
			byte[] binary = gameStructure.FileSystem.File.ReadAllBytes(binaryPath);
			foreach (UnityVersion version in versionCandidates)
			{
				Il2CppInput input = new(binaryPath, binary, MetaDataPath, metadata, version);
				RunPreprocessors(input);
				try
				{
					ClearStaticState?.Invoke();
					Cpp2IlApi.InitializeLibCpp2Il(input.Binary, input.Metadata, input.UnityVersion, false);
					GameAssemblyPath = binaryPath;
					UnityVersion = input.UnityVersion;
					if (failures.Count > 0)
					{
						Logger.Info(LogCategory.Import, $"Il2Cpp initialization succeeded with '{binaryPath}' and Unity {input.UnityVersion} after {failures.Count} failed attempt(s).");
					}
					BuildAssemblies();
					return;
				}
				catch (Exception ex)
				{
					failures.Add(ex);
					string shortPath = $"{Path.GetFileName(Path.GetDirectoryName(binaryPath))}/{Path.GetFileName(binaryPath)}";
					Logger.Warning(LogCategory.Import, $"Il2Cpp initialization failed for '{shortPath}' with Unity {input.UnityVersion}: {ex.GetType().Name}: {ex.Message}");
					TryResetCpp2Il();
				}
			}
		}

		throw new AggregateException($"Il2Cpp initialization failed for all {failures.Count} combination(s) of binary and Unity version.", failures);
	}

	/// <summary>
	/// The Unity versions to try, most trusted first: the platform structure, the game's serialized files, then Cpp2IL's own detection.
	/// </summary>
	private List<UnityVersion> GetVersionCandidates(PlatformGameStructure gameStructure)
	{
		List<UnityVersion> result = [];
		void Add(UnityVersion? version, string source)
		{
			if (version is { } v && v != default && !result.Contains(v))
			{
				Logger.Verbose(LogCategory.Import, $"Unity version candidate {v} from {source}");
				result.Add(v);
			}
		}

		Add(gameStructure.Version, "the platform structure");
		Add(gameStructure.TryGetUnityVersionFromGameFiles(), "the game files");
		if (result.Count > 0)
		{
			return result;
		}
		try
		{
			Add(Cpp2IlApi.DetermineUnityVersion(UnityPlayerPath, GameDataPath), "Cpp2IL");
		}
		catch (Exception ex)
		{
			Logger.Verbose(LogCategory.Import, $"Cpp2IL could not determine the Unity version: {ex.Message}");
		}
		return result;
	}

	private static void RunPreprocessors(Il2CppInput input)
	{
		foreach (IIl2CppInputPreprocessor preprocessor in InputPreprocessors)
		{
			try
			{
				if (preprocessor.Process(input))
				{
					Logger.Info(LogCategory.Import, $"IL2CPP input was modified by '{preprocessor.Name}'.");
				}
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Import, $"IL2CPP preprocessor '{preprocessor.Name}' failed: {ex.Message}");
			}
		}
	}

	private static void TryResetCpp2Il()
	{
		try
		{
			Cpp2IlApi.ResetInternalState();
		}
		catch (Exception)
		{
			// Best effort: the next attempt reinitializes everything anyway.
		}
	}

	private void BuildAssemblies()
	{
		Logger.SendStatusChange("loading_step_generate_dummy_dll");

		var context = Cpp2IlApi.CurrentAppContext ?? throw new InvalidOperationException("Cpp2IL did not create an application context.");

		List<Cpp2IlProcessingLayer> processingLayers = contentLevel == ScriptContentLevel.Level3
			? RecoveryProcessingLayers ?? DefaultProcessingLayers
			: DefaultProcessingLayers;

		foreach (Cpp2IlProcessingLayer cpp2IlProcessingLayer in processingLayers)
		{
			cpp2IlProcessingLayer.PreProcess(context, processingLayers);
		}

		foreach (Cpp2IlProcessingLayer cpp2IlProcessingLayer in processingLayers)
		{
			cpp2IlProcessingLayer.Process(context);
		}

		AsmResolverDllOutputFormat outputFormat = contentLevel == ScriptContentLevel.Level3
			? RecoveryOutputFormat ?? DefaultOutputFormat
			: DefaultOutputFormat;

		List<AssemblyDefinition> assemblies = outputFormat.BuildAssemblies(context);

		foreach (AssemblyDefinition assembly in assemblies)
		{
			Add(assembly);
		}
	}

	~IL2CppManager()
	{
		Dispose(false);
	}
}
