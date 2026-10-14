using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.SerializationLogic;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Processing.Assemblies;

/// <summary>
/// Recovers field initializers (for example <c>public int score = 42;</c>) for IL2CPP scripts,
/// whose constructors have no method bodies after Cpp2IL analysis.
/// </summary>
/// <remarks>
/// For every script type, the serialized values of all its MonoBehaviour/ScriptableObject instances are inspected.
/// For each top-level field of a primitive, enum or string type, the most common value is used
/// if it is not the type's default and at least <see cref="MinimumAgreement"/> of the instances use it.
/// The parameterless constructor is then given a body that assigns those values and calls the base constructor,
/// which decompilers display as field initializers.
/// Only empty or trivial constructors are replaced, so real IL (Mono games) is never modified.
/// </remarks>
public sealed class FieldInitializerRecoveryProcessor(ScriptContentLevel scriptContentLevel) : IAssetProcessor
{
	/// <summary>
	/// The fraction of instances that must share a value for it to be used as the initializer.
	/// </summary>
	public const double MinimumAgreement = 0.5;

	private const int MaxDepth = 6;

	public int RecoveredFieldCount { get; private set; }
	public int UpdatedTypeCount { get; private set; }

	public void Process(GameData gameData)
	{
		IAssemblyManager manager = gameData.AssemblyManager;
		if (manager.ScriptingBackend != ScriptingBackend.IL2Cpp || scriptContentLevel is ScriptContentLevel.Level0 or > ScriptContentLevel.Level2)
		{
			return;
		}

		Logger.Info(LogCategory.Processing, "Recovering field initializers from serialized data");
		Dictionary<TypeDefinition, Dictionary<string, Dictionary<object, int>>> statistics = new();
		Dictionary<TypeDefinition, int> instanceCounts = new();

		Dictionary<string, TypeDefinition>? typesByFullName = null;
		TypeDefinition? FindNestedType(SerializableType serializableType)
		{
			typesByFullName ??= BuildTypeLookup(manager);
			return typesByFullName.GetValueOrDefault(serializableType.FullName);
		}

		void Collect(TypeDefinition type, SerializableStructure structure, int depth)
		{
			instanceCounts[type] = instanceCounts.GetValueOrDefault(type) + 1;
			if (!statistics.TryGetValue(type, out Dictionary<string, Dictionary<object, int>>? fieldStatistics))
			{
				fieldStatistics = new();
				statistics.Add(type, fieldStatistics);
			}
			for (int i = 0; i < structure.Fields.Length; i++)
			{
				SerializableType.Field etalon = structure.Type.Fields[i];
				if (etalon.ArrayDepth != 0)
				{
					continue;
				}
				if (etalon.Type.Type == PrimitiveType.Complex)
				{
					// Nested [Serializable] class or struct: gather statistics for its own type as well.
					if (depth < MaxDepth && structure.Fields[i].CValue is SerializableStructure nested && FindNestedType(nested.Type) is { } nestedType)
					{
						Collect(nestedType, nested, depth + 1);
					}
					continue;
				}
				if (!TryGetValue(etalon.Type.Type, structure.Fields[i], out object? value))
				{
					continue;
				}
				if (!fieldStatistics.TryGetValue(etalon.Name, out Dictionary<object, int>? counts))
				{
					counts = new();
					fieldStatistics.Add(etalon.Name, counts);
				}
				counts[value] = counts.GetValueOrDefault(value) + 1;
			}
		}

		foreach (IMonoBehaviour monoBehaviour in gameData.GameBundle.FetchAssets().OfType<IMonoBehaviour>())
		{
			if (!monoBehaviour.TryGetScript(out IMonoScript? script))
			{
				continue;
			}
			TypeDefinition? type = TryGetType(manager, script);
			SerializableStructure? structure = monoBehaviour.LoadStructure();
			if (type is not null && structure is not null)
			{
				Collect(type, structure, 0);
			}
		}

		foreach ((TypeDefinition type, Dictionary<string, Dictionary<object, int>> fieldStatistics) in statistics)
		{
			int instances = instanceCounts[type];
			List<(FieldDefinition Field, object Value)> initializers = [];
			foreach (FieldDefinition field in type.Fields)
			{
				if (field.IsStatic || field.IsLiteral || field.Name is null || !fieldStatistics.TryGetValue(field.Name, out Dictionary<object, int>? counts))
				{
					continue;
				}
				(object value, int count) = counts.MaxBy(pair => pair.Value);
				if (count < instances * MinimumAgreement || IsDefault(value) || !IsCompatible(field, value))
				{
					continue;
				}
				initializers.Add((field, value));
			}

			if (initializers.Count > 0 && TryEmitConstructor(type, initializers))
			{
				UpdatedTypeCount++;
				RecoveredFieldCount += initializers.Count;
			}
		}

		if (UpdatedTypeCount > 0)
		{
			manager.ClearStreamCache();
		}
		Logger.Info(LogCategory.Processing, $"Recovered {RecoveredFieldCount} field initializer(s) in {UpdatedTypeCount} type(s)");
	}

