using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Celeste.Mod.AudioSplitter.Module;

namespace Celeste.Mod.AudioSplitter.Utility
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HookAttribute : Attribute
    {
        public static void Invoke(Type attribute)
        {
            var methods = AppDomain.CurrentDomain.GetAssemblies()
                // Could not load type 'OptionValue' from assembly 'Steamworks.NET, Version=2024.8.0.0, Culture=neutral, PublicKeyToken=null' because it contains an object field at offset 0 that is incorrectly aligned or overlapped by a non-object field.
                .Where(a => a.FullName != "Steamworks.NET, Version=2024.8.0.0, Culture=neutral, PublicKeyToken=null")
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass)
                .SelectMany(t => t.GetMethods());

            // Warn about instance methods that has an attribute
            var instanceMethods = methods
                .Where(m => !m.IsStatic)
                .Where(m => m.GetCustomAttributes(attribute).Any());
            if (instanceMethods.Any())
            {
                foreach (var method in instanceMethods)
                {
                    Logger.Warn(nameof(AudioSplitterModule), 
                                $"Hook {method.GetType().Name}.{method.Name} of attribute {attribute.Name} is non-static and won't be applied! Fix ASAP!");
                }
            }

            var staticMethods = methods
                .Where(m => m.IsStatic)
                .Where(m => m.GetCustomAttributes(attribute).Any());
            foreach (var method in staticMethods)
            {
                method.Invoke(null, null);
            }
        }
    }

    public class ApplyOnLoadAttribute : HookAttribute { }
    public class RemoveOnUnloadAttribute : HookAttribute { }
}
