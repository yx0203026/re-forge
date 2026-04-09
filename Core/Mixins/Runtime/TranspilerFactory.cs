#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// Transpiler 委托，用于在 IL 级别修改方法代码。
/// </summary>
/// <param name="instructions">原方法的 IL 指令序列。</param>
/// <param name="ilGenerator">IL 生成器，用于必要时生成新指令。</param>
/// <returns>修改后的 IL 指令序列。</returns>
internal delegate IEnumerable<CodeInstruction> RuntimeTranspiler(
	IEnumerable<CodeInstruction> instructions,
	ILGenerator ilGenerator
);

/// <summary>
/// Transpiler 工厂：为高级注入语义（ModifyArg / ModifyConstant / Redirect）生成 IL 操作方法。
/// </summary>
/// <remarks>
/// Transpiler 是 Harmony 提供的强大工具，允许直接修改方法的 IL 中间代码。
/// 此工厂生成用于以下场景的 Transpiler：
/// • ModifyArg：修改方法参数
/// • ModifyConstant：修改常量值
/// • Redirect：重新路由方法调用
/// 
/// 每个工厂方法生成一个 <see cref="RuntimeTranspiler"/> 委托，接受原 IL 指令并返回修改后的指令。
/// </remarks>
internal sealed class TranspilerFactory
{
	// ═══════════════════════════════════════════════════════════════════════════════════════
	// ModifyArg：在调用目标方法前修改指定参数
	// ═══════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 创建 ModifyArg 类型的 Transpiler，在目标调用点前插入对 handler 的调用来修改参数。
	/// </summary>
	/// <remarks>
	/// 流程：
	/// 1. 扫描 IL 中的参数加载指令（如 ldarg.0）
	/// 2. 在加载参数后插入 handler 调用
	/// 3. handler 接收原参数值并返回修改后的值
	/// 4. 原始参数值被替换为 handler 的返回值
	/// 
	/// 这种方式允许在方法调用前动态修改参数。
	/// </remarks>
	/// <param name="targetCallSite">要拦截的目标调用（若指定）。若为 null，则拦截所有匹配参数索引的加载。</param>
	/// <param name="argumentIndex">要修改的参数索引（0-based，this 算作 0）。</param>
	/// <param name="handlerMethod">处理方法，接收参数原值并返回修改值。签名应为 `ParamType(ParamType original)`。</param>
	/// <param name="ordinal">匹配序号。-1 表示修改所有匹配的参数；非负值表示只修改第 N 次匹配。</param>
	/// <returns>产生修改后 IL 指令序列的 Transpiler。</returns>
	public static RuntimeTranspiler CreateModifyArgTranspiler(
		MethodBase? targetCallSite,
		int argumentIndex,
		MethodInfo handlerMethod,
		int ordinal = -1)
	{
		return (instructions, ilGenerator) =>
		{
			List<CodeInstruction> codes = instructions.ToList();
			int matchCount = 0;

			for (int i = 0; i < codes.Count; i++)
			{
				CodeInstruction code = codes[i];

				// 定位目标调用点
				if (!IsCallInstruction(code))
				{
					continue;
				}

				MethodBase? calledMethod = code.operand as MethodBase;
				if (calledMethod == null)
				{
					continue;
				}

				// 如果指定了 targetCallSite，则只匹配该方法
				if (targetCallSite != null && !MethodMatches(calledMethod, targetCallSite))
				{
					continue;
				}

				ParameterInfo[] parameters = calledMethod.GetParameters();
				if (argumentIndex < 0 || argumentIndex >= parameters.Length)
				{
					continue;
				}

				if (parameters[argumentIndex].ParameterType.IsByRef)
				{
					continue;
				}

				if (ordinal >= 0 && matchCount != ordinal)
				{
					matchCount++;
					continue;
				}

				matchCount++;
				List<CodeInstruction> rewritten = BuildModifyArgCallRewrite(
					originalCall: code,
					calledMethod,
					argumentIndex,
					handlerMethod,
					ilGenerator
				);

				if (rewritten.Count == 0)
				{
					continue;
				}

				codes.RemoveAt(i);
				codes.InsertRange(i, rewritten);
				i += rewritten.Count - 1;
			}

			return codes;
		};
	}

