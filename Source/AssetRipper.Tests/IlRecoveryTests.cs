using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.Import.Structure.Assembly.Managers;

namespace AssetRipper.Tests;

public class IlRecoveryTests
{
	[TestCase("mscorlib", true)]
	[TestCase("System.Core", true)]
	[TestCase("UnityEngine.CoreModule", true)]
	[TestCase("Unity.TextMeshPro", true)]
	[TestCase("Assembly-CSharp", false)]
	[TestCase("Systemic.Game", false)]
	[TestCase("", true)]
	public void EngineAndBclAssembliesAreExcluded(string name, bool excluded)
	{
		Assert.That(SafeIlRecoveryOutputFormat.IsExcludedAssembly(name), Is.EqualTo(excluded));
	}

	private static MethodDefinition Method(bool constructor, params CilOpCode[] opCodes)
	{
		ModuleDefinition module = new("T.dll");
		MethodDefinition method = new(constructor ? ".ctor" : "M",
			constructor ? MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName : MethodAttributes.Public,
			MethodSignature.CreateInstance(module.CorLibTypeFactory.Void));
		method.CilMethodBody = new CilMethodBody();
		foreach (CilOpCode op in opCodes)
		{
			method.CilMethodBody.Instructions.Add(op == CilOpCodes.Call
				? new CilInstruction(op, new MemberReference(module.CorLibTypeFactory.Object.Type, ".ctor", MethodSignature.CreateInstance(module.CorLibTypeFactory.Void)))
				: new CilInstruction(op));
		}
		return method;
	}

	[Test]
	public void BareReturnIsNotCountedAsRecovered()
	{
		Assert.That(SafeIlRecoveryOutputFormat.IsMeaningful(Method(false, CilOpCodes.Ret)), Is.False);
		Assert.That(SafeIlRecoveryOutputFormat.IsMeaningful(Method(false, CilOpCodes.Nop, CilOpCodes.Ret)), Is.False);
		Assert.That(SafeIlRecoveryOutputFormat.IsMeaningful(Method(false, CilOpCodes.Ldarg_0, CilOpCodes.Pop, CilOpCodes.Ret)), Is.True);
	}

	[Test]
	public void StubConstructorIsNotCountedAsRecovered()
	{
		Assert.That(SafeIlRecoveryOutputFormat.IsMeaningful(Method(true, CilOpCodes.Ldarg_0, CilOpCodes.Call, CilOpCodes.Ret)), Is.False);
		Assert.That(SafeIlRecoveryOutputFormat.IsMeaningful(Method(true, CilOpCodes.Ldarg_0, CilOpCodes.Ldc_I4_1, CilOpCodes.Pop, CilOpCodes.Call, CilOpCodes.Ret)), Is.True);
	}
}
