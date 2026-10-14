using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Platforms;
using AssetRipper.IO.Files;

namespace AssetRipper.Import.Platforms;

internal sealed class AndroidGameStructure : PlatformGameStructure
{
	public AndroidGameStructure(string rootPath, FileSystem fileSystem) : this(rootPath, string.Empty, fileSystem)
	{
	}

	public AndroidGameStructure(string rootPath, string? obbPath, FileSystem fileSystem) : base(rootPath, fileSystem)
	{
		string apkDataPath = FileSystem.Path.Join(rootPath, AssetName, BinName, DataFolderName);
		if (!FileSystem.Directory.Exists(apkDataPath))
		{
			throw new Exception($"Data directory hasn't been found");
		}
		List<string> dataPaths = [apkDataPath];

		GameDataPath = apkDataPath;
		StreamingAssetsPath = null;
		ResourcesPath = FileSystem.Path.Join(GameDataPath, ResourcesName);
		ManagedPath = FileSystem.Path.Join(GameDataPath, ManagedName);
		LibPath = FileSystem.Path.Join(RootPath, LibName);
		Il2CppGameAssemblyPath = GetIl2CppGameAssemblyPath(LibPath);
		Il2CppMetaDataPath = FileSystem.Path.Join(ManagedPath, MetadataName, DefaultGlobalMetadataName);
		UnityPlayerPath = null;
		Version = GetUnityVersionFromDataDirectory(GameDataPath);

		if (HasIl2CppFiles())
		{
			Backend = ScriptingBackend.IL2Cpp;
		}
		else if (IsMono(ManagedPath))
		{
			Backend = ScriptingBackend.Mono;
		}
		else
		{
			Backend = ScriptingBackend.Unknown;
		}

		if (obbPath != null)
		{
			m_obbRoot = obbPath;
			if (!FileSystem.Directory.Exists(obbPath))
			{
				throw new Exception($"Obb directory '{obbPath}' doesn't exist");
			}

			string obbDataPath = FileSystem.Path.Join(obbPath, AssetName, BinName, DataFolderName);
			if (!FileSystem.Directory.Exists(obbDataPath))
			{
				throw new Exception($"Obb data directory '{obbDataPath}' wasn't found");
			}
			dataPaths.Add(obbDataPath);
		}
		DataPaths = dataPaths.ToArray();
	}

	public override void CollectFiles(bool skipStreamingAssets)
	{
		base.CollectFiles(skipStreamingAssets);
		CollectApkAssetBundles();
	}

	public static bool IsAndroidStructure(string path, FileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists(path))
		{
			return false;
		}

		int match = GetRootAndroidDirectoryMatch(path, fileSystem);
		if (match <= 8)
		{
			return false;
		}

		string dataPath = fileSystem.Path.Join(path, AssetName, BinName, DataFolderName);

		return fileSystem.Directory.Exists(dataPath);
	}

	public static bool IsAndroidObbStructure(string path, FileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists(path))
		{
			return false;
		}

		int match = GetRootAndroidDirectoryMatch(path, fileSystem);
		if (match != 8)
		{
			return false;
		}

		string dataPath = fileSystem.Path.Join(path, AssetName, BinName, DataFolderName);

		return fileSystem.Directory.Exists(dataPath);
	}

	private static int GetRootAndroidDirectoryMatch(string directory, FileSystem fileSystem)
	{
		int matches = 0;
		foreach (string subDirectory in fileSystem.Directory.EnumerateDirectories(directory))
		{
			switch (fileSystem.Path.GetFileName(subDirectory))
			{
				case AssetName:
					matches |= 8;
					break;

				case MetaName:
					matches |= 4;
					break;

				case LibName:
					matches |= 2;
					break;
			}
		}
		return matches;
	}

	private void CollectApkAssetBundles()
	{
		string assetPath = FileSystem.Path.Join(RootPath, AssetName);

		CollectAssetBundles(assetPath);
		foreach (string subDirectory in FileSystem.Directory.EnumerateDirectories(assetPath))
		{
			if (FileSystem.Path.GetFileName(subDirectory) == BinName)
			{
				continue;
			}
			CollectAssetBundlesRecursively(subDirectory);
		}
	}

	private string? GetIl2CppGameAssemblyPath(string libDirectory)
	{
		if (string.IsNullOrEmpty(libDirectory) || !FileSystem.Directory.Exists(libDirectory))
		{
			return null;
		}

		List<string> all = OrderByPreferredAbi(FileSystem.Directory.EnumerateFiles(libDirectory, Il2CppGameAssemblyName, SearchOption.AllDirectories), FileSystem);
		Il2CppGameAssemblyCandidates = all;
		string? path = all.Count > 0 ? all[0] : null;
		if (path is not null)
		{
			Logger.Info(LogCategory.Import, $"Using IL2CPP binary '{FileSystem.Path.GetFileName(FileSystem.Path.GetDirectoryName(path))}/{Il2CppGameAssemblyName}'");
		}
		return path;
	}

	/// <summary>
	/// ABI directory names, most preferred first. 64-bit ARM is the most common and best supported by Cpp2IL.
	/// </summary>
	internal static readonly string[] PreferredAbis = ["arm64-v8a", "armeabi-v7a", "x86_64", "x86", "armeabi"];

	/// <summary>
	/// Chooses the native library from the most preferred ABI directory, so the result does not depend on file system enumeration order.
	/// </summary>
	internal static string? SelectPreferredAbi(IEnumerable<string> paths, FileSystem fileSystem)
	{
		List<string> ordered = OrderByPreferredAbi(paths, fileSystem);
		return ordered.Count > 0 ? ordered[0] : null;
	}

	/// <summary>
	/// Orders native libraries by <see cref="PreferredAbis"/>, then by path for determinism.
	/// </summary>
	internal static List<string> OrderByPreferredAbi(IEnumerable<string> paths, FileSystem fileSystem)
	{
		return paths
			.OrderBy(path => GetAbiRank(path, fileSystem))
			.ThenBy(path => path, StringComparer.Ordinal)
			.ToList();
	}

	private static int GetAbiRank(string path, FileSystem fileSystem)
	{
		string abi = fileSystem.Path.GetFileName(fileSystem.Path.GetDirectoryName(path)) ?? "";
		int rank = Array.IndexOf(PreferredAbis, abi);
		return rank < 0 ? PreferredAbis.Length : rank;
	}

	private string? GetAndroidUnityAssemblyPath(string libDirectory)
	{
		if (string.IsNullOrEmpty(libDirectory) || !FileSystem.Directory.Exists(libDirectory))
		{
			return null;
		}

		return SelectPreferredAbi(FileSystem.Directory.EnumerateFiles(libDirectory, AndroidUnityAssemblyName, SearchOption.AllDirectories), FileSystem);
	}

	private bool IsMono(string managedDirectory)
	{
		if (string.IsNullOrEmpty(managedDirectory) || !FileSystem.Directory.Exists(managedDirectory))
		{
			return false;
		}

		return FileSystem.Directory.EnumerateFiles(managedDirectory, "*.dll").Any();
	}

	public string LibPath { get; private set; }

	private const string AssetName = "assets";
	private const string MetaName = "META-INF";
	private const string BinName = "bin";
	private const string Il2CppGameAssemblyName = "libil2cpp.so";
	private const string AndroidUnityAssemblyName = "libunity.so";

	private readonly string? m_obbRoot;
}