	private static List<CodeInstruction> BuildModifyArgCallRewrite(
		CodeInstruction originalCall,
		MethodBase calledMethod,
		int argumentIndex,
		MethodInfo handlerMethod,
		ILGenerator ilGenerator)
	{
		ParameterInfo[] parameters = calledMethod.GetParameters();
		bool hasInstance = calledMethod is MethodInfo methodInfo && !methodInfo.IsStatic;

		if (hasInstance && calledMethod.DeclaringType != null && calledMethod.DeclaringType.IsValueType)
		{
			return new List<CodeInstruction>();
		}

		List<LocalBuilder> argumentLocals = new(parameters.Length);
		for (int i = 0; i < parameters.Length; i++)
		{
			argumentLocals.Add(ilGenerator.DeclareLocal(parameters[i].ParameterType));
		}

		LocalBuilder? instanceLocal = null;
		if (hasInstance)
		{
			instanceLocal = ilGenerator.DeclareLocal(calledMethod.DeclaringType ?? typeof(object));
		}

		List<CodeInstruction> replacement = new();
		for (int i = parameters.Length - 1; i >= 0; i--)
		{
			replacement.Add(new CodeInstruction(OpCodes.Stloc, argumentLocals[i]));
		}

		if (instanceLocal != null)
		{
			replacement.Add(new CodeInstruction(OpCodes.Stloc, instanceLocal));
			replacement.Add(new CodeInstruction(OpCodes.Ldloc, instanceLocal));
		}

		for (int i = 0; i < parameters.Length; i++)
		{
			replacement.Add(new CodeInstruction(OpCodes.Ldloc, argumentLocals[i]));
			if (i == argumentIndex)
			{
				replacement.Add(new CodeInstruction(OpCodes.Call, handlerMethod));
			}
		}

		replacement.Add(new CodeInstruction(originalCall.opcode, calledMethod));

		if (replacement.Count > 0)
		{
			replacement[0].labels.AddRange(originalCall.labels);
			replacement[0].blocks.AddRange(originalCall.blocks);
		}

		return replacement;
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════
	// ModifyConstant：修改方法中的常量值
	// ═══════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 生成 ModifyConstant Transpiler，替换匹配的常量加载指令。
	/// </summary>
	/// <param name="constantExpression">常量匹配表达式（如 "42"、"true"、"MyText"）</param>
	/// <param name="handlerMethod">处理方法：接收原常量值，返回修改后的值</param>
	/// <param name="ordinal">匹配序号，-1 表示全部匹配</param>
	public static RuntimeTranspiler CreateModifyConstantTranspiler(
		string constantExpression,
		MethodInfo handlerMethod,
		int ordinal = -1,
		bool requireMatch = true)
	{
		return (instructions, _) =>
		{
			List<CodeInstruction> codes = instructions.ToList();
			int matchCount = 0;
			int appliedCount = 0;
			object? targetConstant = ParseConstantExpression(constantExpression, handlerMethod.ReturnType);

			for (int i = 0; i < codes.Count; i++)
			{
				CodeInstruction code = codes[i];

				if (!TryGetConstantValue(code, out object? constantValue, out Type? constantType))
				{
					continue;
				}

				// 匹配常量值
				if (!ConstantMatches(constantValue, targetConstant, constantExpression))
				{
					continue;
				}

				// 检查 ordinal
				if (ordinal >= 0 && matchCount != ordinal)
				{
					matchCount++;
					continue;
				}

				matchCount++;

				// 替换常量加载为：原常量加载 + handler 调用
				// 保留原指令（加载常量），在其后插入 handler 调用
				CodeInstruction callHandler = new(OpCodes.Call, handlerMethod);
				codes.Insert(i + 1, callHandler);
				i++; // 跳过新插入的指令
				appliedCount++;
			}

			if (requireMatch && appliedCount == 0)
			{
				throw new InvalidOperationException(
					$"ModifyConstant did not match any constants. expression='{constantExpression}', ordinal={ordinal}, handler='{handlerMethod.DeclaringType?.FullName}.{handlerMethod.Name}'."
				);
			}

			return codes;
		};
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════
	// Redirect：将方法调用重定向到另一个方法
	// ═══════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 生成 Redirect Transpiler，将目标方法调用替换为 handler 调用。
	/// </summary>
	/// <param name="targetCallSite">要重定向的目标方法</param>
	/// <param name="handlerMethod">替换方法：签名应与原方法兼容</param>
	/// <param name="at">调用点标识（如 "INVOKE:MethodName"）</param>
	/// <param name="ordinal">匹配序号，-1 表示全部匹配</param>
	public static RuntimeTranspiler CreateRedirectTranspiler(
		MethodBase targetCallSite,
		MethodInfo handlerMethod,
		string at,
		int ordinal = -1)
	{
		return (instructions, _) =>
		{
			List<CodeInstruction> codes = instructions.ToList();
			int matchCount = 0;

			// 解析 at 标识
			RedirectTarget? redirectTarget = ParseAtExpression(at, targetCallSite);

			for (int i = 0; i < codes.Count; i++)
			{
				CodeInstruction code = codes[i];

				if (!IsCallInstruction(code))
				{
					continue;
				}

				MethodBase? calledMethod = code.operand as MethodBase;
				if (calledMethod == null)
				{
					continue;
				}

				// 匹配目标调用
				if (!MatchesRedirectTarget(calledMethod, redirectTarget, targetCallSite))
				{
					continue;
				}

				// 检查 ordinal
				if (ordinal >= 0 && matchCount != ordinal)
				{
					matchCount++;
					continue;
				}

				matchCount++;

				// 替换调用目标
				code.operand = handlerMethod;

				// 如果原调用是 callvirt 但 handler 是 static，需要改为 call
				if (code.opcode == OpCodes.Callvirt && handlerMethod.IsStatic)
				{
					code.opcode = OpCodes.Call;
				}
			}

			return codes;
		};
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════
	// Overwrite：完全替换目标方法体
	// ═══════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 生成 Overwrite Prefix，通过 prefix + return false 跳过原方法。
	/// </summary>
	/// <param name="targetMethod">目标方法</param>
	/// <param name="handlerMethod">替换方法：签名应完全匹配</param>
	/// <returns>动态生成的 Prefix 方法</returns>
	public static MethodInfo CreateOverwritePrefix(MethodBase targetMethod, MethodInfo handlerMethod)
	{
		// Overwrite 使用 Prefix + return false 实现
		// 动态生成一个 Prefix 方法，调用 handler 并设置 __result
		Type[] paramTypes = BuildOverwritePrefixParameters(targetMethod);
		DynamicMethod prefix = new(
			$"Overwrite_Prefix_{targetMethod.Name}_{Guid.NewGuid():N}",
			typeof(bool), // 返回 bool 控制是否执行原方法
			paramTypes,
			typeof(TranspilerFactory).Module,
			skipVisibility: true
		);

		ILGenerator il = prefix.GetILGenerator();

		// 调用 handler 方法
		EmitHandlerCall(il, targetMethod, handlerMethod, paramTypes);

		// 如果目标方法有返回值，设置 __result
		if (targetMethod is MethodInfo mi && mi.ReturnType != typeof(void))
		{
			// __result 是 ref 参数，位于参数列表末尾
			int resultIndex = paramTypes.Length - 1;
			il.Emit(OpCodes.Ldarg, resultIndex);
			// handler 返回值已在栈上
			EmitStoreIndirect(il, mi.ReturnType);
		}
		else
		{
			// void 方法，handler 可能也返回 void，需要 pop 如果有返回值
			if (handlerMethod.ReturnType != typeof(void))
			{
				il.Emit(OpCodes.Pop);
			}
		}

		// return false 跳过原方法
		il.Emit(OpCodes.Ldc_I4_0);
		il.Emit(OpCodes.Ret);

		return prefix;
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════
	// 辅助方法
	// ═══════════════════════════════════════════════════════════════════════════════════════

	private static bool IsCallInstruction(CodeInstruction code)
	{
		return code.opcode == OpCodes.Call
			|| code.opcode == OpCodes.Callvirt
			|| code.opcode == OpCodes.Calli;
	}

	private static bool MethodMatches(MethodBase a, MethodBase b)
	{
		if (a.Name != b.Name)
		{
			return false;
		}

		if (a.DeclaringType != b.DeclaringType)
		{
			return false;
		}

		ParameterInfo[] paramsA = a.GetParameters();
		ParameterInfo[] paramsB = b.GetParameters();
		if (paramsA.Length != paramsB.Length)
		{
			return false;
		}

		for (int i = 0; i < paramsA.Length; i++)
		{
			if (paramsA[i].ParameterType != paramsB[i].ParameterType)
			{
				return false;
			}
		}

		return true;
	}


	private static bool TryGetConstantValue(CodeInstruction code, out object? value, out Type? type)
	{
		value = null;
		type = null;
		OpCode op = code.opcode;

		// 整数常量
		if (op == OpCodes.Ldc_I4)
		{
			value = (int)code.operand;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_S)
		{
			value = (int)(sbyte)code.operand;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_0)
		{
			value = 0;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_1)
		{
			value = 1;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_2)
		{
			value = 2;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_3)
		{
			value = 3;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_4)
		{
			value = 4;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_5)
		{
			value = 5;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_6)
		{
			value = 6;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_7)
		{
			value = 7;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_8)
		{
			value = 8;
			type = typeof(int);
			return true;
		}

		if (op == OpCodes.Ldc_I4_M1)
		{
			value = -1;
			type = typeof(int);
			return true;
		}

		// 长整数常量
		if (op == OpCodes.Ldc_I8)
		{
			value = (long)code.operand;
			type = typeof(long);
			return true;
		}

		// 浮点常量
		if (op == OpCodes.Ldc_R4)
		{
			value = (float)code.operand;
			type = typeof(float);
			return true;
		}

		if (op == OpCodes.Ldc_R8)
		{
			value = (double)code.operand;
			type = typeof(double);
			return true;
		}

		// 字符串常量
		if (op == OpCodes.Ldstr)
		{
			value = (string)code.operand;
			type = typeof(string);
			return true;
		}

		// null 常量
		if (op == OpCodes.Ldnull)
		{
			value = null;
			type = typeof(object);
			return true;
		}

		return false;
	}

	private static object? ParseConstantExpression(string expression, Type targetType)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			return null;
		}

		string trimmed = expression.Trim();

		// 布尔
		if (targetType == typeof(bool) || targetType == typeof(int))
		{
			if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				return targetType == typeof(bool) ? true : 1;
			}

			if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				return targetType == typeof(bool) ? false : 0;
			}
		}

		// 整数
		if (int.TryParse(trimmed, out int intVal))
		{
			return intVal;
		}

		// 长整数
		if (long.TryParse(trimmed, out long longVal))
		{
			return longVal;
		}

		// 浮点
		if (float.TryParse(trimmed, out float floatVal))
		{
			return floatVal;
		}

		if (double.TryParse(trimmed, out double doubleVal))
		{
			return doubleVal;
		}

		// 字符串（去除引号）
		if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
		{
			return trimmed[1..^1];
		}

		// 原样返回作为字符串匹配
		return trimmed;
	}

