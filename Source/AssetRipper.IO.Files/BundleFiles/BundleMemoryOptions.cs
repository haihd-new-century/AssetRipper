namespace AssetRipper.IO.Files.BundleFiles;

/// <summary>
/// Controls how much decompressed bundle data is kept in RAM instead of temporary files.
/// </summary>
/// <remarks>
/// Lower values reduce memory usage for heavily compressed games at the cost of more disk I/O.
/// Changes apply to bundles opened afterwards.
/// </remarks>
public static class BundleMemoryOptions
{
	public const int DefaultMaxInMemoryDecompressedSize = 50 * 1024 * 1024;
	public const int DefaultMaxPreAllocatedDecompressedSize = 30 * 1024 * 1024;
	public const int LowMemoryMaxInMemoryDecompressedSize = 4 * 1024 * 1024;

	/// <summary>
	/// Decompressed streams larger than this are stored in temporary files. Default 50 MB.
	/// </summary>
	public static int MaxInMemoryDecompressedSize
	{
		get => field;
		set => field = Math.Max(0, value);
	} = DefaultMaxInMemoryDecompressedSize;

	/// <summary>
	/// Decompressed streams up to this size are pre-allocated in a single array. Default 30 MB.
	/// </summary>
	public static int MaxPreAllocatedDecompressedSize
	{
		get => field;
		set => field = Math.Max(0, value);
	} = DefaultMaxPreAllocatedDecompressedSize;

	/// <summary>
	/// Applies conservative limits suitable for machines with little RAM.
	/// </summary>
	public static void UseLowMemoryLimits()
	{
		MaxInMemoryDecompressedSize = LowMemoryMaxInMemoryDecompressedSize;
		MaxPreAllocatedDecompressedSize = LowMemoryMaxInMemoryDecompressedSize;
	}

	public static void Reset()
	{
		MaxInMemoryDecompressedSize = DefaultMaxInMemoryDecompressedSize;
		MaxPreAllocatedDecompressedSize = DefaultMaxPreAllocatedDecompressedSize;
	}
}
