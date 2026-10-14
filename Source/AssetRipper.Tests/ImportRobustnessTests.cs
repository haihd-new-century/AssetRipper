using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Platforms;
using AssetRipper.Import.Structure;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Assembly.TypeTrees;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Primitives;

namespace AssetRipper.Tests;

/// <summary>
/// Tests for import fallbacks: partial type tree reading, TPK coverage warnings, ABI selection and IL2CPP input checks.
/// </summary>
public class ImportRobustnessTests
{
	private static readonly string PlayerSettingsFixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "globalgamemanagers_6000.3.26f1_android");

	private static TypeTreeNodeStruct Int(string name) => new("int", name, 1, default, []);

	private static SerializableStructure CreateThreeIntStructure()
	{
		TypeTreeNodeStruct root = new("Base", "Base", 1, default, [Int("a"), Int("b"), Int("c")]);
		return SerializableTreeType.FromRootNode(root).CreateSerializableStructure();
	}

	private static byte[] Ints(params int[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

	[Test]
	public void ResetClearsPrimitiveFieldsInPlace()
	{
		SerializableStructure structure = CreateThreeIntStructure();
		EndianSpanReader reader = new(Ints(1, 2, 3), EndianType.LittleEndian);
		structure.Read(ref reader, new UnityVersion(2022, 3), TransferInstructionFlags.SerializeGameRelease, ITypeResolver.Null);
		Assert.That(structure.Fields[1].AsInt32, Is.EqualTo(2));

		structure.Reset();

		Assert.That(structure.Fields.Select(f => f.AsInt32), Is.All.Zero);
	}

	[Test]
	public void ReadPartiallyKeepsFieldsBeforeTheFailure()
	{
		SerializableStructure structure = CreateThreeIntStructure();
		EndianSpanReader reader = new(Ints(10, 20), EndianType.LittleEndian); // the third int is missing

		int read = structure.ReadPartially(ref reader, new UnityVersion(2022, 3), TransferInstructionFlags.SerializeGameRelease, ITypeResolver.Null);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(read, Is.EqualTo(2));
			Assert.That(structure.Fields[0].AsInt32, Is.EqualTo(10));
			Assert.That(structure.Fields[1].AsInt32, Is.EqualTo(20));
			Assert.That(structure.Fields[2].AsInt32, Is.Zero);
			Assert.That(reader.Position, Is.EqualTo(8));
		}
	}

	[Test]
	public void ReadPartiallyReturnsFieldCountOnSuccess()
	{
		SerializableStructure structure = CreateThreeIntStructure();
		EndianSpanReader reader = new(Ints(1, 2, 3), EndianType.LittleEndian);
		int read = structure.ReadPartially(ref reader, new UnityVersion(2022, 3), TransferInstructionFlags.SerializeGameRelease, ITypeResolver.Null);
		Assert.That(read, Is.EqualTo(3));
	}

	[Test]
	public void PlayerSettingsFromNewerPatchReleaseIsRecoveredInsteadOfUnreadable()
	{
		Assume.That(File.Exists(PlayerSettingsFixture), "fixture missing");
		GameBundle bundle = GameBundle.FromPaths([PlayerSettingsFixture], new GameAssetFactory(new BaseManager(_ => { })), LocalFileSystem.Instance);

		IUnityObjectBase playerSettings = bundle.FetchAssets().Single(a => a.ClassID == 129);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(playerSettings, Is.Not.InstanceOf<UnreadableObject>());
			Assert.That(playerSettings, Is.InstanceOf<TypeTreeObject>());
			TypeTreeObject tree = (TypeTreeObject)playerSettings;
			Assert.That(tree.ReleaseFields["productName"].AsString, Is.EqualTo("ar-il2cpp-test"));
			Assert.That(tree.ReleaseFields["defaultScreenWidth"].AsInt32, Is.EqualTo(1920));
		}
	}

	[TestCase("6000.3.26f1", false)]
	[TestCase("6000.3.20f1", true)]
	[TestCase("6000.3.5f1", true)]
	[TestCase("2022.3.10f1", true)]
	[TestCase("9999.1.0f1", false)]
	public void TpkCoverage(string version, bool expected)
	{
		Assert.That(TypeTreeNodeStruct.IsCoveredByTpk(UnityVersion.Parse(version), out _), Is.EqualTo(expected));
	}

	[Test]
	public void VersionWarningsAreReportedOncePerVersion()
	{
		UnityVersion newer = UnityVersion.Parse("6000.3.26f1");
		UnityVersion known = UnityVersion.Parse("2022.3.10f1");
		List<UnityVersion> reported = GameStructure.WarnAboutVersionsNewerThanTypeTreeData([newer, known, newer]);
		Assert.That(reported, Is.EqualTo(new[] { newer }));
	}

	[Test]
	public void EnumerateTpkTreesStartsWithTheClosestVersion()
	{
		UnityVersion version = UnityVersion.Parse("6000.3.26f1");
		List<UnityVersion> versions = TypeTreeNodeStruct.EnumerateTpkTrees((AssetRipper.SourceGenerated.ClassIDType)129, version).Select(t => t.Version).ToList();
		Assert.That(versions, Is.Not.Empty);
		Assert.That(versions[0].Major, Is.EqualTo(6000));
		Assert.That(versions.Distinct().Count(), Is.EqualTo(versions.Count));
	}

	[Test]
	public void AndroidAbiPreferenceIsIndependentOfEnumerationOrder()
	{
		string[] paths =
		[
			"/apk/lib/x86/libil2cpp.so",
			"/apk/lib/armeabi-v7a/libil2cpp.so",
			"/apk/lib/arm64-v8a/libil2cpp.so",
		];
		VirtualFileSystem fs = new();
		using (Assert.EnterMultipleScope())
		{
			Assert.That(AndroidGameStructure.SelectPreferredAbi(paths, fs), Is.EqualTo("/apk/lib/arm64-v8a/libil2cpp.so"));
			Assert.That(AndroidGameStructure.SelectPreferredAbi(paths.Reverse(), fs), Is.EqualTo("/apk/lib/arm64-v8a/libil2cpp.so"));
			Assert.That(AndroidGameStructure.OrderByPreferredAbi(paths, fs), Is.EqualTo(new[] { paths[2], paths[1], paths[0] }));
			Assert.That(AndroidGameStructure.SelectPreferredAbi([], fs), Is.Null);
		}
	}

	[Test]
	public void Il2CppInputDetectsStandardMetadataHeader()
	{
		byte[] standard = [0xAF, 0x1B, 0xB1, 0xFA, 39, 0, 0, 0];
		byte[] encrypted = [1, 2, 3, 4, 5, 6, 7, 8];
		Il2CppInput good = new("lib.so", [], "global-metadata.dat", standard, default);
		Il2CppInput bad = new("lib.so", [], "global-metadata.dat", encrypted, default);
		using (Assert.EnterMultipleScope())
		{
			Assert.That(good.HasStandardMetadataHeader, Is.True);
			Assert.That(good.MetadataVersion, Is.EqualTo(39));
			Assert.That(bad.HasStandardMetadataHeader, Is.False);
			Assert.That(bad.MetadataVersion, Is.EqualTo(-1));
			Assert.That(new MetadataHeaderDiagnostic().Process(bad), Is.False, "the diagnostic must never modify input");
		}
	}

	[Test]
	public void UnityVersionIsReadFromGameFilesWhenThePlatformDoesNotKnowIt()
	{
		Assume.That(File.Exists(PlayerSettingsFixture), "fixture missing");
		VirtualFileSystem fs = new();
		const string root = "/game";
		fs.Directory.Create(root);
		fs.Directory.Create("/game/Game_Data");
		fs.File.WriteAllBytes("/game/UnityPlayer.so", [0]);
		fs.File.WriteAllBytes("/game/Game", [0]);
		fs.File.WriteAllBytes("/game/Game_Data/globalgamemanagers", File.ReadAllBytes(PlayerSettingsFixture));

		PlatformChecker.CheckPlatform([root], fs, out AssetRipper.Import.Structure.Platforms.PlatformGameStructure? platform, out _);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(platform, Is.Not.Null);
			Assert.That(platform!.Version, Is.Null, "Linux structures do not know the version up front");
			Assert.That(platform.TryGetUnityVersionFromGameFiles(), Is.EqualTo(UnityVersion.Parse("6000.3.26f1")));
		}
	}

	[Test]
	public void DefaultPreprocessorsIncludeTheMetadataDiagnostic()
	{
		Assert.That(IL2CppManager.InputPreprocessors.OfType<MetadataHeaderDiagnostic>(), Is.Not.Empty);
	}
}
