using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Assembly.TypeTrees;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using System.Diagnostics;

namespace AssetRipper.Import.AssetCreation;

[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
public abstract class TypeTreeObject : NullObject
{
	public bool IsPlayerSettings => ClassID == 129;
	public abstract SerializableStructure ReleaseFields { get; }
	public abstract SerializableStructure EditorFields { get; }
	public sealed override bool FlowMappedInYaml => EditorFields.FlowMappedInYaml;
	public sealed override int SerializedVersion => EditorFields.SerializedVersion;
	public sealed override string ClassName => EditorFields.Type.Name;

	private TypeTreeObject(AssetInfo assetInfo) : base(assetInfo)
	{
	}

	/// <summary>
	/// Reads as many top-level fields as possible, leaving the remaining fields at their default values.
	/// </summary>
	/// <param name="reader">The reader, positioned at the start of the object data.</param>
	/// <param name="fieldCount">The number of top-level fields in the type tree used for reading.</param>
	/// <returns>The number of top-level fields that were read successfully.</returns>
	public int ReadPartially(ref EndianSpanReader reader, out int fieldCount)
	{
		bool release = Collection.Flags.IsRelease();
		SerializableStructure target = release ? ReleaseFields : EditorFields;
		fieldCount = target.Fields.Length;
		int read = target.ReadPartially(ref reader, Collection.Version, Collection.Flags, Resolver);
		OnPartiallyRead(release);
		return read;
	}

	/// <summary>
	/// The name of a top-level field in the structure used for reading.
	/// </summary>
	public string GetReadFieldName(int index)
	{
		SerializableStructure target = Collection.Flags.IsRelease() ? ReleaseFields : EditorFields;
		return (uint)index < (uint)target.Fields.Length ? target.Type.Fields[index].Name : "<end>";
	}

	private protected abstract ITypeResolver Resolver { get; }

	private protected virtual void OnPartiallyRead(bool release)
	{
	}

	public sealed override void WriteRelease(AssetWriter writer) => ReleaseFields.WriteRelease(writer);

	public sealed override void WriteEditor(AssetWriter writer) => EditorFields.WriteEditor(writer);

	public sealed override void WalkRelease(AssetWalker walker) => ReleaseFields.WalkRelease(walker);

	public sealed override void WalkEditor(AssetWalker walker) => EditorFields.WalkEditor(walker);

	public static TypeTreeObject Create(AssetInfo assetInfo, TypeTreeNodeStruct root, ITypeResolver resolver) => new SingleTypeTreeObject(assetInfo, root, resolver);

	public static TypeTreeObject Create(AssetInfo assetInfo, TypeTreeNodeStruct releaseRoot, TypeTreeNodeStruct editorRoot, ITypeResolver resolver) => new DoubleTypeTreeObject(assetInfo, releaseRoot, editorRoot, resolver);

	private string GetDebuggerDisplay() => ClassName;

	private sealed class SingleTypeTreeObject : TypeTreeObject
	{
		private readonly ITypeResolver resolver;
		public SerializableStructure Fields { get; }
		public override SerializableStructure ReleaseFields => Fields;
		public override SerializableStructure EditorFields => Fields;
		private protected override ITypeResolver Resolver => resolver;

		public SingleTypeTreeObject(AssetInfo assetInfo, TypeTreeNodeStruct root, ITypeResolver resolver) : base(assetInfo)
		{
			this.resolver = resolver;
			Fields = SerializableTreeType.FromRootNode(root).CreateSerializableStructure();
		}

		public override void ReadRelease(ref EndianSpanReader reader)
		{
			Fields.Read(ref reader, Collection.Version, Collection.Flags, resolver);
		}

		public override void ReadEditor(ref EndianSpanReader reader)
		{
			Fields.Read(ref reader, Collection.Version, Collection.Flags, resolver);
		}

		public override void WalkStandard(AssetWalker walker)
		{
			Fields.WalkStandard(walker);
		}

		public override void Reset()
		{
			Fields.Reset();
		}

		public override IEnumerable<(string, PPtr)> FetchDependencies()
		{
			return Fields.FetchDependencies();
		}
	}

	private sealed class DoubleTypeTreeObject : TypeTreeObject
	{
		private readonly ITypeResolver resolver;
		public override SerializableStructure ReleaseFields { get; }
		public override SerializableStructure EditorFields { get; }
		private protected override ITypeResolver Resolver => resolver;

		private protected override void OnPartiallyRead(bool release)
		{
			if (release)
			{
				ConvertFields(ReleaseFields, EditorFields);
			}
			else
			{
				ConvertFields(EditorFields, ReleaseFields);
			}
		}

		public DoubleTypeTreeObject(AssetInfo assetInfo, TypeTreeNodeStruct releaseRoot, TypeTreeNodeStruct editorRoot, ITypeResolver resolver) : base(assetInfo)
		{
			this.resolver = resolver;
			ReleaseFields = SerializableTreeType.FromRootNode(releaseRoot).CreateSerializableStructure();
			EditorFields = SerializableTreeType.FromRootNode(editorRoot).CreateSerializableStructure();
		}

		public override void ReadRelease(ref EndianSpanReader reader)
		{
			ReleaseFields.Read(ref reader, Collection.Version, Collection.Flags, resolver);
			ConvertFields(ReleaseFields, EditorFields);
		}

		public override void ReadEditor(ref EndianSpanReader reader)
		{
			EditorFields.Read(ref reader, Collection.Version, Collection.Flags, resolver);
			ConvertFields(EditorFields, ReleaseFields);
		}

		public override void WalkStandard(AssetWalker walker)
		{
			if (walker.EnterAsset(this))
			{
				if (walker.EnterField(this, "Release"))
				{
					ReleaseFields.WalkStandard(walker);
					walker.ExitField(this, "Release");
				}
				walker.DivideAsset(this);
				if (walker.EnterField(this, "Editor"))
				{
					EditorFields.WalkStandard(walker);
					walker.ExitField(this, "Editor");
				}
				walker.ExitAsset(this);
			}
		}

		public override void Reset()
		{
			ReleaseFields.Reset();
			EditorFields.Reset();
		}

		public override IEnumerable<(string, PPtr)> FetchDependencies()
		{
			return ReleaseFields.FetchDependencies().Union(EditorFields.FetchDependencies());
		}

		private void ConvertFields(SerializableStructure source, SerializableStructure target)
		{
			target.CopyValues(source, new PPtrConverter(Collection, Collection));
		}
	}
}
