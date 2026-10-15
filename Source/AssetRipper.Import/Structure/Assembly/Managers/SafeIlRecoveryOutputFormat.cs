using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;
using AssetRipper.Import.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.OutputFormats;

namespace AssetRipper.Import.Structure.Assembly.Managers;

/// <summary>
/// IL2CPP method body recovery (Script Content Level 3), built on Cpp2IL's ISIL → IL translation.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Assemblies that are part of the engine or the base class library (mscorlib, System*, Unity*, UnityEngine*, Mono*)
/// are excluded for speed: their source is available elsewhere and recovering them adds nothing.</item>
/// <item>Any method that cannot be recovered keeps a safe stub body (<c>throw null</c>), so the exported scripts still compile.</item>
/// <item>The number of recovered and stubbed methods is logged.</item>
/// </list>
/// </remarks>
public sealed class SafeIlRecoveryOutputFormat : AsmResolverDllOutputFormatIlRecovery
{
	private static readonly string[] ExcludedPrefixes = ["mscorlib", "netstandard", "System", "Unity", "UnityEngine", "Mono", "Microsoft", "Newtonsoft"];

	public int RecoveredCount { get; private set; }
	public int StubbedCount { get; private set; }
	public int SkippedCount { get; private set; }

	public static bool IsExcludedAssembly(string? assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return true;
		}
		foreach (string prefix in ExcludedPrefixes)
		{
			if (assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase)
				|| assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
	{
		string? assemblyName = methodDefinition.DeclaringModule?.Assembly?.Name?.ToString();
		if (IsExcludedAssembly(assemblyName))
		{
			SkippedCount++;
			SetStub(methodDefinition);
			return;
		}

		try
		{
			base.FillMethodBody(methodDefinition, methodContext);
			if (IsMeaningful(methodDefinition))
			{
				RecoveredCount++;
				return;
			}
		}
		catch (Exception)
		{
			// Fall through to the stub. Cpp2IL's translation is experimental and fails for many methods.
		}
		StubbedCount++;
		SetStub(methodDefinition);
	}

	/// <summary>
	/// A recovered body only counts if it does more than return; Cpp2IL emits a bare <c>ret</c> when translation silently fails,
	/// which would otherwise look like a successfully recovered empty method.
	/// </summary>
	internal static bool IsMeaningful(MethodDefinition method)
	{
		if (method.CilMethodBody is not { } body || body.Instructions.Count == 0)
		{
			return false;
		}
		int meaningful = body.Instructions.Count(i => i.OpCode.Code is not (CilCode.Nop or CilCode.Ret));
		if (method.IsConstructor)
		{
			// "ldarg.0; call base::.ctor; ret" is what every stub constructor looks like.
			return meaningful > 2;
		}
		return meaningful > 0;
	}

	private static void SetStub(MethodDefinition method)
	{
		if (method.IsAbstract || method.IsPInvokeImpl || method.IsRuntime || method.DeclaringType is { IsInterface: true } && !method.IsStatic)
		{
			method.CilMethodBody = null;
			return;
		}
		if (method.IsConstructor && method.CilMethodBody is { Instructions.Count: > 0 })
		{
			return; // keep the base constructor call emitted by Cpp2IL
		}
		CilMethodBody body = new();
		if (method.Signature?.ReturnType.ElementType == AsmResolver.PE.DotNet.Metadata.Tables.ElementType.Void)
		{
			body.Instructions.Add(CilOpCodes.Ret); // same as Script Content Level 2
		}
		else
		{
			body.Instructions.Add(CilOpCodes.Ldnull);
			body.Instructions.Add(CilOpCodes.Throw);
		}
		method.CilMethodBody = body;
	}

	public void LogSummary()
	{
		int analyzed = RecoveredCount + StubbedCount;
		double rate = analyzed == 0 ? 0 : 100.0 * RecoveredCount / analyzed;
		Logger.Info(LogCategory.Import,
			$"Il2Cpp method recovery: {RecoveredCount:N0} of {analyzed:N0} game methods recovered ({rate:0.0}%), {StubbedCount:N0} stubbed, {SkippedCount:N0} engine/BCL methods skipped.");
	}
}
