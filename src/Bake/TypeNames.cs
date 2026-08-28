using System;
using System.Collections.Generic;
using System.Reflection;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// A declared "type" name -> a real Type, looked for in EVERY assembly the process has loaded.
    ///
    /// WHY NOT typeof(UnityEngine.Object).Assembly.GetType(...), which is what this used to be. Unity
    /// splits its classes across module assemblies: UnityEngine.Object lives in
    /// UnityEngine.CoreModule.dll, AnimationClip in UnityEngine.AnimationModule.dll, AudioClip in
    /// UnityEngine.AudioModule.dll, Sprite in UnityEngine.CoreModule.dll - 63 UnityEngine*.dll in
    /// D:\PP-Instance2\PhoenixPointWin64_Data\Managed, counted 2026-08-27. Asking ONE of them meant
    /// "AnimationClip" - a type the refusal text itself offers as an example - was refused as "not a
    /// type this game has". Type.GetType is no substitute: with no assembly qualifier it searches
    /// mscorlib and the calling assembly only, so the fully-spelled "UnityEngine.AnimationClip"
    /// failed too.
    ///
    /// An unknown name is still refused BY NAME. This widens where we look; it never guesses.
    ///
    /// Free of UnityEngine types on purpose (System.Reflection only), so the rule is gated offline.
    /// </summary>
    internal static class TypeNames
    {
        /// <summary>A bare name is a class in <paramref name="ns"/>, a dotted one is spelled in full;
        /// either way the answer is the first of <paramref name="assemblies"/> that has it.</summary>
        internal static Type Resolve(string name, string ns, IEnumerable<Assembly> assemblies)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string full = name.IndexOf('.') >= 0 ? name : ns + "." + name;
            Type t = Type.GetType(full, false);
            if (t != null) return t;
            if (assemblies == null) return null;
            foreach (Assembly a in assemblies)
            {
                if (a == null) continue;
                // A dynamic or half-loadable assembly is not an answer, it is not a reason to stop.
                try { t = a.GetType(full, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Live: everything Phoenix Point has loaded, Unity's modules included.</summary>
        internal static Type Resolve(string name, string ns)
        {
            return Resolve(name, ns, AppDomain.CurrentDomain.GetAssemblies());
        }
    }
}
