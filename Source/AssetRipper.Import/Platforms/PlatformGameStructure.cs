using AssetRipper.Import.Logging;
using AssetRipper.Import.Platforms;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.BundleFiles;
using AssetRipper.IO.Files.BundleFiles.FileStream;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.Streams;
using System.Text.RegularExpressions;

namespace AssetRipper.Import.Structure.Platforms;

public abstract partial class PlatformGameStructure
{
	public FileSystem FileSystem { get; }
	public string? Name { get; protected set; }
	public string? RootPath { get; }
	public string? GameDataPath { get; protected set; }
	public string? StreamingAssetsPath { get; protected set; }
	public string? ResourcesPath { get; protected set; }
	public ScriptingBackend Backend { get; protected set; } = ScriptingBackend.Unknown;
	public string? ManagedPath { get; protected set; }
	public string? Il2CppGameAssemblyPath { get; protected set; }
	/// <summary>
	/// All IL2CPP binaries found for this game, most preferred first. <see cref="Il2CppGameAssemblyPath"/> is the first entry.
	/// </summary>
	/// <remarks>
	/// Some platforms (for example, Android) ship one binary per ABI. If one cannot be analyzed, the next can be tried.
	/// </remarks>
	public IReadOnlyList<string> Il2CppGameAssemblyCandidates
	{
		get => il2CppGameAssemblyCandidates ?? (Il2CppGameAssemblyPath is null ? [] : [Il2CppGameAssemblyPath]);
		protected set => il2CppGameAssemblyCandidates = value;
	}
	private IReadOnlyList<string>? il2CppGameAssemblyCandidates;
	public string? Il2CppMetaDataPath { get; protected set; }
	public string? UnityPlayerPath { get; protected set; }
	public UnityVersion? Version { get; protected set; }

	public IReadOnlyList<string> DataPaths { get; protected set; } = [];

	/// <summary>Name : FullName</summary>
	public HashSet<KeyValuePair<string, string>> Files { get; } = [];
	/// <summary>AssemblyName : AssemblyPath</summary>
	public Dictionary<string, string> Assemblies { get; } = [];

	protected const string DataFolderName = "Data";
	protected const string ManagedName = "Managed";
	protected const string LibName = "lib";
	protected const string ResourcesName = "Resources";
	protected const string UnityName = "unity";
	protected const string StreamingName = "StreamingAssets";
	protected const string MetadataName = "Metadata";
	protected const string DefaultUnityPlayerName = "UnityPlayer.dll";
	protected const string DefaultGameAssemblyName = "GameAssembly.dll";
	protected const string DefaultGlobalMetadataName = "global-metadata.dat";

	protected const string DataName = "data";
	protected const string DataBundleName = DataName + AssetBundleExtension;
	protected const string DataPackBundleName = DataName + "pack" + AssetBundleExtension;
	protected const string MainDataName = "mainData";
	protected const string GlobalGameManagersName = "globalgamemanagers";
	protected const string GlobalGameManagerAssetsName = "globalgamemanagers.assets";
	protected const string ResourcesAssetsName = "resources.assets";
	protected const string LevelPrefix = "level";

	protected const string AssetBundleExtension = ".unity3d";
	protected const string AlternateBundleExtension = ".bundle";
	protected const string Lz4BundleName = DataName + AssetBundleExtension;