	private static bool ConstantMatches(object? actual, object? target, string expression)
	{
		if (target == null && actual == null)
		{
			return true;
		}

		if (target == null || actual == null)
		{
			return false;
		}

		// 直接比较
		if (actual.Equals(target))
		{
			return true;
		}

		// 字符串包含匹配
		if (actual is string actualStr && target is string targetStr)
		{
			return actualStr.Contains(targetStr, StringComparison.Ordinal);
		}

		// 数值转换比较
		try
		{
			double actualNum = Convert.ToDouble(actual);
			double targetNum = Convert.ToDouble(target);
			return Math.Abs(actualNum - targetNum) < 0.0001;
		}
		catch
		{
			return false;
		}
	}

	private sealed record RedirectTarget(string? MethodName, Type? DeclaringType);

	private static RedirectTarget? ParseAtExpression(string at, MethodBase fallback)
	{
		if (string.IsNullOrWhiteSpace(at))
		{
			return new RedirectTarget(fallback.Name, fallback.DeclaringType);
		}

		string trimmed = at.Trim();

		// 格式：INVOKE:MethodName 或 INVOKE:Type.MethodName
		if (trimmed.StartsWith("INVOKE:", StringComparison.OrdinalIgnoreCase))
		{
			string target = trimmed[7..].Trim();
			int dotIndex = target.LastIndexOf('.');
			if (dotIndex > 0)
			{
				string typeName = target[..dotIndex];
				string methodName = target[(dotIndex + 1)..];
				Type? type = Type.GetType(typeName);
				return new RedirectTarget(methodName, type);
			}

			return new RedirectTarget(target, null);
		}

		// 直接方法名
		return new RedirectTarget(trimmed, null);
	}