	private static Dictionary<string, TypeDefinition> BuildTypeLookup(IAssemblyManager manager)
	{
		Dictionary<string, TypeDefinition> result = new();
		foreach (TypeDefinition type in manager.GetAssemblies().SelectMany(a => a.Modules).SelectMany(m => m.GetAllTypes()))
		{
			if (type.IsInterface || type.IsEnum)
			{
				continue;
			}
			// SerializableType uses "Namespace.Name"; nested types are matched by their simple name within the namespace.
			string key = string.IsNullOrEmpty(type.Namespace) ? (type.Name ?? "") : $"{type.Namespace}.{type.Name}";
			result.TryAdd(key, type);
		}
		return result;
	}

	private static TypeDefinition? TryGetType(IAssemblyManager manager, IMonoScript script)
	{
		try
		{
			ScriptIdentifier id = manager.GetScriptID(script.GetAssemblyNameFixed(), script.Namespace, script.ClassName_R);
			return manager.IsValid(id) ? manager.GetTypeDefinition(id) : null;
		}
		catch
		{
			return null;
		}
	}

	internal static bool TryGetValue(PrimitiveType type, SerializableValue value, [NotNullWhen(true)] out object? result)
	{
		result = type switch
		{
			PrimitiveType.Bool => value.AsBoolean,
			PrimitiveType.Char => (char)value.AsUInt16,
			PrimitiveType.SByte => value.AsSByte,
			PrimitiveType.Byte => value.AsByte,
			PrimitiveType.Short => value.AsInt16,
			PrimitiveType.UShort => value.AsUInt16,
			PrimitiveType.Int => value.AsInt32,
			PrimitiveType.UInt => value.AsUInt32,
			PrimitiveType.Long => value.AsInt64,
			PrimitiveType.ULong => value.AsUInt64,
			PrimitiveType.Single => value.AsSingle,
			PrimitiveType.Double => value.AsDouble,
			PrimitiveType.String => value.CValue as string,
			_ => null,
		};
		return result is not null;
	}