	public PlatformGameStructure(FileSystem fileSystem)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		FileSystem = fileSystem;
	}

	public PlatformGameStructure(string rootPath, FileSystem fileSystem) : this(fileSystem)
	{
		ArgumentException.ThrowIfNullOrEmpty(rootPath);
		if (!FileSystem.Directory.Exists(rootPath))
		{
			throw new DirectoryNotFoundException($"Root directory '{rootPath}' doesn't exist");
		}
		RootPath = rootPath;
	}

	public static bool IsPrimaryEngineFile(string fileName)
	{
		if (fileName == MainDataName ||
			fileName == GlobalGameManagersName ||
			fileName == GlobalGameManagerAssetsName ||
			fileName == ResourcesAssetsName ||
			LevelTemplateRegex.IsMatch(fileName) ||
			SharedAssetTemplateRegex.IsMatch(fileName))
		{
			return true;
		}
		return false;
	}

	/// <summary>Attempts to find the path for the dependency with that name.</summary>
	public string? RequestDependency(string dependency)
	{
		string? dependencyPath = Files.FirstOrDefault(t => t.Key == dependency).Value;
		if (!string.IsNullOrEmpty(dependencyPath))
		{
			return dependencyPath;
		}

		foreach (string dataPath in DataPaths)
		{
			string filePath = FileSystem.Path.Join(dataPath, dependency);
			if (MultiFileStream.Exists(filePath, FileSystem))
			{
				return filePath;
			}

			if (SpecialFileNames.IsDefaultResource(dependency))
			{
				return FindEngineDependency(dataPath, SpecialFileNames.DefaultResourceName1) ??
					FindEngineDependency(dataPath, SpecialFileNames.DefaultResourceName2);
			}
			else if (SpecialFileNames.IsBuiltinExtra(dependency))
			{
				return FindEngineDependency(dataPath, SpecialFileNames.BuiltinExtraName1) ??
					FindEngineDependency(dataPath, SpecialFileNames.BuiltinExtraName2);
			}
		}
		return null;
	}

	public string? RequestAssembly(string assembly)
	{
		string assemblyName = $"{assembly}{MonoManager.AssemblyExtension}";
		if (Assemblies.TryGetValue(assemblyName, out string? assemblyPath))
		{
			return assemblyPath;
		}
		return null;
	}

	public string? RequestResource(string resource)
	{
		foreach (string dataPath in DataPaths)
		{
			string path = FileSystem.Path.Join(dataPath, resource);
			if (MultiFileStream.Exists(path, FileSystem))
			{
				return path;
			}
		}
		return null;
	}

	public virtual void CollectFiles(bool skipStreamingAssets)
	{
		if (this is MixedGameStructure)
		{
			return;
		}

		foreach (string dataPath in DataPaths)
		{
			CollectGameFiles(dataPath);
		}
		CollectMainAssemblies();
		if (!skipStreamingAssets)
		{
			CollectStreamingAssets();
		}
	}

	protected void CollectGameFiles(string root)
	{
		Logger.Info(LogCategory.Import, "Collecting game files...");
		CollectCompressedGameFiles(root);
		CollectDefaultSerializedFiles(root);
	}

	/// <summary>
	/// Finds data.unity3d and datapack.unity3d when Lz4 compressed
	/// 
	/// Accoding to comments in Unity source file in the function at
	/// PlatformDependent/AndroidPlayer/Source/ApkFile.cpp:268,
	/// the datapack asset is only present if Gradle built an AAB with Unity
	/// data asset pack inside and then bundletool converted AAB into universal APK.
	/// </summary>
	protected void CollectCompressedGameFiles(string root)
	{
		string dataBundlePath = FileSystem.Path.Join(root, DataBundleName);
		if (MultiFileStream.Exists(dataBundlePath, FileSystem))
		{
			AddAssetBundle(DataBundleName, dataBundlePath);
		}

		string dataPackBundlePath = FileSystem.Path.Join(root, DataPackBundleName);
		if (MultiFileStream.Exists(dataPackBundlePath, FileSystem))
		{
			AddAssetBundle(DataPackBundleName, dataPackBundlePath);
		}
	}

	/// <summary>
	/// Collects primary engine files
	/// </summary>
	/// <remarks>
	/// Files are selected based on <see cref="IsPrimaryEngineFile(string)"/>
	/// </remarks>
	protected void CollectDefaultSerializedFiles(string root)
	{
		foreach (string file in FileSystem.Directory.EnumerateFiles(root))
		{
			string name = FileSystem.Path.GetFileName(file);
			if (IsPrimaryEngineFile(name))
			{
				string levelName = MultiFileStream.GetFileName(name);
				AddFile(levelName, MultiFileStream.GetFilePath(file));
			}
		}
	}

	/// <summary>
	/// Collects all serialized files in the directory
	/// </summary>
	/// <remarks>
	/// The search is top-level only.
	/// Files are selected based on their file header.
	/// </remarks>
	protected void CollectAllSerializedFiles(string root)
	{
		foreach (string path in FileSystem.Directory.EnumerateFiles(root))
		{
			if (SerializedFile.IsSerializedFile(path, FileSystem))
			{
				string name = FileSystem.Path.GetFileName(path);
				string actualName = MultiFileStream.GetFileName(name);
				AddFile(actualName, MultiFileStream.GetFilePath(path));
			}
		}
	}

	/// <summary>
	/// Collect bundles from the Streaming Assets folder
	/// </summary>
	protected void CollectStreamingAssets()
	{
		if (string.IsNullOrWhiteSpace(StreamingAssetsPath))
		{
			return;
		}

		Logger.Info(LogCategory.Import, "Collecting Streaming Assets...");
		if (FileSystem.Directory.Exists(StreamingAssetsPath))
		{
			CollectAssetBundlesRecursively(StreamingAssetsPath);
		}
	}

	/// <summary>
	/// Collect asset bundles only from this directory
	/// </summary>
	protected void CollectAssetBundles(string root)
	{
		foreach (string file in FileSystem.Directory.EnumerateFiles(root))
		{
			if (BundleHeader.IsBundleHeader(file, FileSystem))
			{
				string name = FileSystem.Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
				AddAssetBundle(name, file);
			}
		}
	}

	/// <summary>
	/// Collect asset bundles from this directory and all subdirectories
	/// </summary>
	protected void CollectAssetBundlesRecursively(string root)
	{
		CollectAssetBundles(root);
		foreach (string directory in FileSystem.Directory.EnumerateDirectories(root))
		{
			CollectAssetBundlesRecursively(directory);
		}
	}

	protected void CollectAssemblies(string root)
	{
		foreach (string file in FileSystem.Directory.EnumerateFiles(root))
		{
			string name = FileSystem.Path.GetFileName(file);
			if (MonoManager.IsMonoAssembly(name))
			{
				if (!Assemblies.TryAdd(name, file))
				{
					Logger.Log(LogType.Warning, LogCategory.Import, $"Duplicate assemblies found: '{Assemblies[name]}' & '{file}'");
				}
			}
		}
	}

	protected void CollectMainAssemblies()
	{
		if (Backend != ScriptingBackend.Mono)
		{
			return;//Only needed for Mono
		}
		else if (!string.IsNullOrWhiteSpace(ManagedPath) && FileSystem.Directory.Exists(ManagedPath))
		{
			CollectAssemblies(ManagedPath);
		}
		else if (!string.IsNullOrEmpty(GameDataPath))
		{
			string libPath = FileSystem.Path.Join(FileSystem.Path.GetFullPath(GameDataPath), LibName);
			if (FileSystem.Directory.Exists(libPath))
			{
				CollectAssemblies(GameDataPath);
				CollectAssemblies(libPath);
			}
		}
	}

	private string? FindEngineDependency(string path, string dependency)
	{
		string filePath = FileSystem.Path.Join(path, dependency);
		if (FileSystem.File.Exists(filePath))
		{
			return filePath;
		}

		string resourcePath = FileSystem.Path.Join(path, ResourcesName);
		filePath = FileSystem.Path.Join(resourcePath, dependency);
		if (FileSystem.File.Exists(filePath))
		{
			return filePath;
		}

		// really old versions contains file in this directory
		string unityPath = FileSystem.Path.Join(path, UnityName);
		filePath = FileSystem.Path.Join(unityPath, dependency);
		if (FileSystem.File.Exists(filePath))
		{
			return filePath;
		}
		return null;
	}

	/// <summary>
	/// Add game file
	/// </summary>
	protected void AddFile(string name, string path)
	{
		if(Files.Add(name, path))
		{
			Logger.Info(LogCategory.Import, $"Game file '{name}' has been found");
		}
	}

	protected void AddAssetBundle(string name, string path)
	{
		if(Files.Add(name, path))
		{
			Logger.Info(LogCategory.Import, $"Asset bundle '{name}' has been found");
		}
	}

	protected UnityVersion GetUnityVersionFromSerializedFile(string filePath)
	{
		return SerializedFile.FromFile(filePath, FileSystem).Version;
	}

	protected UnityVersion GetUnityVersionFromBundleFile(string filePath)
	{
		using Stream stream = FileSystem.File.OpenRead(filePath);
		FileStreamBundleHeader header = new();
		header.Read(stream);
		return UnityVersion.Parse(header.UnityWebMinimumRevision);
	}

	/// <summary>
	/// Tries to determine the Unity version by reading the headers of the game's serialized files.
	/// </summary>
	/// <remarks>
	/// This is a fallback for platforms where <see cref="Version"/> is not known up front and
	/// the scripting backend (for example, Cpp2IL) cannot determine it from the player binary.
	/// Exceptions are swallowed; unreadable candidates are skipped.
	/// </remarks>
	public UnityVersion? TryGetUnityVersionFromGameFiles()
	{
		if (Version is { } known && known != default)
		{
			return known;
		}

		List<string> candidates = [];
		if (!string.IsNullOrEmpty(GameDataPath) && FileSystem.Directory.Exists(GameDataPath))
		{
			candidates.Add(FileSystem.Path.Join(GameDataPath, GlobalGameManagersName));
			candidates.Add(FileSystem.Path.Join(GameDataPath, MainDataName));
			candidates.Add(FileSystem.Path.Join(GameDataPath, LevelPrefix + "0"));
		}
		candidates.AddRange(Files.Values().Order(StringComparer.Ordinal));

		foreach (string path in candidates)
		{
			if (!FileSystem.File.Exists(path))
			{
				continue;
			}
			try
			{
				UnityVersion version = path.EndsWith(AssetBundleExtension, StringComparison.OrdinalIgnoreCase)
					? GetUnityVersionFromBundleFile(path)
					: GetUnityVersionFromSerializedFile(path);
				if (version != default && version.Major >= 3)
				{
					Logger.Info(LogCategory.Import, $"Unity version {version} was read from '{FileSystem.Path.GetFileName(path)}'");
					return version;
				}
			}
			catch (Exception)
			{
				// Not a serialized file (or a split file). Try the next candidate.
			}
		}
		return null;
	}

	protected UnityVersion? GetUnityVersionFromDataDirectory(string dataDirectoryPath)
	{
		string globalGameManagersPath = FileSystem.Path.Join(dataDirectoryPath, GlobalGameManagersName);
		if (FileSystem.File.Exists(globalGameManagersPath))
		{
			return GetUnityVersionFromSerializedFile(globalGameManagersPath);
		}
		string dataBundlePath = FileSystem.Path.Join(dataDirectoryPath, DataBundleName);
		if (FileSystem.File.Exists(dataBundlePath))
		{
			return GetUnityVersionFromBundleFile(dataBundlePath);
		}
		return null;
	}

	protected bool HasMonoAssemblies(string managedDirectory)
	{
		if (string.IsNullOrEmpty(managedDirectory) || !FileSystem.Directory.Exists(managedDirectory))
		{
			return false;
		}

		return FileSystem.Directory.GetFiles(managedDirectory, "*.dll").Length > 0;
	}

	protected bool HasIl2CppFiles()
	{
		return Il2CppGameAssemblyPath != null &&
			Il2CppMetaDataPath != null &&
			FileSystem.File.Exists(Il2CppGameAssemblyPath) &&
			FileSystem.File.Exists(Il2CppMetaDataPath);
	}

	[GeneratedRegex(@"^level\d+(?:\.split0)?$", RegexOptions.Compiled)]
	private static partial Regex LevelTemplateRegex { get; }

	[GeneratedRegex(@"^sharedassets\d+\.assets(?:\.split0)?$", RegexOptions.Compiled)]
	private static partial Regex SharedAssetTemplateRegex { get; }
}
