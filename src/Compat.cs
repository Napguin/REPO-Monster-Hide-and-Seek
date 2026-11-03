using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Version-tolerant access to game members. R.E.P.O. updates change method signatures
    /// (e.g. v0.5.0 changed PlayerController.OverrideSpeed). A direct call to a changed method
    /// makes the whole calling method fail, so everything that may change goes through here:
    /// members are looked up by name, arguments are matched loosely and missing optional
    /// parameters get defaults. Failures are logged once and return false/default.
    /// </summary>
    internal static class Compat
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, MethodInfo[]> MethodCache = new Dictionary<string, MethodInfo[]>();
        private static readonly Dictionary<string, MemberInfo> MemberCache = new Dictionary<string, MemberInfo>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static void WarnOnce(string key, string msg)
        {
            if (Warned.Add(key)) Plugin.Log.LogWarning("[Compat] " + msg);
        }

        /// <summary>Calls the best matching method named <paramref name="name"/>.</summary>
        public static bool Call(object target, string name, params object[] args) => Call(target, name, out _, args);

        public static bool Call(object target, string name, out object result, params object[] args)
        {
            result = null;
            if (target == null) return false;
            var type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            string key = type.FullName + "." + name;

            if (!MethodCache.TryGetValue(key, out var methods))
            {
                methods = type.GetMethods(Flags).Where(m => m.Name == name).OrderBy(m => m.GetParameters().Length).ToArray();
                MethodCache[key] = methods;
                if (methods.Length == 0) WarnOnce(key, $"method {key} not found in this game version");
            }

            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                var call = new object[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (i < args.Length)
                    {
                        var a = args[i];
                        if (a == null) { call[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null; }
                        else if (pt.IsInstanceOfType(a)) call[i] = a;
                        else
                        {
                            try { call[i] = Convert.ChangeType(a, pt); }
                            catch { ok = false; }
                        }
                    }
                    else if (ps[i].HasDefaultValue) call[i] = ps[i].DefaultValue;
                    else call[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                if (!ok) continue;
                try
                {
                    result = m.Invoke(m.IsStatic ? null : instance, call);
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    WarnOnce(key + "#invoke", $"{key} threw: {ex.InnerException?.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    WarnOnce(key + "#invoke2", $"{key} call failed: {ex.Message}");
                    return false;
                }
            }
            if (methods.Length > 0)
                WarnOnce(key + "#sig", $"{key}: no overload fits ({string.Join(" | ", methods.Select(Sig))})");
            return false;
        }

        private static string Sig(MethodInfo m) => m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")";

        private static MemberInfo FindMember(Type type, string name)
        {
            string key = type.FullName + ":" + name;
            if (MemberCache.TryGetValue(key, out var mi)) return mi;
            mi = (MemberInfo)type.GetField(name, Flags) ?? type.GetProperty(name, Flags);
            MemberCache[key] = mi;
            if (mi == null) WarnOnce(key, $"member {type.FullName}.{name} not found in this game version");
            return mi;
        }

        public static T Get<T>(object target, string name, T fallback = default)
        {
            if (target == null) return fallback;
            var type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            var mi = FindMember(type, name);
            try
            {
                object v = mi is FieldInfo f ? f.GetValue(instance) : mi is PropertyInfo p ? p.GetValue(instance) : null;
                if (v is T t) return t;
                if (v != null && typeof(T).IsPrimitive) return (T)Convert.ChangeType(v, typeof(T));
            }
            catch { /* ignore */ }
            return fallback;
        }

        public static bool Set(object target, string name, object value)
        {
            if (target == null) return false;
            var type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            var mi = FindMember(type, name);
            try
            {
                if (mi is FieldInfo f) { f.SetValue(instance, Convert.ChangeType(value, f.FieldType)); return true; }
                if (mi is PropertyInfo p && p.CanWrite) { p.SetValue(instance, Convert.ChangeType(value, p.PropertyType)); return true; }
            }
            catch (Exception ex) { WarnOnce(type.FullName + ":" + name + "#set", $"set {name} failed: {ex.Message}"); }
            return false;
        }

        /// <summary>Static field/property of a game type, looked up by type name (no compile-time reference).</summary>
        public static object Static(string typeName, string member)
        {
            var t = FindType(typeName);
            return t == null ? null : Get<object>(t, member);
        }

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        public static Type FindType(string name)
        {
            if (TypeCache.TryGetValue(name, out var t)) return t;
            t = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType(name, false))
                .FirstOrDefault(x => x != null);
            TypeCache[name] = t;
            if (t == null) WarnOnce("type:" + name, $"type {name} not found in this game version");
            return t;
        }
    }
}
