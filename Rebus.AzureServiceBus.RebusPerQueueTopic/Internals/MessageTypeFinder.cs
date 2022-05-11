using System;
using System.Linq;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Internals
{
    internal static class MessageTypeFinder
    {
        private static readonly Lazy<Type[]> AllTypes = new(() =>
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(x=> !x.IsDynamic)
                .ToList();
            var result = assemblies
                .SelectMany(x => x.GetTypes())
                .ToArray();

            var excludedNamespaces = new[]
            {
                "System",
                "Serilog",
                "Internal",
                "Azure",
                "System",
                "Microsoft",
                "Newtonsoft",
                "Rebus",
                "Elasticsearch",
                "Interop",
                "Swashbuckle"
            };
            //public/protected olanlar geliyor sadece..
            result = result.Where(x => x.IsVisible)
                .ToArray();
            result = result
                .Where(x => excludedNamespaces.All(n => x.Namespace?.StartsWith(n) == false))
                .ToArray();
            return result;
        });

        public static Type FindType(string parsedType)
        {
            var result = AllTypes.Value.FirstOrDefault(x => x.Name.Equals(parsedType, StringComparison.InvariantCultureIgnoreCase));
            return result;
        }
    }
}