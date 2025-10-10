#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

namespace ShaderDecompiler.Structures;

public class Value
{
    public string? Name;
    public object? Object;
    public string? Semantic;
    public TypeInfo Type = new();

    public override string ToString()
    {
        return $"{Type} {Name}{(Semantic is null ? "" : $" : {Semantic}")};";
    }
}