	internal static bool IsDefault(object value) => value switch
	{
		bool b => !b,
		string s => s.Length == 0,
		float f => f == 0 && !float.IsNegative(f),
		double d => d == 0 && !double.IsNegative(d),
		_ => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) == 0,
	};

	private static bool IsCompatible(FieldDefinition field, object value)
	{
		TypeSignature? signature = field.Signature?.FieldType;
		if (signature is null)
		{
			return false;
		}
		if (signature.ToTypeDefOrRef().TryResolve(field.DeclaringModule?.RuntimeContext, out TypeDefinition? enumType) && enumType.IsEnum)
		{
			ElementType? underlying = enumType.GetEnumUnderlyingType()?.ElementType;
			return underlying is ElementType.I4 or ElementType.U4 or ElementType.I2 or ElementType.U2 or ElementType.I1 or ElementType.U1 && value is int or uint or short or ushort or sbyte or byte;
		}
		return (signature.ElementType, value) switch
		{
			(ElementType.Boolean, bool) => true,
			(ElementType.Char, char) => true,
			(ElementType.I1, sbyte) or (ElementType.U1, byte) => true,
			(ElementType.I2, short) or (ElementType.U2, ushort) => true,
			(ElementType.I4, int) or (ElementType.U4, uint) => true,
			(ElementType.I8, long) or (ElementType.U8, ulong) => true,
			(ElementType.R4, float) or (ElementType.R8, double) => true,
			(ElementType.String, string) => true,
			_ => false,
		};
	}

	/// <summary>
	/// Replaces an empty or trivial parameterless constructor with one that assigns <paramref name="initializers"/>.
	/// </summary>
	internal static bool TryEmitConstructor(TypeDefinition type, IReadOnlyList<(FieldDefinition Field, object Value)> initializers)
	{
		MethodDefinition? constructor = type.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
		ModuleDefinition? module = type.DeclaringModule;
		if (constructor is null || module is null || type.BaseType is null || !IsTrivial(constructor))
		{
			return false;
		}

		MemberReference baseConstructor = new(type.BaseType, ".ctor", MethodSignature.CreateInstance(module.CorLibTypeFactory.Void));
		CilMethodBody body = new();
		CilInstructionCollection instructions = body.Instructions;
		foreach ((FieldDefinition field, object value) in initializers)
		{
			instructions.Add(CilOpCodes.Ldarg_0);
			EmitConstant(instructions, value);
			instructions.Add(CilOpCodes.Stfld, field);
		}
		instructions.Add(CilOpCodes.Ldarg_0);
		instructions.Add(CilOpCodes.Call, baseConstructor);
		instructions.Add(CilOpCodes.Ret);
		instructions.OptimizeMacros();
		constructor.CilMethodBody = body;
		constructor.ImplAttributes &= ~(MethodImplAttributes.InternalCall | MethodImplAttributes.Native | MethodImplAttributes.Runtime);
		return true;
	}

	private static bool IsTrivial(MethodDefinition constructor)
	{
		if (constructor.CilMethodBody is not { } body)
		{
			return true;
		}
		// An empty body, a bare "ret", or "throw null" (stub) are all placeholders produced by Cpp2IL or AssetRipper.
		return body.Instructions.Count <= 3 && body.Instructions.All(i =>
			i.OpCode.Code is CilCode.Ret or CilCode.Ldnull or CilCode.Throw or CilCode.Nop or CilCode.Ldarg_0 or CilCode.Ldarg
			|| (i.OpCode.Code is CilCode.Call && i.Operand is IMethodDescriptor { Name.Value: ".ctor" }));
	}

	private static void EmitConstant(CilInstructionCollection instructions, object value)
	{
		switch (value)
		{
			case bool b:
				instructions.Add(CilOpCodes.Ldc_I4, b ? 1 : 0);
				break;
			case char c:
				instructions.Add(CilOpCodes.Ldc_I4, c);
				break;
			case sbyte or byte or short or ushort or int:
				instructions.Add(CilOpCodes.Ldc_I4, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
				break;
			case uint u:
				instructions.Add(CilOpCodes.Ldc_I4, unchecked((int)u));
				break;
			case long l:
				instructions.Add(CilOpCodes.Ldc_I8, l);
				break;
			case ulong ul:
				instructions.Add(CilOpCodes.Ldc_I8, unchecked((long)ul));
				break;
			case float f:
				instructions.Add(CilOpCodes.Ldc_R4, f);
				break;
			case double d:
				instructions.Add(CilOpCodes.Ldc_R8, d);
				break;
			case string s:
				instructions.Add(CilOpCodes.Ldstr, s);
				break;
			default:
				throw new NotSupportedException(value.GetType().Name);
		}
	}
}
