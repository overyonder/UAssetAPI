using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UAssetAPI.JSON;

// The emission callback runs even below the audit's depth/collection limits.
sealed class PayloadWarningResolver(IContractResolver inner, CoverageWarnings warnings) : IContractResolver
{
    public JsonContract ResolveContract(Type type)
    {
        var contract = inner.ResolveContract(type);
        if (contract is JsonObjectContract record)
            foreach (var property in record.Properties)
                if (!property.Ignored && property.PropertyType == typeof(byte[]) && property.Converter is not PayloadBytesConverter
                    && property.AttributeProvider.GetAttributes(typeof(PayloadFormatAttribute), true).FirstOrDefault() is PayloadFormatAttribute format)
                    property.Converter = new PayloadBytesConverter(format.Format, warnings);
        return contract;
    }

    sealed class PayloadBytesConverter(string format, CoverageWarnings warnings) : JsonConverter<byte[]>
    {
        public override void WriteJson(JsonWriter writer, byte[] value, JsonSerializer serializer)
        {
            if (value != null) warnings.Add("labelled payload emitted", $"{writer.Path}: {format} ({value.LongLength} bytes)");
            writer.WriteValue(value);
        }
        public override byte[] ReadJson(JsonReader reader, Type type, byte[] existing, bool hasExisting, JsonSerializer serializer) => throw new NotSupportedException();
    }
}
