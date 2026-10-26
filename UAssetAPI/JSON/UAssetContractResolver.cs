using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.JSON
{
    internal class UAssetContractResolver : DefaultContractResolver
    {
        public Dictionary<FName, string> ToBeFilled;

        protected override JsonConverter ResolveContractConverter(Type objectType)
        {
            if (typeof(FName).IsAssignableFrom(objectType))
            {
                return new FNameJsonConverter(ToBeFilled);
            }
            return base.ResolveContractConverter(objectType);
        }

#if RCT_NATIVE_BRIDGE
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AotContract.Json)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = AotContract.Json)]
#endif
        public UAssetContractResolver(Dictionary<FName, string> toBeFilled) : base()
        {
            ToBeFilled = toBeFilled;
        }
    }

    /// <summary>Marks a byte copy of data that is also decoded, kept only to write the package back.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class WriteBackCopyAttribute : Attribute { }

    /// <summary>The contract for reading decoded data: write-back copies are left out.</summary>
    internal class ReadableContractResolver : UAssetContractResolver
    {
        public ReadableContractResolver(Dictionary<FName, string> toBeFilled) : base(toBeFilled) { }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (member.IsDefined(typeof(WriteBackCopyAttribute), true)) property.Ignored = true;
            return property;
        }
    }
}
