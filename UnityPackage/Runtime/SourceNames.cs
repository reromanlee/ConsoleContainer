using System;
using System.Collections.Concurrent;
using System.Text;

namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Turns whatever a caller passes as a message source into a readable name: a string as is, a
    /// <see cref="Type"/> by its own name (so static classes can pass <c>typeof(...)</c>), and any
    /// other object by its type's name. Generic types read as <c>List&lt;Int32&gt;</c> instead of
    /// <c>List`1</c>. Names are cached per type, so resolving a source allocates nothing after the
    /// first time a type is seen.
    /// </summary>
    internal static class SourceNames
    {
        private static readonly ConcurrentDictionary<Type, string> Cache = new ConcurrentDictionary<Type, string>();

        // Cached delegate: a method group passed to GetOrAdd would allocate on every call.
        private static readonly Func<Type, string> Formatter = Format;

        internal static string Resolve(object source)
        {
            switch (source)
            {
                case null:
                    return null;
                case string text:
                    return text;
                case Type type:
                    return Of(type);
                default:
                    return Of(source.GetType());
            }
        }

        internal static string Of(Type type) => Cache.GetOrAdd(type, Formatter);

        private static string Format(Type type)
        {
            string name = type.Name;
            if (!type.IsGenericType)
            {
                return name;
            }

            int arityMarker = name.IndexOf('`');
            if (arityMarker > 0)
            {
                name = name.Substring(0, arityMarker);
            }

            Type[] arguments = type.GetGenericArguments();
            StringBuilder builder = new StringBuilder(name).Append('<');
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(Of(arguments[i]));
            }

            return builder.Append('>').ToString();
        }
    }
}
