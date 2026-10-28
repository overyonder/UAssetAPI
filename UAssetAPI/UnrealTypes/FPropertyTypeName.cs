using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace UAssetAPI.UnrealTypes;

public struct FPropertyTypeNameNode(AssetBinaryReader Ar)
{
    public FName Name = Ar.ReadFName();
    public int InnerCount = Ar.ReadInt32();
}

public class FPropertyTypeNameConverter : JsonConverter<FPropertyTypeName>
{
    public override void WriteJson(JsonWriter writer, FPropertyTypeName value, JsonSerializer serializer)
    {
        if (value is null || !value.ShouldSerializeNodes)
            writer.WriteNull();
        else
        {
            serializer.Serialize(writer, value.Nodes);
        }
    }
    public override FPropertyTypeName ReadJson(JsonReader reader, System.Type objectType, FPropertyTypeName existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        var nodes = serializer.Deserialize<List<FPropertyTypeNameNode>>(reader);
        return new FPropertyTypeName(nodes, true);
    }
}


[JsonConverter(typeof(FPropertyTypeNameConverter))]
public class FPropertyTypeName
{
    public List<FPropertyTypeNameNode> Nodes;
    public bool ShouldSerializeNodes = true;

    public FPropertyTypeName(List<FPropertyTypeNameNode> list, bool shouldSerialize = false)
    {
        Nodes = list;
        ShouldSerializeNodes = shouldSerialize;
    }

    public FPropertyTypeName(AssetBinaryReader reader)
    {
        Nodes = [];
        int totalNodes = 1;
        for (int i = 0; i < totalNodes; ++i)
        {
            var node = new FPropertyTypeNameNode(reader);
            Nodes.Add(node);
            if (node.InnerCount < 0 || node.InnerCount > int.MaxValue - totalNodes
                || (long)(totalNodes - i - 1 + (long)node.InnerCount) * 12 > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                reader.Asset.Coverage.Report("invalid-type-tree", $"Property type node at byte {reader.BaseStream.Position - 12} declares {node.InnerCount} children; inspection stopped at a negative count, node-count overflow or unavailable child-node bytes.");
                throw new FormatException("Invalid property type-name child count");
            }
            totalNodes += node.InnerCount;
        }
    }

    public void Write(AssetBinaryWriter writer)
    {
        foreach (var node in Nodes)
        {
            writer.Write(node.Name);
            writer.Write(node.InnerCount);
        }
    }

    public FName GetName() => Nodes is { Count: > 0 } ? Nodes[0].Name : FName.DefineDummy(null, "None");

    public FPropertyTypeName GetParameter(int paramIndex)
    {
        if (Nodes is not { Count: > 0 } || paramIndex < 0 || paramIndex >= Nodes[0].InnerCount) return new FPropertyTypeName([]);

        var param = 1;
        for (int skip = paramIndex; skip > 0; --skip, ++param)
        {
            skip += Nodes[param].InnerCount;
        }

        return new(Nodes[param..]);
    }
}
