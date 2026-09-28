using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// WHO SAVES A DESTROYED OBJECT. The game's only word for it is
    /// "Serializing destroyed unity object at: List`1." (SerializationComponent.cs:100-110) - the
    /// containing TYPE, never the path from the save root, so "which list" is unanswerable from the
    /// log. This keeps the path: a prefix on SerializationType.WriteMembers pushes the object being
    /// written, a finalizer pops it, and a postfix on OnValidateSerializedObject prints the whole
    /// chain plus the destroyed object's own type whenever the game's check would fire.
    ///
    /// DEV ONLY - installed at enable only while the ct-dev marker is present (DevGate). Read-only:
    /// nothing is changed, every hook swallows its own exceptions.
    /// </summary>
    internal static class SaveProbe
    {
        private const string HarmonyId = "morgott.contenttool.saveprobe";
        private static Harmony harmony;
        [ThreadStatic] private static List<string> path;
        private static int reported;

        internal static string Install()
        {
            if (harmony != null) return "ct_saveprobe: already installed";
            Type serializationType = AccessTools.TypeByName("Base.Serialization.General.SerializationType");
            Type comp = AccessTools.TypeByName("Base.Serialization.SerializationComponent");
            MethodInfo write = serializationType == null ? null : AccessTools.Method(serializationType, "WriteMembers");
            MethodInfo validate = comp == null ? null : AccessTools.Method(comp, "OnValidateSerializedObject");
            if (write == null || validate == null)
                return "ct_saveprobe VOID: WriteMembers=" + (write != null) + " OnValidateSerializedObject=" + (validate != null);
            harmony = new Harmony(HarmonyId);
            harmony.Patch(write, prefix: new HarmonyMethod(AccessTools.Method(typeof(SaveProbe), nameof(Push))),
                          finalizer: new HarmonyMethod(AccessTools.Method(typeof(SaveProbe), nameof(Pop))));
            harmony.Patch(validate, postfix: new HarmonyMethod(AccessTools.Method(typeof(SaveProbe), nameof(Seen))));
            return "ct_saveprobe: armed (dev) - a destroyed Unity object in a save prints its full write path";
        }

        private static void Push(object o)
        {
            try
            {
                if (path == null) path = new List<string>();
                path.Add(Describe(o));
            }
            catch (Exception) { }
        }

        private static Exception Pop(Exception __exception)
        {
            try { if (path != null && path.Count > 0) path.RemoveAt(path.Count - 1); }
            catch (Exception) { }
            return __exception;
        }

        private static void Seen(object obj, Type containingType, MemberInfo containingMember)
        {
            try
            {
                if (obj == null) return;
                if (!typeof(UnityEngine.Object).IsAssignableFrom(obj.GetType())) return;
                if ((obj as UnityEngine.Object) != null) return;
                if (reported++ >= 20) return;
                StringBuilder b = new StringBuilder();
                b.Append("ct_saveprobe DESTROYED ").Append(obj.GetType().FullName)
                 .Append(" id=").Append(((UnityEngine.Object)obj).GetInstanceID())
                 .Append(" at ").Append(containingType?.Name).Append('.').Append(containingMember?.Name)
                 .Append("\n  write path (root first):");
                if (path != null)
                    foreach (string p in path) b.Append("\n    ").Append(p);
                // Who started the write: the frames below the serializer's own recursion.
                b.Append("\n  caller frames:");
                int shown = 0;
                foreach (System.Diagnostics.StackFrame f in new System.Diagnostics.StackTrace(1).GetFrames() ?? new System.Diagnostics.StackFrame[0])
                {
                    MethodBase m = f.GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    string ns = m.DeclaringType.Namespace ?? "";
                    if (ns.StartsWith("Base.Serialization.General") || m.DeclaringType == typeof(SaveProbe)) continue;
                    b.Append("\n    ").Append(m.DeclaringType.FullName).Append('.').Append(m.Name);
                    if (++shown >= 14) break;
                }
                ContentToolMain.Say(b.ToString());
            }
            catch (Exception) { }
        }

        private static string Describe(object o)
        {
            if (o == null) return "(null)";
            string s = o.GetType().Name;
            if (o is UnityEngine.Object u)
                s += u != null ? " '" + u.name + "'" : " (DESTROYED)";
            return s;
        }
    }
}
