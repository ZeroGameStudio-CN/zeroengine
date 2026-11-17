using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    public static class ConsoleMethodBinding
    {
        public static ConsoleBinding CreateGeneric(string id, string name, string module, MethodInfo definition,
            Type[] typeArguments, object target = null, string description = "",
            ConsoleEnvironment environments = ConsoleEnvironment.Development, IEnumerable<string> capabilities = null)
        {
            if (definition == null || !definition.IsGenericMethodDefinition)
                throw new ArgumentException("An explicit generic method definition is required.", nameof(definition));
            if (typeArguments == null) throw new ArgumentNullException(nameof(typeArguments));
            foreach (var type in typeArguments)
                if (type == null || type.ContainsGenericParameters) throw new ArgumentException("Use closed type arguments.", nameof(typeArguments));
            return Create(id, name, module, definition.MakeGenericMethod(typeArguments), target, description, environments, capabilities);
        }

        public static ConsoleBinding Create(string id, string name, string module, MethodInfo method,
            object target = null, string description = "", ConsoleEnvironment environments = ConsoleEnvironment.Development,
            IEnumerable<string> capabilities = null)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (method.ContainsGenericParameters) throw new ArgumentException("Close generic methods explicitly before binding.", nameof(method));
            if (method.ReturnType == typeof(void) && method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                throw new ArgumentException("Async void methods require an explicit completion adapter.", nameof(method));
            bool valueTask = method.ReturnType == typeof(ValueTask) || method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>);
            if (!valueTask && !typeof(Task).IsAssignableFrom(method.ReturnType) && method.ReturnType.GetMethod("GetAwaiter", Type.EmptyTypes) != null)
                throw new ArgumentException("Custom awaitables require an explicit outcome adapter.", nameof(method));
            if (!method.IsStatic && (target == null || !method.DeclaringType.IsInstanceOfType(target)))
                throw new ArgumentException("An instance method requires its declared target.", nameof(target));
            var reflected = method.GetParameters();
            var visible = new List<ConsoleParameter>();
            var indices = new int[reflected.Length];
            for (int i = 0; i < reflected.Length; i++)
            {
                var parameter = reflected[i];
                if (parameter.ParameterType.IsByRef || parameter.ParameterType.IsPointer)
                    throw new ArgumentException("By-reference and pointer parameters require an explicit binding.", nameof(method));
                if (parameter.ParameterType == typeof(CancellationToken)) { indices[i] = -1; continue; }
                indices[i] = visible.Count;
                bool variadic = parameter.IsDefined(typeof(ParamArrayAttribute), false);
                visible.Add(new ConsoleParameter(parameter.Name, parameter.ParameterType, optional: parameter.IsOptional,
                    defaultValue: parameter.IsOptional ? parameter.DefaultValue : null, variadic: variadic));
            }
            MethodInfo asTask = valueTask ? method.ReturnType.GetMethod("AsTask", Type.EmptyTypes) : null;
            Type taskType = asTask?.ReturnType ?? method.ReturnType;
            PropertyInfo taskResult = typeof(Task).IsAssignableFrom(taskType)
                ? taskType.GetProperty("Result", BindingFlags.Instance | BindingFlags.Public) : null;
            var command = new ConsoleCommand(id, name, module, description, visible, environments: environments, capabilities: capabilities);
            return new ConsoleBinding(command, async (context, arguments, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                var supplied = new object[reflected.Length];
                for (int i = 0; i < supplied.Length; i++) supplied[i] = indices[i] < 0 ? cancellation : arguments.Get<object>(indices[i]);
                object value;
                try { value = method.Invoke(method.IsStatic ? null : target, supplied); }
                catch (TargetInvocationException failure) when (failure.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
                    throw;
                }
                if (asTask != null)
                {
                    try { value = asTask.Invoke(value, null); }
                    catch (TargetInvocationException failure) when (failure.InnerException != null)
                    { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
                }
                if (value is Task pending)
                {
                    await pending;
                    value = taskResult?.GetValue(pending);
                    if (taskResult == null) return ConsoleResult.Unconfirmed("Method completed without a declared business result.");
                }
                if (value is ConsoleResult result) return result;
                if (method.ReturnType == typeof(void)) return ConsoleResult.Unconfirmed("Method returned without a declared business result.");
                return ConsoleResult.Success(data: value);
            });
        }
    }
}
