using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.Processing.Assemblies;

namespace AssetRipper.Tests;

public class FieldInitializerRecoveryTests
{
	private static (TypeDefinition Type, FieldDefinition IntField, FieldDefinition StringField, MethodDefinition Ctor) CreateType(bool withRealBody = false)
	{
		ModuleDefinition module = new("Test.dll");
		CorLibTypeFactory corLib = module.CorLibTypeFactory;
		TypeDefinition type = new("Game", "Player", TypeAttributes.Public, corLib.Object.Type);
		module.TopLevelTypes.Add(type);
		FieldDefinition intField = new("score", FieldAttributes.Public, corLib.Int32);
		FieldDefinition stringField = new("title", FieldAttributes.Public, corLib.String);
		type.Fields.Add(intField);
		type.Fields.Add(stringField);
		MethodDefinition ctor = new(".ctor",
			MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName,
			MethodSignature.CreateInstance(corLib.Void));
		if (withRealBody)
		{
			ctor.CilMethodBody = new CilMethodBody();
			CilInstructionCollection il = ctor.CilMethodBody.Instructions;
			il.Add(CilOpCodes.Ldarg_0);
			il.Add(CilOpCodes.Ldc_I4, 7);
			il.Add(CilOpCodes.Stfld, intField);
			il.Add(CilOpCodes.Ret);
		}
		type.Methods.Add(ctor);
		return (type, intField, stringField, ctor);
	}

	[Test]
	public void EmptyConstructorGetsFieldAssignments()
	{
		(TypeDefinition type, FieldDefinition intField, FieldDefinition stringField, MethodDefinition ctor) = CreateType();

		bool changed = FieldInitializerRecoveryProcessor.TryEmitConstructor(type, [(intField, 42), (stringField, "hero")]);

		Assert.That(changed, Is.True);
		IList<CilInstruction> il = ctor.CilMethodBody!.Instructions;
		using (Assert.EnterMultipleScope())
		{
			Assert.That(il.Any(i => i.OpCode.Code == CilCode.Stfld && i.Operand == intField), Is.True);
			Assert.That(il.Any(i => i.IsLdcI4() && i.GetLdcI4Constant() == 42), Is.True);
			Assert.That(il.Any(i => i.OpCode.Code == CilCode.Ldstr && (string?)i.Operand == "hero"), Is.True);
			Assert.That(il.Any(i => i.OpCode.Code == CilCode.Call && i.Operand is IMethodDescriptor { Name.Value: ".ctor" }), Is.True, "base constructor call");
			Assert.That(il[^1].OpCode.Code, Is.EqualTo(CilCode.Ret));
		}
	}

	[Test]
	public void RealConstructorIsNotReplaced()
	{
		(TypeDefinition type, FieldDefinition intField, _, MethodDefinition ctor) = CreateType(withRealBody: true);
		int before = ctor.CilMethodBody!.Instructions.Count;

		bool changed = FieldInitializerRecoveryProcessor.TryEmitConstructor(type, [(intField, 42)]);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(changed, Is.False);
			Assert.That(ctor.CilMethodBody.Instructions, Has.Count.EqualTo(before));
		}
	}

	[TestCase(0, true)]
	[TestCase(5, false)]
	[TestCase(false, true)]
	[TestCase(true, false)]
	[TestCase("", true)]
	[TestCase("x", false)]
	[TestCase(0f, true)]
	[TestCase(-0f, false)]
	[TestCase(1.5, false)]
	public void DefaultValuesAreSkipped(object value, bool isDefault)
	{
		Assert.That(FieldInitializerRecoveryProcessor.IsDefault(value), Is.EqualTo(isDefault));
	}
}