	private static bool MatchesRedirectTarget(MethodBase method, RedirectTarget? target, MethodBase fallback)
	{
		if (target == null)
		{
			return MethodMatches(method, fallback);
		}

		if (target.MethodName != null && method.Name != target.MethodName)
		{
			return false;
		}

		if (target.DeclaringType != null && method.DeclaringType != target.DeclaringType)
		{
			return false;
		}

		return true;
	}

	private static Type[] BuildOverwritePrefixParameters(MethodBase targetMethod)
	{
		List<Type> types = new();

		// 实例方法需要 __instance
		if (!targetMethod.IsStatic)
		{
			types.Add(targetMethod.DeclaringType ?? typeof(object));
		}

		// 原方法参数
		foreach (ParameterInfo param in targetMethod.GetParameters())
		{
			types.Add(param.ParameterType);
		}

		// 如果有返回值，添加 ref __result
		if (targetMethod is MethodInfo mi && mi.ReturnType != typeof(void))
		{
			types.Add(mi.ReturnType.MakeByRefType());
		}

		return types.ToArray();
	}

	private static void EmitHandlerCall(ILGenerator il, MethodBase targetMethod, MethodInfo handlerMethod, Type[] prefixParams)
	{
		ParameterInfo[] handlerParams = handlerMethod.GetParameters();
		int argIndex = 0;

		// 加载 handler 所需的参数
		for (int i = 0; i < handlerParams.Length && argIndex < prefixParams.Length; i++)
		{
			il.Emit(OpCodes.Ldarg, argIndex);

			// 如果是 ref 参数但 handler 不需要 ref，需要解引用
			if (prefixParams[argIndex].IsByRef && !handlerParams[i].ParameterType.IsByRef)
			{
				EmitLoadIndirect(il, handlerParams[i].ParameterType);
			}

			argIndex++;
		}

		il.Emit(OpCodes.Call, handlerMethod);
	}

