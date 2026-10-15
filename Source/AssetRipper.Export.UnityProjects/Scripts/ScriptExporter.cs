using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_115;

namespace AssetRipper.Export.UnityProjects.Scripts;

public class ScriptExporter : IAssetExporter
{
	public ScriptExporter(IAssemblyManager assemblyManager, FullConfiguration configuration)
	{
		AssemblyManager = assemblyManager;
		Decompiler = new ScriptDecompiler(AssemblyManager)
		{
			LanguageVersion = configuration.ExportSettings.ScriptLanguageVersion.ToCSharpLanguageVersion(configuration.Version),
			ScriptContentLevel = configuration.ImportSettings.ScriptContentLevel,
			FullyQualifiedTypeNames = configuration.ExportSettings.ScriptTypesFullyQualified,
		};
		ExportMode = configuration.ExportSettings.ScriptExportMode;
		PackageMappings = configuration.PackageMappings;
		ReferenceAssemblyDictionary = ReferenceAssemblies.GetReferenceAssemblies(AssemblyManager, configuration.Version);
	}

	public IAssemblyManager AssemblyManager { get; }
	public ScriptExportMode ExportMode { get; }
	public PackageMappings PackageMappings { get; }
	internal ScriptDecompiler Decompiler { get; }
	internal Dictionary<string, UnityGuid> ReferenceAssemblyDictionary { get; }
	private bool HasDecompiled { get; set; } = false;
	private static long MonoScriptDecompiledFileID { get; } = ExportIdHandler.GetMainExportID((int)ClassIDType.MonoScript);

	public bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		if (asset is IMonoScript script)
		{
			if (HasDecompiled)
			{
				exportCollection = new SingleRedirectExportCollection(asset, CreateExportPointer(script));
			}
			else
			{
				HasDecompiled = true;
				if (AssemblyManager.IsSet)
				{
					exportCollection = new ScriptExportCollection(this, script);
				}
				else
				{
					exportCollection = new EmptyScriptExportCollection(this, script);
				}
			}
			return true;
		}
		else
		{
			exportCollection = null;
			return false;
		}
	}

	public AssemblyExportType GetExportType(IMonoScript script)
	{
		return GetExportType(script.GetAssemblyNameFixed());
	}

	public MetaPtr CreateExportPointer(IMonoScript script)
	{
		if (PackageMappings.FindByAssembly(script.GetAssemblyNameFixed()) is { } package)
		{
			return CreatePackageScriptPointer(script, package);
		}
		return GetExportType(script) switch
		{
			AssemblyExportType.Decompile => new(MonoScriptDecompiledFileID, ScriptHashing.CalculateScriptGuid(script), AssetType.Meta),
			AssemblyExportType.Skip => new(ScriptHashing.CalculateScriptFileID(script), ReferenceAssemblyDictionary[script.GetAssemblyNameFixed()], AssetType.Meta),
			_ => new(ScriptHashing.CalculateScriptFileID(script), ScriptHashing.CalculateAssemblyGuid(script), AssetType.Meta),
		};
	}

	/// <summary>
	/// A reference to a script provided by a user-defined package.
	/// </summary>
	/// <remarks>
	/// Source packages: the script's own GUID with the standard MonoScript file ID.
	/// Precompiled packages: the DLL's GUID with the script's hashed file ID, like engine assemblies.
	/// </remarks>
	internal static MetaPtr CreatePackageScriptPointer(IMonoScript script, PackageDefinition package)
	{
		string fullName = string.IsNullOrEmpty(script.Namespace.String) ? script.ClassName_R.String : $"{script.Namespace}.{script.ClassName_R}";
		if (package.Scripts.TryGetValue(fullName, out string? scriptGuid) && PackageMappings.TryParseGuid(scriptGuid, out UnityGuid guid))
		{
			return new(MonoScriptDecompiledFileID, guid, AssetType.Meta);
		}
		if (PackageMappings.TryParseGuid(package.AssemblyGuid, out UnityGuid assemblyGuid))
		{
			return new(ScriptHashing.CalculateScriptFileID(script), assemblyGuid, AssetType.Meta);
		}
		AssetRipper.Import.Logging.Logger.Warning(AssetRipper.Import.Logging.LogCategory.Export,
			$"Package '{package.Name}' has no GUID for script '{fullName}'; the reference will be missing until the mapping is completed.");
		return new(MonoScriptDecompiledFileID, ScriptHashing.CalculateScriptGuid(script), AssetType.Meta);
	}

	public AssemblyExportType GetExportType(string assemblyName)
	{
		if (PackageMappings.FindByAssembly(assemblyName) is not null)
		{
			return AssemblyExportType.Skip; // provided by a user-defined package
		}
		if (ReferenceAssemblyDictionary.ContainsKey(assemblyName))
		{
			return AssemblyExportType.Skip;
		}
		else if (!AssemblyManager.IsSet)
		{
			return AssemblyExportType.Decompile;
		}
		else if (ExportMode is ScriptExportMode.Decompiled)
		{
			return AssemblyExportType.Decompile;
		}
		else if (ExportMode is ScriptExportMode.Hybrid)
		{
			return ReferenceAssemblies.IsPredefinedAssembly(assemblyName)
				? AssemblyExportType.Decompile
				: AssemblyExportType.Save;
		}
		else
		{
			return AssemblyExportType.Save;
		}
	}

	AssetType IAssetExporter.ToExportType(IUnityObjectBase asset) => AssetType.Meta;

	bool IAssetExporter.ToUnknownExportType(Type type, out AssetType assetType)
	{
		assetType = AssetType.Meta;
		return true;
	}
}