	private static void EmitLoadIndirect(ILGenerator il, Type type)
	{
		if (type == typeof(int) || type == typeof(uint))
		{
			il.Emit(OpCodes.Ldind_I4);
		}
		else if (type == typeof(long) || type == typeof(ulong))
		{
			il.Emit(OpCodes.Ldind_I8);
		}
		else if (type == typeof(float))
		{
			il.Emit(OpCodes.Ldind_R4);
		}
		else if (type == typeof(double))
		{
			il.Emit(OpCodes.Ldind_R8);
		}
		else if (type == typeof(short) || type == typeof(ushort))
		{
			il.Emit(OpCodes.Ldind_I2);
		}
		else if (type == typeof(byte) || type == typeof(sbyte))
		{
			il.Emit(OpCodes.Ldind_I1);
		}
		else if (type == typeof(bool))
		{
			il.Emit(OpCodes.Ldind_I1);
		}
		else if (type.IsValueType)
		{
			il.Emit(OpCodes.Ldobj, type);
		}
		else
		{
			il.Emit(OpCodes.Ldind_Ref);
		}
	}

	private static void EmitStoreIndirect(ILGenerator il, Type type)
	{
		if (type == typeof(int) || type == typeof(uint))
		{
			il.Emit(OpCodes.Stind_I4);
		}
		else if (type == typeof(long) || type == typeof(ulong))
		{
			il.Emit(OpCodes.Stind_I8);
		}
		else if (type == typeof(float))
		{
			il.Emit(OpCodes.Stind_R4);
		}
		else if (type == typeof(double))
		{
			il.Emit(OpCodes.Stind_R8);
		}
		else if (type == typeof(short) || type == typeof(ushort))
		{
			il.Emit(OpCodes.Stind_I2);
		}
		else if (type == typeof(byte) || type == typeof(sbyte))
		{
			il.Emit(OpCodes.Stind_I1);
		}
		else if (type == typeof(bool))
		{
			il.Emit(OpCodes.Stind_I1);
		}
		else if (type.IsValueType)
		{
			il.Emit(OpCodes.Stobj, type);
		}
		else
		{
			il.Emit(OpCodes.Stind_Ref);
		}
	}
}
